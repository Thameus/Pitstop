using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pitstop;

public sealed record ClasseMain(string Classe, bool Teste);

/// <summary>
/// Perfil "aplicação Java" (ex.: app desktop Swing/JavaFX, serviço com main): o que o IntelliJ faz numa Run
/// Configuration "Application".
/// <list type="bullet">
/// <item>classpath: <c>mvn -o dependency:build-classpath</c> (resolve, não compila), com os módulos do próprio
/// repositório trocados pelo target/classes deles (o IntelliJ roda o código do disco, não o jar do .m2);</item>
/// <item>compila só se algum módulo da cadeia tem fonte mais novo que as classes;</item>
/// <item>jar de classpath (só MANIFEST com Class-Path): a lista passa de 32 767 caracteres, o limite da linha de
/// comando do Windows, e o Java 8 não aceita @arquivo (é o shortenClasspath=MANIFEST da IDE).</item>
/// </list>
/// </summary>
public static class JavaApp
{
    static readonly HashSet<string> Ignorar = ["target", ".git", ".idea", "node_modules", ".settings"];

    // ---------------------------------------------------------------- pom e módulos do repositório

    /// <summary>Raiz do reactor: o ancestral mais alto do módulo que ainda tem pom.xml.</summary>
    public static string? RaizRepo(string modulo)
    {
        string? raiz = null;
        var d = Path.GetFullPath(modulo);
        while (File.Exists(Path.Combine(d, "pom.xml")))
        {
            raiz = d;
            var pai = Path.GetDirectoryName(d);
            if (pai == null) break;
            d = pai;
        }
        return raiz;
    }

    /// <summary>'groupId:artifactId' -> pasta, para todos os módulos do reactor.</summary>
    static Dictionary<string, string> ModulosDoRepo(string raiz)
    {
        var r = new Dictionary<string, string>();
        void Andar(string dir, int prof)
        {
            var pom = Pom.Ler(dir);
            if (pom == null) return;
            if (pom.GroupId != null && pom.ArtifactId != null) r[pom.GroupId + ":" + pom.ArtifactId] = dir;
            if (prof < 4)
                foreach (var mo in pom.Modulos) Andar(Path.GetFullPath(Path.Combine(dir, mo)), prof + 1);
        }
        Andar(raiz, 0);
        return r;
    }

    // ---------------------------------------------------------------- datas: fonte x classes

