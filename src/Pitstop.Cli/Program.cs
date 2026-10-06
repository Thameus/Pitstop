using System.Runtime.InteropServices;
using System.Text;
using Pitstop;

// pit — linha de comando do Pitstop (pit.cmd no Windows, ./pit no Linux, na raiz):
//   pit ui [porta]                      tela web em http://localhost:9999 (fechar o console para tudo)
//   pit up <perfil> [--debug] [--build] sobe no terminal atual (Ctrl+C para parar)
//        [--sem-compilar]              app Java: não compila antes (roda o target/classes como está)
//   pit down <perfil>                   mesmo Stop da tela
//   pit build <perfil>                  build dos artefatos ativos (app Java: classpath + cadeia inteira)
//   pit sync <perfil>                   copia estáticos/classes para a pasta explodida
//   pit status                          perfis, NO AR/parado, docBase resolvido
//   pit projetos                        repositórios e módulos war encontrados

// console do Windows em cp850/cp1252: sem o provedor o .NET não tem essas páginas de código
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

try
{
    return await Cli.Rodar(args);
}
catch (Exception e)
{
    Console.Error.WriteLine("[pit] ERRO " + e.Message);
    return 1;
}

static class Cli
{
    public static async Task<int> Rodar(string[] argv)
    {
        var cmd = argv.ElementAtOrDefault(0);
        var nome = argv.ElementAtOrDefault(1);
        bool Flag(string f) => argv.Contains(f);
        var runner = new Runner();

        if (cmd == "ui") return await Tela(runner, int.TryParse(nome, out var pt) ? pt : Env.Num("RUNNER_PORTA", 9999));

        // no terminal o log vai direto para o console
        runner.Logs.Direto = Console.WriteLine;

        if (cmd == "projetos")
        {
            foreach (var pr in Projetos.Listar())
            {
                Console.WriteLine(pr.Nome + "  (" + pr.Artefatos.Count + " war)");
                foreach (var a in pr.Artefatos)
                    Console.WriteLine("    " + (a.Modulo == "" ? "." : a.Modulo) + "  " + a.Contexto + "  " + (a.DocBase ?? "(sem pasta explodida)"));
            }
            return 0;
        }
        if (cmd == "ajustes")
        {
            Console.WriteLine("raiz:   " + Raiz.Dir);
            Console.WriteLine(".env:   " + Env.Arquivo + (File.Exists(Env.Arquivo) ? "" : "  (não existe: abra o Pitstop para o assistente)"));
            Console.WriteLine("perfis: " + Config.Arquivo);
            foreach (var (k, v) in Ajustes.Ler()) Console.WriteLine("  " + k.PadRight(18) + (v == "" ? "(padrão)" : v));
            return 0;
        }
        if (cmd == "status")
        {
            foreach (var s in await runner.Status())
            {
                Console.WriteLine((s.EmUso ? "NO AR " : "parado") + "  " + s.Nome +
                    s.Tipo switch
                    {
                        "java" => "  [java] " + (s.MainClass ?? s.Pacote),
                        "npm" => "  [npm] " + s.Script + "  :" + s.Porta + "  " + s.Url,
                        "comando" => "  [comando] " + (s.Comando ?? "") + (s.Porta is int pc ? "  :" + pc : ""),
                        _ => "  :" + s.Porta + "  " + s.Url,
                    });
                foreach (var a in s.Artefatos)
                    Console.WriteLine("          " + a.Contexto + " -> " + (a.DocBase ?? "(sem pasta explodida)"));
            }
            return 0;
        }
        if (cmd is null or "-h" or "--help" or "/?") { Ajuda(); return cmd is null ? 1 : 0; }
        if (nome == null) throw new ErroRunner("informe o perfil. Ex.: pit up meu-tomcat");
        var p = Config.LerPerfil(Config.Ler(), nome);

        if (cmd == "logs")
        {
            if (!p.EhComando) throw new ErroRunner("logs persistidos se aplicam ao perfil Comando");
            foreach (var l in LogArquivoComando.LerUltimo(p, Config.ReplayMax)) Console.WriteLine(l);
            return 0;
        }

        switch (cmd)
        {
            case "build":
                await runner.Build(nome, console: true);
                return 0;
            case "sync":
                runner.Sync(nome);
                return 0;
            case "down":
            case "stop":
                await runner.Parar(nome);
                return 0;
            case "restart":
                await runner.Parar(nome);
                p = Config.LerPerfil(Config.Ler(), nome);
                return await Up(runner, p, Flag("--debug"), Flag("--build"), Flag("--sem-compilar"));
            case "up":
            case "start":
                return await Up(runner, p, Flag("--debug"), Flag("--build"), Flag("--sem-compilar"));
            default:
                throw new ErroRunner("comando desconhecido: " + cmd);
        }
    }

