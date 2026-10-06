using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Pitstop;

public sealed record VersaoPacote(string Versao, string Caminho, long Data, long Tamanho);

/// <summary>
/// Pacote pronto (um .war ou o .zip/.jar de um release) no lugar do código do repositório. Cada arquivo é extraído
/// uma vez em &lt;raiz&gt;/&lt;prefixo&gt;-&lt;chave&gt;; a chave é caminho + data + tamanho, então pacote novo (outra versão,
/// build novo no mesmo caminho) = pasta nova, e a velha é apagada.
/// </summary>
public static class Pacote
{
    /// <summary>
    /// Onde procurar as versões quando o campo está vazio (PACOTES_DIR: uma pasta por versão, com o pacote dentro).
    /// Vazio = não sugere nada.
    /// </summary>
    public static string? VersoesDir => Env.Txt("PACOTES_DIR");

    static readonly Regex ReChave = new("^[0-9a-f]{10}$");

    /// <summary>Pasta de extração do arquivo. Arquivo que não existe = chave "ausente" (pasta que nunca é criada).</summary>
    public static string Destino(string raiz, string prefixo, string arquivo)
    {
        var chave = "ausente";
        if (arquivo != "" && new FileInfo(arquivo) is { Exists: true } f)
        {
            var h = SHA1.HashData(Encoding.UTF8.GetBytes(f.FullName.ToLowerInvariant() + "|" + f.LastWriteTimeUtc.Ticks + "|" + f.Length));
            chave = Convert.ToHexString(h)[..10].ToLowerInvariant();
        }
        return Path.Combine(raiz, prefixo + "-" + chave);
    }

    /// <summary>A pasta final só existe completa: a extração vai para .tmp e é renomeada no fim.</summary>
    public static bool Extraido(string destino) => Directory.Exists(destino);

    /// <summary>
    /// Extrai (se ainda não extraído, ou sempre com <paramref name="forcar"/>) e apaga as extrações velhas do mesmo
    /// prefixo. <paramref name="cancelado"/> é conferido entre um arquivo e outro. Devolve false se já estava extraído.
    /// </summary>
    public static bool Extrair(string arquivo, string destino, Action<string> log, Func<bool> cancelado, bool forcar)
    {
        if (arquivo == "" || !File.Exists(arquivo)) throw new ErroRunner("pacote não encontrado: " + arquivo);
        if (forcar && Directory.Exists(destino))
        {
            log("[pit] apagando a extração anterior " + destino);
            Directory.Delete(destino, true);
        }
        if (Directory.Exists(destino)) return false;
        var tmp = destino + ".tmp";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        Directory.CreateDirectory(tmp);
        var raiz = Path.GetFullPath(tmp + Path.DirectorySeparatorChar);
        var comparacaoCaminho = So.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var t0 = Agora.Ms;
        var aviso = t0 + 10000;
        log("[pit] extraindo " + arquivo + " (" + (new FileInfo(arquivo).Length / 1048576) + " MB) -> " + destino);
        try
        {
            using (var zip = ZipFile.OpenRead(arquivo))
            {
                var total = zip.Entries.Count;
                var n = 0;
                foreach (var e in zip.Entries)
                {
                    if (cancelado()) throw new ErroRunner("extração cancelada") { Cancelado = true };
                    n++;
                    var alvo = Path.GetFullPath(Path.Combine(tmp, e.FullName));
                    if (!alvo.StartsWith(raiz, comparacaoCaminho))
                        throw new ErroRunner("pacote com caminho fora da pasta de extração: " + e.FullName);
                    if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')) { Directory.CreateDirectory(alvo); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(alvo)!);
                    e.ExtractToFile(alvo, true);
                    if (Agora.Ms >= aviso)
                    {
                        log("[pit] extraindo... " + n + "/" + total + " arquivos, " + Agora.Duracao(Agora.Ms - t0));
                        aviso += 10000;
                    }
                }
            }
            Directory.Move(tmp, destino);
        }
        catch
        {
            try { Directory.Delete(tmp, true); } catch (Exception) { /* fica para a próxima extração apagar */ }
            throw;
        }
        log("[pit] extraído em " + Agora.Duracao(Agora.Ms - t0));
        ApagarVelhas(destino, log);
        return true;
    }

