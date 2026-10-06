using Microsoft.Win32;

namespace Pitstop.App;

/// <summary>
/// "Iniciar com o sistema": no Windows, o valor Pitstop em HKCU\...\Run; no Linux, ~/.config/autostart/pitstop.desktop
/// (padrão XDG, sem root). Os dois sobem o Pitstop com --auto (só a bandeja, sem abrir a janela).
/// </summary>
static class Autostart
{
    const string Nome = "Pitstop";
    const string ChaveRun = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>PIT_SEM_AUTOSTART=1: execução de teste/portátil, não lê nem grava o autostart do sistema.</summary>
    static bool Desligado => Environment.GetEnvironmentVariable("PIT_SEM_AUTOSTART") == "1";

    static string Exe => Environment.ProcessPath ?? "";
    static string Comando => "\"" + Exe + "\" --auto";

    static string ArquivoLinux => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "autostart", "pitstop.desktop");

    public static bool Ligado
    {
        get
        {
            if (Desligado) return false;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var k = Registry.CurrentUser.OpenSubKey(ChaveRun);
                    return k?.GetValue(Nome) != null;
                }
                return File.Exists(ArquivoLinux);
            }
            catch { return false; }
        }
    }

    public static void Definir(bool ligar)
    {
        if (Desligado) return;
        if (OperatingSystem.IsWindows())
        {
            using var k = Registry.CurrentUser.CreateSubKey(ChaveRun);
            if (ligar) k.SetValue(Nome, Comando);
            else if (k.GetValue(Nome) != null) k.DeleteValue(Nome, false);
            return;
        }
        if (!ligar) { if (File.Exists(ArquivoLinux)) File.Delete(ArquivoLinux); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(ArquivoLinux)!);
        File.WriteAllText(ArquivoLinux, string.Join("\n",
            "[Desktop Entry]",
            "Type=Application",
            "Name=Pitstop",
            "Comment=Runner local de Tomcat, Java, npm e comandos personalizados",
            "Exec=" + Comando.Replace("%", "%%"),
            "Icon=" + Path.Combine(Raiz.Web, "icone.png"),
            "Terminal=false",
            "X-GNOME-Autostart-enabled=true",
            "") );
    }

    /// <summary>Pasta do Pitstop mudou de lugar: o valor ligado aponta para o executável antigo. Regrava.</summary>
    public static void Corrigir()
    {
        if (Desligado) return;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var k = Registry.CurrentUser.OpenSubKey(ChaveRun, writable: true);
                if (k?.GetValue(Nome) is not string atual || atual == Comando) return;
                k.SetValue(Nome, Comando);
                Registro.Erro("autostart apontava para outro executável (" + atual + "); regravado");
                return;
            }
            if (File.Exists(ArquivoLinux) && !File.ReadAllText(ArquivoLinux).Contains(Comando.Replace("%", "%%"))) Definir(true);
        }
        catch (Exception e) { Registro.Erro(e); }
    }
}
