using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Pitstop;

/// <summary>
/// ResourceSets para modo desenvolvimento sem cópia da webapp/classes.
/// Só é ativado explicitamente e exige artefato convencional válido como fallback.
/// </summary>
public static class TomcatDireto
{
    static string Atr(string texto) => texto.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;");

    static int VersaoMaior(Perfil p)
    {
        try
        {
            var jar = Path.Combine(p.Home!, "lib", "catalina.jar");
            using var zip = ZipFile.OpenRead(jar);
            var entry = zip.GetEntry("org/apache/catalina/util/ServerInfo.properties");
            if (entry == null) return 0;
            using var reader = new StreamReader(entry.Open());
            var match = Regex.Match(reader.ReadToEnd(), @"(?m)^server\.number\s*=\s*(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { return 0; }
    }

    public static bool Tentar(Perfil p, Artefato a, string convencional, out string xml, out string web, out string motivo)
    {
        xml = ""; web = convencional; motivo = "";
        if (VersaoMaior(p) < 8) { motivo = "Tomcat inferior a 8 ou versão não identificada"; return false; }
        var modulo = Path.GetFullPath(Path.Combine(a.Repo, a.Modulo));
        var fonte = Path.Combine(modulo, "WebContent");
        if (!Directory.Exists(fonte)) fonte = Path.Combine(modulo, "src", "main", "webapp");
        var classes = Path.Combine(modulo, "target", "classes");
        if (!Directory.Exists(fonte) || !Directory.Exists(classes))
        {
            motivo = "fontes web ou target/classes indisponíveis";
            return false;
        }
        var webXmlMontado = Path.Combine(convencional, "WEB-INF", "web.xml");
        if (File.Exists(webXmlMontado) && !File.Exists(Path.Combine(fonte, "WEB-INF", "web.xml")))
        {
            motivo = "web.xml gerado pela montagem WAR (não está na pasta web)";
            return false;
        }
        var recursos = new List<string>
        {
            "    <PreResources className=\"org.apache.catalina.webresources.DirResourceSet\" " +
              "base=\"" + Atr(classes) + "\" webAppMount=\"/WEB-INF/classes\" readOnly=\"true\" />"
        };
        var libs = Path.Combine(convencional, "WEB-INF", "lib");
        if (Directory.Exists(libs))
        {
            foreach (var jar in Directory.EnumerateFiles(libs, "*.jar").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                recursos.Add("    <PostResources className=\"org.apache.catalina.webresources.FileResourceSet\" " +
                    "base=\"" + Atr(jar) + "\" webAppMount=\"/WEB-INF/lib/" + Atr(Path.GetFileName(jar)) +
                    "\" readOnly=\"true\" />");
        }
        web = fonte;
        xml = "<Context path=\"" + Atr(a.Contexto) + "\" antiResourceLocking=\"false\" " +
              "docBase=\"" + Atr(web) + "\" reloadable=\"" + (p.ReloadAutomatico ? "true" : "false") + "\">\n" +
              "  <Resources className=\"org.apache.catalina.webresources.StandardRoot\">\n" +
              string.Join("\n", recursos) + "\n  </Resources>\n</Context>\n";
        return true;
    }
}
