using System.Text;
using System.Text.Json.Nodes;

namespace Pitstop;

public sealed record RegraSync(string De, string Para);

public sealed class Artefato
{
    public bool Ativo { get; init; } = true;
    public string Repo { get; init; } = "";
    public string Modulo { get; init; } = "";
    public string Contexto { get; init; } = "";
    public string? Build { get; init; }
    public List<RegraSync> Sync { get; init; } = [];
    /// <summary>Fixo no perfil; vazio = pasta explodida mais nova em target\.</summary>
    public string? DocBase { get; init; }
    /// <summary>Perfil "war": o .war pronto (ex.: o app.war de um release) no lugar do repo/módulo.</summary>
    public string? War { get; init; }
    /// <summary>Perfil "war": pasta onde o war é extraído (cache\&lt;perfil&gt;\war\&lt;contexto&gt;-&lt;chave&gt;).</summary>
    public string Extracao { get; init; } = "";
}

/// <summary>
/// Perfil normalizado (portas padrão, base, só artefatos ativos). Tomcat, aplicação Java ou npm; "war" é um Tomcat
/// com os wars prontos no lugar dos projetos e "zip" é uma aplicação Java com o pacote pronto (.zip ou .jar).
/// </summary>
public sealed class Perfil
{
    public string Nome { get; init; } = "";
    public string Tipo { get; init; } = "tomcat";
    public bool EhTomcat => Tipo is "tomcat" or "war";
    public bool EhJava => Tipo is "java" or "zip";
    public bool EhNpm => Tipo == "npm";
    public bool EhComando => Tipo == "comando";
    public bool EhWar => Tipo == "war";
    public bool EhZip => Tipo == "zip";

    // Tomcat
    public string? Home { get; init; }
    public string? JavaHome { get; init; }
    /// <summary>Maven do build (Tomcat e app Java); vazio = mvn do PATH.</summary>
    public string? MavenHome { get; init; }
    /// <summary>CATALINA_BASE do perfil (Tomcat) ou cache\&lt;perfil&gt; (Java): o pit.pid mora aqui.</summary>
    public string Base { get; init; } = "";
    public string? ConfOrigem { get; init; }
    public int Porta { get; init; }
    public int PortaShutdown { get; init; }
    public int PortaDebug { get; init; }
    public int? PortaJmx { get; init; }
    public int? PortaAjp { get; init; }
    public string VmArgs { get; init; } = "";
    public string? Url { get; init; }
    /// <summary>Endpoint localhost opcional para confirmar aplicação pronta após a subida do Tomcat.</summary>
    public string? ProntoUrl { get; init; }
    public List<Artefato> Artefatos { get; init; } = [];
    /// <summary>Preparação Maven automática antes de iniciar projetos Tomcat (não se aplica a WAR pronto).</summary>
    public bool PrepararAoIniciar { get; init; }
    /// <summary>Monitorar alterações e sincronizar arquivos durante o desenvolvimento.</summary>
    public bool SyncAutomatico { get; init; }
    /// <summary>Publica recursos e classes diretamente via Tomcat ResourceSets (exige artefato anterior para libs).</summary>
    public bool PublicacaoDireta { get; init; }
    /// <summary>Tomcat recarrega contexto quando classes/JARs mudam; tem custo de monitoramento, padrão desligado.</summary>
    public bool ReloadAutomatico { get; init; }

    // aplicação Java (ex.: app desktop Swing/JavaFX)
    public string MainClass { get; init; } = "";
    public string Modulo { get; init; } = "";
    public string Trabalho { get; init; } = "";
    /// <summary>JDK do Maven; o app pode rodar num JRE.</summary>
    public string? JavaHomeBuild { get; init; }
    public string Args { get; init; } = "";
    public string PathExtra { get; init; } = "";
    public bool CompilarAntes { get; init; }
    public string ProntoLog { get; init; } = "";
    public string Cache { get; init; } = "";

