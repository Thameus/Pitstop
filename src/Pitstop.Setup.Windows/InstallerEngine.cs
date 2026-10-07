using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

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
                !File.Exists(Path.Combine(tmp, "third-party", "dotnet", "LICENSE-INFORMATION-WINDOWS.md")) ||
                !File.Exists(Path.Combine(tmp, "third-party", "dotnet", "DOTNET-LIBRARY-LICENSE.html")) ||
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
                !File.Exists(Path.Combine(destino, "third-party", "dotnet", "LICENSE-INFORMATION-WINDOWS.md")) ||
                !File.Exists(Path.Combine(destino, "third-party", "dotnet", "DOTNET-LIBRARY-LICENSE.html")) ||
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

    public static int SmokeUpdate()
    {
        var destino = Path.Combine(Path.GetTempPath(), "pitstop-setup-update-smoke-" + Guid.NewGuid().ToString("N"));
        var cwdAnterior = Environment.CurrentDirectory;
        try
        {
            Directory.CreateDirectory(Path.Combine(destino, "app"));
            Directory.CreateDirectory(Path.Combine(destino, "config"));
            File.WriteAllText(Path.Combine(destino, "app", "versao-antiga.txt"), "antiga");
            File.WriteAllText(Path.Combine(destino, ".env"), "JDK_HOME=C:\\jdk-teste");
            File.WriteAllText(Path.Combine(destino, "config", "perfis.json"), "{\"perfis\":{\"teste\":{\"tipo\":\"comando\",\"comando\":\"echo ok\"}}}");

            // Regressão do updater V1: o Setup herdava <raiz>\app como diretório atual
            // e o próprio Windows impedia Directory.Move(app, app.update-old).
            Environment.CurrentDirectory = Path.Combine(destino, "app");
            UpdateExisting(["--update", "--destination", destino]);

            if (!File.Exists(Path.Combine(destino, "app", "Pitstop.exe"))) return 12;
            if (File.Exists(Path.Combine(destino, "app", "versao-antiga.txt"))) return 13;
            if (File.ReadAllText(Path.Combine(destino, ".env")) != "JDK_HOME=C:\\jdk-teste") return 14;
            if (!File.ReadAllText(Path.Combine(destino, "config", "perfis.json")).Contains("\"teste\"")) return 15;
            if (CaminhoDentro(Environment.CurrentDirectory, Path.Combine(destino, "app"))) return 17;
            return 0;
        }
        catch { return 16; }
        finally
        {
            Environment.CurrentDirectory = cwdAnterior;
            try { if (Directory.Exists(destino)) Directory.Delete(destino, true); } catch { }
        }
    }

    public static int SmokeUpdateBlocker()
    {
        var destino = Path.Combine(Path.GetTempPath(), "pitstop-setup-blocker-smoke-" + Guid.NewGuid().ToString("N"));
        Process? bloqueador = null;
        try
        {
            var app = Path.Combine(destino, "app");
            Directory.CreateDirectory(app);
            var pit = Path.Combine(app, "pit.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), pit, true);

            var psi = new ProcessStartInfo(pit)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = app,
                Arguments = "/d /c ping 127.0.0.1 -n 20 >nul"
            };
            bloqueador = Process.Start(psi);
            if (bloqueador == null) return 18;
            Thread.Sleep(300);

            try
            {
                AguardarBloqueadoresPitstop(destino);
                return 19;
            }
            catch (IOException ex)
            {
                return ex.Message.Contains("PID " + bloqueador.Id, StringComparison.Ordinal) ? 0 : 20;
            }
        }
        catch { return 21; }
        finally
        {
            try
            {
                if (bloqueador is { HasExited: false }) bloqueador.Kill(true);
                bloqueador?.WaitForExit(3000);
            }
            catch { }
            bloqueador?.Dispose();
            try { if (Directory.Exists(destino)) Directory.Delete(destino, true); } catch { }
        }
    }

    public static void UpdateExisting(string[] args)
    {
        var destinoArg = Argumento(args, "--destination")
            ?? throw new ArgumentException("Falta --destination.");
        var destino = NormalizeDestination(destinoArg);
        var pidTxt = Argumento(args, "--parent-pid");
        var reiniciar = args.Any(a => a.Equals("--restart", StringComparison.OrdinalIgnoreCase));

        // Defesa em profundidade: mesmo iniciado manualmente a partir de <raiz>\app,
        // o updater sai da árvore que precisará renomear.
        Environment.CurrentDirectory = Path.GetTempPath();

        using var mutex = new Mutex(false, NomeMutexUpdate(destino));
        var possuiMutex = false;
        try
        {
            try { possuiMutex = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { possuiMutex = true; }
            if (!possuiMutex)
                throw new InvalidOperationException("Já existe uma atualização do Pitstop em andamento para esta instalação.");

            if (!Directory.Exists(destino))
                throw new DirectoryNotFoundException("Instalação do Pitstop não encontrada: " + destino);

            // Extrai e valida a estrutura antes de esperar/parar a instalação antiga.
            var tmp = Path.Combine(Path.GetTempPath(), "pitstop-update-payload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                ExtractPayload(tmp);
                if (!File.Exists(Path.Combine(tmp, "app", "Pitstop.exe")))
                    throw new InvalidDataException("Payload de atualização sem app\\Pitstop.exe.");

                if (!string.IsNullOrWhiteSpace(pidTxt) && int.TryParse(pidTxt, out var pid) &&
                    pid > 0 && pid != Environment.ProcessId)
                {
                    try
                    {
                        using var pai = Process.GetProcessById(pid);
                        if (!pai.WaitForExit(120_000))
                            throw new TimeoutException("O Pitstop não encerrou no tempo esperado.");
                    }
                    catch (ArgumentException)
                    {
                        // O processo já terminou.
                    }
                }

                AguardarBloqueadoresPitstop(destino);
                ApplyUpdatePayload(tmp, destino);
                UpdateInstalledAppRegistration(destino);
            }
            finally
            {
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            }

            if (reiniciar)
            {
                var appDir = Path.Combine(destino, "app");
                var exe = Path.Combine(appDir, "Pitstop.exe");
                if (!File.Exists(exe)) throw new FileNotFoundException("Pitstop.exe não existe após a atualização.", exe);
                Process.Start(new ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    WorkingDirectory = appDir
                });
            }
        }
        finally
        {
            if (possuiMutex)
            {
                try { mutex.ReleaseMutex(); } catch { }
            }
        }
    }

    static string NomeMutexUpdate(string destino)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(destino).ToUpperInvariant()));
        return "Pitstop.Update." + Convert.ToHexString(bytes.AsSpan(0, 12));
    }

    static void AguardarBloqueadoresPitstop(string destino)
    {
        string[] bloqueadores = [];
        for (var tentativa = 0; tentativa < 20; tentativa++)
        {
            bloqueadores = ProcessosPitstopNaInstalacao(destino);
            if (bloqueadores.Length == 0) return;
            Thread.Sleep(250);
        }

        throw new IOException(
            "Outro processo do Pitstop ainda está usando esta instalação: " +
            string.Join(", ", bloqueadores) +
            ". Feche-o e tente a atualização novamente.");
    }

    static string[] ProcessosPitstopNaInstalacao(string destino)
    {
        var lista = new List<string>();
        foreach (var nome in new[] { "Pitstop", "pit" })
        {
            foreach (var processo in Process.GetProcessesByName(nome))
            {
                using (processo)
                {
                    if (processo.Id == Environment.ProcessId) continue;
                    try
                    {
                        var caminho = processo.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(caminho) && CaminhoDentro(caminho, destino))
                            lista.Add($"{processo.ProcessName}.exe (PID {processo.Id})");
                    }
                    catch
                    {
                        // Processos de outro usuário/sessão podem negar acesso ao módulo.
                    }
                }
            }
        }
        return lista.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    static bool CaminhoDentro(string caminho, string raiz)
    {
        var full = Path.GetFullPath(caminho).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetFullPath(raiz).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(full, root, StringComparison.OrdinalIgnoreCase) ||
               full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               full.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    static void IoComRetry(Action acao, string descricao, int tentativas = 20)
    {
        Exception? ultima = null;
        for (var tentativa = 1; tentativa <= tentativas; tentativa++)
        {
            try
            {
                acao();
                return;
            }
            catch (IOException ex) { ultima = ex; }
            catch (UnauthorizedAccessException ex) { ultima = ex; }

            if (tentativa < tentativas) Thread.Sleep(250);
        }

        throw new IOException(
            descricao + " falhou porque o Windows ainda está usando algum arquivo. " +
            "Feche terminais, Explorador ou outro processo aberto dentro da pasta do Pitstop e tente novamente.",
            ultima);
    }

    static void UpdateInstalledAppRegistration(string destino)
    {
        const string subKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Pitstop";
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKey, writable: true);
        if (key == null) return; // pacote portátil: não cria registro novo

        var registrado = key.GetValue("InstallLocation") as string;
        if (string.IsNullOrWhiteSpace(registrado) ||
            !string.Equals(Path.GetFullPath(registrado).TrimEnd('\\'),
                           Path.GetFullPath(destino).TrimEnd('\\'),
                           StringComparison.OrdinalIgnoreCase))
            return; // outra instalação/uma cópia portátil não altera este registro

        var exe = Path.Combine(destino, "app", "Pitstop.exe");
        var versao = FileVersionInfo.GetVersionInfo(exe).ProductVersion?.Split('+')[0] ?? "";
        if (versao != "")
            key.SetValue("DisplayVersion", versao, Microsoft.Win32.RegistryValueKind.String);
        key.SetValue("DisplayIcon", exe + ",0", Microsoft.Win32.RegistryValueKind.String);
        key.SetValue("InstallLocation", destino, Microsoft.Win32.RegistryValueKind.String);
    }

    static string? Argumento(string[] args, string nome)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(nome, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    static void ApplyUpdatePayload(string origem, string destino)
    {
        if (!File.Exists(Path.Combine(origem, "app", "Pitstop.exe")))
            throw new InvalidDataException("Payload de atualização sem app\\Pitstop.exe.");

        Directory.CreateDirectory(destino);

        foreach (var nome in new[] { "app", "web", "third-party" })
        {
            var fonte = Path.Combine(origem, nome);
            if (Directory.Exists(fonte)) ReplaceDirectory(fonte, Path.Combine(destino, nome));
        }

        foreach (var f in Directory.GetFiles(origem))
        {
            var alvo = Path.Combine(destino, Path.GetFileName(f));
            IoComRetry(() => File.Copy(f, alvo, true), "Copiar " + Path.GetFileName(f));
        }
    }

    static void ReplaceDirectory(string fonte, string alvo)
    {
        var nova = alvo + ".update-new";
        var antiga = alvo + ".update-old";

        if (Directory.Exists(nova))
            IoComRetry(() => Directory.Delete(nova, true), "Limpar " + Path.GetFileName(nova));
        if (Directory.Exists(antiga))
            IoComRetry(() => Directory.Delete(antiga, true), "Limpar " + Path.GetFileName(antiga));

        // A cópia é preparada antes da troca. O período em que <alvo> não existe
        // fica limitado aos dois renames abaixo.
        CopyDirectory(fonte, nova);

        var tinhaAntiga = Directory.Exists(alvo);
        if (tinhaAntiga)
            IoComRetry(() => Directory.Move(alvo, antiga), "Liberar " + Path.GetFileName(alvo));

        try
        {
            IoComRetry(() => Directory.Move(nova, alvo), "Ativar " + Path.GetFileName(alvo));
        }
        catch
        {
            try
            {
                if (Directory.Exists(alvo))
                    IoComRetry(() => Directory.Delete(alvo, true), "Remover atualização incompleta", 8);
                if (tinhaAntiga && Directory.Exists(antiga))
                    IoComRetry(() => Directory.Move(antiga, alvo), "Restaurar versão anterior", 8);
            }
            catch { }
            throw;
        }

        // A versão nova já está ativa. Resíduo antigo não deve derrubar uma
        // atualização bem-sucedida; a próxima execução tentará limpá-lo novamente.
        try
        {
            if (Directory.Exists(antiga))
                IoComRetry(() => Directory.Delete(antiga, true), "Limpar versão anterior", 8);
        }
        catch { }
    }

    static void CopyDirectory(string origem, string destino)
    {
        Directory.CreateDirectory(destino);
        foreach (var f in Directory.GetFiles(origem))
        {
            var alvo = Path.Combine(destino, Path.GetFileName(f));
            IoComRetry(() => File.Copy(f, alvo, true), "Copiar " + Path.GetFileName(f));
        }
        foreach (var d in Directory.GetDirectories(origem))
            CopyDirectory(d, Path.Combine(destino, Path.GetFileName(d)));
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
