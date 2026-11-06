using Pitstop;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

static void Checar(bool condicao, string mensagem)
{
    if (!condicao) throw new Exception("FALHA: " + mensagem);
}
var raiz = Path.Combine(Path.GetTempPath(), "pitstop-integration-" + Guid.NewGuid().ToString("N"));
var raizAnterior = Environment.GetEnvironmentVariable("PIT_RAIZ");
try
{
    Directory.CreateDirectory(raiz);
    // Fixture isolada para a parada em lote; nenhum perfil real ou processo real é usado.
    Environment.SetEnvironmentVariable("PIT_RAIZ", raiz);
    // #36: remoção isolada altera somente o registro e exige a versão lida.
    var configDir = Path.Combine(raiz, "config");
    Directory.CreateDirectory(configDir);
    var dados = Path.Combine(raiz, "cache", "RemoverTeste");
    Directory.CreateDirectory(dados);
    var arquivoDados = Path.Combine(dados, "nao-apagar.txt");
    File.WriteAllText(arquivoDados, "preservar");
    File.WriteAllText(Path.Combine(configDir, "perfis.json"), "{\n  \"perfis\": {\n    \"RemoverTeste\": { \"tipo\": \"comando\", \"comando\": \"echo teste\" }\n  }\n}\n");
    var versaoRemover = Config.Versao();
    new Runner().Remover("RemoverTeste", versaoRemover);
    Checar(!Config.NomesPerfis(Config.Ler()).Contains("RemoverTeste"), "remover deve apagar somente o registro");
    Checar(File.Exists(arquivoDados), "remover não pode apagar cache/base/dados");
    var perfilParaConflito = Path.Combine(configDir, "perfis.json");
    File.WriteAllText(perfilParaConflito, "{\n  \"perfis\": {\n    \"Conflito\": { \"tipo\": \"comando\", \"comando\": \"echo teste\" }\n  }\n}\n");
    var versaoAntiga = Config.Versao();
    File.AppendAllText(perfilParaConflito, "\n");
    File.SetLastWriteTimeUtc(perfilParaConflito, DateTime.UtcNow.AddSeconds(2));
    var recusouConflito = false;
    try { Config.Remover("Conflito", versaoAntiga); } catch (ErroRunner) { recusouConflito = true; }
    Checar(recusouConflito && Config.NomesPerfis(Config.Ler()).Contains("Conflito"), "versão concorrente deve recusar remoção");
    Console.WriteLine("OK #36 remoção somente da configuração, preservação de dados e versão concorrente");
    // #40: o marcador de falha é literal, guarda a primeira linha e vence prontidão na mesma linha.
    using var processoTeste = new Process();
    var execucaoTeste = new Execucao
    {
        Child = processoTeste, Tipo = "teste", FalhaLog = "ERROR [x]",
        ProntoRe = new System.Text.RegularExpressions.Regex("pronto"),
    };
    Checar(!Runner.ProcessarLinhaSaida(execucaoTeste, "iniciando"), "linha sem marcador não pode marcar pronto");
    var linhaComAmbos = "ERROR [x] pronto";
    Checar(!Runner.ProcessarLinhaSaida(execucaoTeste, linhaComAmbos) && execucaoTeste.LinhaFalha == linhaComAmbos,
        "falhaLog deve vencer prontoLog na mesma linha no estado do Runner");
    Checar(!Runner.ProcessarLinhaSaida(execucaoTeste, "ERROR [x] pronto novamente") &&
        execucaoTeste.LinhaFalha == linhaComAmbos, "falhaLog deve guardar a primeira linha correspondente");
    Checar(!Runner.LinhaCasaFalha("ERROR [x]", "ERROR x"), "falhaLog não deve interpretar regex");
    var novaExecucao = new Execucao { Child = processoTeste, Tipo = "teste", FalhaLog = "ERROR [x]" };
    Checar(novaExecucao.LinhaFalha == null, "nova execução deve limpar falha anterior");
    Console.WriteLine("OK #40 falhaLog literal, primeira linha e precedência sobre prontoLog");
    Checar(MemoriaProcessos.Medir(0) is { MemoriaBytes: null, Estado: "semPid" },
        "memória sem PID deve ser explicitamente indisponível");
    Checar(MemoriaProcessos.Medir(-1) is { MemoriaBytes: null, Estado: "semPid" },
        "PID inválido não pode virar zero");
    Checar(MemoriaProcessos.SomarDeterministico([10, 20, 30]) == 60, "soma determinística de memória");
    Checar(MemoriaProcessos.SomarDeterministico([10, null]) == null, "soma parcial deve ser recusada");
    Console.WriteLine("OK #41 contrato de memória: sem PID, indisponibilidade e soma determinística");
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
    File.WriteAllText(Path.Combine(tomcat, "bin", "catalina.sh"), "#!/bin/sh\nexit 0\n");
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

    // Polling de readiness: resposta transitória 503 não conclui; a seguinte 200 conclui sem perfil Tomcat.
    using (var servidor = new TcpListener(IPAddress.Loopback, 0))
    {
        servidor.Start();
        var portaPronto = ((IPEndPoint)servidor.LocalEndpoint).Port;
        var respostas = new[] { 503, 200 };
        var atendimento = Task.Run(async () =>
        {
            foreach (var status in respostas)
            {
                using var conexao = await servidor.AcceptTcpClientAsync();
                using var fluxo = conexao.GetStream();
                var resposta = Encoding.ASCII.GetBytes("HTTP/1.0 " + status + (status == 503 ? " Service Unavailable" : " OK") +
                    "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                await fluxo.WriteAsync(resposta);
                await fluxo.FlushAsync();
                await Task.Delay(100);
            }
        });
        using var processo = Process.Start(new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = "/c ping -n 10 127.0.0.1 > nul",
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new Exception("não iniciou processo auxiliar do teste");
        try
        {
            var urlPronto = Tomcat.ValidarProntoUrl(new Perfil
            {
                Porta = portaPronto,
                ProntoUrl = "http://127.0.0.1:" + portaPronto + "/health",
            })!;
            Checar(await Tomcat.AguardarPronto(processo, urlPronto, CancellationToken.None) is long,
                "readiness deve ignorar 503 e concluir em 200");
            await atendimento;
            processo.WaitForExit();
        }
        finally
        {
            servidor.Stop();
            if (!processo.HasExited) processo.Kill(true);
        }
    }
    Console.WriteLine("OK polling readiness 503 -> 200");

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

    // Issues #22 e #23: dois repositórios independentes, contextos isolados e JVM persistente.
    var repoA = Path.Combine(raiz, "workspace-a", "mesmo-nome");
    var repoB = Path.Combine(raiz, "workspace-b", "mesmo-nome");
    Directory.CreateDirectory(repoA);
    Directory.CreateDirectory(repoB);
    Pom(repoA, "war");
    Pom(repoB, "war");
    var combinados = Projetos.Listar(repoA, [repoB, repoB, repoA]);
    Checar(combinados.Count == 2, "dois caminhos independentes, sem duplicar repositórios");
    Checar(combinados.All(x => x.Erro == null && x.Artefatos.Count == 1), "artefatos Maven de ambos os discos");
    Checar(combinados.Select(x => x.Repo).Distinct(So.Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Count() == 2,
        "projetos com o mesmo nome devem manter identidades por caminho");
    var perdido = Path.Combine(raiz, "workspace-offline", "projeto");
    var comAusente = Projetos.Listar(repoA, [repoB, perdido]);
    Checar(comAusente.Count == 3 && comAusente.Single(x => x.Repo == perdido).Erro != null,
        "projeto temporariamente indisponível deve continuar visível para preservar configuração");
    var docA = Path.Combine(repoA, "target", "app-a");
    var docB = Path.Combine(repoB, "target", "app-b");
    Directory.CreateDirectory(Path.Combine(docA, "WEB-INF"));
    Directory.CreateDirectory(Path.Combine(docB, "WEB-INF"));
    var artA = new Artefato { Repo = repoA, Contexto = "/sistema", Build = "mvn package", DocBase = docA };
    var artB = new Artefato { Repo = repoB, Contexto = "/api", Build = "mvn package", DocBase = docB };
    var multi = new Perfil
    {
        Nome = "MultiRepositorio", Tipo = "tomcat", Home = tomcat, JavaHome = tomcat, ConfOrigem = Path.Combine(tomcat, "conf"),
        Base = Path.Combine(raiz, "base-multirepo"), Cache = Path.Combine(raiz, "cache-multirepo"),
        Porta = 18080, PortaShutdown = 18005, Artefatos = [artA, artB]
    };
    var conflito = new Perfil
    {
        Nome = "Conflito", Tipo = "tomcat", Home = tomcat, JavaHome = tomcat,
        ConfOrigem = Path.Combine(tomcat, "conf"), Base = Path.Combine(raiz, "base-conflito"),
        Artefatos = [artA, new Artefato { Repo = repoB, Contexto = "/SISTEMA", DocBase = docB }]
    };
    var bloqueouConflito = false;
    try { Tomcat.PrepararBase(conflito); }
    catch (ErroRunner) { bloqueouConflito = true; }
    Checar(bloqueouConflito, "contextos duplicados por caixa devem falhar antes da publicação");
    var publicadosMulti = Tomcat.PrepararBase(multi);
    Checar(publicadosMulti.Count == 2 && publicadosMulti.Any(x => x.Contexto == "/sistema") &&
        publicadosMulti.Any(x => x.Contexto == "/api"), "mesmo Tomcat deve publicar dois contextos");
    Checar(File.Exists(Path.Combine(multi.Base, "conf", "Catalina", "localhost", "sistema.xml")) &&
        File.Exists(Path.Combine(multi.Base, "conf", "Catalina", "localhost", "api.xml")),
        "dois descritores Tomcat devem coexistir");
    PreparacaoTomcat.Registrar(multi);
    Checar(PreparacaoTomcat.Atualizado(multi), "cache Maven para dois repositórios");
    File.WriteAllText(Path.Combine(repoB, "arquivo-novo.txt"), "alterado");
    Checar(!PreparacaoTomcat.Atualizado(multi), "alteração no segundo repositório invalida cache conjunto");
    Console.WriteLine("OK #22 dois repositórios, descoberta, indisponibilidade, publicação e cache");

    var configJvm = System.Text.Json.Nodes.JsonNode.Parse("""
        {"perfis":{"dev":{"tipo":"tomcat","vmArgs":"-Xmx512m -Dvalidar-xxxxx=true","artefatos":[]},
                   "prod":{"tipo":"tomcat","vmArgs":"-Dvalidar-xxxxx=false","artefatos":[]}}}
        """) as System.Text.Json.Nodes.JsonObject ?? throw new Exception("fixture JVM inválida");
    Checar(Config.LerPerfil(configJvm, "dev").VmArgs == "-Xmx512m -Dvalidar-xxxxx=true" &&
        Config.LerPerfil(configJvm, "prod").VmArgs == "-Dvalidar-xxxxx=false",
        "argumentos JVM persistem por perfil sem compartilhar valores");
    var baseJvm = Path.Combine(raiz, "base-jvm");
    Directory.CreateDirectory(baseJvm);
    var script = Path.Combine(tomcat, "bin", So.Catalina);
    if (So.Windows)
        File.WriteAllText(script, "@echo off\r\necho %CATALINA_OPTS% > \"%CATALINA_BASE%\\jvm-args.txt\"\r\n");
    else
        File.WriteAllText(script, "#!/bin/sh\nprintf '%s' \"$CATALINA_OPTS\" > \"$CATALINA_BASE/jvm-args.txt\"\n");
    var perfilJvm = new Perfil
    {
        Nome = "Jvm", Tipo = "tomcat", Home = tomcat, JavaHome = tomcat, Base = baseJvm,
        VmArgs = Config.LerPerfil(configJvm, "dev").VmArgs
    };
    using (var filho = Tomcat.Catalina(perfilJvm, "run", false, false))
    {
        Checar(filho.WaitForExit(10000) && filho.ExitCode == 0, "script Tomcat simulado deve terminar");
    }
    Checar(File.ReadAllText(Path.Combine(baseJvm, "jvm-args.txt")).Contains("-Dvalidar-xxxxx=true"),
        "CATALINA_OPTS deve chegar ao script Tomcat");
    using (var filho = Tomcat.Catalina(perfilJvm, "jpda run", true, false))
    {
        Checar(filho.WaitForExit(10000) && filho.ExitCode == 0, "Tomcat Debug simulado deve terminar");
    }
    Checar(File.ReadAllText(Path.Combine(baseJvm, "jvm-args.txt")).Contains("-Dvalidar-xxxxx=true"),
        "CATALINA_OPTS deve chegar também no modo Debug");
    Console.WriteLine("OK #23 vmArgs isolados e encaminhados ao Tomcat normal e Debug");

    // #42: fixture Comando isolado; o prazo começa no pronto e só afeta a execução gerenciada.
    var comandoFixture = So.Windows ? "ping -n 30 127.0.0.1 > nul" : "sleep 30";
    var shellFixture = So.Windows ? "cmd" : "sh";
    var configAuto = new System.Text.Json.Nodes.JsonObject
    {
        ["perfis"] = new System.Text.Json.Nodes.JsonObject
        {
            ["auto-fixture"] = new System.Text.Json.Nodes.JsonObject
            {
                ["tipo"] = "comando", ["comando"] = comandoFixture, ["shell"] = shellFixture, ["autoPararAposSeg"] = 1,
            },
            ["sem-auto-fixture"] = new System.Text.Json.Nodes.JsonObject
            {
                ["tipo"] = "comando", ["comando"] = comandoFixture, ["shell"] = shellFixture,
            },
        },
    };
    File.WriteAllText(Path.Combine(raiz, "config", "perfis.json"), configAuto.ToJsonString());
    var runnerAuto = new Runner();
    await runnerAuto.Iniciar("auto-fixture", false);
    Ultimo? ultimoAuto = null;
    for (var i = 0; i < 60 && ultimoAuto == null; i++)
    {
        ultimoAuto = (await runnerAuto.Status()).Single(s => s.Nome == "auto-fixture").Ultimo;
        if (ultimoAuto == null) await Task.Delay(100);
    }
    Checar(ultimoAuto?.Motivo == "auto-duracao", "auto-parada deve encerrar o Comando fixture com motivo próprio");

    await runnerAuto.Iniciar("sem-auto-fixture", false);
    await Task.Delay(1500);
    var semAuto = (await runnerAuto.Status()).Single(s => s.Nome == "sem-auto-fixture");
    Checar(semAuto.Execucao != null && semAuto.Ultimo == null, "perfil sem autoPararAposSeg não pode ser afetado");
    await runnerAuto.Parar("sem-auto-fixture");

    await runnerAuto.Iniciar("auto-fixture", false);
    await runnerAuto.Parar("auto-fixture");
    var manual = (await runnerAuto.Status()).Single(s => s.Nome == "auto-fixture").Ultimo;
    Checar(manual?.Motivo == "usuario", "stop manual antes do prazo deve vencer a auto-parada");
    await runnerAuto.Iniciar("auto-fixture", false);
    var pidAntesReinicio = (await runnerAuto.Status()).Single(s => s.Nome == "auto-fixture").Execucao?.Pid;
    runnerAuto.ReservarReinicio("auto-fixture");
    Ultimo? depoisReinicio = null;
    var viuIntervaloParado = false;
    var viuNovoProcesso = false;
    for (var i = 0; i < 160 && depoisReinicio?.Motivo != "auto-duracao"; i++)
    {
        var estadoReinicio = (await runnerAuto.Status()).Single(s => s.Nome == "auto-fixture");
        viuIntervaloParado |= estadoReinicio.Execucao == null;
        viuNovoProcesso |= estadoReinicio.Execucao?.Pid is int pid && pid != pidAntesReinicio;
        depoisReinicio = estadoReinicio.Ultimo;
        if (depoisReinicio?.Motivo != "auto-duracao") await Task.Delay(100);
    }
    Checar(viuNovoProcesso && depoisReinicio?.Motivo == "auto-duracao",
        "reinício deve criar novo prazo sem corrida com o stop anterior (intervalo=" + viuIntervaloParado +
        ", novo=" + viuNovoProcesso + ", motivo=" + depoisReinicio?.Motivo + ")");
    Console.WriteLine("OK #42 auto-parada desde pronto, opt-in, stop/restart e ownership do Runner");

    // #39: uma porta ocupada sem pit.pid/ownership não pode ser parada em lote.
    using (var portaAlheia = new TcpListener(IPAddress.Loopback, 0))
    {
        portaAlheia.Start();
        var porta = ((IPEndPoint)portaAlheia.LocalEndpoint).Port;
        Directory.CreateDirectory(Path.Combine(raiz, "config"));
        File.WriteAllText(Path.Combine(raiz, "config", "perfis.json"),
            "{\"perfis\":{\"sem-dono\":{\"tipo\":\"tomcat\",\"porta\":" + porta +
            ",\"portaShutdown\":" + (porta + 1) + ",\"artefatos\":[]}}}");
        await new Runner().PararTodos(["sem-dono"]);
        using var prova = new TcpClient();
        await prova.ConnectAsync(IPAddress.Loopback, porta);
        Console.WriteLine("OK #39 parada em lote não usa porta sem ownership");
    }

    Console.WriteLine("INTEGRATION OK");
}
finally
{
    Environment.SetEnvironmentVariable("PIT_RAIZ", raizAnterior);
    try { if (Directory.Exists(raiz)) Directory.Delete(raiz, true); }
    catch (IOException) { }
}
