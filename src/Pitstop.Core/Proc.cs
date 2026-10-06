using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace Pitstop;

/// <summary>Disparo de processos filhos, kill, sonda de porta e abertura de URL.</summary>
public static class Proc
{
    /// <summary>
    /// redirecionar = modo janela/tela: saída vai para o log do perfil, sem console (no Windows, CREATE_NO_WINDOW, que
    /// não esconde janela gráfica: um app Swing abre normal). Sem redirecionar = modo CLI: herda o console.
    /// </summary>
    public static ProcessStartInfo Psi(string exe, string cwd, bool redirecionar, Encoding? codificacao = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = redirecionar,
            RedirectStandardOutput = redirecionar,
            RedirectStandardError = redirecionar,
        };
        if (redirecionar)
        {
            psi.StandardOutputEncoding = codificacao ?? Encoding.UTF8;
            psi.StandardErrorEncoding = codificacao ?? Encoding.UTF8;
        }
        return psi;
    }

    /// <summary>
    /// Inicia e, no Windows, põe no Job Object do runner: se o runner morrer, os filhos morrem junto (sem órfãos). No
    /// Linux quem derruba os filhos é a saída do runner (Parar e Sair, Ctrl+C, SIGTERM).
    /// </summary>
    public static Process Disparar(ProcessStartInfo psi)
    {
        Process p;
        try { p = Process.Start(psi) ?? throw new ErroRunner("não consegui iniciar " + psi.FileName); }
        catch (Win32Exception e) { throw new ErroRunner("não consegui iniciar " + psi.FileName + ": " + e.Message); }
        Job.Adicionar(p);
        return p;
    }

    public static async Task<bool> PortaOcupada(int porta, int ms = 700)
    {
        if (porta <= 0) return false;
        using var c = new TcpClient();
        try
        {
            using var cts = new CancellationTokenSource(ms);
            await c.ConnectAsync(IPAddress.Loopback, porta, cts.Token).ConfigureAwait(false);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Navegador padrão (ShellExecute no Windows, xdg-open no Linux): não fecha junto com o runner.</summary>
    public static void AbrirUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); } catch { }
    }
}

/// <summary>
/// pit.pid na base do perfil: janela, tela web e terminal conseguem parar o que o outro subiu. Grava "pid|início":
/// o sistema recicla pid, então só vale se o processo vivo tiver a mesma hora de início (e, no Windows, o mesmo
/// executável; no Linux o shell faz exec e o nome muda para java/node, então vale só a hora).
/// </summary>
public static class PidArquivo
{
    public readonly record struct Registro(int Pid, long? Inicio);

    static string Caminho(Perfil p) => Path.Combine(p.Base, "pit.pid");

    public static void Gravar(Perfil p, Process child)
    {
        try
        {
            Directory.CreateDirectory(p.Base);
            File.WriteAllText(Caminho(p), child.Id + "|" + So.Inicio(child));
        }
        catch { /* sem arquivo só perde o stop de outra instância */ }
        child.EnableRaisingEvents = true;
        child.Exited += (_, _) =>
        {
            try { if (Ler(p)?.Pid == child.Id) File.Delete(Caminho(p)); } catch { }
        };
    }

    public static Registro? Ler(Perfil p)
    {
        try
        {
            var t = File.ReadAllText(Caminho(p)).Trim().Split('|');
            if (int.TryParse(t[0], out var pid) && pid > 0)
                return new Registro(pid, t.Length > 1 && long.TryParse(t[1], out var i) ? i : null);
        }
        catch { }
        return null;
    }

    /// <summary><paramref name="nome"/> = nome do processo sem extensão (cmd, sh, java).</summary>
    public static bool Vivo(Registro r, string nome)
    {
        try
        {
            using var pr = Process.GetProcessById(r.Pid);
            if (So.Windows && !string.Equals(pr.ProcessName, nome, StringComparison.OrdinalIgnoreCase)) return false;
            if (!So.Windows && r.Inicio == null && pr.ProcessName != nome) return false;
            if (r.Inicio is long t)
            {
                try { return So.Inicio(pr) == t; }
                catch { /* sem acesso à hora de início: fica só o nome */ }
            }
            return true;
        }
        catch { return false; }
    }
}

/// <summary>
/// Job Object com KILL_ON_JOB_CLOSE. O handle só fecha quando o processo do runner termina (de qualquer jeito,
/// inclusive kill pelo Gerenciador de Tarefas): aí o Windows derruba os Tomcats/Maven/java filhos. Só Windows.
/// </summary>
static class Job
{
    static readonly IntPtr Handle = Criar();

    public static void Adicionar(Process p)
    {
        if (!So.Windows || Handle == IntPtr.Zero) return;
        try { AssignProcessToJobObject(Handle, p.Handle); } catch { }
    }

    static IntPtr Criar()
    {
        if (!So.Windows) return IntPtr.Zero;
        var h = CreateJobObject(IntPtr.Zero, null);
        if (h == IntPtr.Zero) return h;
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = 0x2000;   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        var tam = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(tam);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(h, 9 /* JobObjectExtendedLimitInformation */, ptr, (uint)tam))
            {
                CloseHandle(h);
                return IntPtr.Zero;
            }
        }
        finally { Marshal.FreeHGlobal(ptr); }
        return h;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateJobObject(IntPtr atributos, string? nome);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetInformationJobObject(IntPtr job, int classe, IntPtr info, uint tamanho);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AssignProcessToJobObject(IntPtr job, IntPtr processo);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
