using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pitstop;

/// <summary>
/// Validação conservadora de um build web: entradas do repositório + saídas publicadas.
/// Uma exclusão também muda o fingerprint; um build falho nunca deixa um novo estado válido.
/// </summary>
public static class PreparacaoTomcat
{
    sealed record Marca(string Entradas, string Saidas);
    static readonly HashSet<string> Ignorar = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".idea", ".vscode", "target", "bin", "obj", "node_modules", ".gradle",
        ".settings", "dist", "build", "coverage"
    };

    static string Estado(Perfil p) => Path.Combine(p.Cache, "tomcat-preparado.json");

    static void Arquivos(string raiz, string pasta, List<string> itens, bool ignorarGerados)
    {
        if (!Directory.Exists(pasta)) return;
        foreach (var arquivo in Directory.EnumerateFiles(pasta))
        {
            var info = new FileInfo(arquivo);
            var rel = Path.GetRelativePath(raiz, arquivo).Replace('\\', '/');
            itens.Add(rel + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks);
        }
        foreach (var sub in Directory.EnumerateDirectories(pasta))
        {
            if (ignorarGerados && Ignorar.Contains(Path.GetFileName(sub))) continue;
            // Não seguir junctions/symlinks para fora do repositório.
            if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue;
            Arquivos(raiz, sub, itens, ignorarGerados);
        }
    }

    static string Hash(IEnumerable<string> itens)
    {
        using var sha = SHA256.Create();
        var texto = string.Join("\n", itens.OrderBy(x => x, StringComparer.Ordinal));
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(texto)));
    }

    static string FingerprintEntradas(Perfil p)
    {
        var linhas = new List<string> { "v2", p.JavaHome ?? "", p.MavenHome ?? "" };
        // Ferramentas/configuração externa influenciam o reactor mesmo sem alterar fontes.
        foreach (var arq in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".m2", "settings.xml"),
            Path.Combine(p.JavaHome ?? "", "release"),
            Path.Combine(p.MavenHome ?? "", "conf", "settings.xml"),
        })
            if (File.Exists(arq))
            {
                var i = new FileInfo(arq);
                linhas.Add(arq + "|" + i.Length + "|" + i.LastWriteTimeUtc.Ticks);
            }
        foreach (var a in p.Artefatos.OrderBy(a => a.Repo + "|" + a.Modulo, StringComparer.Ordinal))
        {
            linhas.Add(a.Repo + "|" + a.Modulo + "|" + a.Contexto + "|" + a.Build + "|" + a.DocBase);
            Arquivos(a.Repo, a.Repo, linhas, true);
        }
        return Hash(linhas);
    }

    static string? FingerprintSaidas(Perfil p)
    {
        var linhas = new List<string>();
        foreach (var a in p.Artefatos)
        {
            var destino = Tomcat.ResolverDocBase(a);
            if (destino == null || !Directory.Exists(Path.Combine(destino, "WEB-INF"))) return null;
            linhas.Add(a.Repo + "|" + a.Modulo + "|" + destino);
            // Conferir também exclusões/modificações na saída, sem reconstruir por timestamps iguais.
            Arquivos(destino, destino, linhas, false);
            var target = Path.Combine(a.Repo, a.Modulo, "target");
            if (Directory.Exists(target))
            {
                // Artefatos auxiliares gerados em package (ex.: replicador) e relatórios em classes.
                foreach (var jar in Directory.EnumerateFiles(target, "*.jar", SearchOption.TopDirectoryOnly))
                {
                    var i = new FileInfo(jar);
                    linhas.Add(jar + "|" + i.Length + "|" + i.LastWriteTimeUtc.Ticks);
                }
                var classes = Path.Combine(target, "classes");
                if (Directory.Exists(classes)) Arquivos(classes, classes, linhas, false);
            }
        }
        return Hash(linhas);
    }

    public static bool Atualizado(Perfil p)
    {
        try
        {
            if (p.Artefatos.Count == 0 || !File.Exists(Estado(p))) return false;
            var marca = JsonSerializer.Deserialize<Marca>(File.ReadAllText(Estado(p)));
            return marca != null && marca.Entradas == FingerprintEntradas(p)
                && marca.Saidas == FingerprintSaidas(p);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    public static void Registrar(Perfil p)
    {
        var saidas = FingerprintSaidas(p) ?? throw new ErroRunner("build sem WEB-INF publicado; cache não atualizado");
        var marca = new Marca(FingerprintEntradas(p), saidas);
        Config.GravarAtomico(Estado(p), JsonSerializer.Serialize(marca) + "\n", false);
    }

    public static void Invalidar(Perfil p)
    {
        try { if (File.Exists(Estado(p))) File.Delete(Estado(p)); }
        catch (IOException) { }
    }
}
