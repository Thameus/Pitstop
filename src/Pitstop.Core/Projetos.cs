namespace Pitstop;

public sealed class ArtefatoDescoberto
{
    public string Repo { get; init; } = "";
    public string Modulo { get; init; } = "";
    public string? ArtifactId { get; init; }
    public string Contexto { get; init; } = "";
    public string Build { get; init; } = "";
    public List<RegraSync> Sync { get; init; } = [];
    public string? DocBase { get; init; }
}

public sealed class ProjetoDescoberto
{
    public string Nome { get; init; } = "";
    public string Repo { get; init; } = "";
    public List<ArtefatoDescoberto> Artefatos { get; init; } = [];
    public string? Erro { get; init; }
}

/// <summary>Repositórios Maven em projetosDir e os módulos war de cada um (o "war exploded" do IntelliJ).</summary>
public static class Projetos
{
    /// <summary><paramref name="dir"/> = pasta de projetos do perfil; vazio = PROJETOS_DIR do .env (bloco global).</summary>
    /// <summary>
    /// Descobre a pasta padrão e também os repositórios selecionados explicitamente no perfil.
    /// Repositórios externos inacessíveis continuam na lista para que a UI nunca apague o perfil.
    /// </summary>
    public static List<ProjetoDescoberto> Listar(string? dir = null, IEnumerable<string>? selecionados = null)
    {
        var comparer = So.Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var escolhidos = (selecionados ?? []).Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => Path.GetFullPath(s.Trim())).Distinct(comparer).ToArray();
        var raiz = !string.IsNullOrWhiteSpace(dir) ? dir.Trim() : JsonAux.Txt(Config.Ler(), "projetosDir");
        if (string.IsNullOrWhiteSpace(raiz) && escolhidos.Length == 0)
            throw new ErroRunner("defina a pasta de projetos (neste perfil ou em Ajustes)");
        var repos = new HashSet<string>(comparer);
        if (!string.IsNullOrWhiteSpace(raiz))
        {
            raiz = Path.GetFullPath(raiz);
            if (!Directory.Exists(raiz))
            {
                if (escolhidos.Length == 0) throw new ErroRunner("pasta de projetos não existe: " + raiz);
            }
            else if (File.Exists(Path.Combine(raiz, "pom.xml"))) repos.Add(raiz);
            else
                foreach (var d in Directory.EnumerateDirectories(raiz))
                    if (File.Exists(Path.Combine(d, "pom.xml"))) repos.Add(Path.GetFullPath(d));
        }
        foreach (var repo in escolhidos) repos.Add(repo);
        return repos.OrderBy(d => d, comparer).Select(repo =>
        {
            var lista = new List<ArtefatoDescoberto>();
            if (!Directory.Exists(repo))
                return new ProjetoDescoberto { Nome = Path.GetFileName(repo), Repo = repo, Erro = "Pasta indisponível: " + repo };
            if (!File.Exists(Path.Combine(repo, "pom.xml")))
                return new ProjetoDescoberto { Nome = Path.GetFileName(repo), Repo = repo, Erro = "Projeto Maven sem pom.xml: " + repo };
            try
            {
                DescobrirWars(repo, "", lista, new HashSet<string>(comparer));
                return new ProjetoDescoberto { Nome = Path.GetFileName(repo), Repo = repo, Artefatos = lista };
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new ProjetoDescoberto { Nome = Path.GetFileName(repo), Repo = repo, Erro = "Não foi possível ler o projeto: " + e.Message };
            }
        }).ToList();
    }

    static void DescobrirWars(string repo, string rel, List<ArtefatoDescoberto> lista, HashSet<string> visitados)
    {
        var dir = Path.GetFullPath(Path.Combine(repo, rel));
        // Reactor Maven pode ter mais de dois níveis e até módulos cíclicos/malformados.
        if (!visitados.Add(dir)) return;
        var pom = Pom.Ler(dir);
        if (pom == null) return;
        if (pom.Packaging == "war")
        {
            var web = Directory.Exists(Path.Combine(dir, "WebContent")) ? "WebContent"
                : Directory.Exists(Path.Combine(dir, "src", "main", "webapp")) ? "src/main/webapp" : null;
            var sync = new List<RegraSync>();
            if (web != null) sync.Add(new RegraSync(web, ""));
            sync.Add(new RegraSync("target/classes", "WEB-INF/classes"));
            lista.Add(new ArtefatoDescoberto
            {
                Repo = repo,
                Modulo = rel,
                ArtifactId = pom.ArtifactId,
                Contexto = "/" + pom.Contexto,
                Build = "mvn " + Config.MavenArgs + (rel != "" ? " -pl " + rel.Replace('\\', '/') + " -am" : ""),
                Sync = sync,
                DocBase = Tomcat.ResolverDocBase(new Artefato { Repo = repo, Modulo = rel }),
            });
        }
        foreach (var mo in pom.Modulos)
            DescobrirWars(repo, Path.GetRelativePath(repo, Path.GetFullPath(Path.Combine(dir, mo))), lista, visitados);
    }
}