    // pacote Java (tipo "zip"); usa também JavaHome, VmArgs, Args, PortaDebug, PathExtra, ProntoLog e Trabalho
    /// <summary>.zip (extraído em cache/&lt;perfil&gt;/pacote) ou um .jar executável (roda de onde está).</summary>
    public string Pacote { get; init; } = "";
    /// <summary>Pasta de extração do zip; vazio = pacote .jar.</summary>
    public string Extracao { get; init; } = "";
    /// <summary>Jar relativo à extração; vazio = o jar com Main-Class fora de lib\.</summary>
    public string Jar { get; init; } = "";
    /// <summary>Pasta copiada (sem sobrescrever) para <see cref="ConfDestino"/> antes de subir; vazio = nada.</summary>
    public string ConfApp { get; init; } = "";
    /// <summary>Relativo à pasta de trabalho; vazio = a própria pasta de trabalho.</summary>
    public string ConfDestino { get; init; } = "";

    // npm (ex.: frontend Angular/React/Vite); usa também Porta, Url, Args, ProntoLog, Cache e Base
    /// <summary>Pasta do package.json.</summary>
    public string Pasta { get; init; } = "";
    public string Script { get; init; } = "";
    /// <summary>Pasta do Node (a raiz da distribuição ou a bin); vazio = o do PATH.</summary>
    public string? NodeHome { get; init; }
    /// <summary>Variáveis extras, CHAVE=valor;CHAVE=valor.</summary>
    public string Env { get; init; } = "";
    /// <summary>Comando do botão Build (roda na pasta); vazio = sem build.</summary>
    public string Build { get; init; } = "";

    // comando genérico; reutiliza Pasta, Env, Porta, Url, ProntoLog, Cache e Base
    public string Comando { get; init; } = "";
    public string ShellComando { get; init; } = "auto";
    public string ShellPersonalizado { get; init; } = "";
    public string EnvArquivo { get; init; } = "";
    public bool AbrirNavegadorPronto { get; init; }
    public bool AutoIniciar { get; init; }
    public int? TimeoutExecucaoSeg { get; init; }
}

/// <summary>config/perfis.json + globais do .env (o .env manda sobre o bloco global; o perfil ainda sobrescreve).</summary>
public static class Config
{
    public static readonly string Arquivo =
        Path.GetFullPath(Path.Combine(Raiz.Dir, Env.Txt("PERFIS_ARQUIVO", "config/perfis.json")));
    public static readonly int LogMax = Env.Num("LOG_MAX_LINHAS", 5000);
    public static readonly int StopTimeout = Env.Num("STOP_TIMEOUT_SEG", 20);
    public static readonly string MavenArgs = Env.Txt("MAVEN_ARGS", "-B -ntp package -DskipTests");
    public static readonly int ReplayMax = Env.Num("LOG_REPLAY_LINHAS", 1500);

    static readonly (string Chave, string Env)[] GlobaisEnv =
        [("tomcatHome", "TOMCAT_HOME"), ("javaHome", "JDK_HOME"), ("mavenHome", "MAVEN_HOME"), ("nodeHome", "NODE_HOME"),
         ("basesDir", "BASES_DIR"), ("projetosDir", "PROJETOS_DIR")];

    /// <summary>Onde ficam os CATALINA_BASE quando o .env não diz: &lt;raiz&gt;/bases.</summary>
    public static string BasesPadrao => Path.Combine(Raiz.Dir, "bases");

    /// <summary>Primeira execução: sem perfis.json, cria um vazio (a tela mostra "crie seu primeiro perfil").</summary>
    public static void GarantirArquivo()
    {
        if (File.Exists(Arquivo)) return;
        GravarAtomico(Arquivo, "{\n  \"perfis\": {}\n}\n", backup: false);
    }

    public static JsonObject Ler()
    {
        GarantirArquivo();
        var cfg = JsonNode.Parse(File.ReadAllText(Arquivo, Encoding.UTF8)) as JsonObject
                  ?? throw new ErroRunner("config inválida: " + Arquivo);
        ValidarNomesPerfis(cfg);
        foreach (var (k, e) in GlobaisEnv)
            if (Env.Txt(e) is { } v) cfg[k] = v;
        cfg["_versao"] = Versao();
        return cfg;
    }

    /// <summary>Data de gravação do perfis.json. A tela devolve no PUT: mudou desde a leitura = gravação recusada.</summary>
    public static string Versao() => File.Exists(Arquivo) ? File.GetLastWriteTimeUtc(Arquivo).Ticks.ToString() : "0";

