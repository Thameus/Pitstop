using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pitstop;

/// <summary>Sync de desenvolvimento com registro dos arquivos que o Pitstop copiou e exclusões conservadoras.</summary>
public static class SyncTomcat
{
    sealed record Copia(long Tamanho, long DataTicks);
    sealed class Estado
    {
        public Dictionary<string, Copia> Copiados { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    static string Marca(Perfil p, string chave)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(chave)))[..20];
        return Path.Combine(p.Cache, "sync", hash + ".json");
    }

    static bool Dentro(string raiz, string caminho)
    {
        var rel = Path.GetRelativePath(raiz, caminho);
        return rel != ".." && !rel.StartsWith(".." + Path.DirectorySeparatorChar) && !Path.IsPathRooted(rel);
    }

    public static (int Copiados, int Excluidos, List<string> Falhas) Executar(Perfil p, Artefato a, RegraSync regra)
    {
        var falhas = new List<string>();
        var baseModulo = Path.GetFullPath(Path.Combine(a.Repo, a.Modulo));
        var origem = Path.GetFullPath(Path.Combine(baseModulo, regra.De));
        var docBase = Tomcat.ResolverDocBase(a);
        if (docBase == null) return (0, 0, ["sem pasta explodida para " + a.Contexto]);
        var raiz = Path.GetFullPath(docBase);
        var destino = Path.GetFullPath(Path.Combine(raiz, regra.Para));
        if (!Dentro(baseModulo, origem) || !Dentro(raiz, destino))
            throw new ErroRunner("regra Sync fora do módulo/docBase: " + regra.De + " -> " + regra.Para);
        if (origem.Equals(destino, So.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return (0, 0, []);
        var chave = baseModulo + "|" + origem + "|" + destino;
        var arquivo = Marca(p, chave);
        Estado estado;
        try { estado = File.Exists(arquivo) ? JsonSerializer.Deserialize<Estado>(File.ReadAllText(arquivo)) ?? new Estado() : new Estado(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { estado = new Estado(); }
        var novos = new Dictionary<string, Copia>(So.Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var encontrados = new HashSet<string>(So.Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var copiados = 0;
        if (!Directory.Exists(origem)) return (0, 0, ["origem de Sync indisponível; exclusões suspensas: " + origem]);
        if (Directory.Exists(origem))
        {
            foreach (var fonte in Directory.EnumerateFiles(origem, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }))
            {
                var relativo = Path.GetRelativePath(origem, fonte);
                var alvo = Path.GetFullPath(Path.Combine(destino, relativo));
                if (!Dentro(destino, alvo)) continue;
                encontrados.Add(alvo);
                try
                {
                    var fi = new FileInfo(fonte);
                    var existe = File.Exists(alvo);
                    var foiCopiado = false;
                    if (!existe || File.GetLastWriteTimeUtc(alvo) < fi.LastWriteTimeUtc ||
                        (estado.Copiados.ContainsKey(alvo) && new FileInfo(alvo).Length != fi.Length))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(alvo)!);
                        // Publica apenas depois que a cópia para o temporário terminou.
                        var temp = alvo + ".pitstop-" + Guid.NewGuid().ToString("N") + ".tmp";
                        try
                        {
                            File.Copy(fonte, temp, true);
                            File.Move(temp, alvo, true);
                        }
                        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } }
                        copiados++;
                        foiCopiado = true;
                        existe = true;
                    }
                    // Arquivos preexistentes nunca entram no manifesto sem terem sido copiados aqui.
                    if (foiCopiado) novos[alvo] = new Copia(new FileInfo(alvo).Length, File.GetLastWriteTimeUtc(alvo).Ticks);
                    else if (existe && estado.Copiados.TryGetValue(alvo, out var anteriormente))
                    {
                        // Modificações externas não passam a ser "nossas"; preserva a assinatura original.
                        novos[alvo] = anteriormente;
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    falhas.Add(alvo + ": " + e.Message);
                    if (estado.Copiados.TryGetValue(alvo, out var antigo)) novos[alvo] = antigo;
                }
            }
        }
        var excluidos = 0;
        foreach (var (alvo, estadoAntigo) in estado.Copiados)
        {
            if (encontrados.Contains(alvo)) continue;
            try
            {
                if (!Dentro(destino, alvo)) continue;
                if (!File.Exists(alvo)) continue;
                var info = new FileInfo(alvo);
                if (info.Length != estadoAntigo.Tamanho || info.LastWriteTimeUtc.Ticks != estadoAntigo.DataTicks)
                {
                    falhas.Add("não removi arquivo alterado externamente: " + alvo);
                    continue;
                }
                File.Delete(alvo);
                excluidos++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { falhas.Add(alvo + ": " + e.Message); }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        Config.GravarAtomico(arquivo, JsonSerializer.Serialize(new Estado { Copiados = novos }) + "\n", false);
        return (copiados, excluidos, falhas);
    }
}

/// <summary>Monitora somente fontes de um perfil ativo; agrupa eventos sem empilhar builds e é descartado no stop.</summary>
public sealed class ObservadorSync : IDisposable
{
    readonly List<FileSystemWatcher> watchers = [];
    readonly System.Threading.Timer timer;
    readonly Action executar;
    int rodando;
    bool descartado;

    public ObservadorSync(Perfil p, Action executar)
    {
        this.executar = executar;
        timer = new System.Threading.Timer(_ => Disparar(), null, Timeout.Infinite, Timeout.Infinite);
        foreach (var a in p.Artefatos)
            foreach (var regra in a.Sync)
            {
                var dir = Path.GetFullPath(Path.Combine(a.Repo, a.Modulo, regra.De));
                if (!Directory.Exists(dir)) continue;
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
                var w = new FileSystemWatcher(dir)
                {
                    IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                        NotifyFilters.LastWrite | NotifyFilters.Size, EnableRaisingEvents = false,
                };
                w.Changed += Evento;
                w.Created += Evento;
                w.Deleted += Evento;
                w.Renamed += (_, _) => Agendar();
                w.Error += (_, _) => Agendar();
                w.EnableRaisingEvents = true;
                watchers.Add(w);
            }
    }

    void Evento(object sender, FileSystemEventArgs e) => Agendar();

    void Agendar()
    {
        if (!descartado)
            try { timer.Change(700, Timeout.Infinite); }
            catch (ObjectDisposedException) { }
    }

    void Disparar()
    {
        if (descartado || Interlocked.Exchange(ref rodando, 1) == 1) { Agendar(); return; }
        try { if (!descartado) executar(); }
        catch (Exception) { /* callback de Sync registra erro; evento de arquivo não pode derrubar o runner */ }
        finally
        {
            Interlocked.Exchange(ref rodando, 0);
        }
    }

    public void Dispose()
    {
        descartado = true;
        timer.Dispose();
        foreach (var w in watchers) w.Dispose();
    }
}
