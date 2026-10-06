using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Pitstop.App;

/// <summary>Artefato (war exploded) de um projeto marcado, no perfil Tomcat.</summary>
sealed class CartaoArt
{
    public string Repo = "", Modulo = "";
    public CheckBox Ativo = null!;
    public TextBox Ctx = null!, Build = null!, Sync = null!, DocBase = null!;
    public TextBlock Doc = null!;
    public Border Raiz = null!;
}

/// <summary>War pronto do perfil "war": arquivo + contexto.</summary>
sealed class CartaoWar
{
    public CheckBox Ativo = null!;
    public TextBox War = null!, Ctx = null!;
    public TextBlock Nome = null!, Doc = null!;
    public Border Raiz = null!;
    /// <summary>"v1.2.0 · 165 MB · gerado 29/09 15:44" (vazio enquanto carrega ou sem arquivo).</summary>
    public string Versao = "";
}

sealed partial class Janela
{
    static readonly HashSet<string> Numericos = ["porta", "portaDebug", "portaShutdown", "portaJmx", "portaAjp", "timeoutExecucaoSeg"];

    readonly Dictionary<string, TextBox> fServ = new(), fApp = new(), fNpm = new(), fZip = new(), fComando = new();
    TextBox projetosDir = null!;
    CheckBox compilarAntes = null!, mostrarSemWar = null!, comandoAbrirPronto = null!, comandoAutoIniciar = null!;
    readonly TextBlock mainsInfo = Ui.Txt("", 12, "Mut"), scriptsInfo = Ui.Txt("", 12, "Mut"), versoesZipInfo = Ui.Txt("", 12, "Mut");
    readonly TextBlock npmLinha = Ui.Txt("", 12, "Mut", mono: true), zipInfo = Ui.Txt("", 12, "Mut", mono: true), projInfo = Ui.Txt("", 13, "Mut");
    readonly WrapPanel projsBox = new();
    readonly StackPanel artsBox = new(), warsBox = new();

    List<ProjetoDescoberto> descobertos = [];
    string? descobertosDe;          // pasta que gerou "descobertos" (null = ainda não procurou)
    bool descobertaOk = true;       // false = pasta inválida: salvar mantém projetos/artefatos já gravados
    HashSet<string> selProj = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, JsonObject> artsPerfil = new();
    readonly List<CartaoArt> cartoesArt = [];
    readonly List<CartaoWar> cartoesWar = [];
    string? scriptsDe;
    List<ScriptNpm> scripts = [];

    static string Chave(string repo, string? modulo) => (repo + "|" + (modulo ?? "")).ToLowerInvariant();

    TextBox F(Dictionary<string, TextBox> d, string chave, string exemplo = "", double altura = 42)
    {
        var t = Ui.Inp(exemplo, altura);
        t.TextChanged += (_, _) => MarcarSujo();
        d[chave] = t;
        return t;
    }

    /// <summary>Campo de pasta: texto + 📁.</summary>
    static Control Pasta(TextBox t, string titulo) => Ui.ComBotoes(t, Ui.Procurar(t, titulo));

