using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Pitstop.App;

static class Program
{
    /// <summary>
    /// Pitstop [--auto] [--porta N]. --auto = veio do "iniciar com o sistema" (sobe só na bandeja, sem abrir a janela;
    /// com o Pitstop já no ar, não faz nada). Sem --auto e com o Pitstop no ar: só traz a janela para a frente.
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        var auto = args.Any(a => a.Equals("--auto", StringComparison.OrdinalIgnoreCase));
        var i = Array.FindIndex(args, a => a.Equals("--porta", StringComparison.OrdinalIgnoreCase));
        var porta = i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var n) ? n : Env.Num("RUNNER_PORTA", 9999);

        // instância única por porta (Mutex com nome vale entre processos no Windows e no Linux)
        using var mutex = new Mutex(true, "PitstopApp-" + porta, out var novo);
        if (!novo)
        {
            if (!auto) Instancia.PedirJanela(porta);
            return 0;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Registro.Erro(e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => { Registro.Erro(e.Exception); e.SetObserved(); };

        Instancia.Porta = porta;
        Instancia.Auto = auto;
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception e)
        {
            Registro.Erro(e);
            throw;
        }
        finally
        {
            Instancia.Runner.MatarTodos();   // o que não parou no prazo (normalmente nada)
        }
    }

    /// <summary>Usado também pelo designer do Avalonia.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions { DefaultFamilyName = "avares://Pitstop/Fontes#Space Grotesk" })
            .With(new X11PlatformOptions { WmClass = "pitstop" })
            .LogToTrace();
}

/// <summary>Estado do processo: porta, origem (--auto) e o Runner único (janela, bandeja e servidor usam o mesmo).</summary>
static class Instancia
{
    public static int Porta { get; set; }
    public static bool Auto { get; set; }
    public static Runner Runner { get; } = new();

    /// <summary>Segunda instância: o Pitstop no ar mostra a janela dele (127.0.0.1: o Kestrel só escuta em IPv4).</summary>
    public static void PedirJanela(int porta)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var req = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:" + porta + "/api/tela");
            req.Headers.Add("X-PIT", "1");
            using var r = http.Send(req);
            if (!r.IsSuccessStatusCode) Registro.Erro("POST /api/tela: " + (int)r.StatusCode);
        }
        catch (Exception e) { Registro.Erro(e); }
    }
}

/// <summary>logs/pitstop.log; o mesmo erro repetido entra no máximo a cada 5 min.</summary>
static class Registro
{
    static readonly object Trava = new();
    static string ultimo = "";
    static DateTime em = DateTime.MinValue;

    public static void Erro(object? erro)
    {
        try
        {
            var txt = (erro?.ToString() ?? "").Trim();
            lock (Trava)
            {
                if (txt == ultimo && (DateTime.Now - em).TotalMinutes < 5) return;
                ultimo = txt;
                em = DateTime.Now;
                Directory.CreateDirectory(Raiz.Logs);
                File.AppendAllText(Path.Combine(Raiz.Logs, "pitstop.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {txt}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
