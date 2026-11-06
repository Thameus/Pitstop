using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pitstop;

static void Check(bool value, string message)
{
    if (!value) throw new Exception("FALHA: " + message);
}

static void WriteText(string path, string text)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, text);
}

var root = Path.Combine(Path.GetTempPath(), "pitstop-six-types-" + Guid.NewGuid().ToString("N"));
var oldRoot = Environment.GetEnvironmentVariable("PIT_RAIZ");
var runners = new List<Runner>();
var startedNames = new List<string>();
TcpListener? external = null;
try
{
    Directory.CreateDirectory(root);
    Environment.SetEnvironmentVariable("PIT_RAIZ", root);

    var fakeTomcat = Path.Combine(root, "fake-tomcat");
    WriteText(Path.Combine(fakeTomcat, "bin", So.Catalina), So.Windows ? "@echo off\r\n" : "#!/bin/sh\nexit 0\n");
    WriteText(Path.Combine(fakeTomcat, "conf", "server.xml"),
        "<Server port=\"8005\"><Service><Connector port=\"8080\" protocol=\"HTTP/1.1\" /></Service></Server>");
    WriteText(Path.Combine(fakeTomcat, "conf", "logging.properties"), "handlers=java.util.logging.ConsoleHandler\n");
    WriteText(Path.Combine(fakeTomcat, "lib", "catalina.jar"), "fake");
    var fakeJava = Path.Combine(root, "fake-java");
    WriteText(Path.Combine(fakeJava, "bin", So.Exe("java")), "not a JVM");
    var npmApp = Path.Combine(root, "npm-app");
    WriteText(Path.Combine(root, So.Exe("node")), "fake node");
    WriteText(Path.Combine(root, So.Lancador("npm")), "fake npm");
    WriteText(Path.Combine(npmApp, "package.json"), "{\"name\":\"fixture\",\"scripts\":{\"dev\":\"echo ready\",\"build\":\"echo build\"}}\n");
    Directory.CreateDirectory(Path.Combine(npmApp, "node_modules"));

    var repo = Path.Combine(root, "web-repo");
    WriteText(Path.Combine(repo, "pom.xml"), "<project><modelVersion>4.0.0</modelVersion><groupId>fixture</groupId><artifactId>fixture</artifactId><version>1</version></project>");
    WriteText(Path.Combine(repo, "target", "app", "WEB-INF", "web.xml"), "<web-app/>");
    WriteText(Path.Combine(repo, "src", "main", "webapp", "index.html"), "fixture");
    var war = Path.Combine(root, "release.war");
    using (var z = ZipFile.Open(war, ZipArchiveMode.Create))
    {
        var e = z.CreateEntry("WEB-INF/web.xml");
        using var w = new StreamWriter(e.Open());
        w.Write("<web-app/>");
    }
    var zip = Path.Combine(root, "release.zip");
    using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
    {
        var e = z.CreateEntry("app/app.jar");
        using var w = new StreamWriter(e.Open());
        w.Write("fake jar");
    }

    external = new TcpListener(IPAddress.Loopback, 0);
    external.Start();
    var foreignPort = ((IPEndPoint)external.LocalEndpoint).Port;

    var profiles = new JsonObject
    {
        ["basesDir"] = Path.Combine(root, "bases"),
        ["javaHome"] = fakeJava,
        ["nodeHome"] = root,
        ["perfis"] = new JsonObject
        {
            ["tomcat"] = new JsonObject
            {
                ["tipo"] = "tomcat", ["tomcatHome"] = fakeTomcat, ["porta"] = 18101,
                ["artefatos"] = new JsonArray
                {
                    new JsonObject { ["repo"] = repo, ["contexto"] = "/app", ["ativo"] = true },
                    new JsonObject { ["repo"] = Path.Combine(root, "inactive"), ["contexto"] = "/off", ["ativo"] = false }
                }
            },
            ["war"] = new JsonObject
            {
                ["tipo"] = "war", ["tomcatHome"] = fakeTomcat, ["porta"] = 18102,
                ["prontoUrl"] = "http://127.0.0.1:18102/health", ["falhaLog"] = "WAR-FAIL",
                ["artefatos"] = new JsonArray { new JsonObject { ["war"] = war, ["contexto"] = "/release" } }
            },
            ["java"] = new JsonObject
            {
                ["tipo"] = "java", ["modulo"] = repo, ["mainClass"] = "fixture.Main",
                ["prontoLog"] = "READY", ["falhaLog"] = "JAVA-FAIL", ["autoPararAposSeg"] = 9
            },
            ["zip"] = new JsonObject
            {
                ["tipo"] = "zip", ["pacote"] = zip, ["jar"] = "app/app.jar", ["trabalho"] = "app",
                ["prontoLog"] = "READY", ["falhaLog"] = "ZIP-FAIL"
            },
            ["npm"] = new JsonObject
            {
                ["tipo"] = "npm", ["pasta"] = npmApp, ["script"] = "dev", ["porta"] = 18105,
                ["env"] = "FIXTURE=yes", ["falhaLog"] = "NPM-FAIL"
            },
            ["foreign"] = new JsonObject
            {
                ["tipo"] = "comando", ["pasta"] = root, ["comando"] = "echo foreign", ["porta"] = foreignPort
            },
            ["cmd-a"] = new JsonObject { ["tipo"] = "comando", ["pasta"] = root, ["shell"] = "cmd", ["comando"] = "echo READY\necho A\nping -n 30 127.0.0.1 > nul", ["prontoLog"] = "READY" },
            ["cmd-b"] = new JsonObject { ["tipo"] = "comando", ["pasta"] = root, ["shell"] = "cmd", ["comando"] = "echo READY\necho B\nping -n 30 127.0.0.1 > nul", ["prontoLog"] = "READY" },
            ["cmd-c"] = new JsonObject { ["tipo"] = "comando", ["pasta"] = root, ["shell"] = "cmd", ["comando"] = "echo READY\necho C\nping -n 30 127.0.0.1 > nul", ["prontoLog"] = "READY" },
            ["cmd-fail"] = new JsonObject { ["tipo"] = "comando", ["pasta"] = root, ["shell"] = "cmd", ["comando"] = "echo READY\necho FAIL-LITERAL\nping -n 30 127.0.0.1 > nul", ["prontoLog"] = "READY", ["falhaLog"] = "FAIL-LITERAL" }
        }
    };
    WriteText(Path.Combine(root, "config", "perfis.json"), profiles.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    var cfg = Config.Ler();
    var names = Config.NomesPerfis(cfg).ToArray();
    Check(names.Length == 10, "configuração isolada com os seis tipos, foreign e quatro comandos (" + string.Join(",", names) + ")");
    var tom = Config.LerPerfil(cfg, "tomcat");
    Check(tom.Tipo == "tomcat" && tom.PortaShutdown == 18026 && tom.Base.EndsWith(Path.Combine("bases", "tomcat")) && tom.Artefatos.Count == 1, "defaults, Base e artefato ativo");
    var warP = Config.LerPerfil(cfg, "war");
    Check(warP.EhWar && warP.Artefatos.Single().Extracao.Contains("release-"), "normalização do war e extração determinística");
    var javaP = Config.LerPerfil(cfg, "java");
    Check(javaP.EhJava && javaP.Base == javaP.Cache && javaP.FalhaLog == "JAVA-FAIL" && javaP.AutoPararAposSeg == 9, "normalização java");
    var zipP = Config.LerPerfil(cfg, "zip");
    Check(zipP.EhZip && zipP.Pacote == zip && Path.GetFileName(zipP.Extracao).StartsWith("pacote-"), "normalização zip");
    var npmP = Config.LerPerfil(cfg, "npm");
    Check(npmP.EhNpm && npmP.Script == "dev" && npmP.Url == "http://localhost:18105/" && npmP.Env == "FIXTURE=yes", "normalização npm");
    var cmdP = Config.LerPerfil(cfg, "cmd-a");
    Check(cmdP.EhComando && cmdP.Base == cmdP.Cache && cmdP.Pasta == root, "normalização comando");
    Console.WriteLine("OK configuração/normalização dos seis tipos, defaults, Base/Cache e artefatos");

    var pub = Tomcat.PrepararBase(tom);
    Check(pub.Count == 1 && File.Exists(Path.Combine(tom.Base, "conf", "server.xml")) && File.Exists(Path.Combine(tom.Base, "conf", "Catalina", "localhost", "app.xml")), "PrepararBase Tomcat e paths");
    Check(Tomcat.ValidarProntoUrl(warP)!.Host == "127.0.0.1", "ValidarProntoUrl loopback");
    var rejected = false;
    var remoteWar = new Perfil { Nome = warP.Nome, Tipo = warP.Tipo, Porta = warP.Porta, ProntoUrl = "http://example.com:18102/" };
    try { Tomcat.ValidarProntoUrl(remoteWar); } catch (ErroRunner) { rejected = true; }
    Check(rejected, "ValidarProntoUrl rejeita host remoto");
    var runner = new Runner();
    runners.Add(runner);
    runner.PrepararWar("war", warP, false, null);
    Check(File.Exists(Path.Combine(warP.Artefatos[0].Extracao, "WEB-INF", "web.xml")), "extração do WAR artificial");
    Console.WriteLine("OK Tomcat/WAR: fake home, health URL, PrepararBase, extração e paths");

    Runner.ValidarJava(javaP);
    Runner.ValidarJava(zipP);
    var zipPrepared = runner.PrepararZip("zip", zipP, false, null);
    Check(File.Exists(zipPrepared.Jar) && zipPrepared.Trabalho.EndsWith(Path.Combine("release-")) == false, "preparação ZIP/JAR sem JVM");
    Check(NodeApp.Scripts(npmApp).Select(x => x.Nome).SequenceEqual(["dev", "build"]), "scripts do package.json");
    NodeApp.Validar(npmP);
    var env = new Dictionary<string, string?>();
    NodeApp.Ambiente(npmP, env);
    Check(env.GetValueOrDefault("BROWSER") == "none" && env.GetValueOrDefault("FIXTURE") == "yes" && NodeApp.LinhaStart(npmP).Contains("run dev"), "ambiente e builder npm");
    Console.WriteLine("OK Java/ZIP e npm: validação, pacote, scripts, builders e ambiente (sem JVM/Node)");

    var commandNames = new[] { "cmd-a", "cmd-b", "cmd-c", "cmd-fail" };
    var stopNames = commandNames.Append("foreign").ToArray();
    startedNames.AddRange(commandNames);
    await Task.WhenAll(commandNames.Select(name => runner.Iniciar(name, false)));
    List<StatusPerfil> status = [];
    for (var i = 0; i < 30; i++)
    {
        await Task.Delay(100);
        status = await runner.Status();
        if (status.Where(x => commandNames.Contains(x.Nome)).All(x => x.Execucao?.Pid is > 0)) break;
    }
    var running = status.Where(x => commandNames.Contains(x.Nome)).ToArray();
    Check(running.Length == 4 && running.Select(x => x.Nome).Distinct().Count() == 4 && running.Select(x => x.Execucao?.Pid).Distinct().Count() == 4, "quatro comandos simultâneos com nomes/PIDs distintos");
    for (var i = 0; i < 30 && status.Single(x => x.Nome == "cmd-fail").Execucao?.Falha != true; i++) { await Task.Delay(100); status = await runner.Status(); }
    Check(status.Single(x => x.Nome == "cmd-fail").Execucao?.LinhaFalha?.Contains("FAIL-LITERAL") == true, "falha literal no status");
    var falhaCapturada = status.Single(x => x.Nome == "cmd-fail").Execucao!.LinhaFalha ?? "";
    await runner.PararTodos(stopNames);
    await Task.Delay(150);

    var cli = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Pitstop.Cli", "bin", "Release", "net10.0", "pit.dll"));
    if (!File.Exists(cli)) throw new Exception("CLI Release ausente; execute dotnet build src/Pitstop.Cli/Pitstop.Cli.csproj -c Release");
    using var statusProcess = Process.Start(new ProcessStartInfo("dotnet", "\"" + cli + "\" status --json") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, Environment = { ["PIT_RAIZ"] = root } })!;
    var jsonText = await statusProcess.StandardOutput.ReadToEndAsync();
    await statusProcess.WaitForExitAsync();
    Check(statusProcess.ExitCode == 0, "CLI status --json retorna sucesso: " + await statusProcess.StandardError.ReadToEndAsync());
    var json = JsonNode.Parse(jsonText)!.AsArray();
    Check(json.Count == 10 && json.Any(x => x!["nome"]?.ToString() == "cmd-fail"), "status --json puro com perfis múltiplos: " + jsonText);
    Check(falhaCapturada.Contains("FAIL-LITERAL") && json.All(x => x!["estadoMemoria"]?.ToString() == "semPid" && x["base"] != null), "status --json/core inclui memória indisponível explícita, base e falha literal");
    Check(external.Server.IsBound, "listener externo não é ownership do Runner");
    using (var cliente = new TcpClient())
    {
        await cliente.ConnectAsync(IPAddress.Loopback, foreignPort);
        using var aceito = await external.AcceptTcpClientAsync();
        Check(aceito.Connected, "listener associado ao perfil foreign continua aceitando conexão após PararTodos");
    }
    Check(LogArquivoComando.LerUltimo(Config.LerPerfil(Config.Ler(), "cmd-fail")).Any(x => x.Contains("FAIL-LITERAL", StringComparison.Ordinal)), "log literal do comando");
    Console.WriteLine("OK comando: dois+ comandos curtos, log/falha literal, status --json, PararTodos incluindo foreign e listener preservado");
    Console.WriteLine("LIMITAÇÃO: Tomcat, JVM e Node/npm não são iniciados; CLI status --json é validado em processo separado, e falhaLog em execução é validado no Runner em memória.");
    Console.WriteLine("SIX TYPES OK");
}
finally
{
    foreach (var r in runners)
        try { await r.PararTodos(startedNames.Append("foreign")); } catch { }
    external?.Stop();
    Environment.SetEnvironmentVariable("PIT_RAIZ", oldRoot);
    try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
}
