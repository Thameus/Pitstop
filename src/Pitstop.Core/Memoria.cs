using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Pitstop;

/// <summary>Resultado explícito da medição. Falha de identidade nunca vira zero.</summary>
public sealed record ResultadoMemoria(long? MemoriaBytes, string Estado);

/// <summary>
/// Medição aproximada e somente leitura da árvore de um processo. No Windows usa working set; no Linux, RSS de
/// /proc. A identidade da raiz é validada antes de somar qualquer descendente.
/// </summary>
public sealed class ArvoreProcessos
{
    /// <summary>pai -> filhos no instante da captura.</summary>
    public IReadOnlyDictionary<int, List<int>> Filhos { get; }
    public ArvoreProcessos(IReadOnlyDictionary<int, List<int>> filhos) => Filhos = filhos;

    /// <summary>Uma captura do sistema inteiro (Toolhelp no Windows, /proc no Linux); null se indisponível.</summary>
    public static ArvoreProcessos? Capturar()
    {
        var mapa = OperatingSystem.IsWindows() ? MemoriaProcessos.CapturarWindows()
            : OperatingSystem.IsLinux() ? MemoriaProcessos.CapturarLinux() : null;
        return mapa == null ? null : new ArvoreProcessos(mapa);
    }
}

public static class MemoriaProcessos
{
    public static ResultadoMemoria Medir(int pid) => Medir(pid, new Lazy<ArvoreProcessos?>(ArvoreProcessos.Capturar));

    /// <summary>Usa uma árvore capturada uma única vez (compartilhada entre perfis de uma mesma consulta de status).</summary>
    public static ResultadoMemoria Medir(int pid, Lazy<ArvoreProcessos?> arvore)
    {
        if (pid <= 0) return new(null, "semPid");
        try
        {
            using var raiz = Process.GetProcessById(pid);
            // StartTime é a identidade mínima contra reciclagem do PID; sem ela não há atribuição segura.
            var identidade = raiz.StartTime;
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows()) return new(null, "indisponivel");
            var snapshot = arvore.Value;
            if (snapshot == null) return new(null, "indisponivel");
            return Medir(pid, identidade, snapshot);
        }
        catch { return new(null, "indisponivel"); }
    }

    /// <summary>Soma a raiz e os descendentes do snapshot. Filho que sumiu é ignorado; só a raiz ilegível invalida.</summary>
    public static ResultadoMemoria MedirComArvore(int pid, ArvoreProcessos arvore)
    {
        if (pid <= 0) return new(null, "semPid");
        try
        {
            using var raiz = Process.GetProcessById(pid);
            return Medir(pid, raiz.StartTime, arvore);
        }
        catch { return new(null, "indisponivel"); }
    }

    static ResultadoMemoria Medir(int pid, DateTime identidade, ArvoreProcessos arvore)
    {
        if (OperatingSystem.IsLinux()) return MedirLinux(pid, arvore);
        if (OperatingSystem.IsWindows()) return MedirWindows(pid, identidade, arvore);
        return new(null, "indisponivel");
    }

    /// <summary>Contrato determinístico usado pelos testes: valores ilegíveis são indisponíveis, não zero.</summary>
    public static long? SomarDeterministico(IEnumerable<long?> valores)
    {
        long total = 0;
        try
        {
            foreach (var valor in valores)
            {
                if (valor is not long bytes || bytes < 0) return null;
                checked { total += bytes; }
            }
            return total;
        }
        catch (OverflowException) { return null; }
    }

    static ResultadoMemoria MedirLinux(int raiz, ArvoreProcessos arvore)
    {
        if (!LerStat(raiz, out var identidadeRaiz, out _)) return new(null, "indisponivel");
        var fila = new Queue<int>([raiz]);
        long total = 0;
        while (fila.Count > 0)
        {
            var pid = fila.Dequeue();
            if (!LerStat(pid, out var identidade, out _))
            {
                if (pid == raiz) return new(null, "indisponivel");
                continue; // filho que terminou depois do snapshot
            }
            try
            {
                if (pid == raiz && identidade.StartTime != identidadeRaiz.StartTime) return new(null, "indisponivel");
                total = checked(total + checked(identidade.RssPaginas * Environment.SystemPageSize));
            }
            catch { return new(null, "indisponivel"); }
            if (arvore.Filhos.TryGetValue(pid, out var descendentes)) foreach (var filho in descendentes) fila.Enqueue(filho);
        }
        return new(total, "disponivel");
    }

    readonly record struct Stat(long StartTime, long RssPaginas);
    static bool LerStat(int pid, out Stat stat, out int pai)
    {
        stat = default; pai = 0;
        try
        {
            var texto = File.ReadAllText($"/proc/{pid}/stat");
            var fimNome = texto.LastIndexOf(')');
            if (fimNome < 0) return false;
            var campos = texto[(fimNome + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // Após comm: state=1, ppid=2, starttime=20, rss=22.
            pai = int.Parse(campos[1]);
            stat = new(long.Parse(campos[19]), long.Parse(campos[21]));
            return true;
        }
        catch { return false; }
    }

    static ResultadoMemoria MedirWindows(int raiz, DateTime identidadeRaiz, ArvoreProcessos arvore)
    {
        var fila = new Queue<int>([raiz]);
        long total = 0;
        while (fila.Count > 0)
        {
            var pid = fila.Dequeue();
            try
            {
                using var p = Process.GetProcessById(pid);
                if (pid == raiz && p.StartTime != identidadeRaiz) return new(null, "indisponivel");
                total = checked(total + p.WorkingSet64);
            }
            catch
            {
                if (pid == raiz) return new(null, "indisponivel");
                continue; // filho que terminou (ou sem acesso) depois do snapshot: ignorado
            }
            if (arvore.Filhos.TryGetValue(pid, out var descendentes)) foreach (var filho in descendentes) fila.Enqueue(filho);
        }
        return new(total, "disponivel");
    }

    internal static Dictionary<int, List<int>>? CapturarWindows()
    {
        var pais = new Dictionary<int, List<int>>();
        var snapshot = CreateToolhelp32Snapshot(0x00000002, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return null;
        try
        {
            var item = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Process32First(snapshot, ref item)) return null;
            do
            {
                var pid = (int)item.th32ProcessID;
                if (!pais.TryGetValue((int)item.th32ParentProcessID, out var lista)) pais[(int)item.th32ParentProcessID] = lista = [];
                lista.Add(pid);
            } while (Process32Next(snapshot, ref item));
        }
        finally { CloseHandle(snapshot); }
        return pais;
    }

    internal static Dictionary<int, List<int>>? CapturarLinux()
    {
        var filhos = new Dictionary<int, List<int>>();
        try
        {
            var processos = Directory.EnumerateDirectories("/proc", "[0-9]*")
                .Select(d => int.TryParse(Path.GetFileName(d), out var p) ? p : 0).Where(p => p > 0).ToArray();
            foreach (var pid in processos)
            {
                if (!LerStat(pid, out _, out var pai)) continue; // terminou durante a enumeração
                if (!filhos.TryGetValue(pai, out var lista)) filhos[pai] = lista = [];
                lista.Add(pid);
            }
            return filhos;
        }
        catch { return null; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32
    {
        public uint dwSize, cntUsage, th32ProcessID, th32DefaultHeapID, th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase, dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr handle);
}
