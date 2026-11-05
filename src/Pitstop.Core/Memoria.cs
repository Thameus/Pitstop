using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Pitstop;

/// <summary>Resultado explícito da medição. Falha de identidade nunca vira zero.</summary>
public sealed record ResultadoMemoria(long? MemoriaBytes, string Estado);

/// <summary>
/// Medição aproximada e somente leitura da árvore de um processo. No Windows usa working set; no Linux, RSS de
/// /proc. A identidade da raiz é validada antes de somar qualquer descendente.
/// </summary>
public static class MemoriaProcessos
{
    public static ResultadoMemoria Medir(int pid)
    {
        if (pid <= 0) return new(null, "semPid");
        try
        {
            using var raiz = Process.GetProcessById(pid);
            if (OperatingSystem.IsLinux()) return MedirLinux(pid);
            if (OperatingSystem.IsWindows())
            {
                // StartTime é a identidade mínima contra reciclagem do PID; sem ela não há atribuição segura.
                var identidade = raiz.StartTime;
                return MedirWindows(pid, identidade);
            }
            return new(null, "indisponivel");
        }
        catch { return new(null, "indisponivel"); }
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

    static ResultadoMemoria MedirLinux(int raiz)
    {
        if (!LerStat(raiz, out var identidadeRaiz, out _)) return new(null, "indisponivel");
        var filhos = new Dictionary<int, List<int>>();
        var processos = Directory.EnumerateDirectories("/proc", "[0-9]*")
            .Select(d => int.TryParse(Path.GetFileName(d), out var p) ? p : 0).Where(p => p > 0).ToArray();
        foreach (var pid in processos)
        {
            if (!LerStat(pid, out _, out var pai))
            {
                // Um processo que terminou durante a enumeração não pode ser somado; qualquer outra falha
                // torna a árvore incompleta e, portanto, indisponível em vez de retornar parcial.
                if (Directory.Exists($"/proc/{pid}")) return new(null, "indisponivel");
                continue;
            }
            if (!filhos.TryGetValue(pai, out var lista)) filhos[pai] = lista = [];
            lista.Add(pid);
        }

        var fila = new Queue<int>([raiz]);
        long total = 0;
        while (fila.Count > 0)
        {
            var pid = fila.Dequeue();
            try
            {
                if (!LerStat(pid, out var identidade, out _)) return new(null, "indisponivel");
                var rssPaginas = identidade.RssPaginas;
                if (pid == raiz && identidade.StartTime != identidadeRaiz.StartTime) return new(null, "indisponivel");
                total = checked(total + checked(rssPaginas * Environment.SystemPageSize));
            }
            catch { return new(null, "indisponivel"); }
            if (filhos.TryGetValue(pid, out var descendentes)) foreach (var filho in descendentes) fila.Enqueue(filho);
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

    static ResultadoMemoria MedirWindows(int raiz, DateTime identidadeRaiz)
    {
        var pais = new Dictionary<int, List<int>>();
        var snapshot = CreateToolhelp32Snapshot(0x00000002, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return new(null, "indisponivel");
        try
        {
            var item = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Process32First(snapshot, ref item)) return new(null, "indisponivel");
            do
            {
                var pid = (int)item.th32ProcessID;
                if (!pais.TryGetValue((int)item.th32ParentProcessID, out var lista)) pais[(int)item.th32ParentProcessID] = lista = [];
                lista.Add(pid);
            } while (Process32Next(snapshot, ref item));
        }
        finally { CloseHandle(snapshot); }

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
            catch { return new(null, "indisponivel"); }
            if (pais.TryGetValue(pid, out var descendentes)) foreach (var filho in descendentes) fila.Enqueue(filho);
        }
        return new(total, "disponivel");
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