    static TextBlock Doc() => Ui.Txt("", 12, "Mut", mono: true).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; });

    static TextBlock Vazio(string texto) => Ui.Txt(texto, 14, "Mut").Also(t => t.Margin = new Thickness(0, 8));

    // ---------------------------------------------------------------- montagem

    void MontarPaineis()
    {
        RegistrarAba("log", "Log", MontarLog());
        RegistrarAba("artefatos", "Artefatos", PainelArtefatos());
        RegistrarAba("wars", "Pacotes", PainelWars());
        RegistrarAba("servidor", "Servidor", PainelServidor());
        RegistrarAba("app", "Aplicação", PainelApp());
        RegistrarAba("zip", "Pacote", PainelZip());
        RegistrarAba("npm", "Projeto", PainelNpm());
        RegistrarAba("comando", "Comando", PainelComando());
    }

    Control PainelArtefatos()
    {
        mostrarSemWar = Ui.Chk("mostrar sem war");
        mostrarSemWar.Res(CheckBox.ForegroundProperty, "Mut");
        mostrarSemWar.IsCheckedChanged += (_, _) => PintarProjetos();
        var procurar = Ui.Btn("Procurar de novo", "link", "sync", "AccTxt").Dica("Procurar de novo na pasta de projetos");
        procurar.Click += (_, _) => Redescobrir(true, "projetos atualizados");
        var direita = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { mostrarSemWar, procurar } };

        projetosDir = Ui.Inp("vazio = pasta de projetos de Ajustes");
        projetosDir.TextChanged += (_, _) => MarcarSujo();
        projetosDir.LostFocus += (_, _) => { if ((projetosDir.Text ?? "").Trim() != descobertosDe) Redescobrir(true); };
        projetosDir.PropertyChanged += (_, e) =>
        {
            // escolhido pelo 📁: o campo não perde o foco, então procura aqui
            if (e.Property == TextBox.TextProperty && !projetosDir.IsFocused && !preenchendo && (projetosDir.Text ?? "").Trim() != descobertosDe) Redescobrir(true);
        };
        return Rolavel(
            Ui.Bloco("Projetos", null, direita, projInfo, Ui.Campo("Pasta de projetos deste perfil", Pasta(projetosDir, "Pasta de projetos")), projsBox),
            Ui.Bloco("Artefatos", "war exploded dos projetos marcados", null, artsBox));
    }

    Control PainelWars()
    {
        var add = Ui.Btn("Adicionar war", "link", "novo", "AccTxt");
        add.Click += async (_, _) =>
        {
            var l = await Task.Run(() => Pacote.Versoes("", ".war"));   // sugere o mais novo da pasta de releases
            if (cartoesWar.Count == 0) warsBox.Children.Clear();
            NovoCartaoWar(true, l.Count > 0 ? l[0].Caminho : "", "");
            MarcarSujo();
        };
        return Rolavel(Ui.Bloco("Wars", "extraídos no cache do perfil na primeira subida; o Build extrai de novo", add, warsBox));
    }

    Control PainelServidor()
    {
        var home = F(fServ, "tomcatHome");
        var java = F(fServ, "javaHome");
        var maven = F(fServ, "mavenHome");
        var conf = F(fServ, "confOrigem");
        return Rolavel(
            Ui.Bloco("Instalação", null, null,
                Ui.Grade(2,
                    Ui.Campo("Tomcat (CATALINA_HOME)", Ui.ComBotoes(home,
                        Ui.Opcoes(home, async () => (await Task.Run(() => Tomcat.Instalados())).Select(t => (t, "")).ToList()),
                        Ui.Procurar(home, "Pasta do Tomcat"))),
                    Ui.Campo("JDK (JAVA_HOME)", Pasta(java, "Pasta do JDK"))),
                Ui.Grade(2,
                    Ui.Campo("Maven do build (vazio = padrão ou mvn do PATH)", Pasta(maven, "Pasta do Maven")),
                    Ui.Campo("conf de origem (vazio = <Tomcat>/conf)", Pasta(conf, "Pasta conf de origem")))),
            Ui.Bloco("Portas", null, null,
                Ui.Grade(5,
                    Ui.Campo("HTTP", F(fServ, "porta")),
                    Ui.Campo("Debug (JPDA)", F(fServ, "portaDebug")),
                    Ui.Campo("Shutdown", F(fServ, "portaShutdown", "HTTP − 75")),
                    Ui.Campo("JMX", F(fServ, "portaJmx", "desligado")),
                    Ui.Campo("AJP", F(fServ, "portaAjp", "desligado")))),
            Ui.Bloco("JVM", null, null,
                Ui.Campo("VM args (CATALINA_OPTS)", F(fServ, "vmArgs", "ex.: -Xmx1g -Dspring.profiles.active=dev")),
                Ui.Campo("URL ao abrir", F(fServ, "url"))));
    }

    Control PainelApp()
    {
        var main = F(fApp, "mainClass", "ex.: com.exemplo.App — ▾ lista as classes com main do módulo");
        var modulo = F(fApp, "modulo", "pasta com o pom.xml");
        modulo.LostFocus += (_, _) => AtualizarMains();
        compilarAntes = Ui.Chk("Compilar antes de subir (só os módulos com fonte mais novo que as classes)");
        compilarAntes.IsCheckedChanged += (_, _) => MarcarSujo();
        return Rolavel(
            Ui.Bloco("Aplicação Java", "java direto, classpath do Maven com os módulos do repo em target/classes", null,
                Ui.Campo("Classe main", Ui.ComBotoes(main, Ui.Opcoes(main, async () =>
                {
                    var mod = (modulo.Text ?? "").Trim();
                    if (mod == "" || !File.Exists(Path.Combine(mod, "pom.xml"))) throw new ErroRunner("módulo sem pom.xml: " + mod);
                    return (await Task.Run(() => JavaApp.ClassesMain(mod))).Select(c => (c.Classe, c.Teste ? "teste" : "")).ToList();
                })), mainsInfo),
                Ui.Grade(2,
                    Ui.Campo("Módulo Maven (pasta com o pom.xml)", Pasta(modulo, "Módulo Maven")),
                    Ui.Campo("Pasta de trabalho (vazio = pasta do módulo)", Pasta(F(fApp, "trabalho"), "Pasta de trabalho")),
                    Ui.Campo("Java para rodar (JAVA_HOME)", Pasta(F(fApp, "javaHome"), "Java para rodar")),
                    Ui.Campo("JDK para compilar (Maven)", Pasta(F(fApp, "javaHomeBuild"), "JDK para compilar")),
                    Ui.Campo("Maven (vazio = padrão ou mvn do PATH)", Pasta(F(fApp, "mavenHome"), "Pasta do Maven")))),
            Ui.Bloco("Execução", null, null,
                Ui.Campo("VM args", F(fApp, "vmArgs", "ex.: -Xms256m -Xmx512m")),
                Ui.Campo("Argumentos do programa", F(fApp, "args")),
                Ui.Grade(2,
                    Ui.Campo("Debug (JDWP)", F(fApp, "portaDebug", "5006")),
                    Ui.Campo("Pastas extras no PATH (bibliotecas nativas; relativas ao módulo; separar por ;)", F(fApp, "pathExtra"))),
                Ui.Campo("Pronto quando o log tiver (regex; vazio = 3 s depois de subir)", F(fApp, "prontoLog")),
                compilarAntes));
    }

    Control PainelZip()
    {
        var pacote = F(fZip, "pacote", "arquivo .zip ou .jar — ▾ lista as versões");
        pacote.TextChanged += (_, _) => AtualizarZipInfo();
        return Rolavel(
            Ui.Bloco("Pacote", "java -jar do pacote pronto, sem Maven; o zip é extraído no cache do perfil", null,
                Ui.Campo("Pacote (.zip ou .jar)", Ui.ComBotoes(pacote,
                    Ui.Opcoes(pacote, () => OpcoesVersao(pacote.Text ?? "", ".zip,.jar")),
                    Ui.Procurar(pacote, "Pacote", false, ".zip", ".jar")), versoesZipInfo),
                zipInfo,
                Ui.Grade(2,
                    Ui.Campo("Jar (relativo à extração; vazio = o que tem Main-Class fora de lib)", F(fZip, "jar", "ex.: app/bin/app.jar")),
                    Ui.Campo("Pasta de trabalho (relativa à extração; vazio = pasta do jar)", F(fZip, "trabalho", "ex.: app/bin")),
                    Ui.Campo("conf a copiar antes de subir (não sobrescreve; vazio = nada)", Pasta(F(fZip, "conf"), "Pasta de configuração")),
                    Ui.Campo("Destino da conf (relativo à pasta de trabalho)", F(fZip, "confDestino", "vazio = a própria pasta de trabalho")))),
            Ui.Bloco("Execução", null, null,
                Ui.Campo("Java para rodar (JAVA_HOME)", Pasta(F(fZip, "javaHome"), "Java para rodar")),
                Ui.Campo("VM args", F(fZip, "vmArgs", "ex.: -Xms256m -Xmx512m")),
                Ui.Campo("Argumentos do programa", F(fZip, "args")),
                Ui.Grade(2,
                    Ui.Campo("Debug (JDWP)", F(fZip, "portaDebug", "5006")),
                    Ui.Campo("Pastas extras no PATH (relativas à pasta de trabalho; separar por ;)", F(fZip, "pathExtra"))),
                Ui.Campo("Pronto quando o log tiver (regex; vazio = 3 s depois de subir)", F(fZip, "prontoLog"))));
    }

    Control PainelNpm()
    {
        var pasta = F(fNpm, "pasta", "pasta com o package.json");
        pasta.LostFocus += (_, _) => AtualizarNpm();
        pasta.TextChanged += (_, _) => { if (!pasta.IsFocused) AtualizarNpm(); };
        var script = F(fNpm, "script", "start");
        script.TextChanged += (_, _) => PintarLinhaNpm();
        var args = F(fNpm, "args", "ex.: --port 4201");
        args.TextChanged += (_, _) => PintarLinhaNpm();
        npmLinha.TextWrapping = TextWrapping.Wrap;
        npmLinha.TextTrimming = TextTrimming.None;
        return Rolavel(
            Ui.Bloco("Projeto npm", "npm run <script> na pasta do package.json", null,
                Ui.Campo("Pasta do package.json", Pasta(pasta, "Pasta do projeto")),
                Ui.Grade(2,
                    Ui.Campo("Script", Ui.ComBotoes(script, Ui.Opcoes(script, async () =>
                    {
                        var p = (pasta.Text ?? "").Trim();
                        return (await Task.Run(() => NodeApp.Scripts(p))).Select(s => (s.Nome, s.Comando)).ToList();
                    })), scriptsInfo),
                    Ui.Campo("Argumentos (vão depois do --, para o programa do script)", args)),
                npmLinha),
            Ui.Bloco("Servidor", null, null,
                Ui.Grade(2,
                    Ui.Campo("Porta HTTP (a que o script abre)", F(fNpm, "porta", "4200")),
                    Ui.Campo("URL ao abrir", F(fNpm, "url"))),
                Ui.Campo("Pronto quando o log tiver (regex)", F(fNpm, "prontoLog", "vazio = Compiled successfully (Angular/webpack), ready in (Vite)"))),
            Ui.Bloco("Node e build", null, null,
                Ui.Grade(2,
                    Ui.Campo("Pasta do Node (vazio = node do PATH)", Pasta(F(fNpm, "nodeHome", "vazio = Node do PATH"), "Pasta do Node.js")),
                    Ui.Campo("Build (roda na pasta do projeto)", F(fNpm, "build", "npm run build"))),
                Ui.Campo("Variáveis de ambiente (CHAVE=valor; separar por ;)", F(fNpm, "env", "ex.: NODE_OPTIONS=--max-old-space-size=4096"))));
    }

    Control PainelComando()
    {
        var comando = F(fComando, "comando", "um comando por linha; para no primeiro erro", 170);
        comando.AcceptsReturn = true;
        comando.TextWrapping = TextWrapping.Wrap;

        var env = F(fComando, "env", "CHAVE=valor; uma por linha ou separadas por ;", 90);
        env.AcceptsReturn = true;
        env.TextWrapping = TextWrapping.Wrap;

        comandoAbrirPronto = Ui.Chk("Abrir a URL quando o processo ficar pronto");
        comandoAbrirPronto.IsCheckedChanged += (_, _) => MarcarSujo();
        comandoAutoIniciar = Ui.Chk("Iniciar automaticamente quando o Pitstop abrir");
        comandoAutoIniciar.IsCheckedChanged += (_, _) => MarcarSujo();

        return Rolavel(
            Ui.Bloco("Comando", "executa as linhas em sequência na mesma sessão e para no primeiro erro", null,
                Ui.Campo("Comando(s)", comando),
                Ui.Campo("Pasta de execução", Pasta(F(fComando, "pasta", "vazio = pasta de projetos de Ajustes; se não houver, HOME"), "Pasta de execução"))),
            Ui.Bloco("Opções avançadas", null, null,
                Ui.Grade(2,
                    Ui.Campo("Shell", F(fComando, "shell", "auto | cmd | powershell | sh | bash | custom")),
                    Ui.Campo("Interpretador personalizado (quando shell = custom)", F(fComando, "shellPersonalizado")),
                    Ui.Campo("Arquivo .env", Ui.ComBotoes(F(fComando, "envArquivo"), Ui.Procurar(fComando["envArquivo"], "Arquivo .env", false))),
                    Ui.Campo("Porta opcional", F(fComando, "porta", "vazio = não observar porta")),
                    Ui.Campo("URL opcional", F(fComando, "url", "https:// ou http://")),
                    Ui.Campo("Pronto quando o log contiver", F(fComando, "prontoLog", "vazio = pronto ao iniciar ou pela porta")),
                    Ui.Campo("Tempo limite em segundos", F(fComando, "timeoutExecucaoSeg", "vazio = sem limite"))),
                Ui.Campo("Variáveis de ambiente extras", env),
                comandoAbrirPronto,
                comandoAutoIniciar));
    }

    // ---------------------------------------------------------------- preencher (perfis.json -> tela)

    static void Encher(Dictionary<string, TextBox> d, JsonObject p)
    {
        foreach (var (k, t) in d) t.Text = JsonAux.Txt(p, k) ?? "";
    }

    string PadraoJava => "vazio = " + (JsonAux.Txt(cfg, "javaHome") ?? "JAVA_HOME do sistema");
    string PadraoMaven => "vazio = " + (JsonAux.Txt(cfg, "mavenHome") ?? "mvn do PATH");

    void Preencher()
    {
        var p = PerfilCfg(atual);
        if (p == null) return;
        var tipo = TipoAtual;
        preenchendo = true;
        try
        {
            PintarAbas();
            EscolherAba(prefs.Aba ?? "log");
            switch (tipo)
            {
                case "zip":
                    Encher(fZip, p);
                    fZip["javaHome"].Watermark = PadraoJava;
                    AtualizarZipInfo();
                    break;
                case "war":
                    Encher(fServ, p);
                    PlaceholdersServidor();
                    PintarWars(p["artefatos"] as JsonArray);
                    break;
                case "npm":
                    Encher(fNpm, p);
                    fNpm["url"].Watermark = "http://localhost:" + (JsonAux.Num(p, "porta") ?? 4200) + "/";
                    scriptsDe = null;
                    AtualizarNpm();
                    break;
                case "java":
                    Encher(fApp, p);
                    compilarAntes.IsChecked = JsonAux.NaoFalso(p, "compilarAntes");
                    fApp["javaHome"].Watermark = PadraoJava;
                    fApp["javaHomeBuild"].Watermark = PadraoJava;
                    fApp["mavenHome"].Watermark = PadraoMaven;
                    fApp["trabalho"].Watermark = "vazio = " + (JsonAux.Txt(p, "modulo") ?? "pasta do módulo");
                    AtualizarMains();
                    break;
                case "comando":
                    Encher(fComando, p);
                    fComando["pasta"].Watermark = "vazio = " + (JsonAux.Txt(cfg, "projetosDir") ?? "HOME do usuário");
                    comandoAbrirPronto.IsChecked = p["abrirNavegadorPronto"]?.GetValue<bool>() == true;
                    comandoAutoIniciar.IsChecked = p["autoIniciar"]?.GetValue<bool>() == true;
                    break;
                default:
                    Encher(fServ, p);
                    PlaceholdersServidor();
                    projetosDir.Text = JsonAux.Txt(p, "projetosDir") ?? "";
                    projetosDir.Watermark = "vazio = " + (JsonAux.Txt(cfg, "projetosDir") ?? "defina em Ajustes");
                    DepoisDeSalvar(p);
                    // perfil antigo sem "projetos": deduz pelos artefatos
                    var repos = p["projetos"] is JsonArray pj
                        ? pj.Select(x => x?.GetValue<string>() ?? "")
                        : (p["artefatos"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(a => JsonAux.Txt(a, "repo") ?? "");
                    selProj = new HashSet<string>(repos.Where(r => r != ""), StringComparer.OrdinalIgnoreCase);
                    // outro perfil pode apontar outra pasta de projetos: procura de novo antes de pintar
                    if ((projetosDir.Text ?? "").Trim() != descobertosDe) Redescobrir(false);
                    else { PintarProjetos(); PintarArtefatos(); }
                    break;
            }
        }
        finally { preenchendo = false; }
        PintarCabecalho();
    }

    void PlaceholdersServidor()
    {
        fServ["tomcatHome"].Watermark = "vazio = " + (JsonAux.Txt(cfg, "tomcatHome") ?? "defina aqui ou em Ajustes");
        fServ["javaHome"].Watermark = PadraoJava;
        fServ["mavenHome"].Watermark = PadraoMaven;
        fServ["url"].Watermark = StatusAtual?.Url ?? "";
    }

    /// <summary>Artefatos gravados do perfil Tomcat, por repo|módulo (os cartões usam o gravado sobre o descoberto).</summary>
    void DepoisDeSalvar(JsonObject p)
    {
        if (TipoDe(p) != "tomcat") return;
        artsPerfil = (p["artefatos"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .GroupBy(a => Chave(JsonAux.Txt(a, "repo") ?? "", JsonAux.Txt(a, "modulo")))
            .ToDictionary(g => g.Key, g => g.Last());
    }

    // ---------------------------------------------------------------- coletar (tela -> perfis.json) e validar

    /// <summary>
    /// Perfil a partir da tela. Parte do que está gravado: chave que a tela não conhece continua no json. Campo vazio
    /// remove a chave (volta ao padrão do runner).
    /// </summary>
    JsonObject Coletar()
    {
        var tipo = TipoAtual;
        var p = PerfilCfg(atual)?.DeepClone() as JsonObject ?? new JsonObject();
        void Campos(Dictionary<string, TextBox> d)
        {
            foreach (var (k, t) in d)
            {
                var v = (t.Text ?? "").Trim();
                if (v == "") p.Remove(k);
                else p[k] = Numericos.Contains(k) && int.TryParse(v, out var n) ? n : v;
            }
        }
        switch (tipo)
        {
            case "zip":
                p["tipo"] = "zip";
                Campos(fZip);
                break;
            case "war":
                p["tipo"] = "war";
                Campos(fServ);
                var ws = new JsonArray();
                foreach (var c in cartoesWar)
                {
                    var a = new JsonObject { ["ativo"] = c.Ativo.IsChecked == true, ["war"] = (c.War.Text ?? "").Trim() };
                    if ((c.Ctx.Text ?? "").Trim() is { Length: > 0 } ctx) a["contexto"] = ctx;
                    ws.Add(a);
                }
                p["artefatos"] = ws;
                break;
            case "npm":
                p["tipo"] = "npm";
                Campos(fNpm);
                break;
            case "java":
                p["tipo"] = "java";
                Campos(fApp);
                p["compilarAntes"] = compilarAntes.IsChecked == true;
                break;
            case "comando":
                p["tipo"] = "comando";
                Campos(fComando);
                p["abrirNavegadorPronto"] = comandoAbrirPronto.IsChecked == true;
                p["autoIniciar"] = comandoAutoIniciar.IsChecked == true;
                break;
            default:
                Campos(fServ);
                var dir = (projetosDir.Text ?? "").Trim();
                if (dir == "") p.Remove("projetosDir"); else p["projetosDir"] = dir;
                // pasta de projetos inválida (descoberta falhou): mantém o que está gravado em vez de salvar vazio
                if (descobertaOk && descobertosDe != null)
                {
                    p["projetos"] = new JsonArray(descobertos.Where(x => selProj.Contains(x.Repo)).Select(x => (JsonNode)x.Repo).ToArray());
                    p["artefatos"] = new JsonArray(cartoesArt.Select(c => (JsonNode)LerCartao(c)).ToArray());
                }
                break;
        }
        return p;
    }

    static void Validar(JsonObject p)
    {
        switch (TipoDe(p))
        {
            case "java":
                if (JsonAux.Txt(p, "mainClass") == null) throw new ErroRunner("informe a classe main");
                if (JsonAux.Txt(p, "modulo") == null) throw new ErroRunner("informe o módulo Maven da aplicação");
                return;
            case "npm":
                if (JsonAux.Txt(p, "pasta") == null) throw new ErroRunner("informe a pasta do package.json");
                if (JsonAux.Txt(p, "script") == null) throw new ErroRunner("informe o script do package.json");
                return;
            case "zip":
                if (JsonAux.Txt(p, "pacote") == null) throw new ErroRunner("informe o pacote (.zip ou .jar)");
                return;
            case "comando":
                if (JsonAux.Txt(p, "comando") == null) throw new ErroRunner("informe o comando");
                if (JsonAux.Num(p, "porta") is int cp && cp is < 1 or > 65535) throw new ErroRunner("porta precisa estar entre 1 e 65535");
                if (JsonAux.Num(p, "timeoutExecucaoSeg") is int ct && ct <= 0) throw new ErroRunner("tempo limite precisa ser maior que zero");
                return;
        }
        var portas = Numericos.Select(k => JsonAux.Num(p, k)).OfType<int>().ToList();
        if (portas.Distinct().Count() != portas.Count) throw new ErroRunner("portas repetidas no perfil");
        var arts = (p["artefatos"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
        if (TipoDe(p) == "war" && arts.Any(a => JsonAux.Txt(a, "war") == null)) throw new ErroRunner("war sem arquivo (aba Pacotes)");
        // war sem contexto = nome do arquivo (app.war -> /app), igual ao runner
        var ctx = arts.Where(a => JsonAux.NaoFalso(a, "ativo"))
            .Select(a => JsonAux.Txt(a, "contexto") ?? (JsonAux.Txt(a, "war") is { } w ? "/" + Path.GetFileNameWithoutExtension(w) : "")).ToList();
        if (ctx.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ctx.Count) throw new ErroRunner("dois artefatos marcados com o mesmo contexto");
    }

    // ---------------------------------------------------------------- projetos e artefatos (Tomcat)

    async void Redescobrir(bool lembrar, string? ok = null)
    {
        if (lembrar) LembrarArtefatos();
        var dir = TipoAtual == "tomcat" ? (projetosDir.Text ?? "").Trim() : "";
        var perfil = atual;
        try
        {
            var l = await Task.Run(() => Projetos.Listar(dir));
            if (perfil != atual) return;
            descobertos = l;
            descobertaOk = true;
            if (ok != null) Aviso(ok, "on");
        }
        catch (Exception e)
        {
            if (perfil != atual) return;
            descobertos = [];
            descobertaOk = false;
            Aviso(e.Message, "err");
        }
        descobertosDe = dir;
        PintarProjetos();
        PintarArtefatos();
    }

    void PintarProjetos()
    {
        projInfo.Text = descobertos.Count(p => p.Artefatos.Count > 0) + " com war";
        projsBox.Children.Clear();
        foreach (var p in descobertos.Where(p => mostrarSemWar.IsChecked == true || p.Artefatos.Count > 0 || selProj.Contains(p.Repo)))
        {
            var marcado = selProj.Contains(p.Repo);
            var conteudo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            conteudo.Children.Add(Ui.Txt(p.Nome, 14));
            conteudo.Children.Add(Ui.Txt(p.Artefatos.Count > 0 ? p.Artefatos.Count + " war" : "sem war", 12, "Mut").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
            var chk = Ui.Chk("", marcado);
            chk.Content = conteudo;
            chk.VerticalAlignment = VerticalAlignment.Center;
            var chip = Ui.Cartao(chk, 10, new Thickness(14, 0)).Dica(p.Repo);
            chip.Height = 44;
            chip.Margin = new Thickness(0, 0, 8, 8);
            if (marcado) chip.Res(Border.BorderBrushProperty, "Acc");
            if (p.Artefatos.Count == 0) chip.Opacity = 0.7;
            var repo = p.Repo;
            chk.Click += (_, _) =>
            {
                LembrarArtefatos();
                if (chk.IsChecked == true) selProj.Add(repo); else selProj.Remove(repo);
                MarcarSujo();
                PintarProjetos();
                PintarArtefatos();
            };
            projsBox.Children.Add(chip);
        }
    }

    void PintarArtefatos()
    {
        artsBox.Children.Clear();
        cartoesArt.Clear();
        var marcados = descobertos.Where(p => selProj.Contains(p.Repo)).ToList();
        if (marcados.Count == 0) { artsBox.Children.Add(Vazio("Marque um projeto acima.")); return; }
        foreach (var p in marcados)
        {
            if (p.Artefatos.Count == 0) { artsBox.Children.Add(Vazio(p.Nome + ": nenhum módulo war.")); continue; }
            foreach (var d in p.Artefatos) artsBox.Children.Add(NovoCartaoArt(p, d).Raiz);
        }
    }

    CartaoArt NovoCartaoArt(ProjetoDescoberto p, ArtefatoDescoberto d)
    {
        artsPerfil.TryGetValue(Chave(d.Repo, d.Modulo), out var salvo);
        var c = new CartaoArt { Repo = d.Repo, Modulo = d.Modulo };
        var fixo = JsonAux.Txt(salvo, "docBase");
        var nome = d.Modulo != "" ? d.Modulo : p.Nome;
        c.Ativo = Ui.Chk("", salvo != null && JsonAux.NaoFalso(salvo, "ativo"));
        c.Ctx = Ui.Inp("/contexto", 36);
        c.Ctx.Width = 180;
        c.Ctx.Text = JsonAux.Txt(salvo, "contexto") ?? d.Contexto;
        c.Build = Ui.Inp("", 36);
        c.Build.Text = salvo != null ? JsonAux.Txt(salvo, "build") ?? "" : d.Build;
        var sync = salvo?["sync"] is JsonArray sa
            ? sa.OfType<JsonObject>().Select(s => (JsonAux.Txt(s, "de") ?? "") + "→" + (JsonAux.Txt(s, "para") ?? ""))
            : d.Sync.Select(s => s.De + "→" + s.Para);
        c.Sync = Ui.Inp("", 36);
        c.Sync.Text = string.Join(" ; ", sync);
        c.DocBase = Ui.Inp("vazio = pasta explodida mais nova em target", 36);
        c.DocBase.Text = fixo ?? "";
        c.Doc = Doc();
        PintarDocArt(c, fixo ?? d.DocBase);

        var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        c.Ativo.VerticalAlignment = VerticalAlignment.Center;
        c.Ativo.Margin = new Thickness(0, 0, 10, 0);
        Avalonia.Automation.AutomationProperties.SetName(c.Ativo, "Publicar " + nome);
        topo.Children.Add(c.Ativo);
        var nm = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 3 }.Dica(Path.Combine(d.Repo, d.Modulo));
        nm.Children.Add(Ui.Txt(nome, 15, "Fg", FontWeight.SemiBold));
        nm.Children.Add(c.Doc);
        Grid.SetColumn(nm, 1);
        topo.Children.Add(nm);
        var ctx = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), Spacing = 8 };
        ctx.Children.Add(Ui.Txt("Contexto", 12, "Mut").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        ctx.Children.Add(c.Ctx);
        Grid.SetColumn(ctx, 2);
        topo.Children.Add(ctx);

        var docFixo = Ui.Campo("docBase fixo (vazio = pasta explodida mais nova em target)", c.DocBase);
        docFixo.IsVisible = fixo != null;
        var abreFixo = Ui.Btn(fixo != null ? "ocultar docBase fixo" : "docBase fixo…", "link");
        abreFixo.HorizontalAlignment = HorizontalAlignment.Left;
        abreFixo.Margin = new Thickness(-8, 8, 0, 0);
        abreFixo.Click += (_, _) =>
        {
            docFixo.IsVisible = !docFixo.IsVisible;
            abreFixo.Content = docFixo.IsVisible ? "ocultar docBase fixo" : "docBase fixo…";
        };
        var mais = new StackPanel { Margin = new Thickness(32, 14, 0, 0) };
        mais.Children.Add(Ui.Grade(2,
            Ui.Campo("Build (roda em " + p.Nome + ")", c.Build),
            Ui.Campo("Sync (origem→destino; separar por ;)", c.Sync)));
        mais.Children.Add(abreFixo);
        mais.Children.Add(docFixo);

        var corpoCartao = new StackPanel { Children = { topo, mais } };
        c.Raiz = Ui.Cartao(corpoCartao, 14, new Thickness(20, 18));
        c.Raiz.Margin = new Thickness(0, 0, 0, 12);
        void Marca()
        {
            c.Raiz.Res(Border.BorderBrushProperty, c.Ativo.IsChecked == true ? "Acc" : "Line");
            c.Raiz.Opacity = c.Ativo.IsChecked == true ? 1 : 0.78;
        }
        Marca();
        c.Ativo.Click += (_, _) => { Marca(); MarcarSujo(); };
        foreach (var t in new[] { c.Ctx, c.Build, c.Sync, c.DocBase }) t.TextChanged += (_, _) => MarcarSujo();
        cartoesArt.Add(c);
        return c;
    }

    static void PintarDocArt(CartaoArt c, string? docBase)
    {
        c.Doc.Text = docBase != null ? "→ " + docBase : "sem pasta explodida em target — rode o Build";
        c.Doc.Res(TextBlock.ForegroundProperty, docBase != null ? "Mut" : "Warn");
    }

    static JsonObject LerCartao(CartaoArt c)
    {
        var a = new JsonObject
        {
            ["ativo"] = c.Ativo.IsChecked == true, ["repo"] = c.Repo, ["modulo"] = c.Modulo,
            ["contexto"] = (c.Ctx.Text ?? "").Trim() is { Length: > 0 } ctx ? ctx : "/", ["build"] = (c.Build.Text ?? "").Trim(),
        };
        if ((c.DocBase.Text ?? "").Trim() is { Length: > 0 } fixo) a["docBase"] = fixo;
        var sync = new JsonArray();
        foreach (var s in (c.Sync.Text ?? "").Split(';').Select(s => s.Trim()).Where(s => s != ""))
        {
            var partes = s.Split(["→", "->"], 2, StringSplitOptions.None);
            sync.Add(new JsonObject { ["de"] = partes[0].Trim(), ["para"] = partes.Length > 1 ? partes[1].Trim() : "" });
        }
        a["sync"] = sync;
        return a;
    }

    /// <summary>Guarda o que está nos cartões antes de repintar (marcar/desmarcar projeto não perde edição).</summary>
    void LembrarArtefatos()
    {
        foreach (var c in cartoesArt) artsPerfil[Chave(c.Repo, c.Modulo)] = LerCartao(c);
    }

    // ---------------------------------------------------------------- wars (perfil "war")

    void PintarWars(JsonArray? arts)
    {
        warsBox.Children.Clear();
        cartoesWar.Clear();
        var l = (arts ?? new JsonArray()).OfType<JsonObject>().ToList();
        if (l.Count == 0) { warsBox.Children.Add(Vazio("Nenhum war. Use Adicionar war.")); return; }
        foreach (var a in l) NovoCartaoWar(JsonAux.NaoFalso(a, "ativo"), JsonAux.Txt(a, "war") ?? "", JsonAux.Txt(a, "contexto") ?? "");
    }

    void NovoCartaoWar(bool ativo, string war, string contexto)
    {
        var c = new CartaoWar
        {
            Ativo = Ui.Chk("", ativo),
            War = Ui.Inp("arquivo .war", 36),
            Ctx = Ui.Inp("/contexto", 36),
            Nome = Ui.Txt("", 15, "Fg", FontWeight.SemiBold),
            Doc = Doc(),
        };
        c.War.Text = war;
        c.Ctx.Text = contexto;
        c.Ctx.Width = 180;

        var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        c.Ativo.VerticalAlignment = VerticalAlignment.Center;
        c.Ativo.Margin = new Thickness(0, 0, 10, 0);
        topo.Children.Add(c.Ativo);
        var nm = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 3, Children = { c.Nome, c.Doc } };
        Grid.SetColumn(nm, 1);
        topo.Children.Add(nm);
        var ctx = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), Spacing = 8 };
        ctx.Children.Add(Ui.Txt("Contexto", 12, "Mut").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        ctx.Children.Add(c.Ctx);
        Grid.SetColumn(ctx, 2);
        topo.Children.Add(ctx);
        var rem = Ui.BtnIco("lixo", "Remover war", "fantasma");
        rem.Margin = new Thickness(8, 0, 0, 0);
        rem.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(rem, 3);
        topo.Children.Add(rem);

        var arq = Ui.Campo("Arquivo .war (▾ lista as versões)", Ui.ComBotoes(c.War,
            Ui.Opcoes(c.War, () => OpcoesVersao(c.War.Text ?? "", ".war")),
            Ui.Procurar(c.War, "Arquivo .war", false, ".war")));
        arq.Margin = new Thickness(32, 14, 0, 0);
        c.Raiz = Ui.Cartao(new StackPanel { Children = { topo, arq } }, 14, new Thickness(20, 18));
        c.Raiz.Margin = new Thickness(0, 0, 0, 12);
        void Marca()
        {
            c.Raiz.Res(Border.BorderBrushProperty, c.Ativo.IsChecked == true ? "Acc" : "Line");
            c.Raiz.Opacity = c.Ativo.IsChecked == true ? 1 : 0.78;
        }
        Marca();
        c.Ativo.Click += (_, _) => { Marca(); MarcarSujo(); };
        c.Ctx.TextChanged += (_, _) => MarcarSujo();
        c.War.TextChanged += (_, _) => { MarcarSujo(); AtualizarWar(c); PintarCabecalho(); };
        rem.Click += (_, _) =>
        {
            cartoesWar.Remove(c);
            warsBox.Children.Remove(c.Raiz);
            if (cartoesWar.Count == 0) warsBox.Children.Add(Vazio("Nenhum war. Use Adicionar war."));
            MarcarSujo();
        };
        cartoesWar.Add(c);
        warsBox.Children.Add(c.Raiz);
        AtualizarWar(c);
    }

    async void AtualizarWar(CartaoWar c)
    {
        var arq = (c.War.Text ?? "").Trim();
        c.Nome.Text = arq != "" ? Base(arq) + (VersaoDe(arq) is { Length: > 0 } v ? " · " + v : "") : "war novo";
        c.Ctx.Watermark = arq != "" ? "/" + Path.GetFileNameWithoutExtension(arq) : "/contexto";
        var l = await Task.Run(() => Pacote.Versoes(arq, ".war"));
        if ((c.War.Text ?? "").Trim() != arq) return;   // trocou enquanto carregava
        c.Versao = arq == "" ? "" : TextoVersao(l, arq);
        c.Doc.Text = arq == "" ? "escolha o arquivo .war" : c.Versao;
        c.Doc.Res(TextBlock.ForegroundProperty, arq == "" || c.Versao == "arquivo não encontrado" ? "Warn" : "Mut");
        PintarStatusPaineis();
    }

    // ---------------------------------------------------------------- versões (war e zip)

    static string Mb(long n) => Math.Round(n / 1048576.0) + " MB";
    static string Quando(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.ToString("dd/MM HH:mm");

    static string TextoVersao(List<VersaoPacote> l, string arq)
    {
        var v = l.FirstOrDefault(x => x.Caminho.Equals(arq, StringComparison.OrdinalIgnoreCase));
        return v != null ? v.Versao + " · " + Mb(v.Tamanho) + " · gerado " + Quando(v.Data)
            : File.Exists(arq) ? "fora de uma pasta de versões" : "arquivo não encontrado";
    }

    static async Task<List<(string, string)>> OpcoesVersao(string arq, string ext) =>
        (await Task.Run(() => Pacote.Versoes(arq, ext)))
            .Select(v => (v.Caminho, v.Versao + " · " + Mb(v.Tamanho) + " · " + Quando(v.Data))).ToList();

    async void AtualizarZipInfo()
    {
        var arq = (fZip["pacote"].Text ?? "").Trim();
        var l = await Task.Run(() => Pacote.Versoes(arq, ".zip,.jar"));
        if ((fZip["pacote"].Text ?? "").Trim() != arq) return;
        versoesZipInfo.Text = l.Count > 0 ? "— " + l.Count + " versão(ões)" : "";
        var t = arq == "" ? "" : TextoVersao(l, arq);
        zipInfo.Text = t != "" ? "→ " + t : "";
        zipInfo.Res(TextBlock.ForegroundProperty, t == "arquivo não encontrado" ? "Warn" : "Mut");
        if (!preenchendo) PintarCabecalho();
    }

    // ---------------------------------------------------------------- app Java e npm

    async void AtualizarMains()
    {
        var mod = (fApp["modulo"].Text ?? "").Trim();
        mainsInfo.Text = "";
        if (mod == "") return;
        try
        {
            if (!File.Exists(Path.Combine(mod, "pom.xml"))) throw new ErroRunner("módulo sem pom.xml");
            var l = await Task.Run(() => JavaApp.ClassesMain(mod));
            if ((fApp["modulo"].Text ?? "").Trim() != mod) return;
            var t = l.Count(x => x.Teste);
            mainsInfo.Text = "— " + (l.Count - t) + " com main no módulo" + (t > 0 ? " + " + t + " de teste" : "");
        }
        catch (Exception e) { mainsInfo.Text = "— " + e.Message; }
    }

    async void AtualizarNpm()
    {
        var pasta = (fNpm["pasta"].Text ?? "").Trim();
        if (pasta == scriptsDe) return;
        scriptsDe = pasta;
        scripts = [];
        scriptsInfo.Text = "";
        if (pasta != "")
        {
            try
            {
                var l = await Task.Run(() => NodeApp.Scripts(pasta));
                if (pasta != scriptsDe) return;   // pasta trocou enquanto carregava
                scripts = l;
                scriptsInfo.Text = "— " + l.Count + " no package.json";
            }
            catch (Exception e) { scriptsInfo.Text = "— " + e.Message; }
        }
        PintarLinhaNpm();
    }

    /// <summary>O que vai rodar, com o comando do script por baixo (ex.: npm run start → ng serve).</summary>
    void PintarLinhaNpm()
    {
        var sc = (fNpm["script"].Text ?? "").Trim() is { Length: > 0 } s ? s : "start";
        var args = (fNpm["args"].Text ?? "").Trim();
        var achado = scripts.FirstOrDefault(x => x.Nome == sc);
        var temPasta = !string.IsNullOrEmpty(scriptsDe);
        npmLinha.Text = "→ npm run " + sc + (args != "" ? " -- " + args : "") +
            (achado != null ? "   (" + achado.Comando + (args != "" ? " " + args : "") + ")" : temPasta ? "   (script não existe no package.json)" : "");
        npmLinha.Res(TextBlock.ForegroundProperty, temPasta && achado == null ? "Warn" : "Mut");
    }

    // ---------------------------------------------------------------- status nos painéis

    /// <summary>docBase dos artefatos (muda com o Build) e onde cada war está extraído.</summary>
    void PintarStatusPaineis()
    {
        var st = StatusAtual;
        if (st == null || st.Tipo != "tomcat") return;
        if (TipoAtual == "war")
        {
            foreach (var c in cartoesWar)
            {
                var x = st.Artefatos.FirstOrDefault(a => a.Repo.Equals((c.War.Text ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                if (x == null || c.Versao == "" || c.Versao == "arquivo não encontrado") continue;
                c.Doc.Text = c.Versao + (x.DocBase != null ? " · extraído em " + x.DocBase : " · ainda não extraído (Iniciar ou Build extraem)");
            }
            return;
        }
        fServ["url"].Watermark = st.Url ?? "";
        foreach (var c in cartoesArt)
        {
            if ((c.DocBase.Text ?? "").Trim() != "") continue;
            var x = st.Artefatos.FirstOrDefault(a => Chave(a.Repo, a.Modulo) == Chave(c.Repo, c.Modulo));
            if (x != null) PintarDocArt(c, x.DocBase);
        }
    }
}
