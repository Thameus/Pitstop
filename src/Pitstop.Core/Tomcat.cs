using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Pitstop;

public sealed record Publicado(string Contexto, string DocBase);

/// <summary>
/// Mesmo mecanismo da Run Configuration Tomcat do IntelliJ: um CATALINA_BASE por perfil, um XML de contexto
/// por artefato apontando o docBase para a pasta explodida em target/, e catalina run (.bat no Windows, .sh no Linux).
/// </summary>
public static class Tomcat
{
    /// <summary>Pasta explodida mais recente em &lt;repo&gt;\&lt;modulo&gt;\target que tenha WEB-INF.</summary>
    public static string? ResolverDocBase(Artefato a)
    {
        if (!string.IsNullOrEmpty(a.DocBase)) return a.DocBase;
        // perfil "war": a extração do war, se já feita (quem extrai é a subida ou o Build)
        if (a.War != null) return Pacote.Extraido(a.Extracao) ? a.Extracao : null;
        var alvo = Path.Combine(a.Repo, a.Modulo, "target");
        if (!Directory.Exists(alvo)) return null;
        string? melhor = null;
        var data = DateTime.MinValue;
        foreach (var dir in Directory.EnumerateDirectories(alvo))
        {
            var webinf = Path.Combine(dir, "WEB-INF");
            if (!Directory.Exists(webinf)) continue;
            var t = Directory.GetLastWriteTimeUtc(webinf);
            if (melhor == null || t > data) { melhor = dir; data = t; }
        }
        return melhor;
    }

    static string NomeArquivoContexto(string ctx)
    {
        var c = ctx.Trim('/');
        return (c == "" ? "ROOT" : c.Replace('/', '#')) + ".xml";
    }

    static string XmlAttr(string s) => s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;");

    /// <summary>Copia a árvore sem sobrescrever o que já existe (o app pode gravar nos próprios .properties). Devolve quantos copiou.</summary>
    internal static int CopiarSemSobrescrever(string de, string para, string[]? ignorar = null)
    {
        Directory.CreateDirectory(para);
        var n = 0;
        foreach (var d in new DirectoryInfo(de).EnumerateFileSystemInfos())
        {
            if (ignorar != null && ignorar.Contains(d.Name)) continue;
            var alvo = Path.Combine(para, d.Name);
            if (d is DirectoryInfo) n += CopiarSemSobrescrever(d.FullName, alvo);
            else if (!File.Exists(alvo)) { File.Copy(d.FullName, alvo); n++; }
        }
        return n;
    }

    /// <summary>Cria/atualiza o CATALINA_BASE do perfil e grava os contextos. Devolve o que foi publicado.</summary>
    public static List<Publicado> PrepararBase(Perfil p)
    {
        if (p.Home == null) throw new ErroRunner("perfil " + p.Nome + ": informe a pasta do Tomcat (aba Servidor)");
        if (!File.Exists(Path.Combine(p.Home, "bin", So.Catalina)))
            throw new ErroRunner("Tomcat não encontrado em " + p.Home + " (falta bin/" + So.Catalina + ")");
        if (string.IsNullOrEmpty(p.JavaHome)) throw new ErroRunner("perfil " + p.Nome + ": informe o JDK (aba Servidor)");
        var origem = p.ConfOrigem!;
        foreach (var d in new[] { "conf", "logs", "temp", "work", "webapps" })
            Directory.CreateDirectory(Path.Combine(p.Base, d));
        CopiarSemSobrescrever(origem, Path.Combine(p.Base, "conf"), ["Catalina", "server.xml"]);

        // server.xml sempre regenerado a partir da origem: portas do perfil; AJP só se o perfil pedir.
        // Regex (não XDocument) de propósito: preserva comentários e formatação do arquivo original.
        // Regex que não casa = erro: gravar assim mesmo subiria o Tomcat nas portas da origem, não nas do perfil.
        var arqOrigem = Path.Combine(origem, "server.xml");
        var xml = File.ReadAllText(arqOrigem);
        var reServer = new Regex("(<Server\\s+port=\")-?\\d+(\")");
        var reHttp = new Regex("(<Connector\\s+port=\")\\d+(\"\\s+protocol=\"HTTP/1\\.1\"[^>]*/>)");
        if (!reServer.IsMatch(xml))
            throw new ErroRunner("server.xml sem <Server port=\"...\"> (port como primeiro atributo): " + arqOrigem);
        xml = reServer.Replace(xml, m => m.Groups[1].Value + p.PortaShutdown + m.Groups[2].Value, 1);
        xml = Regex.Replace(xml, "<Connector[^>]*protocol=\"AJP/1\\.3\"[^>]*/>", "");
        if (!reHttp.IsMatch(xml))
            throw new ErroRunner("server.xml sem <Connector port=\"...\" protocol=\"HTTP/1.1\" ... /> (port e protocol nessa ordem, fechado com />): " + arqOrigem);
        xml = reHttp.Replace(xml, m =>
        {
            var ajp = p.PortaAjp is int a
                ? "\n    <Connector port=\"" + a + "\" protocol=\"AJP/1.3\" address=\"127.0.0.1\" redirectPort=\"8443\" />"
                : "";
            return m.Groups[1].Value + p.Porta + m.Groups[2].Value + ajp;
        }, 1);
        File.WriteAllText(Path.Combine(p.Base, "conf", "server.xml"), xml);

        var ctxDir = Path.Combine(p.Base, "conf", "Catalina", "localhost");
        Directory.CreateDirectory(ctxDir);
        foreach (var f in Directory.EnumerateFiles(ctxDir, "*.xml")) File.Delete(f);
        return p.Artefatos.Select(a =>
        {
            var docBase = ResolverDocBase(a) ?? throw new ErroRunner(a.War != null
                ? "war de " + a.Contexto + " ainda não extraído: " + a.War + " (Iniciar ou Build extraem)"
                : "pasta explodida não encontrada para " + a.Contexto + " (rode o build)");
            File.WriteAllText(Path.Combine(ctxDir, NomeArquivoContexto(a.Contexto)),
                "<Context path=\"" + XmlAttr(a.Contexto) + "\" antiResourceLocking=\"false\" docBase=\"" + XmlAttr(docBase) + "\" />\n");
            return new Publicado(a.Contexto, docBase);
        }).ToList();
    }

