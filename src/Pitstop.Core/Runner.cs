using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Pitstop;

/// <summary>Processo do runner em andamento para um perfil (Tomcat, app Java, npm, debug ou build).</summary>
public sealed class Execucao
{
    public required Process Child { get; init; }
    public required string Tipo { get; init; }
    public bool Debug { get; init; }
    public long Inicio { get; } = Agora.Ms;
    /// <summary>Hora em que ficou pronto ("Server startup in", prontoLog, ou 3 s de vida).</summary>
    public long? Pronto { get; set; }
    public Regex? ProntoRe { get; init; }
    public bool Parando { get; set; }
    public bool Cancelado { get; set; }
    /// <summary>App Java fechado pela própria janela sai com 0: fechamento normal, não queda.</summary>
    public bool SaidaZeroNormal { get; init; }
}

/// <summary>Resultado da última execução encerrada (status/tray: balão de pronto, caiu, build ok/falhou).</summary>
public sealed class Ultimo
{
    public string Tipo { get; init; } = "";
    public bool Ok { get; init; }
    public int? Codigo { get; init; }
    public bool? Cancelado { get; init; }
    public string? Erro { get; init; }
    /// <summary>false = saiu sem parar() pedido (caiu).</summary>
    public bool? Parado { get; init; }
    public long Fim { get; init; }
    public long Duracao { get; init; }
}

public sealed class StatusExecucao
{
    public string Tipo { get; init; } = "";
    public int Pid { get; init; }
    public long Desde { get; init; }
    public long? Pronto { get; init; }
}

public sealed record StatusArtefato(string Repo, string Modulo, string Contexto, string? DocBase);

public sealed class StatusPerfil
{
    public string Nome { get; init; } = "";
    public string Tipo { get; init; } = "tomcat";
    public int? Porta { get; init; }
    public int PortaDebug { get; init; }
    public string? Url { get; init; }
    public string? MainClass { get; init; }
    /// <summary>Perfil "zip": o .zip ou .jar; perfil "war": os wars (; entre eles). Nulo = roda do código.</summary>
    public string? Pacote { get; init; }
    public string? Script { get; init; }
    public bool EmUso { get; init; }
    public StatusExecucao? Execucao { get; init; }
    public Ultimo? Ultimo { get; init; }
    public List<StatusArtefato> Artefatos { get; init; } = [];
}

public sealed class ResultadoAcao
{
    public bool Ok { get; init; } = true;
    public int? Copiados { get; init; }
    public List<Publicado>? Publicado { get; init; }
}

/// <summary>
/// Regras do runner: subir, parar, depurar, buildar e sincronizar perfis. Usado pela API HTTP (tela), pela tray
/// (direto, no mesmo processo) e pela linha de comando.
/// </summary>
public sealed class Runner
{
    public LogHub Logs { get; } = new();

    readonly object trava = new();
    readonly Dictionary<string, Execucao> estado = new();
    readonly Dictionary<string, Reserva> reservas = new();
    readonly Dictionary<string, Ultimo> ultimos = new();
    /// <summary>Reinício em andamento (parar + subir): cobre o intervalo em que o perfil não tem processo nem reserva.</summary>
    readonly Dictionary<string, Reserva> reinicios = new();
    bool saindo;

    public bool Saindo { get { lock (trava) return saindo; } }

    void Log(string nome, string texto) => Logs.Log(nome, texto);

    Execucao? Em(string nome)
    {
        lock (trava) return estado.GetValueOrDefault(nome);
    }

    void GravarUltimo(string nome, Ultimo u)
    {
        lock (trava) ultimos[nome] = u;
    }

    /// <summary>Perfis com processo do runner ou com operação em andamento (inclusive entre um processo e outro).</summary>
    public string[] EmExecucao()
    {
        lock (trava) return estado.Keys.Union(reservas.Keys).Union(reinicios.Keys).ToArray();
    }

    // ---------------------------------------------------------------- reserva do perfil

    /// <summary>
    /// Ocupa o perfil do começo ao fim da operação, sob a trava: dois cliques (tela + tray) não sobem nem buildam em
    /// dobro. <paramref name="emExecucao"/> = mensagem quando já há processo do runner no perfil.
    /// </summary>
    Reserva Reservar(string nome, string tipo, string? emExecucao = null)
    {
        lock (trava)
        {
            if (estado.GetValueOrDefault(nome) is { } e) throw new ErroRunner(emExecucao ?? nome + " já está em execução (" + e.Tipo + ")");
            if (reservas.GetValueOrDefault(nome) is { } r) throw new ErroRunner(nome + " já está ocupado (" + r.Tipo + " em andamento)");
            if (reinicios.ContainsKey(nome)) throw new ErroRunner(nome + " está reiniciando");
            var nova = new Reserva { Tipo = tipo };
            reservas[nome] = nova;
            return nova;
        }
    }

    void Liberar(string nome, Reserva r)
    {
        lock (trava)
            if (reservas.GetValueOrDefault(nome) == r) reservas.Remove(nome);
    }

    /// <summary>Stop pedido entre um processo e outro: o próximo passo não começa.</summary>
    static void ChecarCancelado(Reserva? r, string mensagem = "cancelado")
    {
        if (r?.Cancelado == true) throw new ErroRunner(mensagem) { Cancelado = true };
    }

    // ---------------------------------------------------------------- saída dos processos

    /// <summary>Build: [INFO]/[WARNING] só o que mostra progresso; [ERROR] e texto solto (stacktrace, cmd) passam sempre.</summary>
    static readonly Regex BuildUtil = new(
        @"^\[INFO\] (Building |Compiling |Changes detected|Nothing to compile|Packaging webapp|Reactor Summary|BUILD |Total time|.*\b(SUCCESS|FAILURE|SKIPPED)\b)");

    static bool LinhaBuild(string l) =>
        l.Trim() != "" && (!Regex.IsMatch(l, @"^\[(INFO|WARNING|DEBUG)\]") || BuildUtil.IsMatch(l));

    /// <summary>Build do npm: saída inteira, só sem as linhas em branco.</summary>
    static bool LinhaNaoVazia(string l) => l.Trim() != "";

    /// <summary>prontoLog do perfil lento demais (timeout da regex): a linha só não marca "pronto".</summary>
    static bool Casa(Regex? re, string l)
    {
        try { return re?.IsMatch(l) == true; }
        catch (RegexMatchTimeoutException) { return false; }
    }

