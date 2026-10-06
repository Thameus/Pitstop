using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace Pitstop.Setup;

sealed class InstallOptions
{
    public string Destination { get; set; } = "";
    public bool DesktopShortcut { get; set; }
    public bool AddToPath { get; set; } = true;
    public bool LaunchAfterInstall { get; set; } = true;
    public bool DownloadJdk { get; set; }
    public bool DownloadMaven { get; set; }
    public bool DownloadNode { get; set; }
    public string DownloadTomcat { get; set; } = "";
    public string JdkHome { get; set; } = "";
    public string MavenHome { get; set; } = "";
    public string TomcatHome { get; set; } = "";
    public string NodeHome { get; set; } = "";
    public string ProjectsDir { get; set; } = "";
    public bool TestIsolation { get; set; }
}

static class InstallerEngine
{
    public static string NormalizeDestination(string raw)
    {
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"')));
        var root = Path.GetPathRoot(full);
        if (!string.IsNullOrEmpty(root) &&
            string.Equals(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                          root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                          StringComparison.OrdinalIgnoreCase))
            return Path.Combine(root, "Pitstop");
        return full;
    }

    public static int SmokeRootPath()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrWhiteSpace(root)) return 8;
            var expected = Path.Combine(root, "Pitstop");
            var actual = NormalizeDestination(root);
            if (!string.Equals(Path.GetFullPath(actual), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase))
                return 9;

            var normal = Path.Combine(root, "Apps", "Pitstop");
            if (!string.Equals(NormalizeDestination(normal), Path.GetFullPath(normal), StringComparison.OrdinalIgnoreCase))
                return 10;

            return 0;
        }
        catch { return 11; }
    }

    public static int SmokePayload()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "pitstop-setup-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            ExtractPayload(tmp);
            var pit = Path.Combine(tmp, "app", "pit.exe");
            if (!File.Exists(Path.Combine(tmp, "app", "Pitstop.exe")) ||
                !File.Exists(Path.Combine(tmp, "instalar.ps1")) ||
                !File.Exists(Path.Combine(tmp, "ferramentas.ps1")) ||
                !File.Exists(pit))
                return 2;

            var psi = new ProcessStartInfo(pit, "status") { UseShellExecute = false, CreateNoWindow = true };
            psi.Environment["PIT_RAIZ"] = tmp;
            psi.Environment["PIT_SEM_AUTOSTART"] = "1";
            using var p = Process.Start(psi);
            if (p == null) return 3;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch { return 4; }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    public static int SmokeInstall()
    {
        var destino = Path.Combine(Path.GetTempPath(), "pitstop-setup-install-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            var o = new InstallOptions
            {
                Destination = destino,
                DesktopShortcut = false,
                AddToPath = false,
                LaunchAfterInstall = false,
                TestIsolation = true
            };
            InstallAsync(o, _ => { }).GetAwaiter().GetResult();

            if (!File.Exists(Path.Combine(destino, "app", "Pitstop.exe")) ||
                !File.Exists(Path.Combine(destino, "app", "pit.exe")) ||
                !File.Exists(Path.Combine(destino, "desinstalar.ps1")))
                return 5;

            return 0;
        }
        catch { return 7; }
        finally
        {
            try { if (Directory.Exists(destino)) Directory.Delete(destino, true); } catch { }
        }
    }

    public static async Task InstallAsync(InstallOptions o, Action<string> log, CancellationToken ct = default)
    {
        if (!Environment.Is64BitOperatingSystem)
            throw new InvalidOperationException("O Pitstop precisa do Windows 64 bits.");

        var tmp = Path.Combine(Path.GetTempPath(), "pitstop-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            log("Extraindo os arquivos do Pitstop...");
            ExtractPayload(tmp);

            var installScript = Path.Combine(tmp, "instalar.ps1");
            var toolsScript = Path.Combine(tmp, "ferramentas.ps1");
            if (!File.Exists(installScript)) throw new FileNotFoundException("instalar.ps1 não está no payload.");
            if (!File.Exists(toolsScript)) throw new FileNotFoundException("ferramentas.ps1 não está no payload.");

            var installArgs = new List<string>
            {
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", installScript,
                "-Destino", o.Destination, "-Silencioso", "-NaoAbrir"
            };
            if (o.DesktopShortcut) installArgs.Add("-Desktop");
            if (!o.AddToPath) installArgs.Add("-SemPath");
            if (o.TestIsolation)
            {
                installArgs.Add("-SemMenu");
                installArgs.Add("-SemRegistro");
            }

            log("Instalando o Pitstop...");
            await RunAsync("powershell.exe", installArgs, log, ct);

            var toolArgs = new List<string>
            {
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", toolsScript,
                "-Destino", o.Destination
            };
            AddSwitch(toolArgs, "-BaixarJdk", o.DownloadJdk);
            AddSwitch(toolArgs, "-BaixarMaven", o.DownloadMaven);
            AddSwitch(toolArgs, "-BaixarNode", o.DownloadNode);
            AddValue(toolArgs, "-BaixarTomcat", o.DownloadTomcat);
            if (!o.DownloadJdk) AddValue(toolArgs, "-JdkHome", o.JdkHome);
            if (!o.DownloadMaven) AddValue(toolArgs, "-MavenHome", o.MavenHome);
            if (string.IsNullOrWhiteSpace(o.DownloadTomcat)) AddValue(toolArgs, "-TomcatHome", o.TomcatHome);
            if (!o.DownloadNode) AddValue(toolArgs, "-NodeHome", o.NodeHome);
            AddValue(toolArgs, "-ProjetosDir", o.ProjectsDir);

            if (toolArgs.Count > 7)
            {
                log("Configurando ferramentas opcionais...");
                await RunAsync("powershell.exe", toolArgs, log, ct);
            }

            if (o.LaunchAfterInstall)
            {
                var exe = Path.Combine(o.Destination, "app", "Pitstop.exe");
                if (!File.Exists(exe)) throw new FileNotFoundException("Pitstop.exe não foi instalado.", exe);
                log("Abrindo o Pitstop...");
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            }
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    static void AddSwitch(List<string> a, string name, bool enabled)
    {
        if (enabled) a.Add(name);
    }

    static void AddValue(List<string> a, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        a.Add(name);
        a.Add(value.Trim());
    }
    static void ExtractPayload(string destination)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Pitstop.Payload.zip")
            ?? throw new InvalidOperationException("Este executável não contém o payload do Pitstop.");
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in zip.Entries)
        {
            var full = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Caminho inválido no payload: " + entry.FullName);

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(full);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            entry.ExtractToFile(full, overwrite: true);
        }
    }

    static async Task RunAsync(string file, IEnumerable<string> args, Action<string> log, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log(e.Data); };
        if (!p.Start()) throw new InvalidOperationException("Não foi possível iniciar " + file);
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(file)} terminou com código {p.ExitCode}.");
    }
}