    /// <summary>Outras extrações do mesmo prefixo (versão anterior, .tmp interrompido). Pasta presa fica.</summary>
    static void ApagarVelhas(string destino, Action<string> log)
    {
        var pai = Path.GetDirectoryName(destino)!;
        var nome = Path.GetFileName(destino);
        var prefixo = nome[..(nome.LastIndexOf('-') + 1)];
        foreach (var d in Directory.EnumerateDirectories(pai, prefixo + "*"))
        {
            var resto = Path.GetFileName(d)[prefixo.Length..];
            if (resto.EndsWith(".tmp")) resto = resto[..^4];
            if (d.Equals(destino, StringComparison.OrdinalIgnoreCase) || !(ReChave.IsMatch(resto) || resto == "ausente")) continue;
            try
            {
                Directory.Delete(d, true);
                log("[pit] extração velha apagada: " + d);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log("[pit] não apaguei a extração velha " + d + " (" + ex.Message + ")");
            }
        }
    }

    /// <summary>
    /// Jar executável do pacote: o de menor profundidade com Main-Class no MANIFEST, fora das pastas lib (dependências).
    /// </summary>
    public static string? JarPrincipal(string raiz)
    {
        IEnumerable<string> Jars(string dir)
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*.jar")) yield return f;
            foreach (var d in Directory.EnumerateDirectories(dir))
                if (!Path.GetFileName(d).Equals("lib", StringComparison.OrdinalIgnoreCase))
                    foreach (var f in Jars(d)) yield return f;
        }
        return Jars(raiz)
            .OrderBy(f => f.Count(c => c == Path.DirectorySeparatorChar))
            .FirstOrDefault(TemMain);
    }

    static bool TemMain(string jar)
    {
        try
        {
            using var zip = ZipFile.OpenRead(jar);
            var mf = zip.GetEntry("META-INF/MANIFEST.MF");
            if (mf == null) return false;
            using var r = new StreamReader(mf.Open());
            return Regex.IsMatch(r.ReadToEnd(), @"^Main-Class:\s*\S", RegexOptions.Multiline);
        }
        catch (InvalidDataException) { return false; }
    }

    /// <summary>
    /// Versões do mesmo arquivo nas pastas irmãs: .../releases/v1.2/app.war -> .../releases/*/app.war, mais novo
    /// primeiro. Campo vazio = PACOTES_DIR: todo arquivo com a <paramref name="extensao"/> (.war, .zip, .jar) nas
    /// subpastas dele.
    /// </summary>
    public static List<VersaoPacote> Versoes(string arquivo, string extensao)
    {
        arquivo = arquivo.Trim();
        IEnumerable<FileInfo> achados;
        if (arquivo != "")
        {
            var f = Path.GetFullPath(arquivo);
            var nome = Path.GetFileName(f);
            var raiz = Path.GetDirectoryName(Path.GetDirectoryName(f));
            if (raiz == null || nome == "" || !Directory.Exists(raiz)) return [];
            achados = Directory.EnumerateDirectories(raiz).Select(d => new FileInfo(Path.Combine(d, nome)));
        }
        else
        {
            var raiz = VersoesDir;
            if (raiz == null || !Directory.Exists(raiz)) return [];
            var exts = extensao.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(e => e.StartsWith('.') ? e : "." + e).ToArray();
            if (exts.Length == 0) return [];
            achados = Directory.EnumerateDirectories(raiz)
                .SelectMany(d => Directory.EnumerateFiles(d))
                .Where(f => exts.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                .Select(f => new FileInfo(f));
        }
        return achados
            .Where(f => f.Exists)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new VersaoPacote(Path.GetFileName(f.DirectoryName!), f.FullName,
                new DateTimeOffset(f.LastWriteTimeUtc).ToUnixTimeMilliseconds(), f.Length))
            .ToList();
    }
}
