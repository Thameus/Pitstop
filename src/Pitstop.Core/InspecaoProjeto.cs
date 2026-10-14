namespace Pitstop;

public sealed record InspecaoResultado(string Tipo, string Nome, string Pasta, string Script,
    List<string> Scripts, List<ProjetoDescoberto> Projetos);

/// <summary>Inspeção local e somente leitura para adicionar projetos usando apenas uma pasta.</summary>
public static class InspecaoProjeto
{
    public static InspecaoResultado Inspecionar(string? pasta)
    {
        if (string.IsNullOrWhiteSpace(pasta)) throw new ErroRunner("escolha uma pasta para detectar o projeto");
        var raiz = Path.GetFullPath(pasta);
        if (!Directory.Exists(raiz)) throw new ErroRunner("pasta não existe: " + raiz);
        var nome = Path.GetFileName(Path.TrimEndingDirectorySeparator(raiz));
        if (File.Exists(Path.Combine(raiz, "package.json")))
        {
            var scripts = NodeApp.Scripts(raiz).Select(s => s.Nome).ToList();
            var script = scripts.Contains("dev") ? "dev" : scripts.Contains("start") ? "start" : scripts.FirstOrDefault() ?? "";
            return new InspecaoResultado("npm", nome, raiz, script, scripts, []);
        }
        if (File.Exists(Path.Combine(raiz, "pom.xml")))
        {
            var projetos = Projetos.Listar(raiz);
            return new InspecaoResultado(projetos.Any(p => p.Artefatos.Count > 0) ? "tomcat" : "java",
                nome, raiz, "", [], projetos);
        }
        var repos = Projetos.Listar(raiz);
        if (repos.Count > 0)
            return new InspecaoResultado("tomcat", nome, raiz, "", [], repos);
        throw new ErroRunner("não identifiquei package.json nem projeto Maven nessa pasta");
    }
}
