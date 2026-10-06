using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace Pitstop;

public sealed record ScriptNpm(string Nome, string Comando);

/// <summary>
/// Perfil "npm" (ex.: frontend Angular, React, Vite): o que o IntelliJ faz numa Run Configuration "npm" —
/// <c>npm run &lt;script&gt; -- &lt;args&gt;</c> na pasta do package.json, com o Node do perfil na frente do PATH.
/// </summary>
public static class NodeApp
{
    /// <summary>Scripts do package.json da pasta (seletor da tela), na ordem do arquivo.</summary>
    public static List<ScriptNpm> Scripts(string pasta)
    {
        var arq = Path.Combine(pasta, "package.json");
        if (pasta.Trim() == "" || !File.Exists(arq)) throw new ErroRunner("sem package.json em " + pasta);
        var json = JsonNode.Parse(File.ReadAllText(arq, Encoding.UTF8)) as JsonObject;
        return (json?["scripts"] as JsonObject ?? new JsonObject())
            .Select(kv => new ScriptNpm(kv.Key, kv.Value?.ToString() ?? "")).ToList();
    }

    /// <summary>
    /// Pasta com os executáveis do Node do perfil: o node.exe fica na raiz do zip do Windows e em bin/ no tar do
    /// Linux. Aceita as duas (a raiz ou a própria bin). Vazio = o do PATH.
    /// </summary>
    public static string? BinNode(Perfil p)
    {
        if (string.IsNullOrEmpty(p.NodeHome)) return null;
        var bin = Path.Combine(p.NodeHome, "bin");
        return !So.Windows && File.Exists(Path.Combine(bin, "node")) ? bin : p.NodeHome;
    }

    /// <summary>npm do Node do perfil; sem nodeHome, o do PATH.</summary>
    static string Npm(Perfil p) => BinNode(p) is { } b ? "\"" + Path.Combine(b, So.Lancador("npm")) + "\"" : "npm";

    /// <summary>Linha para o shell: os args vão depois do "--" (chegam ao programa do script, não ao npm).</summary>
    public static string LinhaStart(Perfil p) =>
        Npm(p) + " run " + p.Script + (p.Args.Trim() != "" ? " -- " + p.Args.Trim() : "");

    /// <summary>Comando de build do perfil com "npm" trocado pelo npm do Node do perfil.</summary>
    public static string LinhaBuild(Perfil p)
    {
        var b = p.Build.Trim();
        return b.StartsWith("npm ", StringComparison.OrdinalIgnoreCase) ? Npm(p) + b[3..] : b;
    }

    /// <summary>
    /// Node do perfil na frente do PATH, variáveis do perfil (CHAVE=valor;...) e saída sem cor (o log é texto puro).
    /// BROWSER=none: o dev server não abre aba sozinho — quem abre é o botão ↗.
    /// </summary>
    public static void Ambiente(Perfil p, IDictionary<string, string?> env)
    {
        if (BinNode(p) is { } b) So.PrefixarPath(env, b);
        env["NO_COLOR"] = "1";
        env["FORCE_COLOR"] = "0";
        env["NPM_CONFIG_COLOR"] = "false";
        env["BROWSER"] = "none";
        foreach (var par in p.Env.Split(';').Select(s => s.Trim()).Where(s => s != ""))
        {
            var i = par.IndexOf('=');
            if (i > 0) env[par[..i].Trim()] = par[(i + 1)..].Trim();
        }
    }

    public static void Validar(Perfil p)
    {
        if (p.Pasta == "" || !File.Exists(Path.Combine(p.Pasta, "package.json")))
            throw new ErroRunner("perfil " + p.Nome + ": pasta sem package.json: " + p.Pasta);
        if (p.Script == "") throw new ErroRunner("perfil " + p.Nome + ": informe o script do package.json");
        if (!Scripts(p.Pasta).Any(s => s.Nome == p.Script))
            throw new ErroRunner("perfil " + p.Nome + ": script \"" + p.Script + "\" não existe no package.json");
        if (BinNode(p) is { } b && !File.Exists(Path.Combine(b, So.Exe("node"))))
            throw new ErroRunner("perfil " + p.Nome + ": " + So.Exe("node") + " não encontrado em " + p.NodeHome);
        if (!Directory.Exists(Path.Combine(p.Pasta, "node_modules")))
            throw new ErroRunner("perfil " + p.Nome + ": sem node_modules em " + p.Pasta + " — rode \"npm install\" (ou ponha no comando de Build)");
    }

    /// <summary>npm run ... pelo shell na pasta do projeto. O pit.pid guarda o shell (a árvore inclui o node).</summary>
    public static ProcessStartInfo Psi(Perfil p, string linha, bool redirecionar)
    {
        var psi = So.Linha(linha, p.Pasta, redirecionar, Encoding.UTF8);
        Ambiente(p, psi.Environment);
        return psi;
    }
}
