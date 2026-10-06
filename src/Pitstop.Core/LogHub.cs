using System.Text.Json;
using System.Threading.Channels;

namespace Pitstop;

/// <summary>
/// Log por perfil em memória (LOG_MAX_LINHAS) + ouvintes SSE da tela. Envia em lotes de 150 ms, não linha a
/// linha; comentário a cada 15 s mantém a conexão viva (e derruba a morta).
/// </summary>
public sealed class LogHub
{
    const int LinhaMax = 4000;   // linha gigante (XML, JSON de integração) é cortada

    readonly object trava = new();
    readonly Dictionary<string, List<string>> buffers = new();
    readonly Dictionary<string, List<string>> pendentes = new();
    readonly Dictionary<string, List<Channel<string>>> ouvintes = new();
    readonly Dictionary<string, List<Action<List<string>>>> locais = new();
    readonly Timer lote;
    readonly Timer ping;

    /// <summary>Modo CLI: o log vai direto para o console, sem buffer.</summary>
    public Action<string>? Direto { get; set; }

    public LogHub()
    {
        lote = new Timer(_ => Despachar(), null, 150, 150);
        ping = new Timer(_ => Ping(), null, 15000, 15000);
    }

    public void Log(string nome, string texto)
    {
        if (Direto is { } d) { d(texto.TrimEnd()); return; }
        var linhas = texto.Replace("\r\n", "\n").Split('\n');
        var n = linhas.Length > 0 && linhas[^1] == "" ? linhas.Length - 1 : linhas.Length;
        lock (trava)
        {
            var buf = Obter(buffers, nome);
            var fila = Obter(pendentes, nome);
            var temOuvinte = (ouvintes.TryGetValue(nome, out var o) && o.Count > 0) || (locais.TryGetValue(nome, out var lo) && lo.Count > 0);
            for (var i = 0; i < n; i++)
            {
                var l = linhas[i];
                if (l.Length > LinhaMax) l = l[..LinhaMax] + " …[+" + (l.Length - LinhaMax) + " caracteres]";
                buf.Add(l);
                if (temOuvinte) fila.Add(l);
            }
            if (buf.Count > Config.LogMax) buf.RemoveRange(0, buf.Count - Config.LogMax);
            if (fila.Count > Config.LogMax) fila.RemoveRange(0, fila.Count - Config.LogMax);
        }
    }

    public void Limpar(string nome)
    {
        lock (trava) buffers[nome] = [];
    }

    /// <summary>Perfil renomeado: o log acumulado segue com ele (quem ouvia o nome velho reconecta).</summary>
    public void Renomear(string velho, string novo)
    {
        lock (trava)
        {
            if (buffers.Remove(velho, out var buf)) buffers[novo] = buf;
            pendentes.Remove(velho);
        }
    }

    /// <summary>
    /// Novo ouvinte SSE: recebe na hora o fim do buffer (F5), num evento só. Fila limitada: aba lenta ou congelada
    /// perde os lotes mais antigos em vez de crescer a memória do runner.
    /// </summary>
    public Channel<string> Ouvir(string nome)
    {
        var c = Channel.CreateBounded<string>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });
        lock (trava)
        {
            var buf = Obter(buffers, nome);
            var replay = buf.Count > Config.ReplayMax
                ? new[] { "[pit] … " + (buf.Count - Config.ReplayMax) + " linha(s) anteriores omitidas" }
                    .Concat(buf.Skip(buf.Count - Config.ReplayMax)).ToList()
                : buf.ToList();
            c.Writer.TryWrite(Evento(replay));
            Obter(ouvintes, nome).Add(c);
        }
        return c;
    }

    public void Largar(string nome, Channel<string> c)
    {
        lock (trava)
            if (ouvintes.TryGetValue(nome, out var l)) l.Remove(c);
        c.Writer.TryComplete();
    }

    /// <summary>
    /// Ouvinte no mesmo processo (janela nativa): recebe o fim do buffer na hora e depois os lotes de 150 ms, na
    /// thread do timer. Dispose larga.
    /// </summary>
    public IDisposable Assinar(string nome, Action<List<string>> aoLote)
    {
        List<string> replay;
        lock (trava)
        {
            var buf = Obter(buffers, nome);
            replay = buf.Count > Config.LogMax ? buf.Skip(buf.Count - Config.LogMax).ToList() : buf.ToList();
            if (!locais.TryGetValue(nome, out var l)) locais[nome] = l = [];
            l.Add(aoLote);
        }
        aoLote(replay);
        return new Assinatura(() =>
        {
            lock (trava)
                if (locais.TryGetValue(nome, out var l)) l.Remove(aoLote);
        });
    }

    sealed class Assinatura(Action largar) : IDisposable
    {
        int feito;
        public void Dispose() { if (Interlocked.Exchange(ref feito, 1) == 0) largar(); }
    }

    static string Evento(List<string> linhas) => "data: " + JsonSerializer.Serialize(linhas, JsonAux.Opcoes) + "\n\n";

    void Despachar()
    {
        var entregar = new List<(Action<List<string>> Ouvinte, List<string> Lote)>();
        lock (trava)
        {
            foreach (var (nome, fila) in pendentes)
            {
                if (fila.Count == 0) continue;
                if (ouvintes.TryGetValue(nome, out var l) && l.Count > 0)
                {
                    var ev = Evento(fila);
                    foreach (var c in l) c.Writer.TryWrite(ev);
                }
                if (locais.TryGetValue(nome, out var lo))
                    foreach (var a in lo) entregar.Add((a, fila.ToList()));
                fila.Clear();
            }
        }
        // fora da trava: o ouvinte local repassa para a thread da janela e não pode segurar o Log() dos processos
        foreach (var (a, lote) in entregar)
            try { a(lote); } catch (Exception) { /* janela fechando */ }
    }

    void Ping()
    {
        lock (trava)
            foreach (var l in ouvintes.Values)
                foreach (var c in l) c.Writer.TryWrite(": ping\n\n");
    }

    static List<string> Obter(Dictionary<string, List<string>> d, string nome) =>
        d.TryGetValue(nome, out var l) ? l : d[nome] = [];

    static List<Channel<string>> Obter(Dictionary<string, List<Channel<string>>> d, string nome) =>
        d.TryGetValue(nome, out var l) ? l : d[nome] = [];
}