    /// <summary>Maior data sob <paramref name="dir"/> (recursivo), pulando target/.git/.idea.</summary>
    static long MaisNovo(string dir)
    {
        if (File.Exists(dir)) return File.GetLastWriteTimeUtc(dir).Ticks;
        if (!Directory.Exists(dir)) return 0;
        long max = 0;
        foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos())
        {
            if (Ignorar.Contains(e.Name)) continue;
            var t = e is DirectoryInfo ? MaisNovo(e.FullName) : e.LastWriteTimeUtc.Ticks;
            if (t > max) max = t;
        }
        return max;
    }

    /// <summary>Fontes de um módulo: pom.xml + pastas src* e resources na raiz do módulo (layout antigo e o padrão Maven).</summary>
    static long FontesMaisNovas(string modDir)
    {
        var pom = Path.Combine(modDir, "pom.xml");
        var max = File.Exists(pom) ? File.GetLastWriteTimeUtc(pom).Ticks : 0;
        foreach (var d in Directory.EnumerateDirectories(modDir))
        {
            var nome = Path.GetFileName(d);
            if (!(nome.StartsWith("src", StringComparison.OrdinalIgnoreCase) || nome == "resources")) continue;
            max = Math.Max(max, MaisNovo(d));
        }
        return max;
    }

    /// <summary>Módulos com fonte mais nova que o target\classes (ou sem classes ainda).</summary>
    public static List<string> Desatualizados(IEnumerable<string> modDirs) =>
        modDirs.Where(dir =>
        {
            var classes = Path.Combine(dir, "target", "classes");
            return !Directory.Exists(classes) || FontesMaisNovas(dir) > MaisNovo(classes);
        }).ToList();

    // ---------------------------------------------------------------- classpath

    /// <summary>pom mudou (dependência nova/versão) = classpath velho.</summary>
    static string ChaveCache(Perfil p, Dictionary<string, string> mods) => JsonSerializer.Serialize(new
    {
        modulo = Path.GetFullPath(p.Modulo),
        poms = mods.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k =>
        {
            var f = Path.Combine(mods[k], "pom.xml");
            return k + "@" + (File.Exists(f) ? File.GetLastWriteTimeUtc(f).Ticks : 0);
        }),
    });

    /// <summary>Caminho no .m2 -> 'groupId:artifactId' (…\repository\br\com\x\artifact\versao\arquivo.jar).</summary>
    static string? Coordenada(string jar)
    {
        var partes = Path.GetFullPath(jar).Split(Path.DirectorySeparatorChar);
        var i = Array.FindLastIndex(partes, s => s.Equals("repository", StringComparison.OrdinalIgnoreCase));
        if (i < 0 || partes.Length - i < 5) return null;
        return string.Join(".", partes[(i + 1)..(partes.Length - 3)]) + ":" + partes[^3];
    }

    public sealed record Classpath(string Raiz, List<string> Entradas, List<string> ModulosRepo);

    /// <summary>
    /// Entradas do classpath: target\classes do módulo principal, depois as dependências de runtime com os módulos
    /// do repositório trocados pelo target\classes deles. Cacheado em &lt;cache&gt;\classpath.txt.
    /// <paramref name="rodar"/>(cmd, cwd) executa um comando e devolve o código de saída.
    /// </summary>
    public static async Task<Classpath> ResolverClasspath(Perfil p, Func<string, string, Task<int>> rodar, Action<string> log, bool forcar)
    {
        var raiz = RaizRepo(p.Modulo) ?? throw new ErroRunner("sem pom.xml em " + p.Modulo);
        var mods = ModulosDoRepo(raiz);
        var arq = Path.Combine(p.Cache, "classpath.txt");
        var arqChave = Path.Combine(p.Cache, "classpath.chave");
        var chave = ChaveCache(p, mods);
        Directory.CreateDirectory(p.Cache);
        if (forcar || !File.Exists(arq) || !File.Exists(arqChave) || File.ReadAllText(arqChave) != chave)
        {
            var rel = Path.GetRelativePath(raiz, Path.GetFullPath(p.Modulo)).Replace('\\', '/');
            if (rel == ".") rel = "";
            log("[pit] classpath: resolvendo dependências de " + (rel == "" ? "." : rel) + " (Maven, não compila)...");
            var bruto = Path.Combine(p.Cache, "classpath-maven.txt");
            if (File.Exists(bruto)) File.Delete(bruto);
            var alvo = (rel != "" ? " -pl " + rel : "") + " \"-Dmdep.outputFile=" + bruto + "\" -Dmdep.includeScope=runtime";
            var cmd = Maven.Comando(p) + Maven.FlagOffline + " -q -B dependency:build-classpath" + alvo;
            var code = await rodar(cmd, raiz).ConfigureAwait(false);
            if (code != 0 || !File.Exists(bruto))
            {
                // módulos do repo na versão do pom ainda não instalados no .m2 (ex.: branch de HF com versão nova).
                // O Maven 3 só entrega o target\classes de um módulo do reactor quando a fase compile roda na mesma
                // execução: -am põe os módulos de que ele depende no reactor e compila só o que mudou. O outputFile é
                // sobrescrito a cada módulo; o último do reactor é o próprio módulo (os outros são dependências dele).
                log("[pit] classpath: módulos do repositório nesta versão não estão no .m2 (erros acima); resolvendo pelo reactor, compilando a cadeia...");
                if (File.Exists(bruto)) File.Delete(bruto);
                cmd = Maven.Comando(p) + Maven.FlagOffline + " -q -B compile dependency:build-classpath" + (rel != "" ? " -am" : "") + alvo;
                code = await rodar(cmd, raiz).ConfigureAwait(false);
                if (code != 0 || !File.Exists(bruto)) throw new ErroRunner("não consegui gerar o classpath (código " + code + "): " + cmd);
            }
            File.WriteAllText(arq, File.ReadAllText(bruto).Trim());
            File.WriteAllText(arqChave, chave);
        }
        var doRepo = new List<string>();
        var entradas = new List<string> { Path.Combine(Path.GetFullPath(p.Modulo), "target", "classes") };
        // resolvido pelo reactor: o módulo do repo já vem como <módulo>\target\classes, não como jar do .m2
        var porClasses = mods.Values.Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(d => Path.GetFullPath(Path.Combine(d, "target", "classes")), d => d, StringComparer.OrdinalIgnoreCase);
        // o Maven separa com o separador do sistema (; no Windows, : no Linux)
        foreach (var e in File.ReadAllText(arq).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Regex.IsMatch(e, @"\.(jar|zip)$", RegexOptions.IgnoreCase) && !Path.Exists(e)) continue;
            if (e.EndsWith(".pom", StringComparison.OrdinalIgnoreCase)) continue;   // dependência "pom" não entra
            var c = Coordenada(e);
            if (c != null && mods.TryGetValue(c, out var dir) && Directory.Exists(Path.Combine(dir, "target", "classes")))
            {
                doRepo.Add(dir);
                entradas.Add(Path.Combine(dir, "target", "classes"));
            }
            else if (Directory.Exists(e) && porClasses.TryGetValue(Path.GetFullPath(e).TrimEnd('\\', '/'), out var dirMod))
            {
                doRepo.Add(dirMod);
                entradas.Add(e);
            }
            else entradas.Add(e);
        }
        return new Classpath(raiz, entradas, [Path.GetFullPath(p.Modulo), .. doRepo]);
    }

    // ---------------------------------------------------------------- jar de classpath

    /// <summary>encodeURI do JS (o que a JVM espera no Class-Path) + '#' escapado.</summary>
    static string UrlArquivo(string f, bool dir)
    {
        // C:\a -> file:/C:/a ; /home/a -> file:/home/a
        var cheio = Path.GetFullPath(f).Replace('\\', '/');
        var u = "file:" + (cheio.StartsWith('/') ? "" : "/") + cheio + (dir ? "/" : "");
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(u))
        {
            var c = (char)b;
            if (b < 128 && (char.IsAsciiLetterOrDigit(c) || ";,/?:@&=+$-_.!~*'()".Contains(c))) sb.Append(c);
            else sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    /// <summary>MANIFEST com linhas de no máximo 72 bytes (continuação começa com um espaço).</summary>
    public static string Manifesto(IEnumerable<string> entradas)
    {
        var cp = string.Join(" ", entradas.Select(e =>
        {
            var dir = Directory.Exists(e) || (!File.Exists(e) && !Regex.IsMatch(e, @"\.(jar|zip)$", RegexOptions.IgnoreCase));
            return UrlArquivo(e, dir);
        }));
        var linhas = new List<string>();
        var resto = "Class-Path: " + cp;
        linhas.Add(resto[..Math.Min(72, resto.Length)]);
        resto = resto[Math.Min(72, resto.Length)..];
        while (resto.Length > 0)
        {
            var n = Math.Min(71, resto.Length);
            linhas.Add(" " + resto[..n]);
            resto = resto[n..];
        }
        return "Manifest-Version: 1.0\r\nCreated-By: Pitstop\r\n" + string.Join("\r\n", linhas) + "\r\n\r\n";
    }

    public static void GravarJarClasspath(string destino, List<string> entradas)
    {
        using var fs = new FileStream(destino, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        using var s = zip.CreateEntry("META-INF/MANIFEST.MF", CompressionLevel.NoCompression).Open();
        s.Write(Encoding.ASCII.GetBytes(Manifesto(entradas)));
    }

    // ---------------------------------------------------------------- linha de comando

    /// <summary>
    /// Divide a linha em argumentos. Aspas duplas valem em qualquer ponto do argumento (-Dx="C:\a b" vira -Dx=C:\a b);
    /// aspas simples só no começo dele ('c d'). As aspas saem do argumento; "" sozinho vira argumento vazio.
    /// </summary>
    public static List<string> DividirArgs(string? s)
    {
        var r = new List<string>();
        var atual = new StringBuilder();
        var aberto = false;   // argumento começado (inclusive "" vazio)
        char? aspa = null;
        foreach (var c in s ?? "")
        {
            if (aspa != null)
            {
                if (c == aspa) aspa = null;
                else atual.Append(c);
            }
            else if (c == '"' || (c == '\'' && !aberto)) { aspa = c; aberto = true; }
            else if (char.IsWhiteSpace(c))
            {
                if (aberto) { r.Add(atual.ToString()); atual.Clear(); aberto = false; }
            }
            else { atual.Append(c); aberto = true; }
        }
        if (aberto) r.Add(atual.ToString());
        return r;
    }

    /// <summary>
    /// App do código: -cp &lt;jar de classpath&gt; &lt;classe main&gt;. Pacote (tipo "zip"): -jar &lt;jar do pacote&gt;, o
    /// MANIFEST dele traz a Main-Class e o Class-Path (ex.: ../lib/).
    /// </summary>
    public static List<string> ArgsJava(Perfil p, string jar, bool debug)
    {
        var a = DividirArgs(p.VmArgs);
        if (debug) a.Add("-agentlib:jdwp=transport=dt_socket,server=y,suspend=n,address=" + p.PortaDebug);
        a.AddRange(p.EhZip ? new[] { "-jar", jar } : new[] { "-cp", jar, p.MainClass });
        a.AddRange(DividirArgs(p.Args));
        return a;
    }

    /// <summary>
    /// PATH do processo com as pastas extras do perfil na frente (bibliotecas nativas: DLL/.so), relativas a
    /// <paramref name="baseExtra"/>: a pasta do módulo ou, no pacote, a pasta de trabalho. No Linux as mesmas pastas
    /// vão também no LD_LIBRARY_PATH. Separador no perfil: ; (vale nos dois sistemas).
    /// </summary>
    public static void EnvJava(Perfil p, IDictionary<string, string?> env, string baseExtra)
    {
        env["JAVA_HOME"] = p.JavaHome;
        var extras = p.PathExtra.Split(';').Select(s => s.Trim()).Where(s => s != "")
            .Select(s => Path.GetFullPath(Path.Combine(baseExtra, s))).ToArray();
        So.PrefixarPath(env, [.. extras, Path.Combine(p.JavaHome!, "bin")]);
        if (!So.Windows && extras.Length > 0)
        {
            env.TryGetValue("LD_LIBRARY_PATH", out var ld);
            env["LD_LIBRARY_PATH"] = So.JuntarPath(extras.Append(ld ?? ""));
        }
    }

    // ---------------------------------------------------------------- classes com main (seletor da tela)

    static readonly Regex ReMain = new(
        @"\b(public\s+static|static\s+public)\s+void\s+main\s*\(\s*(final\s+)?String\s*(\[\s*\]\s*\w+|\.\.\.\s*\w+|\w+\s*\[\s*\])\s*\)");
    static readonly Regex RePacote = new(@"^\s*package\s+([\w.]+)\s*;", RegexOptions.Multiline);

    /// <summary>
    /// Classes com <c>public static void main(String[])</c> nos fontes do módulo (fora de target). O pacote vem da
    /// declaração package (não do caminho: há fontes de teste fora do layout padrão).
    /// </summary>
    public static List<ClasseMain> ClassesMain(string modulo)
    {
        var raiz = Path.GetFullPath(modulo);
        var r = new List<ClasseMain>();
        void Andar(string dir)
        {
            foreach (var d in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            {
                if (Ignorar.Contains(d.Name)) continue;
                if (d is DirectoryInfo) { Andar(d.FullName); continue; }
                if (!d.Name.EndsWith(".java", StringComparison.OrdinalIgnoreCase)) continue;
                var src = File.ReadAllText(d.FullName, Encoding.Latin1);
                if (!ReMain.IsMatch(src)) continue;
                var pac = RePacote.Match(src);
                var classe = (pac.Success ? pac.Groups[1].Value + "." : "") + Path.GetFileNameWithoutExtension(d.Name);
                var teste = Regex.IsMatch("\\" + Path.GetRelativePath(raiz, d.FullName), @"[\\/]test[\\/]", RegexOptions.IgnoreCase);
                r.Add(new ClasseMain(classe, teste));
            }
        }
        Andar(raiz);
        return r.OrderBy(c => c.Teste).ThenBy(c => c.Classe, StringComparer.CurrentCulture).ToList();
    }
}
