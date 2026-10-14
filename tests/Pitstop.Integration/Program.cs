using Pitstop;

static void Checar(bool condicao, string mensagem)
{
    if (!condicao) throw new Exception("FALHA: " + mensagem);
}
var raiz = Path.Combine(Path.GetTempPath(), "pitstop-integration-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(raiz);
    var repo = Path.Combine(raiz, "repo");
    var src = Path.Combine(repo, "src", "main", "webapp");
    var doc = Path.Combine(repo, "target", "app");
    var webinf = Path.Combine(doc, "WEB-INF");
    Directory.CreateDirectory(src);
    Directory.CreateDirectory(webinf);
    var fonte = Path.Combine(src, "index.html");
    File.WriteAllText(fonte, "versao 1");
    var art = new Artefato
    {
        Repo = repo, Contexto = "/app", Modulo = "", Build = "mvn package",
        Sync = [new RegraSync("src/main/webapp", "")],
    };
    var p = new Perfil { Nome = "Teste", Tipo = "tomcat", Cache = Path.Combine(raiz, "cache"), Artefatos = [art] };
    Checar(!PreparacaoTomcat.Atualizado(p), "cache inicial não pode existir");
    PreparacaoTomcat.Registrar(p);
    Checar(PreparacaoTomcat.Atualizado(p), "cache de build após sucesso");
    var texto = Path.Combine(src, "new.txt");
    File.WriteAllText(texto, "adicionado");
    Checar(!PreparacaoTomcat.Atualizado(p), "inclusão de arquivo precisa invalidar cache");
    PreparacaoTomcat.Registrar(p);
    File.Delete(texto);
    Checar(!PreparacaoTomcat.Atualizado(p), "exclusão de arquivo precisa invalidar cache");
    PreparacaoTomcat.Registrar(p);
    File.WriteAllText(Path.Combine(webinf, "new.class"), "alterado");
    Checar(!PreparacaoTomcat.Atualizado(p), "mudança de saída precisa invalidar cache");
    PreparacaoTomcat.Invalidar(p);
    Checar(!PreparacaoTomcat.Atualizado(p), "cache inválido nunca pode ser reaproveitado");
    Console.WriteLine("OK cache Maven: inclusão, exclusão, saída e invalidação");

    var primeiro = SyncTomcat.Executar(p, art, art.Sync[0]);
    Checar(primeiro.Copiados == 1 && File.Exists(Path.Combine(doc, "index.html")), "primeiro Sync");
    Checar(SyncTomcat.Executar(p, art, art.Sync[0]).Copiados == 0, "sync inalterado deve pular");
    File.WriteAllText(fonte, "versao 2 mais longa");
    File.SetLastWriteTimeUtc(fonte, DateTime.UtcNow.AddSeconds(3));
    var segundo = SyncTomcat.Executar(p, art, art.Sync[0]);
    Checar(segundo.Copiados == 1 && File.ReadAllText(Path.Combine(doc, "index.html")) == "versao 2 mais longa", "Sync atualizado");
    File.Delete(fonte);
    var remocao = SyncTomcat.Executar(p, art, art.Sync[0]);
    Checar(remocao.Excluidos == 1 && !File.Exists(Path.Combine(doc, "index.html")), "Sync removido");
    Checar(SyncTomcat.Executar(p, art, art.Sync[0]).Excluidos == 0, "remoção repetida deve ser idempotente");
    Console.WriteLine("OK sync gerenciado: inclusão, atualização, exclusão e idempotência");

    File.WriteAllText(fonte, "arquivo de teste");
    SyncTomcat.Executar(p, art, art.Sync[0]);
    File.WriteAllText(Path.Combine(doc, "index.html"), "alterado pelo usuário");
    File.Delete(fonte);
    var protegido = SyncTomcat.Executar(p, art, art.Sync[0]);
    Checar(protegido.Excluidos == 0 && File.Exists(Path.Combine(doc, "index.html")), "não apagar alteração externa");
    Directory.Move(src, src + "-movida");
    Checar(SyncTomcat.Executar(p, art, art.Sync[0]).Excluidos == 0, "não apagar se pasta origem sumiu");
    bool rejeitou = false;
    try { SyncTomcat.Executar(p, art, new RegraSync("src/main/webapp", "../fora")); }
    catch (ErroRunner) { rejeitou = true; }
    Checar(rejeitou, "Sync fora do destino deve ser recusado");
    Console.WriteLine("OK proteções: edição externa, origem indisponível e path traversal");
    // Contexto direto: Tomcat 9 fictício, sem iniciar nenhum serviço real.
    Directory.Move(src + "-movida", src);
    var tomcat = Path.Combine(raiz, "tomcat");
    Directory.CreateDirectory(Path.Combine(tomcat, "bin"));
    Directory.CreateDirectory(Path.Combine(tomcat, "conf"));
    Directory.CreateDirectory(Path.Combine(tomcat, "lib"));
    Directory.CreateDirectory(Path.Combine(repo, "target", "classes"));
    Directory.CreateDirectory(Path.Combine(webinf, "lib"));
    Directory.CreateDirectory(Path.Combine(src, "WEB-INF"));
    File.WriteAllText(Path.Combine(src, "WEB-INF", "web.xml"), "<web-app/>");
    File.WriteAllText(Path.Combine(webinf, "web.xml"), "<web-app/>");
    File.WriteAllText(Path.Combine(webinf, "lib", "dependencia.jar"), "teste");
    File.WriteAllText(Path.Combine(tomcat, "bin", "catalina.bat"), "@echo off");
    File.WriteAllText(Path.Combine(tomcat, "conf", "server.xml"),
        "<Server port=\"8005\"><Service name=\"Catalina\"><Connector port=\"8080\" protocol=\"HTTP/1.1\" /></Service></Server>");
    using (var jar = System.IO.Compression.ZipFile.Open(Path.Combine(tomcat, "lib", "catalina.jar"), System.IO.Compression.ZipArchiveMode.Create))
    {
        var entry = jar.CreateEntry("org/apache/catalina/util/ServerInfo.properties");
        using var writer = new StreamWriter(entry.Open());
        writer.WriteLine("server.number=9.0.99.0");
    }
    var pd = new Perfil
    {
        Nome = "TesteDireto", Tipo = "tomcat", Home = tomcat, JavaHome = tomcat,
        Base = Path.Combine(raiz, "base"), ConfOrigem = Path.Combine(tomcat, "conf"),
        Cache = Path.Combine(raiz, "cache-direto"), Porta = 18080, PortaShutdown = 18005,
        PublicacaoDireta = true, ReloadAutomatico = true, Artefatos = [art],
    };
    var publicados = Tomcat.PrepararBase(pd);
    var ctxXml = File.ReadAllText(Path.Combine(pd.Base, "conf", "Catalina", "localhost", "app.xml"));
    Checar(publicados.Count == 1 && publicados[0].Modo.StartsWith("direto"), "publicação direta habilitada");
    Checar(ctxXml.Contains("DirResourceSet") && ctxXml.Contains("FileResourceSet") &&
        ctxXml.Contains("reloadable=\"true\"") && !ctxXml.Contains("allowLinking"), "ResourceSets/classes/libs e reload");
    Directory.Delete(Path.Combine(repo, "target", "classes"));
    var fallback = Tomcat.PrepararBase(pd);
    Checar(fallback[0].Modo.Contains("fallback"), "fallback quando classes diretas não existem");
    Console.WriteLine("OK publicação direta, resource sets, recarga opcional e fallback");
    var health = new Perfil { Porta = 18080, ProntoUrl = "http://localhost:18080/health" };
    Checar(Tomcat.ValidarProntoUrl(health)?.AbsolutePath == "/health", "health URL localhost");
    var recusou = false;
    try { Tomcat.ValidarProntoUrl(new Perfil { Porta = 18080, ProntoUrl = "http://example.com:18080/" }); }
    catch (ErroRunner) { recusou = true; }
    Checar(recusou, "health URL remota deve ser recusada");
    Console.WriteLine("OK healthcheck restrito ao localhost");

    var eventos = 0;
    using (var watcher = new ObservadorSync(p, () => Interlocked.Increment(ref eventos)))
    {
        File.WriteAllText(Path.Combine(src, "watcher.txt"), "mudança");
        Thread.Sleep(2200);
    }
    Checar(eventos > 0, "FileSystemWatcher deve detectar alteração");
    Console.WriteLine("OK watcher automático");
    // Reactor com mais de dois níveis, anteriormente limitado por profundidade fixa.
    var raizMaven = Path.Combine(raiz, "reactor");
    var nivel1 = Path.Combine(raizMaven, "a");
    var nivel2 = Path.Combine(nivel1, "b");
    var nivel3 = Path.Combine(nivel2, "c");
    Directory.CreateDirectory(nivel3);
    static void Pom(string dir, string packaging, string modulo = "")
    {
        File.WriteAllText(Path.Combine(dir, "pom.xml"),
            "<project><modelVersion>4.0.0</modelVersion><groupId>test</groupId><artifactId>" +
            Path.GetFileName(dir) + "</artifactId><version>1</version><packaging>" + packaging +
            "</packaging>" + (modulo == "" ? "" : "<modules><module>" + modulo + "</module></modules>") + "</project>");
    }
    Pom(raizMaven, "pom", "a");
    Pom(nivel1, "pom", "b");
    Pom(nivel2, "pom", "c");
    Pom(nivel3, "war");
    var descobertos = Projetos.Listar(raizMaven);
    Checar(descobertos.Count == 1 && descobertos[0].Artefatos.Count == 1, "reactor Maven profundo");
    Console.WriteLine("OK descoberta reactor Maven com múltiplos níveis");
    // Os perfis Tomcat antigos não podem ganhar Maven/Sync automático silenciosamente.
    var cfgLegado = System.Text.Json.Nodes.JsonNode.Parse("""
        {"perfis":{"legado":{"artefatos":[]},"novo":{"prepararAoIniciar":true,"syncAutomatico":true,"artefatos":[]}}}
        """) as System.Text.Json.Nodes.JsonObject ?? throw new Exception("fixture JSON inválida");
    var legado = Config.LerPerfil(cfgLegado, "legado");
    var novo = Config.LerPerfil(cfgLegado, "novo");
    Checar(!legado.PrepararAoIniciar && !legado.SyncAutomatico, "preservação de perfil legado");
    Checar(novo.PrepararAoIniciar && novo.SyncAutomatico, "preparação e sync de perfil novo");
    Console.WriteLine("OK compatibilidade dos perfis antigos");
    Console.WriteLine("INTEGRATION OK");
}
finally
{
    try { if (Directory.Exists(raiz)) Directory.Delete(raiz, true); }
    catch (IOException) { }
}