    static async Task<int> Up(Runner runner, Perfil p, bool debug, bool build, bool semCompilar)
    {
        // Ctrl+C chega também ao Tomcat/java (mesmo console): aqui só espera ele sair. Sem isso o pit.exe
        // morreria antes e (no Windows) o Job Object derrubaria o filho sem o shutdown dele.
        using var sinal = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => c.Cancel = true);
        System.Diagnostics.Process child;
        string? scriptComando = null;
        if (p.EhComando)
        {
            if (debug) throw new ErroRunner("perfil Comando não tem modo Depurar");
            if (build) throw new ErroRunner("perfil Comando não tem Build");
            await Runner.ChecarLivre(p, false);
            child = runner.LancarComandoConsole(p, out scriptComando);
        }
        else if (p.EhJava)
        {
            // app Java no terminal: compila o que mudou (ou tudo com --build), sobe com a saída aqui
            Runner.ValidarJava(p);
            await Runner.ChecarLivre(p, debug);   // mesma checagem da tela: porta de debug e java.exe do pit.pid
            Directory.CreateDirectory(p.Cache);
            if (p.EhZip)
            {
                // pacote pronto: extrai se preciso (--build extrai de novo), sem Maven
                var (jarPacote, trabalho) = runner.PrepararZip(p.Nome, p, build, null);
                child = runner.LancarJava(p.Nome, p, jarPacote, debug, console: true, trabalho);
            }
            else
            {
                var jar = await runner.PrepararJava(p.Nome, p, build, semCompilar || !p.CompilarAntes, console: true);
                child = runner.LancarJava(p.Nome, p, jar, debug, console: true);
            }
        }
        else if (p.EhNpm)
        {
            // npm no terminal: --build roda o comando de build do perfil antes
            if (build) await runner.Build(p.Nome, console: true);
            await Runner.ValidarNpm(p, debug);   // mesma checagem da tela: package.json, script, node_modules, porta
            child = runner.LancarNpm(p.Nome, p, console: true);
        }
        else
        {
            if (build) await runner.Build(p.Nome, console: true);
            await Runner.ChecarLivre(p, debug);   // mesma checagem da tela: HTTP, shutdown, debug, JMX, AJP
            if (p.EhWar) runner.PrepararWar(p.Nome, p, false, null);   // war ainda não extraído
            foreach (var x in Tomcat.PrepararBase(p)) Console.WriteLine("[pit] " + x.Contexto + " -> " + x.DocBase);
            child = Tomcat.Catalina(p, debug ? "jpda run" : "run", debug, redirecionar: false);
            PidArquivo.Gravar(p, child);
        }
        try
        {
            await child.WaitForExitAsync();
            return child.ExitCode;
        }
        finally
        {
            if (scriptComando != null) ComandoApp.ApagarScript(scriptComando);
        }
    }

    /// <summary>Tela com console: fechar a janela ou Ctrl+C derruba o runner e os Tomcats dele.</summary>
    static async Task<int> Tela(Runner runner, int porta)
    {
        var srv = new Servidor(runner, porta);
        var abrirVar = Environment.GetEnvironmentVariable("RUNNER_ABRIR_NAVEGADOR");
        void Abrir()
        {
            // variável de ambiente do processo manda sobre o .env
            if (Env.Sim(string.IsNullOrEmpty(abrirVar) ? Env.Txt("RUNNER_ABRIR_NAVEGADOR", "true") : abrirVar)) Proc.AbrirUrl(srv.Endereco);
        }
        if (!await srv.Iniciar())
        {
            Console.WriteLine("Runner já está no ar em " + srv.Endereco);
            Abrir();
            return 0;
        }
        void Encerrar()
        {
            // stop gracioso de cada perfil (Tomcat: catalina stop) e só então sai; o que não parar no prazo morre
            runner.IniciarSaida();
            try { runner.PararTodos().Wait(TimeSpan.FromSeconds(Config.StopTimeout + 5)); } catch { }
            runner.MatarTodos();
            Environment.Exit(0);
        }
        srv.Encerrar = Encerrar;
        // Ctrl+C, Ctrl+Break e fechar a janela (SIGHUP = CTRL_CLOSE_EVENT no Windows); SIGTERM no Linux (kill, systemd)
        using var s1 = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => { c.Cancel = true; Encerrar(); });
        using var s2 = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, c => { c.Cancel = true; Encerrar(); });
        using var s3 = PosixSignalRegistration.Create(PosixSignal.SIGHUP, c => { c.Cancel = true; Encerrar(); });
        using var s4 = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; Encerrar(); });
        try { await runner.IniciarAutomaticos(); }
        catch (Exception ex) { Console.Error.WriteLine("[pit] ERRO início automático: " + ex.Message); }
        Console.WriteLine("Pitstop em " + srv.Endereco + "  (Ctrl+C ou fechar o terminal para o runner e o que ele subiu)");
        Abrir();
        await Task.Delay(Timeout.Infinite);
        return 0;
    }

    static void Ajuda() => Console.WriteLine("""
        uso: pit <comando> [perfil] [flags]
          ui [porta]                         tela web (padrão 9999 / RUNNER_PORTA)
          up <perfil> [--debug] [--build]    sobe no terminal atual (Ctrl+C para parar)
          up <app> [--sem-compilar]          app Java: não compila antes
          down|stop <perfil>                 para o perfil
          start|up <perfil>                  inicia no terminal atual
          restart <perfil>                   para e inicia novamente no terminal
          logs <perfil>                      último log persistido de um perfil Comando
          build <perfil>                     build dos artefatos ativos
          sync <perfil>                      copia estáticos/classes para a pasta explodida
          status                             perfis, NO AR/parado, docBase resolvido
          projetos                           repositórios e módulos war encontrados
          ajustes                            ajustes globais (.env) e onde ficam os arquivos
        """);
}
