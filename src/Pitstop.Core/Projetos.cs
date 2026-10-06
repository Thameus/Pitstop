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
}

/// <summary>Repositórios Maven em projetosDir e os módulos war de cada um (o "war exploded" do IntelliJ).</summary>
public static class Projetos
{
    /// <summary><paramref name="dir"/> = pasta de projetos do perfil; vazio = PROJETOS_DIR do .env (bloco global).</summary>
    public static List<ProjetoDescoberto> Listar(string? dir = null)
    {
        var raiz = !string.IsNullOrWhiteSpace(dir) ? dir.Trim()
            : JsonAux.Txt(Config.Ler(), "projetosDir") ?? throw new ErroRunner("defina a pasta de projetos (neste perfil ou em Ajustes)");
        if (!Directory.Exists(raiz)) throw new ErroRunner("pasta de projetos não existe: " + raiz);
        return Directory.EnumerateDirectories(raiz)
            .Where(d => File.Exists(Path.Combine(d, "pom.xml")))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .Select(repo =>
            {
                var lista = new List<ArtefatoDescoberto>();
                DescobrirWars(repo, "", 0, lista);
                return new ProjetoDescoberto { Nome = Path.GetFileName(repo), Repo = repo, Artefatos = lista };
            })
            .ToList();
    }

    static void DescobrirWars(string repo, string rel, int prof, List<ArtefatoDescoberto> lista)
    {
        var dir = Path.Combine(repo, rel);
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
        if (prof < 2)
            foreach (var mo in pom.Modulos)
                DescobrirWars(repo, Path.GetRelativePath(repo, Path.GetFullPath(Path.Combine(dir, mo))), prof + 1, lista);
    }
}
