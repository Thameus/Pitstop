using System.Text.Json;

namespace Pitstop.App;

/// <summary>O que a janela lembra entre uma abertura e outra (cache/tela.json). Conveniência: sem gravar, abre no padrão.</summary>
sealed class Prefs
{
    public string? Perfil { get; set; }
    public string? Aba { get; set; }
    /// <summary>"claro" / "escuro"; null = segue o sistema.</summary>
    public string? Tema { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? W { get; set; }
    public double? H { get; set; }
    public bool Max { get; set; }

    static string Arquivo => Path.Combine(Raiz.Dir, "cache", "tela.json");

    public static Prefs Atual { get; private set; } = new();

    public static void Carregar()
    {
        try { if (File.Exists(Arquivo)) Atual = JsonSerializer.Deserialize<Prefs>(File.ReadAllText(Arquivo)) ?? new(); }
        catch { Atual = new(); }
    }

    public static void Gravar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Arquivo)!);
            File.WriteAllText(Arquivo, JsonSerializer.Serialize(Atual));
        }
        catch { /* preferência é conveniência */ }
    }
}
