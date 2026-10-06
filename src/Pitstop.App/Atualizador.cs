using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pitstop.App;

sealed record ReleasePitstop(
    Version Versao,
    string Tag,
    string Url,
    string AssetNome,
    string DownloadUrl,
    long Tamanho,
    string Sha256)
{
    public string VersaoTexto => $"{Versao.Major}.{Versao.Minor}.{Versao.Build}";
    public bool Nova => Versao > Atualizador.VersaoAtualNumero;
}

/// <summary>
/// Atualizador leve: só consulta o GitHub quando o usuário pede. Download e instalação também são explícitos.
/// Não mantém serviço, timer ou processo em segundo plano.
/// </summary>
static class Atualizador
{
    const string ApiLatest = "https://api.github.com/repos/Thameus/Pitstop/releases/latest";
    static readonly HttpClient Http = CriarHttp();

    public static Version VersaoAtualNumero
    {
        get
        {
            var v = typeof(Atualizador).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    public static string VersaoAtual
    {
        get
        {
            var v = VersaoAtualNumero;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    static HttpClient CriarHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        h.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Pitstop", VersaoAtual));
        h.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        h.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return h;
    }

    public static async Task<ReleasePitstop> ConsultarAsync(CancellationToken ct = default)
    {
        using var resposta = await Http.GetAsync(ApiLatest, HttpCompletionOption.ResponseHeadersRead, ct);
        resposta.EnsureSuccessStatusCode();

        await using var stream = await resposta.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = json.RootElement;

        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            throw new InvalidOperationException("A release mais recente ainda está em rascunho.");
        if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
            throw new InvalidOperationException("A release mais recente é uma pré-versão.");

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!TentarVersao(tag, out var versao))
            throw new InvalidOperationException("A versão publicada no GitHub é inválida: " + tag);

        var versaoTexto = $"{versao.Major}.{versao.Minor}.{versao.Build}";
        var esperado = NomeAsset(versaoTexto);
        JsonElement? asset = null;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            if (string.Equals(a.GetProperty("name").GetString(), esperado, StringComparison.OrdinalIgnoreCase))
            {
                asset = a;
                break;
            }
        }

        if (asset == null)
            throw new InvalidOperationException("A release " + tag + " não possui o pacote esperado para este sistema: " + esperado);

        var digest = asset.Value.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
        if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || digest.Length != 71)
            throw new InvalidOperationException("O pacote publicado não possui SHA-256 verificável.");

        return new ReleasePitstop(
            versao,
            tag,
            root.GetProperty("html_url").GetString() ?? "https://github.com/Thameus/Pitstop/releases",
            esperado,
            asset.Value.GetProperty("browser_download_url").GetString()
                ?? throw new InvalidOperationException("A release não possui URL de download."),
            asset.Value.TryGetProperty("size", out var s) ? s.GetInt64() : 0,
            digest["sha256:".Length..].ToLowerInvariant());
    }

