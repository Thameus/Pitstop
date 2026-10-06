namespace Pitstop;

/// <summary>
/// Maven dos perfis Tomcat e aplicação Java: a pasta do perfil (mavenHome), a global (MAVEN_HOME do .env) ou o mvn
/// do PATH. Offline (-o) só se MAVEN_OFFLINE=true: sem as dependências já baixadas no .m2, o modo offline falha.
/// </summary>
public static class Maven
{
    public static bool Offline => Env.Sim(Env.Txt("MAVEN_OFFLINE", "false"));

    /// <summary>" -o" quando offline, senão vazio (para concatenar na linha do mvn).</summary>
    public static string FlagOffline => Offline ? " -o" : "";

    /// <summary>mvn do perfil entre aspas, ou "mvn" (o do PATH).</summary>
    public static string Comando(Perfil p) =>
        string.IsNullOrEmpty(p.MavenHome) ? "mvn" : "\"" + Path.Combine(p.MavenHome, "bin", So.Lancador("mvn")) + "\"";

    /// <summary>Comando de build do artefato com o "mvn" do começo trocado pelo Maven do perfil.</summary>
    public static string Linha(Perfil p, string build)
    {
        var b = build.Trim();
        return b == "mvn" || b.StartsWith("mvn ", StringComparison.Ordinal) ? Comando(p) + b[3..] : b;
    }

    public static void Validar(Perfil p)
    {
        if (string.IsNullOrEmpty(p.MavenHome)) return;
        var mvn = Path.Combine(p.MavenHome, "bin", So.Lancador("mvn"));
        if (!File.Exists(mvn)) throw new ErroRunner("perfil " + p.Nome + ": Maven não encontrado em " + p.MavenHome + " (falta bin/" + So.Lancador("mvn") + ")");
        So.GarantirExecutavel(mvn);
    }
}
