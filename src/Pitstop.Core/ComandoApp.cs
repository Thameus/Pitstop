using System.Diagnostics;
using System.Text;

namespace Pitstop;

/// <summary>Executor genérico do perfil "comando": shell, script temporário, ambiente e validação.</summary>
public static class ComandoApp
{
    public const int StopTimeoutSeg = 5;
    public const int ProntoTimeoutSeg = 60;

    public sealed record Preparado(ProcessStartInfo Info, string Script, string ProcessoShell);

    sealed record ShellSpec(string Exe, string Tipo, string Extensao);

    public static void Validar(Perfil p)
    {
        if (string.IsNullOrWhiteSpace(p.Comando))
            throw new ErroRunner("perfil " + p.Nome + ": informe o comando");
        if (!Directory.Exists(p.Pasta))
            throw new ErroRunner("perfil " + p.Nome + ": pasta não existe: " + p.Pasta);
        if (p.EnvArquivo != "" && !File.Exists(p.EnvArquivo))
            throw new ErroRunner("perfil " + p.Nome + ": arquivo .env não existe: " + p.EnvArquivo);
        if (p.Porta is < 0 or > 65535)
            throw new ErroRunner("perfil " + p.Nome + ": porta precisa estar entre 1 e 65535");
        if (p.Url is { Length: > 0 } u &&
            (!Uri.TryCreate(u, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
            throw new ErroRunner("perfil " + p.Nome + ": URL precisa ser http:// ou https://");
        if (p.TimeoutExecucaoSeg is <= 0)
            throw new ErroRunner("perfil " + p.Nome + ": tempo limite precisa ser maior que zero");
        _ = ResolverShell(p);
    }

    public static Preparado Preparar(Perfil p, bool redirecionar, string marcador)
    {
        Validar(p);
        var shell = ResolverShell(p);
        var dir = Path.Combine(p.Cache, "comando");
        Directory.CreateDirectory(dir);
        var script = Path.Combine(dir, "exec-" + Guid.NewGuid().ToString("N") + shell.Extensao);
        File.WriteAllText(script, MontarScript(p, shell.Tipo, marcador), new UTF8Encoding(false));
        if (!So.Windows && shell.Tipo is "sh" or "bash" or "custom")
            So.GarantirExecutavel(script);

        var psi = Proc.Psi(shell.Exe, p.Pasta, redirecionar, Encoding.UTF8);
        switch (shell.Tipo)
        {
            case "cmd":
                psi.ArgumentList.Add("/d");
                psi.ArgumentList.Add("/s");
                psi.ArgumentList.Add("/c");
                psi.ArgumentList.Add(script);
                break;
            case "powershell":
                psi.ArgumentList.Add("-NoLogo");
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-NonInteractive");
                if (So.Windows && Path.GetFileName(shell.Exe).StartsWith("powershell", StringComparison.OrdinalIgnoreCase))
                {
                    psi.ArgumentList.Add("-ExecutionPolicy");
                    psi.ArgumentList.Add("Bypass");
                }
                psi.ArgumentList.Add("-File");
                psi.ArgumentList.Add(script);
                break;
            default:
                psi.ArgumentList.Add(script);
                break;
        }
        AplicarAmbiente(p, psi.Environment);
        return new Preparado(psi, script, Path.GetFileNameWithoutExtension(shell.Exe));
    }

    public static IReadOnlyList<(int Numero, string Texto)> Linhas(string comando) =>
        comando.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select((texto, i) => (Numero: i + 1, Texto: texto))
            .Where(x => !string.IsNullOrWhiteSpace(x.Texto))
            .ToList();

    static string MontarScript(Perfil p, string tipoShell, string marcador)
    {
        var linhas = Linhas(p.Comando);
        var sb = new StringBuilder();

        if (tipoShell == "cmd")
        {
            sb.AppendLine("@echo off");
            sb.AppendLine("setlocal EnableExtensions");
            foreach (var (n, linha) in linhas)
            {
                if (marcador != "") sb.AppendLine("echo " + Marcador(marcador, n));
                sb.AppendLine(linha);
                sb.AppendLine("set \"PIT_EXIT=%ERRORLEVEL%\"");
                sb.AppendLine("if not \"%PIT_EXIT%\"==\"0\" exit /b %PIT_EXIT%");
            }
            sb.AppendLine("exit /b 0");
            return sb.ToString();
        }

        if (tipoShell == "powershell")
        {
            sb.AppendLine("$ErrorActionPreference = 'Stop'");
            foreach (var (n, linha) in linhas)
            {
                if (marcador != "") sb.AppendLine("Write-Output '" + Marcador(marcador, n) + "'");
                sb.AppendLine("$global:LASTEXITCODE = 0");
                sb.AppendLine("try {");
                sb.AppendLine(linha);
                sb.AppendLine("} catch { Write-Error $_; exit 1 }");
                sb.AppendLine("$pitExit = $global:LASTEXITCODE");
                sb.AppendLine("if ($pitExit -ne 0) { exit $pitExit }");
            }
            sb.AppendLine("exit 0");
            return sb.ToString();
        }

        sb.AppendLine("#!/bin/sh");
        foreach (var (n, linha) in linhas)
        {
            if (marcador != "") sb.AppendLine("printf '%s\\n' '" + Marcador(marcador, n) + "'");
            sb.AppendLine(linha);
            sb.AppendLine("pit_exit=$?");
            sb.AppendLine("if [ \"$pit_exit\" -ne 0 ]; then exit \"$pit_exit\"; fi");
        }
        sb.AppendLine("exit 0");
        return sb.ToString();
    }

    public static string Marcador(string marcador, int linha) => "@@PIT:" + marcador + ":" + linha + "@@";

    static ShellSpec ResolverShell(Perfil p)
    {
        var pedido = (p.ShellComando == "" ? "auto" : p.ShellComando).Trim().ToLowerInvariant();
        if (pedido == "auto") pedido = So.Windows ? "cmd" : "sh";

        return pedido switch
        {
            "cmd" => new ShellSpec(Encontrar(So.Windows ? "cmd.exe" : "cmd")
                                   ?? throw new ErroRunner("shell cmd não encontrado no PATH"), "cmd", ".cmd"),
            "powershell" or "pwsh" => ResolverPowerShell(),
            "sh" => new ShellSpec(Encontrar(So.Windows ? "sh.exe" : "/bin/sh") ?? Encontrar("sh")
                                  ?? throw new ErroRunner("shell sh não encontrado"), "sh", ".sh"),
            "bash" => new ShellSpec(Encontrar(So.Windows ? "bash.exe" : "bash")
                                    ?? throw new ErroRunner("shell bash não encontrado no PATH"), "bash", ".sh"),
            "custom" or "personalizado" => ResolverPersonalizado(p),
            _ => throw new ErroRunner("perfil " + p.Nome + ": shell inválido: " + p.ShellComando),
        };
    }

    static ShellSpec ResolverPowerShell()
    {
        var exe = Encontrar(So.Windows ? "pwsh.exe" : "pwsh") ??
                  (So.Windows ? Encontrar("powershell.exe") : null);
        return exe != null
            ? new ShellSpec(exe, "powershell", ".ps1")
            : throw new ErroRunner("PowerShell não encontrado no PATH");
    }

    static ShellSpec ResolverPersonalizado(Perfil p)
    {
        if (string.IsNullOrWhiteSpace(p.ShellPersonalizado))
            throw new ErroRunner("perfil " + p.Nome + ": informe o interpretador personalizado");
        var exe = Encontrar(p.ShellPersonalizado.Trim())
                  ?? throw new ErroRunner("interpretador não encontrado: " + p.ShellPersonalizado);
        var nome = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
        if (nome.Contains("powershell") || nome == "pwsh") return new ShellSpec(exe, "powershell", ".ps1");
        if (nome is "cmd" or "cmd.exe") return new ShellSpec(exe, "cmd", ".cmd");
        return new ShellSpec(exe, "custom", ".sh");
    }

    static string? Encontrar(string exe)
    {
        if (Path.IsPathRooted(exe))
            return File.Exists(exe) ? Path.GetFullPath(exe) : null;
        if (exe.Contains(Path.DirectorySeparatorChar) || exe.Contains(Path.AltDirectorySeparatorChar))
        {
            var f = Path.GetFullPath(exe, Raiz.Dir);
            return File.Exists(f) ? f : null;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var nomes = new List<string> { exe };
        if (So.Windows && Path.GetExtension(exe) == "")
        {
            var ext = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM")
                .Split(';', StringSplitOptions.RemoveEmptyEntries);
            nomes.AddRange(ext.Select(e => exe + e.ToLowerInvariant()));
        }
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var nome in nomes)
            {
                try
                {
                    var f = Path.Combine(dir.Trim().Trim('"'), nome);
                    if (File.Exists(f)) return Path.GetFullPath(f);
                }
                catch { }
            }
        return null;
    }

    static void AplicarAmbiente(Perfil p, IDictionary<string, string?> env)
    {
        if (p.EnvArquivo != "")
            foreach (var (k, v) in LerEnv(p.EnvArquivo))
                env[k] = v;
        foreach (var (k, v) in LerPares(p.Env))
            env[k] = v;
    }

    static IEnumerable<KeyValuePair<string, string>> LerEnv(string arquivo)
    {
        foreach (var bruta in File.ReadLines(arquivo))
        {
            var linha = bruta.Trim().TrimStart('\uFEFF');
            if (linha == "" || linha.StartsWith('#')) continue;
            if (linha.StartsWith("export ", StringComparison.Ordinal)) linha = linha[7..].TrimStart();
            var i = linha.IndexOf('=');
            if (i <= 0) continue;
            var chave = linha[..i].Trim();
            if (!ChaveEnvValida(chave)) continue;
            var valor = linha[(i + 1)..].Trim();
            if (valor.Length >= 2 && ((valor[0] == '"' && valor[^1] == '"') || (valor[0] == '\'' && valor[^1] == '\'')))
                valor = valor[1..^1];
            yield return new(chave, valor);
        }
    }

    static IEnumerable<KeyValuePair<string, string>> LerPares(string texto)
    {
        var normal = texto.Replace("\r\n", "\n").Replace('\r', '\n');
        var partes = normal.Contains('\n') ? normal.Split('\n') : normal.Split(';');
        foreach (var bruta in partes)
        {
            var linha = bruta.Trim();
            if (linha == "") continue;
            var i = linha.IndexOf('=');
            if (i <= 0) throw new ErroRunner("variável de ambiente inválida: " + linha + " (use CHAVE=valor)");
            var chave = linha[..i].Trim();
            if (!ChaveEnvValida(chave)) throw new ErroRunner("nome de variável de ambiente inválido: " + chave);
            yield return new(chave, linha[(i + 1)..]);
        }
    }

    static bool ChaveEnvValida(string chave) =>
        chave.Length > 0 && (char.IsLetter(chave[0]) || chave[0] == '_') &&
        chave.Skip(1).All(c => char.IsLetterOrDigit(c) || c == '_');

    public static string NomeProcessoShell(Perfil p) =>
        Path.GetFileNameWithoutExtension(ResolverShell(p).Exe);

    public static void ApagarScript(string caminho)
    {
        try { if (File.Exists(caminho)) File.Delete(caminho); } catch { }
    }

    public static void AbrirTerminal(Perfil p) => Proc.AbrirTerminal(p.Pasta);
}

/// <summary>Arquivo de log de uma execução de perfil comando, limitado e rotacionado.</summary>
public sealed class LogArquivoComando : IDisposable
{
    public const int MaxArquivos = 10;
    public const long MaxBytes = 5L * 1024 * 1024;

    readonly object trava = new();
    readonly StreamWriter writer;
    long bytes;
    bool truncado;

    public string Caminho { get; }

    LogArquivoComando(string caminho)
    {
        Caminho = caminho;
        var fs = new FileStream(caminho, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.SequentialScan);
        writer = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public static LogArquivoComando Abrir(Perfil p)
    {
        var dir = Diretorio(p);
        Directory.CreateDirectory(dir);
        Rotacionar(dir, MaxArquivos - 1);
        var nome = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..6] + ".log";
        return new LogArquivoComando(Path.Combine(dir, nome));
    }

    public void Escrever(string texto)
    {
        lock (trava)
        {
            if (truncado) return;
            var linhas = texto.Replace("\r\n", "\n").Split('\n');
            foreach (var linha in linhas)
            {
                var dado = Encoding.UTF8.GetByteCount(linha + Environment.NewLine);
                if (bytes + dado > MaxBytes)
                {
                    const string aviso = "[pit] log atingiu 5 MB; restante desta execução foi omitido";
                    if (bytes + Encoding.UTF8.GetByteCount(aviso + Environment.NewLine) <= MaxBytes)
                    {
                        writer.WriteLine(aviso);
                        bytes += Encoding.UTF8.GetByteCount(aviso + Environment.NewLine);
                    }
                    truncado = true;
                    return;
                }
                writer.WriteLine(linha);
                bytes += dado;
            }
        }
    }

    public void Dispose()
    {
        lock (trava) writer.Dispose();
        try { Rotacionar(Path.GetDirectoryName(Caminho)!, MaxArquivos); } catch { }
    }

    public static string Diretorio(Perfil p) => Path.Combine(p.Cache, "logs");

    public static string[] Arquivos(Perfil p)
    {
        var dir = Diretorio(p);
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, "*.log").OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
    }

    public static IEnumerable<string> LerUltimo(Perfil p, int maxLinhas = 1500)
    {
        var arq = Arquivos(p).FirstOrDefault();
        if (arq == null) return [];
        try { return File.ReadLines(arq).TakeLast(maxLinhas).ToArray(); }
        catch { return []; }
    }

    static void Rotacionar(string dir, int manter)
    {
        if (!Directory.Exists(dir)) return;
        var arquivos = Directory.GetFiles(dir, "*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc).ToList();
        foreach (var velho in arquivos.Skip(Math.Max(0, manter)))
            try { File.Delete(velho); } catch { }
    }
}