    public static async Task<string> BaixarAsync(
        ReleasePitstop release,
        IProgress<(long Recebidos, long Total)>? progresso = null,
        CancellationToken ct = default)
    {
        var updates = Path.Combine(Path.GetTempPath(), "Pitstop", "updates");
        var dir = Path.Combine(updates, release.VersaoTexto);
        try
        {
            if (Directory.Exists(updates))
                foreach (var antiga in Directory.GetDirectories(updates).Where(x => !string.Equals(x, dir, StringComparison.OrdinalIgnoreCase)))
                    Directory.Delete(antiga, true);
        }
        catch { }

        Directory.CreateDirectory(dir);
        var destino = Path.Combine(dir, release.AssetNome);
        var parcial = destino + ".part";
        if (File.Exists(parcial)) File.Delete(parcial);

        try
        {
            using var resposta = await Http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resposta.EnsureSuccessStatusCode();
            var total = resposta.Content.Headers.ContentLength ?? release.Tamanho;

            await using (var entrada = await resposta.Content.ReadAsStreamAsync(ct))
            await using (var saida = new FileStream(parcial, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true))
            {
                var buffer = new byte[128 * 1024];
                long recebidos = 0;
                while (true)
                {
                    var lidos = await entrada.ReadAsync(buffer, ct);
                    if (lidos == 0) break;
                    await saida.WriteAsync(buffer.AsMemory(0, lidos), ct);
                    recebidos += lidos;
                    progresso?.Report((recebidos, total));
                }
            }

            await using var arquivo = File.OpenRead(parcial);
            using var sha = SHA256.Create();
            var hash = Convert.ToHexString(await sha.ComputeHashAsync(arquivo, ct)).ToLowerInvariant();
            if (!string.Equals(hash, release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O SHA-256 do download não confere com o publicado no GitHub.");

            File.Move(parcial, destino, true);
            return destino;
        }
        catch
        {
            try { if (File.Exists(parcial)) File.Delete(parcial); } catch { }
            throw;
        }
    }

    public static string CriarBackupMinimo()
    {
        var baseDir = Path.Combine(Raiz.Dir, "cache", "update-backups");
        var fontes = new[]
        {
            (Path.Combine(Raiz.Dir, ".env"), ".env"),
            (Path.Combine(Raiz.Dir, "config", "perfis.json"), Path.Combine("config", "perfis.json")),
            (Path.Combine(Raiz.Dir, "config", "perfis.json.bak"), Path.Combine("config", "perfis.json.bak"))
        }.Where(x => File.Exists(x.Item1)).ToList();

        if (fontes.Count == 0) return "";

        var destino = Path.Combine(baseDir, VersaoAtual + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        foreach (var (fonte, rel) in fontes)
        {
            var alvo = Path.Combine(destino, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(alvo)!);
            File.Copy(fonte, alvo, true);
        }

        try
        {
            foreach (var velho in new DirectoryInfo(baseDir).GetDirectories()
                         .OrderByDescending(x => x.CreationTimeUtc).Skip(3))
                velho.Delete(true);
        }
        catch { }

        return destino;
    }

    public static void IniciarInstalador(ReleasePitstop release, string arquivo)
    {
        if (OperatingSystem.IsWindows())
        {
            var psi = new ProcessStartInfo(arquivo) { UseShellExecute = true };
            psi.ArgumentList.Add("--update");
            psi.ArgumentList.Add("--destination");
            psi.ArgumentList.Add(Raiz.Dir);
            psi.ArgumentList.Add("--parent-pid");
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            psi.ArgumentList.Add("--restart");
            if (Process.Start(psi) == null) throw new InvalidOperationException("Não foi possível iniciar o atualizador.");
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            var script = Path.Combine(Path.GetTempPath(), "pitstop-update-" + Guid.NewGuid().ToString("N") + ".sh");
            var qPacote = ShellQuote(arquivo);
            var qDestino = ShellQuote(Raiz.Dir);
            var qScript = ShellQuote(script);
            var texto =
                "#!/bin/sh\n" +
                "pid=" + Environment.ProcessId + "\n" +
                "while kill -0 \"$pid\" 2>/dev/null; do sleep 1; done\n" +
                "sh " + qPacote + " --update " + qDestino + "\n" +
                "rc=$?\n" +
                "if [ \"$rc\" -eq 0 ]; then nohup " + ShellQuote(Path.Combine(Raiz.Dir, "app", "Pitstop")) + " >/dev/null 2>&1 & fi\n" +
                "rm -f " + qPacote + " " + qScript + "\n" +
                "exit \"$rc\"\n";
            File.WriteAllText(script, texto, new UTF8Encoding(false));

            var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            psi.ArgumentList.Add(script);
            if (Process.Start(psi) == null) throw new InvalidOperationException("Não foi possível iniciar o atualizador.");
            return;
        }

        throw new PlatformNotSupportedException("Atualização automática disponível apenas para Windows e Linux x64.");
    }

    public static string Tamanho(long bytes) =>
        bytes <= 0 ? "" : bytes >= 1024L * 1024L
            ? Math.Round(bytes / 1024d / 1024d, 1) + " MB"
            : Math.Round(bytes / 1024d, 1) + " KB";

    static string NomeAsset(string versao)
    {
        if (RuntimeInformation.OSArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Esta versão do Pitstop suporta atualização automática apenas em x64.");
        if (OperatingSystem.IsWindows()) return $"pitstop-{versao}-setup-win-x64.exe";
        if (OperatingSystem.IsLinux()) return $"pitstop-{versao}-linux-x64.run";
        throw new PlatformNotSupportedException("Atualização automática disponível apenas para Windows e Linux x64.");
    }

    static bool TentarVersao(string tag, out Version versao)
    {
        var limpa = tag.Trim();
        if (limpa.StartsWith('v') || limpa.StartsWith('V')) limpa = limpa[1..];
        var hifen = limpa.IndexOf('-');
        if (hifen >= 0) limpa = limpa[..hifen];

        if (Version.TryParse(limpa, out var v) && v.Major >= 0 && v.Minor >= 0)
        {
            versao = new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
            return true;
        }

        versao = new Version(0, 0, 0);
        return false;
    }

    static string ShellQuote(string valor) => "'" + valor.Replace("'", "'\"'\"'") + "'";
}
