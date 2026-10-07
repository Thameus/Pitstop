using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Pitstop.Updater;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Any(a => a.Equals("--smoke-blocker", StringComparison.OrdinalIgnoreCase)))
                return SmokeBlocker();
            if (args.Any(a => a.Equals("--smoke-update", StringComparison.OrdinalIgnoreCase)))
                return SmokeUpdate(args);

            Aplicar(args);
            return 0;
        }
        catch (Exception ex)
        {
            Registrar("ERRO: " + ex);
            MostrarErro("A atualização do Pitstop falhou.\n\n" + ex.Message +
                        "\n\nA instalação anterior foi preservada sempre que possível.");
            return 20;
        }
    }

    static void Aplicar(string[] args)
    {
        var pacote = Argumento(args, "--package") ?? throw new ArgumentException("Falta --package.");
        var destino = NormalizarDestino(Argumento(args, "--destination") ?? throw new ArgumentException("Falta --destination."));
        var sha256 = Argumento(args, "--sha256") ?? throw new ArgumentException("Falta --sha256.");
        var pidTxt = Argumento(args, "--parent-pid");
        var reiniciar = args.Any(a => a.Equals("--restart", StringComparison.OrdinalIgnoreCase));
        var limpar = Argumento(args, "--cleanup");

        Environment.CurrentDirectory = Path.GetTempPath();
        Registrar($"Iniciando atualização em {destino}.");

        if (!File.Exists(pacote)) throw new FileNotFoundException("Pacote de atualização não encontrado.", pacote);
        if (!Directory.Exists(destino)) throw new DirectoryNotFoundException("Instalação do Pitstop não encontrada: " + destino);

        VerificarSha256(pacote, sha256);

        using var mutex = new Mutex(false, NomeMutex(destino));
        var possuiMutex = false;
        try
        {
            try { possuiMutex = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { possuiMutex = true; }
            if (!possuiMutex)
                throw new InvalidOperationException("Já existe uma atualização do Pitstop em andamento para esta instalação.");

            var trabalho = Path.Combine(Path.GetTempPath(), "Pitstop", "apply", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(trabalho);
            try
            {
                ExtrairZipSeguro(pacote, trabalho);
                var origem = EncontrarRaizPayload(trabalho);

                if (!string.IsNullOrWhiteSpace(pidTxt) && int.TryParse(pidTxt, out var pid) &&
                    pid > 0 && pid != Environment.ProcessId)
                    EsperarProcesso(pid);

                AguardarBloqueadores(destino);
                AplicarPayload(origem, destino);
                AtualizarRegistro(destino);
            }
            finally
            {
                try { if (Directory.Exists(trabalho)) Directory.Delete(trabalho, true); } catch { }
            }

            if (reiniciar) Reiniciar(destino);
            if (!string.IsNullOrWhiteSpace(limpar)) AgendarLimpeza(limpar!);
            Registrar("Atualização concluída.");
        }
        finally
        {
            if (possuiMutex)
            {
                try { mutex.ReleaseMutex(); } catch { }
            }
        }
    }

    static void VerificarSha256(string arquivo, string esperado)
    {
        esperado = esperado.Trim().ToLowerInvariant();
        if (esperado.Length != 64 || esperado.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("SHA-256 esperado é inválido.");

        using var stream = File.OpenRead(arquivo);
        var atual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(atual, esperado, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O SHA-256 do pacote mudou depois do download.");
    }

    static void EsperarProcesso(int pid)
    {
        try
        {
            using var processo = Process.GetProcessById(pid);
            Registrar($"Aguardando Pitstop PID {pid} encerrar.");
            if (!processo.WaitForExit(120_000))
                throw new TimeoutException("O Pitstop não encerrou no tempo esperado.");
        }
        catch (ArgumentException)
        {
            // Já encerrou.
        }
    }

    static void ExtrairZipSeguro(string pacote, string destino)
    {
        var root = Path.GetFullPath(destino) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(pacote);
        foreach (var entry in zip.Entries)
        {
            var full = Path.GetFullPath(Path.Combine(destino, entry.FullName));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Caminho inválido no pacote: " + entry.FullName);

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(full);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            entry.ExtractToFile(full, overwrite: true);
        }
    }

    static string EncontrarRaizPayload(string extraido)
    {
        var candidatos = new List<string>();
        if (File.Exists(Path.Combine(extraido, "app", "Pitstop.exe"))) candidatos.Add(extraido);
        candidatos.AddRange(Directory.GetDirectories(extraido)
            .Where(d => File.Exists(Path.Combine(d, "app", "Pitstop.exe"))));

        candidatos = candidatos.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (candidatos.Count != 1)
            throw new InvalidDataException("O pacote de atualização não possui uma única raiz válida do Pitstop.");
        return candidatos[0];
    }

    static void AplicarPayload(string origem, string destino)
    {
        foreach (var nome in new[] { "app", "web", "third-party" })
        {
            var fonte = Path.Combine(origem, nome);
            if (Directory.Exists(fonte)) SubstituirDiretorio(fonte, Path.Combine(destino, nome));
        }

        foreach (var arquivo in Directory.GetFiles(origem))
        {
            var alvo = Path.Combine(destino, Path.GetFileName(arquivo));
            IoComRetry(() => File.Copy(arquivo, alvo, true), "Copiar " + Path.GetFileName(arquivo));
        }
    }

    static void SubstituirDiretorio(string fonte, string alvo)
    {
        var nova = alvo + ".update-new";
        var antiga = alvo + ".update-old";

        if (Directory.Exists(nova))
            IoComRetry(() => Directory.Delete(nova, true), "Limpar " + Path.GetFileName(nova));
        if (Directory.Exists(antiga))
            IoComRetry(() => Directory.Delete(antiga, true), "Limpar " + Path.GetFileName(antiga));

        CopiarDiretorio(fonte, nova);

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

        try
        {
            if (Directory.Exists(antiga))
                IoComRetry(() => Directory.Delete(antiga, true), "Limpar versão anterior", 8);
        }
        catch { }
    }

    static void CopiarDiretorio(string origem, string destino)
    {
        Directory.CreateDirectory(destino);
        foreach (var arquivo in Directory.GetFiles(origem))
        {
            var alvo = Path.Combine(destino, Path.GetFileName(arquivo));
            IoComRetry(() => File.Copy(arquivo, alvo, true), "Copiar " + Path.GetFileName(arquivo));
        }
        foreach (var dir in Directory.GetDirectories(origem))
            CopiarDiretorio(dir, Path.Combine(destino, Path.GetFileName(dir)));
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

    static void AguardarBloqueadores(string destino)
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
                    catch { }
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

    static string NomeMutex(string destino)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(destino).ToUpperInvariant()));
        return "Pitstop.Update." + Convert.ToHexString(bytes.AsSpan(0, 12));
    }

    static void AtualizarRegistro(string destino)
    {
        const string subKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Pitstop";
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKey, writable: true);
        if (key == null) return;

        var registrado = key.GetValue("InstallLocation") as string;
        if (string.IsNullOrWhiteSpace(registrado) ||
            !string.Equals(Path.GetFullPath(registrado).TrimEnd('\\'),
                           Path.GetFullPath(destino).TrimEnd('\\'),
                           StringComparison.OrdinalIgnoreCase))
            return;

        var exe = Path.Combine(destino, "app", "Pitstop.exe");
        var versao = FileVersionInfo.GetVersionInfo(exe).ProductVersion?.Split('+')[0] ?? "";
        if (versao != "") key.SetValue("DisplayVersion", versao, Microsoft.Win32.RegistryValueKind.String);
        key.SetValue("DisplayIcon", exe + ",0", Microsoft.Win32.RegistryValueKind.String);
        key.SetValue("InstallLocation", destino, Microsoft.Win32.RegistryValueKind.String);
    }

    static void Reiniciar(string destino)
    {
        var appDir = Path.Combine(destino, "app");
        var exe = Path.Combine(appDir, "Pitstop.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("Pitstop.exe não existe após a atualização.", exe);
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = appDir });
    }

    static void AgendarLimpeza(string caminho)
    {
        try
        {
            var full = Path.GetFullPath(caminho);
            var temp = Path.GetFullPath(Path.GetTempPath());
            if (!CaminhoDentro(full, temp)) return;

            var psi = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = temp,
                Arguments = $"/d /c timeout /t 3 /nobreak >nul & rmdir /s /q \"{full}\""
            };
            Process.Start(psi)?.Dispose();
        }
        catch { }
    }

    static string NormalizarDestino(string raw) =>
        Path.GetFullPath(Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"')));

    static string? Argumento(string[] args, string nome)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(nome, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    static void Registrar(string texto)
    {
        try
        {
            var dir = AppContext.BaseDirectory;
            File.AppendAllText(Path.Combine(dir, "update.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + texto + Environment.NewLine,
                new UTF8Encoding(false));
        }
        catch { }
    }

    static void MostrarErro(string texto)
    {
        try { MessageBoxW(IntPtr.Zero, texto, "Pitstop - atualização", 0x10); } catch { }
    }

    static int SmokeBlocker()
    {
        var destino = Path.Combine(Path.GetTempPath(), "pitstop-updater-blocker-" + Guid.NewGuid().ToString("N"));
        Process? bloqueador = null;
        try
        {
            var app = Path.Combine(destino, "app");
            Directory.CreateDirectory(app);
            var pit = Path.Combine(app, "pit.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), pit, true);
            bloqueador = Process.Start(new ProcessStartInfo(pit)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = app,
                Arguments = "/d /c ping 127.0.0.1 -n 20 >nul"
            });
            if (bloqueador == null) return 31;
            Thread.Sleep(300);

            try
            {
                AguardarBloqueadores(destino);
                return 32;
            }
            catch (IOException ex)
            {
                return ex.Message.Contains("PID " + bloqueador.Id, StringComparison.Ordinal) ? 0 : 33;
            }
        }
        catch { return 34; }
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

    static int SmokeUpdate(string[] args)
    {
        var pacote = Argumento(args, "--package");
        var sha = Argumento(args, "--sha256");
        if (string.IsNullOrWhiteSpace(pacote) || string.IsNullOrWhiteSpace(sha)) return 41;

        var destino = Path.Combine(Path.GetTempPath(), "pitstop-updater-smoke-" + Guid.NewGuid().ToString("N"));
        var cwd = Environment.CurrentDirectory;
        try
        {
            Directory.CreateDirectory(Path.Combine(destino, "app"));
            Directory.CreateDirectory(Path.Combine(destino, "config"));
            File.WriteAllText(Path.Combine(destino, "app", "old.txt"), "old");
            File.WriteAllText(Path.Combine(destino, ".env"), "PRESERVAR=1");
            File.WriteAllText(Path.Combine(destino, "config", "perfis.json"), "{\"perfis\":{\"ok\":{\"tipo\":\"comando\",\"comando\":\"echo ok\"}}}");

            Environment.CurrentDirectory = Path.Combine(destino, "app");
            Aplicar(["--package", pacote, "--sha256", sha, "--destination", destino]);

            if (!File.Exists(Path.Combine(destino, "app", "Pitstop.exe"))) return 42;
            if (File.Exists(Path.Combine(destino, "app", "old.txt"))) return 43;
            if (File.ReadAllText(Path.Combine(destino, ".env")) != "PRESERVAR=1") return 44;
            if (!File.ReadAllText(Path.Combine(destino, "config", "perfis.json")).Contains("\"ok\"")) return 45;
            return 0;
        }
        catch { return 46; }
        finally
        {
            Environment.CurrentDirectory = cwd;
            try { if (Directory.Exists(destino)) Directory.Delete(destino, true); } catch { }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