    /// <summary>
    /// Grava o que a tela mandou (com .bak), sem copiar para o json as chaves que são do .env. A tela manda o arquivo
    /// inteiro que leu ao abrir: se ele mudou desde então (outra aba, edição à mão), recusa em vez de apagar a mudança.
    /// Devolve a versão nova.
    /// </summary>
    public static string Gravar(JsonNode? cfg)
    {
        if (cfg is not JsonObject obj || obj["perfis"] is null) throw new ErroRunner("config inválida: falta \"perfis\"");
        ValidarNomesPerfis(obj);
        ValidarPerfisComando(obj);
        if (JsonAux.Txt(obj, "_versao") is { } lida && lida != Versao())
            throw new ErroRunner("perfis.json mudou fora desta tela (outra aba ou edição manual): recarregue a tela (F5) e refaça a alteração");
        var salvar = (JsonObject)obj.DeepClone();
        salvar.Remove("_versao");
        foreach (var (k, e) in GlobaisEnv)
            if (Env.Txt(e) != null) salvar.Remove(k);
        GravarAtomico(Arquivo, salvar.ToJsonString(JsonAux.Indentado) + "\n");
        return Versao();
    }

    static readonly System.Text.RegularExpressions.Regex RegexNome =
        new(@"^[\w.-]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    static readonly System.Text.RegularExpressions.Regex RegexNomeReservadoWindows =
        new(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Nome de perfil: vira chave do json, pasta (base/cache) e argumento do pit. Sem separadores de caminho,
    /// ponto no início/fim ou nomes de dispositivo reservados pelo Windows.
    /// </summary>
    public static bool NomeValido(string nome) =>
        nome.Length is > 0 and <= 64 &&
        nome[0] != '.' && nome[^1] != '.' &&
        RegexNome.IsMatch(nome) &&
        !RegexNomeReservadoWindows.IsMatch(nome);

    static void ValidarNomesPerfis(JsonObject cfg)
    {
        if (cfg["perfis"] is not JsonObject perfis) throw new ErroRunner("config inválida: \"perfis\" precisa ser um objeto");
        foreach (var nome in perfis.Select(kv => kv.Key))
            if (!NomeValido(nome))
                throw new ErroRunner("nome de perfil inválido: " + nome + " (use até 64 letras/números e - . _, sem ponto no início/fim)");
    }

    static void ValidarPerfisComando(JsonObject cfg)
    {
        if (cfg["perfis"] is not JsonObject perfis) return;
        foreach (var (nome, node) in perfis)
            if (node is JsonObject p && JsonAux.Txt(p, "tipo") == "comando")
                ComandoApp.Validar(PerfilComando(cfg, nome, p));
    }

    /// <summary>
    /// Troca a chave do perfil no perfis.json (com .bak), mantendo a posição na lista. Lê o arquivo cru, sem os
    /// globais do .env. Devolve a versão nova.
    /// </summary>
    public static string Renomear(string velho, string novo)
    {
        if (!NomeValido(novo)) throw new ErroRunner("nome de perfil inválido: " + novo);
        var raiz = JsonNode.Parse(File.ReadAllText(Arquivo, Encoding.UTF8)) as JsonObject
                   ?? throw new ErroRunner("config inválida: " + Arquivo);
        if (raiz["perfis"] is not JsonObject perfis || !perfis.ContainsKey(velho)) throw new ErroRunner("perfil não existe: " + velho);
        if (perfis.ContainsKey(novo)) throw new ErroRunner("já existe um perfil " + novo);
        var novos = new JsonObject();
        foreach (var (k, v) in perfis.ToList())
        {
            perfis.Remove(k);
            novos[k == velho ? novo : k] = v;
        }
        raiz["perfis"] = novos;
        GravarAtomico(Arquivo, raiz.ToJsonString(JsonAux.Indentado) + "\n");
        return Versao();
    }

    internal static void GravarAtomico(string arquivo, string conteudo, bool backup = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        var temporario = arquivo + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporario, conteudo, new UTF8Encoding(false));
            if (File.Exists(arquivo))
            {
                if (backup) File.Copy(arquivo, arquivo + ".bak", true);
                File.Move(temporario, arquivo, true);
            }
            else File.Move(temporario, arquivo);
        }
        finally { try { if (File.Exists(temporario)) File.Delete(temporario); } catch { } }
    }

    public static IEnumerable<string> NomesPerfis(JsonObject cfg) =>
        (cfg["perfis"] as JsonObject)?.Select(kv => kv.Key) ?? [];