    /// <summary>catalina &lt;args&gt; pelo shell, com CATALINA_HOME/BASE, JAVA_HOME e CATALINA_OPTS do perfil.</summary>
    public static Process Catalina(Perfil p, string args, bool debug, bool redirecionar)
    {
        var script = Path.Combine(p.Home!, "bin", So.Catalina);
        So.GarantirExecutavel(script);
        // console do Tomcat sai em UTF-8 (logging.properties)
        var psi = So.Linha("\"" + script + "\" " + args, p.Base, redirecionar, Encoding.UTF8);
        var env = psi.Environment;
        env["CATALINA_HOME"] = p.Home;
        env["CATALINA_BASE"] = p.Base;
        env["JAVA_HOME"] = p.JavaHome;
        env["CATALINA_OPTS"] = string.Join(" ", new[] { p.VmArgs }.Concat(OpcoesJmx(p))).Trim();
        env["TITLE"] = "pit-" + p.Nome;
        env.Remove("JRE_HOME");
        if (debug)
        {
            env["JPDA_ADDRESS"] = "localhost:" + p.PortaDebug;
            env["JPDA_TRANSPORT"] = "dt_socket";
            env["JPDA_SUSPEND"] = "n";
        }
        return Proc.Disparar(psi);
    }

    /// <summary>JMX como o IntelliJ liga para conversar com o Tomcat; só localhost e sem senha.</summary>
    static IEnumerable<string> OpcoesJmx(Perfil p)
    {
        if (p.PortaJmx is not int j) return [];
        return
        [
            "-Dcom.sun.management.jmxremote",
            "-Dcom.sun.management.jmxremote.port=" + j,
            "-Dcom.sun.management.jmxremote.rmi.port=" + j,
            "-Dcom.sun.management.jmxremote.host=127.0.0.1",
            "-Dcom.sun.management.jmxremote.authenticate=false",
            "-Dcom.sun.management.jmxremote.ssl=false",
            "-Djava.rmi.server.hostname=127.0.0.1",
        ];
    }

    /// <summary>
    /// Copia só o que é mais novo que o destino (File.Copy preserva a data). Devolve a quantidade copiada. Arquivo que
    /// não copia (jar travado pelo Tomcat, sem permissão) vai para <paramref name="falhas"/> e o resto segue.
    /// Não é espelho: o que sumiu da origem continua no destino.
    /// </summary>
    public static int CopiarNovos(string de, string para, List<string> falhas)
    {
        if (!Directory.Exists(de)) return 0;
        var n = 0;
        foreach (var d in new DirectoryInfo(de).EnumerateFileSystemInfos())
        {
            var t = Path.Combine(para, d.Name);
            if (d is DirectoryInfo) { n += CopiarNovos(d.FullName, t, falhas); continue; }
            if (File.Exists(t) && File.GetLastWriteTimeUtc(t) >= d.LastWriteTimeUtc) continue;
            try
            {
                Directory.CreateDirectory(para);
                File.Copy(d.FullName, t, true);
                n++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                falhas.Add(t + ": " + e.Message);
            }
        }
        return n;
    }

    /// <summary>Tomcats irmãos do TOMCAT_HOME (as outras pastas com bin/catalina na mesma pasta-mãe).</summary>
    public static List<string> Instalados()
    {
        var home = JsonAux.Txt(Config.Ler(), "tomcatHome");
        if (home == null) return [];
        var raiz = Path.GetDirectoryName(Path.GetFullPath(home));
        if (raiz == null || !Directory.Exists(raiz)) return [];
        return Directory.EnumerateDirectories(raiz)
            .Where(d => File.Exists(Path.Combine(d, "bin", So.Catalina)))
            .ToList();
    }
}
