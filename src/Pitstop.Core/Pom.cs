using System.Xml.Linq;

namespace Pitstop;

/// <summary>
/// pom.xml lido com XDocument (namespace ignorado; comentários e &lt;parent&gt;/&lt;dependencies&gt; não confundem
/// groupId/artifactId, que vêm só dos filhos diretos da raiz). pom inválido = null.
/// </summary>
public sealed record Pom(string? GroupId, string? ArtifactId, string Packaging, string? Contexto, List<string> Modulos)
{
    public static Pom? Ler(string dir)
    {
        var f = Path.Combine(dir, "pom.xml");
        if (!File.Exists(f)) return null;
        XElement raiz;
        try { raiz = XDocument.Load(f).Root!; }
        catch { return null; }

        var pai = Filho(raiz, "parent");
        var artifactId = Texto(Filho(raiz, "artifactId"));
        // módulos de qualquer lugar (inclusive <profiles>), como o regex antigo
        var modulos = Todos(raiz, "module").Select(x => x.Value.Trim()).Where(s => s != "").Distinct().ToList();
        // contexto: o mesmo que a IDE sugere (wtpContextName > finalName literal > artifactId)
        var wtp = Texto(Todos(raiz, "wtpContextName").FirstOrDefault());
        var finalName = Todos(raiz, "finalName").Select(x => x.Value.Trim()).FirstOrDefault(x => x != "" && !x.Contains("${"));
        return new Pom(
            Texto(Filho(raiz, "groupId")) ?? Texto(Filho(pai, "groupId")),
            artifactId,
            Texto(Filho(raiz, "packaging")) ?? "jar",
            wtp ?? finalName ?? artifactId,
            modulos);
    }

    static XElement? Filho(XElement? e, string nome) => e?.Elements().FirstOrDefault(x => x.Name.LocalName == nome);
    static IEnumerable<XElement> Todos(XElement e, string nome) => e.Descendants().Where(x => x.Name.LocalName == nome);
    static string? Texto(XElement? e) => e?.Value.Trim() is { Length: > 0 } v ? v : null;
}