    public static Perfil LerPerfil(JsonObject cfg, string nome)
    {
        if ((cfg["perfis"] as JsonObject)?[nome] is not JsonObject p) throw new ErroRunner("perfil não existe: " + nome);
        var tipo = JsonAux.Txt(p, "tipo");
        switch (tipo)
        {
            case "java": return PerfilJava(cfg, nome, p);
            case "zip": return PerfilZip(cfg, nome, p);
            case "npm": return PerfilNpm(cfg, nome, p);
            case "comando": return PerfilComando(cfg, nome, p);
        }
        var war = tipo == "war";
        var cache = Path.Combine(Raiz.Dir, "cache", nome);
        var porta = JsonAux.Num(p, "porta") ?? 8080;
        var home = JsonAux.Txt(p, "tomcatHome") ?? JsonAux.Txt(cfg, "tomcatHome");
        var todos = (p["artefatos"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Select(a => war ? LerWar(a, cache) : LerArtefato(a)).ToList();
        return new Perfil
        {
            Nome = nome,
            Tipo = war ? "war" : "tomcat",
            Cache = cache,
            Home = home,
            JavaHome = JsonAux.Txt(p, "javaHome") ?? JsonAux.Txt(cfg, "javaHome") ?? Environment.GetEnvironmentVariable("JAVA_HOME"),
            MavenHome = JsonAux.Txt(p, "mavenHome") ?? JsonAux.Txt(cfg, "mavenHome"),
            Base = Path.Combine(JsonAux.Txt(cfg, "basesDir") ?? BasesPadrao, nome),
            ConfOrigem = JsonAux.Txt(p, "confOrigem") ?? (home != null ? Path.Combine(home, "conf") : null),
            Porta = porta,
            PortaShutdown = JsonAux.Num(p, "portaShutdown") ?? porta - 75,
            PortaDebug = JsonAux.Num(p, "portaDebug") ?? 5005,
            PortaJmx = JsonAux.Num(p, "portaJmx"),
            PortaAjp = JsonAux.Num(p, "portaAjp"),
            VmArgs = JsonAux.Txt(p, "vmArgs") ?? "",
            Url = JsonAux.Txt(p, "url") ?? "http://localhost:" + porta + UrlContexto(todos),
            ProntoUrl = JsonAux.Txt(p, "prontoUrl"),
            Artefatos = todos.Where(a => a.Ativo).ToList(),
            PrepararAoIniciar = p["prepararAoIniciar"]?.GetValue<bool>() == true,
            SyncAutomatico = p["syncAutomatico"]?.GetValue<bool>() == true,
            PublicacaoDireta = p["publicacaoDireta"]?.GetValue<bool>() == true,
            ReloadAutomatico = p["reloadAutomatico"]?.GetValue<bool>() == true,
        };
    }

    static Artefato LerArtefato(JsonObject a) => new()
    {
        Ativo = JsonAux.NaoFalso(a, "ativo"),
        Repo = JsonAux.Txt(a, "repo") ?? "",
        Modulo = JsonAux.Txt(a, "modulo") ?? "",
        Contexto = JsonAux.Txt(a, "contexto") ?? "",
        Build = JsonAux.Txt(a, "build"),
        DocBase = JsonAux.Txt(a, "docBase"),
        // perfis gravados no Windows usam \ (target\classes): / vale nos dois sistemas
        Sync = (a["sync"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Select(s => new RegraSync(Barra(JsonAux.Txt(s, "de")), Barra(JsonAux.Txt(s, "para")))).ToList(),
    };

    /// <summary>Caminho relativo do perfil com / (o Windows aceita; no Linux \ seria parte do nome).</summary>
    public static string Barra(string? rel) => (rel ?? "").Replace('\\', '/');

    /// <summary>
    /// Artefato do perfil "war": o .war e o contexto (vazio = nome do arquivo: app.war -> /app). A extração
    /// fica no cache do perfil, uma pasta por contexto.
    /// </summary>
    static Artefato LerWar(JsonObject a, string cache)
    {
        var war = JsonAux.Txt(a, "war") is { } w ? Path.GetFullPath(w, Raiz.Dir) : "";
        var ctx = JsonAux.Txt(a, "contexto") ?? (war != "" ? "/" + Path.GetFileNameWithoutExtension(war) : "/");
        var c = ctx.Trim('/');
        return new Artefato
        {
            Ativo = JsonAux.NaoFalso(a, "ativo"),
            Contexto = ctx,
            War = war,
            Extracao = Pacote.Destino(Path.Combine(cache, "war"), c == "" ? "ROOT" : c.Replace('/', '#'), war),
        };
    }

    /// <summary>URL padrão: contexto do primeiro artefato marcado.</summary>
    static string UrlContexto(List<Artefato> artefatos)
    {
        var c = artefatos.FirstOrDefault(x => x.Ativo)?.Contexto.Trim('/') ?? "";
        return c != "" ? "/" + c + "/" : "/";
    }

    /// <summary>
    /// Perfil "aplicação Java": java direto, sem Tomcat. Pasta de trabalho padrão = pasta do módulo
    /// (o que o IntelliJ usa quando a Run Configuration não define WORKING_DIRECTORY).
    /// </summary>
    static Perfil PerfilJava(JsonObject cfg, string nome, JsonObject p)
    {
        // relativo = à raiz do runner, não à pasta atual do processo (a tray do autostart sobe em qualquer uma)
        var modulo = JsonAux.Txt(p, "modulo") is { } m ? Path.GetFullPath(m, Raiz.Dir) : "";
        var cache = Path.Combine(Raiz.Dir, "cache", nome);
        var javaPadrao = JsonAux.Txt(cfg, "javaHome") ?? Environment.GetEnvironmentVariable("JAVA_HOME");
        var trabalho = JsonAux.Txt(p, "trabalho");
        return new Perfil
        {
            Nome = nome,
            Tipo = "java",
            MainClass = JsonAux.Txt(p, "mainClass") ?? "",
            Modulo = modulo,
            Trabalho = trabalho != null ? Path.GetFullPath(Path.Combine(modulo, Barra(trabalho))) : modulo,
            JavaHome = JsonAux.Txt(p, "javaHome") ?? javaPadrao,
            JavaHomeBuild = JsonAux.Txt(p, "javaHomeBuild") ?? javaPadrao,
            MavenHome = JsonAux.Txt(p, "mavenHome") ?? JsonAux.Txt(cfg, "mavenHome"),
            VmArgs = JsonAux.Txt(p, "vmArgs") ?? "",
            Args = JsonAux.Txt(p, "args") ?? "",
            PathExtra = JsonAux.Txt(p, "pathExtra") ?? "",
            PortaDebug = JsonAux.Num(p, "portaDebug") ?? 5006,
            CompilarAntes = JsonAux.NaoFalso(p, "compilarAntes"),
            ProntoLog = JsonAux.Txt(p, "prontoLog") ?? "",
            Cache = cache,
            Base = cache,
        };
    }

    /// <summary>
    /// Perfil "zip": pacote pronto (.zip de um release ou um .jar executável), java -jar sem Maven. O zip é extraído
    /// em cache/&lt;perfil&gt;/pacote; trabalho vazio = pasta do jar; conf (opcional) é copiada sem sobrescrever para
    /// confDestino (relativo ao trabalho; vazio = a própria pasta de trabalho).
    /// </summary>
    static Perfil PerfilZip(JsonObject cfg, string nome, JsonObject p)
    {
        var pacote = JsonAux.Txt(p, "pacote") is { } f ? Path.GetFullPath(f, Raiz.Dir) : "";
        var cache = Path.Combine(Raiz.Dir, "cache", nome);
        var ehJar = pacote.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
        return new Perfil
        {
            Nome = nome,
            Tipo = "zip",
            Pacote = pacote,
            Extracao = ehJar ? "" : Pitstop.Pacote.Destino(Path.Combine(cache, "pacote"), "pacote", pacote),
            Jar = Barra(JsonAux.Txt(p, "jar")),
            Trabalho = Barra(JsonAux.Txt(p, "trabalho")),
            ConfApp = JsonAux.Txt(p, "conf") is { } c ? Path.GetFullPath(c, Raiz.Dir) : "",
            ConfDestino = JsonAux.Txt(p, "confDestino") is { } cd ? Barra(cd) : ".",
            JavaHome = JsonAux.Txt(p, "javaHome") ?? JsonAux.Txt(cfg, "javaHome") ?? Environment.GetEnvironmentVariable("JAVA_HOME"),
            VmArgs = JsonAux.Txt(p, "vmArgs") ?? "",
            Args = JsonAux.Txt(p, "args") ?? "",
            PathExtra = JsonAux.Txt(p, "pathExtra") ?? "",
            PortaDebug = JsonAux.Num(p, "portaDebug") ?? 5006,
            ProntoLog = JsonAux.Txt(p, "prontoLog") ?? "",
            Cache = cache,
            Base = cache,
        };
    }

    /// <summary>
    /// Pronto do dev server: "Compiled successfully" (webpack/Angular CLI), "compiled with warnings", "ready in"
    /// (Vite) ou "Local: http" (Vite/Next). Falha de compilação não marca pronto, mas o servidor segue no ar.
    /// </summary>
    const string ProntoNpm = @"(?i)compiled successfully|compiled with warnings|ready in \d|Local:\s+https?://";

    static bool Sim(JsonObject p, string chave) =>
        p.TryGetPropertyValue(chave, out var v) && v is JsonValue jv && jv.TryGetValue<bool>(out var b) && b;

    /// <summary>Perfil "comando": usa a pasta do perfil, depois a pasta global de projetos e por fim o HOME.</summary>
    static Perfil PerfilComando(JsonObject cfg, string nome, JsonObject p)
    {
        var pastaPerfil = JsonAux.Txt(p, "pasta");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            home = Environment.GetEnvironmentVariable(OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME") ?? Raiz.Dir;
        string pasta;
        if (!string.IsNullOrWhiteSpace(pastaPerfil))
        {
            pasta = Path.GetFullPath(pastaPerfil, Raiz.Dir);
        }
        else
        {
            var global = JsonAux.Txt(cfg, "projetosDir");
            var pastaGlobal = !string.IsNullOrWhiteSpace(global) ? Path.GetFullPath(global, Raiz.Dir) : null;
            pasta = pastaGlobal != null && Directory.Exists(pastaGlobal) ? pastaGlobal : Path.GetFullPath(home);
        }
        var cache = Path.Combine(Raiz.Dir, "cache", nome);
        var envArq = JsonAux.Txt(p, "envArquivo");
        return new Perfil
        {
            Nome = nome,
            Tipo = "comando",
            Pasta = pasta,
            Comando = JsonAux.Txt(p, "comando") ?? "",
            ShellComando = JsonAux.Txt(p, "shell") ?? "auto",
            ShellPersonalizado = JsonAux.Txt(p, "shellPersonalizado") ?? "",
            Env = JsonAux.Txt(p, "env") ?? "",
            EnvArquivo = envArq != null ? Path.GetFullPath(envArq, Raiz.Dir) : "",
            Porta = JsonAux.Num(p, "porta") ?? 0,
            Url = JsonAux.Txt(p, "url"),
            ProntoLog = JsonAux.Txt(p, "prontoLog") ?? "",
            AbrirNavegadorPronto = Sim(p, "abrirNavegadorPronto"),
            AutoIniciar = Sim(p, "autoIniciar"),
            TimeoutExecucaoSeg = JsonAux.Num(p, "timeoutExecucaoSeg"),
            Cache = cache,
            Base = cache,
        };
    }

    /// <summary>Perfil "npm": npm run &lt;script&gt; na pasta do package.json. prontoLog vazio = padrão dos dev servers.</summary>
    static Perfil PerfilNpm(JsonObject cfg, string nome, JsonObject p)
    {
        var pasta = JsonAux.Txt(p, "pasta") is { } d ? Path.GetFullPath(d, Raiz.Dir) : "";
        var cache = Path.Combine(Raiz.Dir, "cache", nome);
        var porta = JsonAux.Num(p, "porta") ?? 4200;
        return new Perfil
        {
            Nome = nome,
            Tipo = "npm",
            Pasta = pasta,
            Script = JsonAux.Txt(p, "script") ?? "start",
            Args = JsonAux.Txt(p, "args") ?? "",
            NodeHome = JsonAux.Txt(p, "nodeHome") ?? JsonAux.Txt(cfg, "nodeHome"),
            Env = JsonAux.Txt(p, "env") ?? "",
            Build = JsonAux.Txt(p, "build") ?? "",
            Porta = porta,
            Url = JsonAux.Txt(p, "url") ?? "http://localhost:" + porta + "/",
            ProntoLog = JsonAux.Txt(p, "prontoLog") ?? ProntoNpm,
            Cache = cache,
            Base = cache,
        };
    }
}
