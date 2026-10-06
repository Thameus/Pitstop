using System.Text;
using System.Text.RegularExpressions;

namespace Pitstop;

/// <summary>
/// Raiz do runner: a pasta que tem web/ui.html (.env, config/, web/, cache/, logs/ moram nela).
/// Os executáveis ficam em app/ (publicado) ou em src/.../bin/ (desenvolvimento): sobe até achar a raiz.
/// PIT_RAIZ no ambiente força outra pasta.
/// </summary>
public static class Raiz
{
    public static readonly string Dir = Descobrir();
    public static string Web => Path.Combine(Dir, "web");
    public static string Logs => Path.Combine(Dir, "logs");

    static string Descobrir()
    {
        var forcada = Environment.GetEnvironmentVariable("PIT_RAIZ");
        if (!string.IsNullOrEmpty(forcada)) return Path.GetFullPath(forcada);
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "web", "ui.html"))) return d.FullName;
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    }
}

/// <summary>.env da raiz (CHAVE=valor, # comenta, aspas opcionais). Valor vazio = ausente.</summary>
public static class Env
{
    static Dictionary<string, string> Valores = Ler();

    public static string Arquivo => Path.Combine(Raiz.Dir, ".env");

    /// <summary>Relê o .env (depois do assistente ou da tela de Ajustes gravarem).</summary>
    public static void Recarregar() => Valores = Ler();

    static Dictionary<string, string> Ler()
    {
        var r = new Dictionary<string, string>();
        var f = Arquivo;
        if (!File.Exists(f)) return r;
        foreach (var l in File.ReadAllLines(f, Encoding.UTF8))
        {
            if (Regex.IsMatch(l, @"^\s*#")) continue;
            var m = Regex.Match(l, @"^\s*([A-Z0-9_]+)\s*=\s*(.*?)\s*$");
            if (m.Success) r[m.Groups[1].Value] = Regex.Replace(m.Groups[2].Value, @"^([""'])(.*)\1$", "$2");
        }
        return r;
    }

    public static string? Txt(string chave) => Valores.TryGetValue(chave, out var v) && v != "" ? v : null;
    public static string Txt(string chave, string padrao) => Txt(chave) ?? padrao;
    public static int Num(string chave, int padrao) => int.TryParse(Txt(chave), out var n) && n > 0 ? n : padrao;
    public static bool Sim(string? v) => v != null && Regex.IsMatch(v, "^(1|true|sim|s)$", RegexOptions.IgnoreCase);
}

/// <summary>Erro de regra do runner: vira 400 { erro } na API e balão na tray.</summary>
public class ErroRunner(string mensagem) : Exception(mensagem)
{
    /// <summary>Stop durante build/compilação: não é falha.</summary>
    public bool Cancelado { get; init; }
}

public static class Agora
{
    /// <summary>Epoch em ms, como o Date.now() que a tela usa para calcular durações.</summary>
    public static long Ms => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static string Duracao(long ms)
    {
        var s = (long)Math.Round(ms / 1000.0);
        return s < 60 ? s + "s" : s / 60 + "m" + (s % 60).ToString("00") + "s";
    }
}