    /// <summary>Códigos de cor/cursor do terminal (dev server do npm mesmo com NO_COLOR): o log é texto puro.</summary>
    static readonly Regex Ansi = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]");

    void Encaminhar(string nome, Process child, Func<string, bool>? filtro)
    {
        void Sai(string? l)
        {
            if (l == null) return;
            if (l.Contains('\x1B')) l = Ansi.Replace(l, "");
            if (filtro != null) { if (filtro(l)) Log(nome, l); return; }
            // porta aberta não é deploy pronto: "Server startup in" marca o fim da subida
            var reg = Em(nome);
            if (reg != null && reg.Child == child && reg.Pronto == null && Casa(reg.ProntoRe, l))
                lock (trava) reg.Pronto = Agora.Ms;
            Log(nome, l);
        }
        child.OutputDataReceived += (_, e) => Sai(e.Data);
        child.ErrorDataReceived += (_, e) => Sai(e.Data);
        child.BeginOutputReadLine();
        child.BeginErrorReadLine();
    }

    /// <summary>Registra a execução do perfil, encaminha a saída e devolve o código de saída quando terminar.</summary>
    Task<int> Acoplar(string nome, Execucao reg, bool filtrar = true)
    {
        var child = reg.Child;
        lock (trava) estado[nome] = reg;
        Encaminhar(nome, child, reg.Tipo == "build" ? (filtrar ? LinhaBuild : LinhaNaoVazia) : null);
        // sem padrão de log (app Java sem prontoLog): "pronto" = continua vivo depois de 3 s
        if (reg.Tipo != "build" && reg.ProntoRe == null)
            _ = Task.Delay(3000).ContinueWith(_ => { lock (trava) if (estado.GetValueOrDefault(nome) == reg) reg.Pronto = Agora.Ms; });
        return Task.Run(async () =>
        {
            await child.WaitForExitAsync().ConfigureAwait(false);
            var code = child.ExitCode;
            Log(nome, "[pit] " + reg.Tipo + " terminou (código " + code + ")");
            // build registra o resultado do conjunto em Build(); Tomcat sem parar() pedido = caiu
            var normal = reg.Parando || (reg.SaidaZeroNormal && code == 0);
            lock (trava)
            {
                if (reg.Tipo != "build")
                    ultimos[nome] = new Ultimo
                    {
                        Tipo = reg.Tipo, Ok = normal, Codigo = code, Parado = normal, Fim = Agora.Ms, Duracao = Agora.Ms - reg.Inicio,
                    };
                if (estado.GetValueOrDefault(nome)?.Child == child) estado.Remove(nome);
            }
            return code;
        });
    }

    // ---------------------------------------------------------------- Tomcat

    public async Task Iniciar(string nome, bool debug, bool semCompilar = false)
    {
        var p = Config.LerPerfil(Config.Ler(), nome);
        if (p.EhJava) { await IniciarJava(nome, p, debug, semCompilar).ConfigureAwait(false); return; }
        if (p.EhNpm) { await IniciarNpm(nome, p, debug).ConfigureAwait(false); return; }
        var res = Reservar(nome, debug ? "debug" : "tomcat");
        var segundoPlano = false;
        try
        {
            await ChecarLivre(p, debug).ConfigureAwait(false);
            if (p.EhWar && p.Artefatos.Any(a => !Pacote.Extraido(a.Extracao)))
            {
                // war ainda não extraído (primeira subida, versão nova): leva de segundos a minutos, segue em segundo
                // plano como a compilação do app Java. Stop no meio cancela entre um arquivo e outro
                ValidarWar(p);
                segundoPlano = true;
                var t0 = Agora.Ms;
                _ = Task.Run(() =>
                {
                    try
                    {
                        PrepararWar(nome, p, false, res);
                        SubirTomcat(nome, p, debug, res);
                    }
                    catch (Exception ex)
                    {
                        var cancelado = ex is ErroRunner { Cancelado: true };
                        GravarUltimo(nome, new Ultimo
                        {
                            Tipo = "build", Ok = false, Cancelado = cancelado, Erro = ex.Message, Fim = Agora.Ms, Duracao = Agora.Ms - t0,
                        });
                        Log(nome, "[pit] " + (cancelado ? "subida cancelada" : "ERRO " + ex.Message));
                    }
                    finally { Liberar(nome, res); }
                });
                return;
            }
            SubirTomcat(nome, p, debug, res);
        }
        finally { if (!segundoPlano) Liberar(nome, res); }   // com o processo acoplado, estado[] passa a cobrir o perfil
    }

    void SubirTomcat(string nome, Perfil p, bool debug, Reserva res)
    {
        var pub = Tomcat.PrepararBase(p);
        Log(nome, "[pit] base " + p.Base);
        foreach (var x in pub) Log(nome, "[pit] " + x.Contexto + " -> " + x.DocBase);
        Log(nome, "[pit] http " + p.Porta + " | shutdown " + p.PortaShutdown + (debug ? " | debug " + p.PortaDebug : "") +
                  (p.PortaJmx is int pj ? " | jmx " + pj : "") + (p.PortaAjp is int pa ? " | ajp " + pa : ""));
        ChecarCancelado(res, "subida cancelada");
        var child = Tomcat.Catalina(p, debug ? "jpda run" : "run", debug, redirecionar: true);
        PidArquivo.Gravar(p, child);
        _ = Acoplar(nome, new Execucao
        {
            Child = child, Tipo = debug ? "debug" : "tomcat", Debug = debug, ProntoRe = new Regex("Server startup in"),
        });
    }

    // ---------------------------------------------------------------- pacotes prontos (tipos "war" e "zip")

    public static void ValidarWar(Perfil p)
    {
        if (p.Artefatos.Count == 0) throw new ErroRunner("perfil " + p.Nome + ": nenhum war marcado (aba Pacotes)");
        foreach (var a in p.Artefatos)
            if (a.War == "" || !File.Exists(a.War)) throw new ErroRunner("perfil " + p.Nome + ": war não encontrado: " + a.War);
    }

    /// <summary>Extrai os wars que faltam (ou todos, com <paramref name="forcar"/>: o Build do perfil "war").</summary>
    public void PrepararWar(string nome, Perfil p, bool forcar, Reserva? res)
    {
        ValidarWar(p);
        foreach (var a in p.Artefatos)
        {
            ChecarCancelado(res, "extração cancelada antes de " + a.Contexto);
            if (!Pacote.Extrair(a.War!, a.Extracao, l => Log(nome, l), () => res?.Cancelado == true, forcar))
                Log(nome, "[pit] " + a.Contexto + ": war já extraído em " + a.Extracao);
        }
    }

    /// <summary>
    /// Pacote do perfil "zip": extrai o zip (se preciso), acha o jar e a pasta de trabalho e copia a conf sem
    /// sobrescrever (o app pode gravar nela). Pacote .jar roda de onde está. Devolve (jar, pasta de trabalho).
    /// </summary>
    public (string Jar, string Trabalho) PrepararZip(string nome, Perfil p, bool forcar, Reserva? res)
    {
        string raiz, jar;
        if (p.Extracao == "")
        {
            jar = p.Pacote;
            raiz = Path.GetDirectoryName(jar)!;
            if (forcar) Log(nome, "[pit] pacote é um .jar: nada para extrair");
        }
        else
        {
            if (!Pacote.Extrair(p.Pacote, p.Extracao, l => Log(nome, l), () => res?.Cancelado == true, forcar))
                Log(nome, "[pit] pacote já extraído em " + p.Extracao);
            raiz = p.Extracao;
            jar = p.Jar != "" ? Path.GetFullPath(Path.Combine(raiz, p.Jar))
                : Pacote.JarPrincipal(raiz) ?? throw new ErroRunner("nenhum jar com Main-Class no pacote (fora de lib\\): informe o jar na aba Pacote");
        }
        if (!File.Exists(jar)) throw new ErroRunner("jar não encontrado: " + jar);
        var trabalho = p.Trabalho != "" ? Path.GetFullPath(Path.Combine(raiz, p.Trabalho)) : Path.GetDirectoryName(jar)!;
        if (!Directory.Exists(trabalho)) throw new ErroRunner("pasta de trabalho não existe: " + trabalho);
        if (p.ConfApp != "")
        {
            var destino = Path.GetFullPath(Path.Combine(trabalho, p.ConfDestino));
            var n = Tomcat.CopiarSemSobrescrever(p.ConfApp, destino);
            Log(nome, "[pit] conf " + p.ConfApp + " -> " + destino + ": " + (n > 0 ? n + " arquivo(s) novo(s)" : "já estava lá (não sobrescreve)"));
        }
        return (jar, trabalho);
    }

    /// <summary>
    /// Portas do perfil livres (connect em 127.0.0.1: pega IntelliJ ou outro perfil no ar) e, no app Java e no npm,
    /// nenhum processo do pit.pid vivo. A mesma checagem vale para a tela, a tray e o pit up.
    /// </summary>
    public static async Task ChecarLivre(Perfil p, bool debug)
    {
        if (!p.EhJava && await Proc.PortaOcupada(p.Porta).ConfigureAwait(false)) throw new ErroRunner("porta " + p.Porta + " em uso (IntelliJ ou outro perfil no ar?)");
        if (debug && await Proc.PortaOcupada(p.PortaDebug).ConfigureAwait(false)) throw new ErroRunner("porta de debug " + p.PortaDebug + " em uso");
        if (!p.EhTomcat)
        {
            if (PidArquivo.Ler(p) is { } r && PidArquivo.Vivo(r, ExePid(p)))
                throw new ErroRunner(p.Nome + " já está rodando fora desta tela (pid " + r.Pid + ")");
            return;
        }
        // shutdown ocupado: o Tomcat subiria e cairia logo em seguida (StandardServer.await não consegue escutar)
        if (await Proc.PortaOcupada(p.PortaShutdown).ConfigureAwait(false)) throw new ErroRunner("porta de shutdown " + p.PortaShutdown + " em uso (outro Tomcat no ar?)");
        if (p.PortaJmx is int j && await Proc.PortaOcupada(j).ConfigureAwait(false)) throw new ErroRunner("porta JMX " + j + " em uso");
        if (p.PortaAjp is int a && await Proc.PortaOcupada(a).ConfigureAwait(false)) throw new ErroRunner("porta AJP " + a + " em uso");
    }

    /// <summary>Stop da tela, da tray e do pit down. Durante um reinício, cancela a subida que viria depois.</summary>
    public Task Parar(string nome)
    {
        lock (trava)
            if (reinicios.GetValueOrDefault(nome) is { } ri)
            {
                ri.Cancelado = true;
                Log(nome, "[pit] reinício cancelado: fica parado");
            }
        return PararAgora(nome);
    }

    async Task PararAgora(string nome)
    {
        var p = Config.LerPerfil(Config.Ler(), nome);
        var e = Em(nome);
        Reserva? res;
        lock (trava) res = reservas.GetValueOrDefault(nome);
        // build/compilação: derruba a árvore do Maven e avisa o loop. Entre um processo e outro não há árvore para
        // derrubar: só a reserva sabe que o perfil está ocupado, e o próximo passo não começa
        if (e is { Tipo: "build" } || (e == null && res != null))
        {
            if (res != null) res.Cancelado = true;
            if (e == null)
            {
                Log(nome, "[pit] cancelando " + res!.Tipo + " (o próximo passo não começa)...");
                return;
            }
            e.Cancelado = true;
            Log(nome, "[pit] cancelando build (pid " + e.Child.Id + ")...");
            So.Encerrar(e.Child.Id, true);
            return;
        }
        if (p.EhJava) { await PararJava(nome, p, e).ConfigureAwait(false); return; }
        if (p.EhNpm) { await PararNpm(nome, p, e).ConfigureAwait(false); return; }
        // subiu pelo terminal (pit up) ou por outra instância: só o arquivo de pid sabe quem é. Sem processo e sem
        // pit.pid vivo o perfil não está no ar pelo runner: o catalina stop mandaria SHUTDOWN para a porta de shutdown
        // do perfil, que pode ser a de outro Tomcat (ex.: o do IntelliJ)
        var r = e == null ? PidArquivo.Ler(p) : null;
        if (e == null && (r == null || !PidArquivo.Vivo(r.Value, So.Shell)))
        {
            Log(nome, "[pit] " + nome + " não está no ar pelo runner (sem processo nem pit.pid vivo): stop não enviado");
            return;
        }
        if (e != null) e.Parando = true;
        Log(nome, "[pit] parando...");
        var stop = Tomcat.Catalina(p, "stop", false, redirecionar: true);
        Encaminhar(nome, stop, null);
        var fim = Agora.Ms + Config.StopTimeout * 1000L;
        int pid;
        if (e != null)
        {
            pid = e.Child.Id;
            while (Em(nome)?.Child == e.Child && Agora.Ms < fim) await Task.Delay(500).ConfigureAwait(false);
            if (Em(nome)?.Child != e.Child) return;
        }
        else
        {
            var reg = r!.Value;
            pid = reg.Pid;
            while (await Proc.PortaOcupada(p.Porta).ConfigureAwait(false) && Agora.Ms < fim) await Task.Delay(500).ConfigureAwait(false);
            if (!await Proc.PortaOcupada(p.Porta).ConfigureAwait(false) || !PidArquivo.Vivo(reg, So.Shell)) return;
        }
        Log(nome, "[pit] não parou em " + Config.StopTimeout + "s, matando a árvore do processo " + pid);
        So.Encerrar(pid, true);
    }

    // ---------------------------------------------------------------- aplicação Java (perfil tipo "java")

    /// <summary>
    /// Executor de comandos de manutenção (Maven) com saída no log do perfil; Parar() cancela, inclusive entre um
    /// comando e outro (<paramref name="res"/>).
    /// </summary>
    Func<string, string, Task<int>> RodarManutencao(string nome, Perfil p, bool console, Reserva? res) => async (cmd, cwd) =>
    {
        ChecarCancelado(res);
        var psi = So.Linha(cmd, cwd, !console, Encoding.UTF8);
        psi.Environment["JAVA_HOME"] = p.JavaHomeBuild;
        var child = Proc.Disparar(psi);
        if (console)
        {
            await child.WaitForExitAsync().ConfigureAwait(false);
            return child.ExitCode;
        }
        var reg = new Execucao { Child = child, Tipo = "build" };
        var code = await Acoplar(nome, reg).ConfigureAwait(false);
        if (reg.Cancelado || res?.Cancelado == true) throw new ErroRunner("cancelado") { Cancelado = true };
        return code;
    };

    /// <summary>
    /// Classpath + compilação só do que mudou + jar de classpath. <paramref name="forcar"/> (Build): regera o
    /// classpath e compila a cadeia inteira (-am). Devolve o caminho do jar de classpath.
    /// </summary>
    public async Task<string> PrepararJava(string nome, Perfil p, bool forcar, bool semCompilar, bool console, Reserva? res = null)
    {
        var rodar = RodarManutencao(nome, p, console, res);
        var c = await JavaApp.ResolverClasspath(p, rodar, l => Log(nome, l), forcar).ConfigureAwait(false);
        string Rel(string d) => Path.GetRelativePath(c.Raiz, d).Replace('\\', '/');
        if (semCompilar) Log(nome, "[pit] compilação desligada: roda com o que já está em target/classes");
        else
        {
            List<string> velhos = forcar ? [] : JavaApp.Desatualizados(c.ModulosRepo);
            if (forcar || velhos.Count > 0)
            {
                List<string> alvo = forcar ? [Rel(p.Modulo)] : velhos.Select(Rel).ToList();
                Log(nome, "[pit] compilando " + (forcar
                    ? "a cadeia inteira de " + alvo[0] + " (Build)"
                    : velhos.Count + " módulo(s) com fonte mais novo que as classes: " + string.Join(", ", alvo)));
                var t0 = Agora.Ms;
                var code = await rodar(Maven.Comando(p) + Maven.FlagOffline + " -B -ntp compile -pl " + string.Join(",", alvo) + (forcar ? " -am" : ""), c.Raiz).ConfigureAwait(false);
                if (code != 0)
                    throw new ErroRunner("compilação falhou (código " + code + ")" +
                        (forcar ? "" : "; se faltar símbolo de outro módulo, rode o Build (compila a cadeia inteira)"));
                Log(nome, "[pit] compilado em " + Agora.Duracao(Agora.Ms - t0));
            }
            else Log(nome, "[pit] classes em dia (" + c.ModulosRepo.Count + " módulos conferidos), sem compilar");
        }
        var jar = Path.Combine(p.Cache, "classpath.jar");
        JavaApp.GravarJarClasspath(jar, c.Entradas);
        return jar;
    }

    public static void ValidarJava(Perfil p)
    {
        if (p.EhZip)
        {
            // pasta de trabalho só existe depois da extração: PrepararZip confere
            if (p.Pacote == "" || !File.Exists(p.Pacote)) throw new ErroRunner("perfil " + p.Nome + ": pacote não encontrado: " + p.Pacote);
            if (p.ConfApp != "" && !Directory.Exists(p.ConfApp)) throw new ErroRunner("perfil " + p.Nome + ": pasta de conf não existe: " + p.ConfApp);
        }
        else
        {
            if (p.MainClass == "") throw new ErroRunner("perfil " + p.Nome + ": informe a classe main");
            if (p.Modulo == "" || !File.Exists(Path.Combine(p.Modulo, "pom.xml"))) throw new ErroRunner("perfil " + p.Nome + ": módulo sem pom.xml: " + p.Modulo);
            if (!Directory.Exists(p.Trabalho)) throw new ErroRunner("perfil " + p.Nome + ": pasta de trabalho não existe: " + p.Trabalho);
        }
        if (string.IsNullOrEmpty(p.JavaHome)) throw new ErroRunner("perfil " + p.Nome + ": informe o Java (JAVA_HOME) do perfil");
        if (!File.Exists(Path.Combine(p.JavaHome, "bin", So.Exe("java"))))
            throw new ErroRunner("perfil " + p.Nome + ": " + So.Exe("java") + " não encontrado em " + Path.Combine(p.JavaHome, "bin"));
        if (!p.EhZip) Maven.Validar(p);
        // regex inválida só estouraria depois da compilação, na hora de subir
        if (p.ProntoLog != "")
            try { _ = new Regex(p.ProntoLog); }
            catch (ArgumentException ex) { throw new ErroRunner("perfil " + p.Nome + ": prontoLog não é uma regex válida: " + ex.Message); }
    }

    /// <summary>
    /// Sobe o java. A saída do Java 8 no Windows sai na página de código do sistema, não em UTF-8.
    /// <paramref name="trabalho"/>: pasta de trabalho do pacote (tipo "zip"), que só se sabe depois da extração.
    /// </summary>
    public Process LancarJava(string nome, Perfil p, string jar, bool debug, bool console, string? trabalho = null)
    {
        var exe = Path.Combine(p.JavaHome!, "bin", So.Exe("java"));
        var cwd = trabalho ?? p.Trabalho;
        Log(nome, "[pit] " + (p.EhZip ? "-jar " + jar : p.MainClass) + " | pasta de trabalho " + cwd + (debug ? " | debug " + p.PortaDebug : "") + " | " + exe);
        if (p.VmArgs != "" || p.Args != "")
            Log(nome, "[pit] args: " + string.Join(" | ", new[] { p.VmArgs, p.Args }.Where(s => s != "")));
        var psi = Proc.Psi(exe, cwd, !console, So.Windows ? Encoding.Latin1 : Encoding.UTF8);
        foreach (var a in JavaApp.ArgsJava(p, jar, debug)) psi.ArgumentList.Add(a);
        JavaApp.EnvJava(p, psi.Environment, p.EhZip ? cwd : p.Modulo);
        var child = Proc.Disparar(psi);
        PidArquivo.Gravar(p, child);
        if (!console)
            _ = Acoplar(nome, new Execucao
            {
                Child = child, Tipo = debug ? "debug" : "java", Debug = debug, SaidaZeroNormal = true,
                ProntoRe = p.ProntoLog != "" ? new Regex(p.ProntoLog, RegexOptions.None, TimeSpan.FromMilliseconds(200)) : null,
            });
        return child;
    }

    /// <summary>
    /// Validação na hora; compilação + subida em segundo plano (a compilação pode levar minutos). A reserva ocupa o
    /// perfil até o java estar acoplado: Start repetido é recusado e Stop no meio cancela a subida.
    /// </summary>
    async Task IniciarJava(string nome, Perfil p, bool debug, bool semCompilar)
    {
        var res = Reservar(nome, "build");
        try
        {
            ValidarJava(p);
            await ChecarLivre(p, debug).ConfigureAwait(false);
            Directory.CreateDirectory(p.Cache);
        }
        catch
        {
            Liberar(nome, res);
            throw;
        }
        var t0 = Agora.Ms;
        _ = Task.Run(async () =>
        {
            try
            {
                if (p.EhZip)
                {
                    var (jarPacote, trabalho) = PrepararZip(nome, p, false, res);
                    ChecarCancelado(res);
                    LancarJava(nome, p, jarPacote, debug, false, trabalho);
                    return;
                }
                var jar = await PrepararJava(nome, p, false, semCompilar || !p.CompilarAntes, false, res).ConfigureAwait(false);
                ChecarCancelado(res);
                LancarJava(nome, p, jar, debug, false);
            }
            catch (Exception ex)
            {
                var cancelado = ex is ErroRunner { Cancelado: true };
                GravarUltimo(nome, new Ultimo
                {
                    Tipo = "build", Ok = false, Cancelado = cancelado, Erro = ex.Message, Fim = Agora.Ms, Duracao = Agora.Ms - t0,
                });
                Log(nome, "[pit] " + (cancelado ? "subida cancelada" : "ERRO " + ex.Message));
            }
            finally { Liberar(nome, res); }
        });
    }

    /// <summary>
    /// Sem porta de shutdown: pede para fechar (Windows: taskkill sem /F manda WM_CLOSE às janelas, igual ao X da
    /// janela; Linux: SIGTERM, que roda os shutdown hooks da JVM); depois de STOP_TIMEOUT_SEG, força. Kill forçado
    /// pula a finalização do app.
    /// </summary>
    async Task PararJava(string nome, Perfil p, Execucao? e)
    {
        PidArquivo.Registro? r = null;
        int pid;
        if (e != null) { e.Parando = true; pid = e.Child.Id; }
        else
        {
            r = PidArquivo.Ler(p);
            if (r == null || !PidArquivo.Vivo(r.Value, "java")) return;
            pid = r.Value.Pid;
        }
        Log(nome, "[pit] parando (" + (So.Windows ? "fechando as janelas" : "SIGTERM") + " do pid " + pid + ")...");
        So.Encerrar(pid, false);
        bool Vivo() => e != null ? Em(nome)?.Child == e.Child : PidArquivo.Vivo(r!.Value, "java");
        var fim = Agora.Ms + Config.StopTimeout * 1000L;
        while (Vivo() && Agora.Ms < fim) await Task.Delay(500).ConfigureAwait(false);
        if (!Vivo()) return;
        Log(nome, "[pit] não fechou em " + Config.StopTimeout + "s (diálogo aberto?), matando a árvore do processo " + pid);
        So.Encerrar(pid, true);
    }

    // ---------------------------------------------------------------- npm (perfil tipo "npm", ex.: frontend Angular)

    /// <summary>Processo gravado no pit.pid: o Tomcat e o npm sobem pelo shell (cmd/sh); o app Java é o próprio java.</summary>
    public static string ExePid(Perfil p) => p.EhJava ? "java" : So.Shell;

    /// <summary>Sobe npm run &lt;script&gt;. Sem depurar: o código do dev server roda no navegador.</summary>
    public Process LancarNpm(string nome, Perfil p, bool console)
    {
        var linha = NodeApp.LinhaStart(p);
        Log(nome, "[pit] " + linha + " | pasta " + p.Pasta + " | http " + p.Porta +
                  (string.IsNullOrEmpty(p.NodeHome) ? " | Node do PATH" : " | Node " + p.NodeHome));
        var child = Proc.Disparar(NodeApp.Psi(p, linha, !console));
        PidArquivo.Gravar(p, child);
        if (!console)
            _ = Acoplar(nome, new Execucao
            {
                Child = child, Tipo = "npm",
                ProntoRe = p.ProntoLog != "" ? new Regex(p.ProntoLog, RegexOptions.None, TimeSpan.FromMilliseconds(200)) : null,
            });
        return child;
    }

    /// <summary>Validação (package.json, script, node_modules, portas) e subida; o mesmo para a tela, a tray e o pit up.</summary>
    public static async Task ValidarNpm(Perfil p, bool debug)
    {
        if (debug) throw new ErroRunner("perfil npm não tem Depurar: o frontend se depura no navegador (F12)");
        NodeApp.Validar(p);
        if (p.ProntoLog != "")
            try { _ = new Regex(p.ProntoLog); }
            catch (ArgumentException ex) { throw new ErroRunner("perfil " + p.Nome + ": prontoLog não é uma regex válida: " + ex.Message); }
        await ChecarLivre(p, false).ConfigureAwait(false);
        Directory.CreateDirectory(p.Cache);
    }

    async Task IniciarNpm(string nome, Perfil p, bool debug)
    {
        var res = Reservar(nome, "npm");
        try
        {
            await ValidarNpm(p, debug).ConfigureAwait(false);
            ChecarCancelado(res, "subida cancelada");
            LancarNpm(nome, p, false);
        }
        finally { Liberar(nome, res); }   // com o processo acoplado, estado[] passa a cobrir o perfil
    }

    /// <summary>
    /// Dev server não tem janela nem porta de shutdown: derruba a árvore (cmd → npm → node) direto. Não há o que
    /// salvar no processo, e sem /F o node ignora o taskkill.
    /// </summary>
    async Task PararNpm(string nome, Perfil p, Execucao? e)
    {
        PidArquivo.Registro? r = null;
        int pid;
        if (e != null) { e.Parando = true; pid = e.Child.Id; }
        else
        {
            r = PidArquivo.Ler(p);
            if (r == null || !PidArquivo.Vivo(r.Value, So.Shell))
            {
                Log(nome, "[pit] " + nome + " não está no ar pelo runner (sem processo nem pit.pid vivo)");
                return;
            }
            pid = r.Value.Pid;
        }
        Log(nome, "[pit] parando (árvore do pid " + pid + ")...");
        So.Encerrar(pid, true);
        bool Vivo() => e != null ? Em(nome)?.Child == e.Child : PidArquivo.Vivo(r!.Value, So.Shell);
        var fim = Agora.Ms + Config.StopTimeout * 1000L;
        while (Vivo() && Agora.Ms < fim) await Task.Delay(300).ConfigureAwait(false);
        if (Vivo()) Log(nome, "[pit] pid " + pid + " ainda vivo depois de " + Config.StopTimeout + "s");
    }

    // ---------------------------------------------------------------- reiniciar

    /// <summary>
    /// Parar + subir no mesmo modo (debug continua debug). Validação na hora (erro = 400 na tela, balão na tray);
    /// o resto em segundo plano: o stop do Tomcat pode levar STOP_TIMEOUT_SEG. Perfil no ar fora desta tela
    /// (pit up) também reinicia: para pelo pit.pid e sobe pelo runner.
    /// </summary>
    public void ReservarReinicio(string nome)
    {
        var p = Config.LerPerfil(Config.Ler(), nome);
        Reserva? r = null;
        bool debug;
        lock (trava)
        {
            if (reinicios.ContainsKey(nome)) throw new ErroRunner(nome + " já está reiniciando");
            if (reservas.GetValueOrDefault(nome) is { } rs) throw new ErroRunner(nome + " está ocupado (" + rs.Tipo + " em andamento)");
            var e = estado.GetValueOrDefault(nome);
            if (e is { Tipo: "build" }) throw new ErroRunner(nome + ": build em andamento");
            debug = e?.Debug == true;
            if (e != null)   // sem processo do runner: confere "no ar por fora" abaixo, fora da trava (sonda de porta)
            {
                r = new Reserva { Tipo = "reinicio" };
                reinicios[nome] = r;
            }
        }
        if (r == null)
        {
            // pelo pit.pid, não pela porta: porta ocupada pode ser o Tomcat do IntelliJ, que o runner não para
            var fora = PidArquivo.Ler(p) is { } pr && PidArquivo.Vivo(pr, ExePid(p));
            if (!fora) throw new ErroRunner(nome + " não está no ar pelo runner: use Iniciar");
            lock (trava)
            {
                if (estado.ContainsKey(nome) || reservas.ContainsKey(nome) || reinicios.ContainsKey(nome))
                    throw new ErroRunner(nome + " mudou de estado; tente de novo");
                r = new Reserva { Tipo = "reinicio" };
                reinicios[nome] = r;
            }
        }
        var reserva = r;
        _ = Task.Run(async () =>
        {
            try
            {
                Log(nome, "[pit] reiniciando" + (debug ? " em debug" : "") + "...");
                await PararAgora(nome).ConfigureAwait(false);
                // Parar já espera o processo sair; esta espera cobre o fim do acoplamento (estado[] limpo)
                var fim = Agora.Ms + 5000;
                while (Em(nome) != null && Agora.Ms < fim) await Task.Delay(200).ConfigureAwait(false);
                if (Em(nome) != null) throw new ErroRunner("não parou; reinício cancelado");
                lock (trava)
                {
                    if (reserva.Cancelado) return;
                    reinicios.Remove(nome);   // Iniciar reserva o perfil a partir daqui
                }
                await Iniciar(nome, debug).ConfigureAwait(false);
            }
            catch (Exception ex) { Log(nome, "[pit] ERRO " + ex.Message); }
            finally
            {
                lock (trava)
                    if (reinicios.GetValueOrDefault(nome) == reserva) reinicios.Remove(nome);
            }
        });
    }

    // ---------------------------------------------------------------- build e sync

    /// <summary>Build pela linha de comando: valida, ocupa o perfil e roda até o fim.</summary>
    public async Task Build(string nome, bool console = false) =>
        await RodarBuild(nome, await ReservarBuild(nome).ConfigureAwait(false), console).ConfigureAwait(false);

    /// <summary>
    /// Recusa o build com o perfil no ar e ocupa o perfil. Vale também para o que subiu por fora (pit up, outra
    /// instância): não está em estado[], mas trava os arquivos do mesmo jeito. Erro aqui = 400 na tela.
    /// </summary>
    async Task<Reserva> ReservarBuild(string nome)
    {
        var p = Config.LerPerfil(Config.Ler(), nome);
        var travado = p.Tipo switch
        {
            "java" => " (o java em execução usa o classpath.jar)",
            "zip" => " (o java em execução usa os jars da pasta extraída)",
            "npm" => " (o dev server trava node_modules e a pasta de saída)",
            _ => " (o Tomcat em execução usa os jars de WEB-INF/lib)",
        };
        var res = Reservar(nome, "build", "pare o " + nome + " antes do build" + travado);
        try
        {
            var fora = p.EhJava
                ? PidArquivo.Ler(p) is { } r && PidArquivo.Vivo(r, "java")
                : await Proc.PortaOcupada(p.Porta).ConfigureAwait(false);
            if (fora)
                throw new ErroRunner("pare o " + nome + " antes do build: " +
                    (p.EhJava ? "está rodando fora desta tela" : "porta " + p.Porta + " em uso") + travado);
            if (p.EhWar) ValidarWar(p);
            else if (p.EhZip) ValidarJava(p);
            else if (p.EhTomcat && !p.Artefatos.Any(a => !string.IsNullOrEmpty(a.Build)))
                throw new ErroRunner("nenhum artefato ativo com comando de build");
            if ((p.EhTomcat && !p.EhWar) || (p.EhJava && !p.EhZip)) Maven.Validar(p);
            if (p.EhNpm)
            {
                if (p.Build.Trim() == "") throw new ErroRunner("perfil " + nome + ": sem comando de build (aba Projeto)");
                if (!File.Exists(Path.Combine(p.Pasta, "package.json"))) throw new ErroRunner("perfil " + nome + ": pasta sem package.json: " + p.Pasta);
            }
            return res;
        }
        catch
        {
            Liberar(nome, res);
            throw;
        }
    }

    /// <summary>Resultado do build inteiro (todos os artefatos) fica em ultimos para o status/tray. Libera a reserva no fim.</summary>
    async Task RodarBuild(string nome, Reserva res, bool console)
    {
        var t0 = Agora.Ms;
        try
        {
            await BuildArtefatos(nome, res, console).ConfigureAwait(false);
            GravarUltimo(nome, new Ultimo { Tipo = "build", Ok = true, Codigo = 0, Fim = Agora.Ms, Duracao = Agora.Ms - t0 });
        }
        catch (Exception ex)
        {
            GravarUltimo(nome, new Ultimo
            {
                Tipo = "build", Ok = false, Cancelado = ex is ErroRunner { Cancelado: true }, Erro = ex.Message, Fim = Agora.Ms, Duracao = Agora.Ms - t0,
            });
            throw;
        }
        finally { Liberar(nome, res); }
    }

    async Task BuildArtefatos(string nome, Reserva res, bool console)
    {
        var p = Config.LerPerfil(Config.Ler(), nome);
        // pacote pronto: não há o que compilar; o Build extrai de novo (descarta o que o app gravou na extração)
        if (p.EhWar)
        {
            await Task.Run(() => PrepararWar(nome, p, true, res)).ConfigureAwait(false);
            return;
        }
        if (p.EhZip)
        {
            await Task.Run(() => PrepararZip(nome, p, true, res)).ConfigureAwait(false);
            return;
        }
        if (p.EhJava)
        {
            await PrepararJava(nome, p, true, false, console, res).ConfigureAwait(false);
            return;
        }
        if (p.EhNpm)
        {
            var linha = NodeApp.LinhaBuild(p);
            Log(nome, "[pit] build: " + linha + "  (em " + p.Pasta + ")");
            var tb = Agora.Ms;
            var filho = Proc.Disparar(NodeApp.Psi(p, linha, !console));
            int cb;
            if (console)
            {
                await filho.WaitForExitAsync().ConfigureAwait(false);
                cb = filho.ExitCode;
            }
            else
            {
                // saída inteira no log: o filtro de linhas do build é do Maven
                var reg = new Execucao { Child = filho, Tipo = "build" };
                cb = await Acoplar(nome, reg, filtrar: false).ConfigureAwait(false);
                if (reg.Cancelado || res.Cancelado) throw new ErroRunner("build cancelado") { Cancelado = true };
            }
            if (cb != 0) throw new ErroRunner("build falhou (código " + cb + ")");
            Log(nome, "[pit] build ok em " + Agora.Duracao(Agora.Ms - tb));
            return;
        }
        var alvos = p.Artefatos.Where(a => !string.IsNullOrEmpty(a.Build)).ToList();
        if (alvos.Count == 0) throw new ErroRunner("nenhum artefato ativo com comando de build");
        var t0 = Agora.Ms;
        for (var i = 0; i < alvos.Count; i++)
        {
            var a = alvos[i];
            // Stop entre um artefato e outro: não há Maven para derrubar, só a reserva marcada
            ChecarCancelado(res, "build cancelado antes de " + a.Contexto + "; artefatos seguintes não rodaram");
            Log(nome, "[pit] build " + (i + 1) + "/" + alvos.Count + " " + a.Contexto + ": " + a.Build + "  (em " + a.Repo + ")");
            var psi = So.Linha(Maven.Linha(p, a.Build!), a.Repo, !console, Encoding.UTF8);
            psi.Environment["JAVA_HOME"] = p.JavaHome;
            var child = Proc.Disparar(psi);
            var ta = Agora.Ms;
            int code;
            using (new Timer(_ => Log(nome, "[pit] build " + a.Contexto + " rodando... " + Agora.Duracao(Agora.Ms - ta)), null, 30000, 30000))
            {
                if (console)
                {
                    await child.WaitForExitAsync().ConfigureAwait(false);
                    code = child.ExitCode;
                }
                else
                {
                    var reg = new Execucao { Child = child, Tipo = "build" };   // Parar() marca Cancelado aqui
                    code = await Acoplar(nome, reg).ConfigureAwait(false);
                    if (reg.Cancelado || res.Cancelado)
                        throw new ErroRunner("build cancelado em " + a.Contexto + "; artefatos seguintes não rodaram") { Cancelado = true };
                }
            }
            Log(nome, "[pit] build " + a.Contexto + " levou " + Agora.Duracao(Agora.Ms - ta));
            if (code != 0) throw new ErroRunner("build falhou em " + a.Contexto + " (código " + code + ")");
        }
        Log(nome, "[pit] build ok em " + Agora.Duracao(Agora.Ms - t0));
    }

    public int Sync(string nome)
    {
        var p = Config.LerPerfil(Config.Ler(), nome);
        if (p.EhWar || p.EhZip) throw new ErroRunner("sync não se aplica a pacote pronto: o Build extrai o pacote de novo");
        if (!p.EhTomcat) throw new ErroRunner("sync é do Tomcat (war exploded); " +
            (p.EhJava ? "aplicação Java roda direto do target/classes" : "o dev server do npm recarrega sozinho"));
        var total = 0;
        foreach (var a in p.Artefatos)
        {
            var docBase = Tomcat.ResolverDocBase(a);
            if (docBase == null) { Log(nome, "[pit] sync " + a.Contexto + ": sem pasta explodida"); continue; }
            foreach (var s in a.Sync)
            {
                var falhas = new List<string>();
                var n = Tomcat.CopiarNovos(Path.Combine(a.Repo, a.Modulo, s.De), Path.Combine(docBase, s.Para), falhas);
                Log(nome, "[pit] sync " + a.Contexto + " " + s.De + " -> " + (s.Para == "" ? "." : s.Para) + ": " + n + " arquivo(s)" +
                          (falhas.Count > 0 ? ", " + falhas.Count + " não copiado(s)" : ""));
                foreach (var f in falhas.Take(10)) Log(nome, "[pit]   não copiou " + f);
                total += n;
            }
        }
        return total;
    }

    // ---------------------------------------------------------------- status e ações

    /// <summary>Estado de todos os perfis. Sondas de porta em paralelo (porta fechada demora até o timeout).</summary>
    public async Task<List<StatusPerfil>> Status()
    {
        var cfg = Config.Ler();
        var itens = await Task.WhenAll(Config.NomesPerfis(cfg).Select(async nome =>
        {
            var p = Config.LerPerfil(cfg, nome);
            var e = Em(nome);
            StatusExecucao? exec;
            Ultimo? u;
            lock (trava)
            {
                // entre um processo e outro de uma operação (build, compilação + subida) só a reserva está lá
                exec = e != null ? new StatusExecucao { Tipo = e.Tipo, Pid = e.Child.Id, Desde = e.Inicio, Pronto = e.Pronto }
                    : reservas.GetValueOrDefault(nome) is { } rs ? new StatusExecucao { Tipo = rs.Tipo, Desde = rs.Inicio }
                    : reinicios.GetValueOrDefault(nome) is { } ri ? new StatusExecucao { Tipo = "reinicio", Desde = ri.Inicio }
                    : null;
                u = ultimos.GetValueOrDefault(nome);
            }
            if (p.EhJava)
            {
                // sem porta: "em uso" = processo do runner rodando, ou o pid gravado (pit up / outra instância)
                var r = e == null ? PidArquivo.Ler(p) : null;
                return new StatusPerfil
                {
                    Nome = nome, Tipo = "java", PortaDebug = p.PortaDebug, MainClass = p.EhZip ? null : p.MainClass,
                    Pacote = p.EhZip ? p.Pacote : null,
                    EmUso = (e != null && e.Tipo != "build") || (r != null && PidArquivo.Vivo(r.Value, "java")),
                    Execucao = exec, Ultimo = u,
                };
            }
            if (p.EhNpm)
            {
                var r = e == null ? PidArquivo.Ler(p) : null;
                return new StatusPerfil
                {
                    Nome = nome, Tipo = "npm", Porta = p.Porta, Url = p.Url, Script = p.Script,
                    EmUso = (e != null && e.Tipo != "build") || (r != null && PidArquivo.Vivo(r.Value, So.Shell))
                            || await Proc.PortaOcupada(p.Porta).ConfigureAwait(false),
                    Execucao = exec, Ultimo = u,
                };
            }
            return new StatusPerfil
            {
                Nome = nome, Tipo = "tomcat", Porta = p.Porta, PortaDebug = p.PortaDebug, Url = p.Url,
                Pacote = p.EhWar ? string.Join("; ", p.Artefatos.Select(a => a.War)) : null,
                EmUso = await Proc.PortaOcupada(p.Porta).ConfigureAwait(false),
                Execucao = exec, Ultimo = u,
                // perfil "war": o war no lugar do repo (a tela casa o cartão por ele)
                Artefatos = p.Artefatos.Select(a => new StatusArtefato(a.War ?? a.Repo, a.Modulo, a.Contexto, Tomcat.ResolverDocBase(a))).ToList(),
            };
        })).ConfigureAwait(false);
        return itens.ToList();
    }

    /// <summary>
    /// Ação de perfil, igual para a tela (POST /api/p/&lt;perfil&gt;/&lt;ação&gt;) e para a tray. Erro vai também para o log
    /// do perfil como "[pit] ERRO". start/debug de app Java e build voltam na hora e seguem em segundo plano.
    /// </summary>
    public async Task<ResultadoAcao> Acao(string nome, string acao)
    {
        try
        {
            // durante a saída um start/build novo seria morto logo em seguida
            if (Saindo && acao != "stop" && acao != "limpar") throw new ErroRunner("runner encerrando");
            switch (acao)
            {
                case "start":
                case "debug":
                    await Iniciar(nome, acao == "debug").ConfigureAwait(false);
                    break;
                case "stop":
                    await Parar(nome).ConfigureAwait(false);
                    break;
                case "reiniciar":
                    ReservarReinicio(nome);
                    break;
                case "build":
                    // validação e reserva na hora (erro = 400 na tela, balão na tray); o build segue em segundo plano
                    var reserva = await ReservarBuild(nome).ConfigureAwait(false);
                    _ = RodarBuild(nome, reserva, false).ContinueWith(t => Log(nome, "[pit] ERRO " + t.Exception!.InnerException?.Message),
                        TaskContinuationOptions.OnlyOnFaulted);
                    break;
                case "sync":
                    return new ResultadoAcao { Copiados = Sync(nome) };
                case "preparar":
                    var pp = Config.LerPerfil(Config.Ler(), nome);
                    return new ResultadoAcao { Publicado = pp.EhTomcat ? Tomcat.PrepararBase(pp) : [] };
                case "limpar":
                    Logs.Limpar(nome);
                    break;
                case "abrir":
                    var url = Config.LerPerfil(Config.Ler(), nome).Url
                              ?? throw new ErroRunner("perfil sem URL (aplicação Java abre a própria janela)");
                    Proc.AbrirUrl(url);
                    break;
                default:
                    throw new ErroRunner("ação desconhecida: " + acao);
            }
            return new ResultadoAcao();
        }
        catch (Exception ex)
        {
            Log(nome, "[pit] ERRO " + ex.Message);
            throw;
        }
    }

    // ---------------------------------------------------------------- renomear

    /// <summary>
    /// Renomeia um perfil parado: chave do perfis.json, pasta própria (base do Tomcat ou cache do app Java/npm),
    /// log e último resultado. No ar (por esta tela ou pelo pit.pid) é recusado: o processo usa a pasta do nome
    /// velho. Pasta que não dá para mover (arquivo aberto) fica onde está; a próxima subida cria a nova.
    /// </summary>
    public ResultadoAcao Renomear(string nome, string novo)
    {
        novo = novo.Trim();
        if (!Config.NomeValido(novo)) throw new ErroRunner("nome inválido: use até 64 letras/números e - . _, sem ponto no início/fim");
        if (novo == nome) return new ResultadoAcao();
        var p = Config.LerPerfil(Config.Ler(), nome);
        var res = Reservar(nome, "renomear", "pare o " + nome + " antes de renomear");
        try
        {
            if (PidArquivo.Ler(p) is { } r && PidArquivo.Vivo(r, ExePid(p)))
                throw new ErroRunner("pare o " + nome + " antes de renomear (rodando fora desta tela, pid " + r.Pid + ")");
            Config.Renomear(nome, novo);
            var q = Config.LerPerfil(Config.Ler(), novo);
            foreach (var (de, para) in new[] { (p.Base, q.Base), (p.Cache, q.Cache) }.Distinct())
            {
                if (de == "" || de == para || !Directory.Exists(de)) continue;
                if (Directory.Exists(para)) { Log(nome, "[pit] renomear: " + para + " já existe, " + de + " ficou onde estava"); continue; }
                try { Directory.Move(de, para); }
                catch (Exception ex) { Log(nome, "[pit] renomear: não movi " + de + " (" + ex.Message + ")"); }
            }
            lock (trava)
                if (ultimos.Remove(nome, out var u)) ultimos[novo] = u;
            Log(nome, "[pit] perfil renomeado: " + nome + " -> " + novo);
            Logs.Renomear(nome, novo);
            return new ResultadoAcao();
        }
        finally { Liberar(nome, res); }
    }

    // ---------------------------------------------------------------- saída do runner

    /// <summary>Marca a saída (recusa start/build novos). false = já estava saindo.</summary>
    public bool IniciarSaida()
    {
        lock (trava)
        {
            if (saindo) return false;
            saindo = true;
            return true;
        }
    }

    /// <summary>Stop gracioso de cada perfil em execução (Parar() já mata após STOP_TIMEOUT_SEG).</summary>
    public Task PararTodos() => Task.WhenAll(EmExecucao().Select(async n =>
    {
        try { await Parar(n).ConfigureAwait(false); }
        catch (Exception ex) { Log(n, "[pit] ERRO " + ex.Message); }
    }));

    /// <summary>Derruba na hora a árvore de tudo que o runner subiu (o Job Object cobre o que escapar).</summary>
    public void MatarTodos()
    {
        Execucao[] todos;
        lock (trava)
        {
            todos = estado.Values.ToArray();
            foreach (var r in reservas.Values.Concat(reinicios.Values)) r.Cancelado = true;   // o próximo passo de build/subida não começa
        }
        foreach (var e in todos) So.Encerrar(e.Child.Id, true);
    }
}

/// <summary>
/// Perfil ocupado por uma operação de vários passos (build de vários artefatos; classpath + compilação + subida do
/// app Java; checagem de portas + subida do Tomcat). Cobre o intervalo entre um processo e o próximo, quando estado[]
/// fica vazio: sem ela, Start/Build repetido passaria e o Stop não acharia o que cancelar.
/// </summary>
public sealed class Reserva
{
    public required string Tipo { get; init; }
    public long Inicio { get; } = Agora.Ms;
    /// <summary>Stop pedido: o próximo passo não começa.</summary>
    public bool Cancelado { get; set; }
}
