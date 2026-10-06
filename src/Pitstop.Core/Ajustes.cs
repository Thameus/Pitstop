using System.Text;
using System.Text.RegularExpressions;

namespace Pitstop;

/// <summary>Um ajuste global editável pelo assistente e pela tela de Ajustes (uma chave do .env).</summary>
public sealed record CampoAjuste(string Chave, string Rotulo, string Dica, string Tipo = "texto", string Padrao = "");

/// <summary>
/// Ajustes globais (o .env da raiz) lidos e gravados pela janela, pela tela web e pelo assistente da primeira
/// execução. Gravar preserva comentários e a ordem do arquivo; chave nova vai para o fim. Nenhum caminho vem
/// preenchido: o que o usuário não informar fica vazio (vale o do perfil ou o do PATH).
/// </summary>
public static class Ajustes
{
    public static readonly CampoAjuste[] Campos =
    [
        new("RUNNER_PORTA", "Porta da tela web", "http://localhost:<porta>/ (pit ui e a API)", "numero", "9999"),
        new("PROJETOS_DIR", "Pasta dos projetos", "onde ficam os repositórios Maven que viram artefatos do Tomcat", "pasta"),
        new("TOMCAT_HOME", "Tomcat padrão", "pasta do Tomcat (CATALINA_HOME) quando o perfil não informa outro", "pasta"),
        new("JDK_HOME", "JDK padrão", "JAVA_HOME do Tomcat, do Maven e dos apps quando o perfil não informa outro", "pasta"),
        new("MAVEN_HOME", "Maven padrão", "pasta do Maven; vazio = mvn do PATH", "pasta"),
        new("NODE_HOME", "Node.js padrão", "pasta do Node.js; vazio = node/npm do PATH", "pasta"),
        new("BASES_DIR", "Pasta das bases do Tomcat", "um CATALINA_BASE por perfil; vazio = <Pitstop>/bases", "pasta"),
        new("PACOTES_DIR", "Pasta de releases", "uma subpasta por versão com o .war/.zip dentro (perfis war e zip)", "pasta"),
        new("MAVEN_ARGS", "Argumentos do Maven", "sugeridos para artefato novo (mvn <args> -pl <módulo> -am)", "texto", "-B -ntp package -DskipTests"),
        new("MAVEN_OFFLINE", "Maven offline (-o)", "só com as dependências já baixadas no .m2", "sim", "false"),
        new("STOP_TIMEOUT_SEG", "Espera do stop (s)", "segundos esperando o stop gracioso antes de matar o processo", "numero", "20"),
    ];

    /// <summary>Primeira execução: ainda não existe .env (o assistente aparece).</summary>
    public static bool PrimeiraVez => !File.Exists(Env.Arquivo);

    /// <summary>Valor atual de cada campo (vazio = não definido).</summary>
    public static Dictionary<string, string> Ler() => Campos.ToDictionary(c => c.Chave, c => Env.Txt(c.Chave) ?? "");

    /// <summary>
    /// Grava os campos conhecidos no .env (cria a partir do .env.exemplo, se existir) e relê. Chave desconhecida é
    /// ignorada; valor vazio comenta a linha (volta ao padrão). Pasta informada que não existe = erro.
    /// </summary>
    public static void Gravar(IDictionary<string, string?> valores)
    {
        foreach (var c in Campos)
        {
            if (!valores.TryGetValue(c.Chave, out var v) || string.IsNullOrWhiteSpace(v)) continue;
            v = v.Trim();
            if (c.Tipo == "pasta" && !Directory.Exists(v)) throw new ErroRunner(c.Rotulo + ": pasta não existe: " + v);
            if (c.Tipo == "numero" && !(int.TryParse(v, out var n) && n > 0)) throw new ErroRunner(c.Rotulo + ": número inválido: " + v);
        }
        var arq = Env.Arquivo;
        var modelo = Path.Combine(Raiz.Dir, ".env.exemplo");
        var linhas = File.Exists(arq) ? File.ReadAllLines(arq, Encoding.UTF8).ToList()
            : File.Exists(modelo) ? File.ReadAllLines(modelo, Encoding.UTF8).ToList()
            : ["# Pitstop - ajustes globais (CHAVE=valor). Editado pelo assistente e pela tela de Ajustes."];
        foreach (var c in Campos)
        {
            if (!valores.TryGetValue(c.Chave, out var v)) continue;
            v = (v ?? "").Trim();
            var nova = v == "" ? "# " + c.Chave + "=" : c.Chave + "=" + v;
            var re = new Regex(@"^\s*#?\s*" + c.Chave + @"\s*=");
            var i = linhas.FindIndex(l => re.IsMatch(l));
            if (i >= 0) linhas[i] = nova;
            else if (v != "") linhas.Add(nova);
        }
        File.WriteAllText(arq, string.Join("\n", linhas) + "\n", new UTF8Encoding(false));
        Env.Recarregar();
    }

    /// <summary>
    /// Sugestões para os campos de pasta: o que já existe no ambiente (JAVA_HOME, MAVEN_HOME, CATALINA_HOME do
    /// sistema). Só sugere: nada é gravado sem o usuário confirmar.
    /// </summary>
    public static Dictionary<string, string> Sugestoes()
    {
        var r = new Dictionary<string, string>();
        void De(string chave, string variavel, Func<string, bool> ok)
        {
            var v = Environment.GetEnvironmentVariable(variavel);
            if (!string.IsNullOrWhiteSpace(v) && ok(v)) r[chave] = v;
        }
        De("JDK_HOME", "JAVA_HOME", d => File.Exists(Path.Combine(d, "bin", So.Exe("java"))));
        De("MAVEN_HOME", "MAVEN_HOME", d => File.Exists(Path.Combine(d, "bin", So.Lancador("mvn"))));
        if (!r.ContainsKey("MAVEN_HOME")) De("MAVEN_HOME", "M2_HOME", d => File.Exists(Path.Combine(d, "bin", So.Lancador("mvn"))));
        De("TOMCAT_HOME", "CATALINA_HOME", d => File.Exists(Path.Combine(d, "bin", So.Catalina)));
        De("NODE_HOME", "NODE_HOME", d => File.Exists(Path.Combine(d, So.Exe("node"))) || File.Exists(Path.Combine(d, "bin", So.Exe("node"))));
        return r;
    }
}
