using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Pitstop;

/// <summary>
/// O que muda entre Windows e Linux: shell, nome dos executáveis e scripts, separador do PATH, encerrar processo.
/// O resto do runner pergunta aqui em vez de escrever "cmd.exe" ou "catalina.bat".
/// </summary>
public static class So
{
    public static bool Windows => OperatingSystem.IsWindows();

    /// <summary>Executável dentro de uma pasta bin: java -> java.exe no Windows.</summary>
    public static string Exe(string nome) => Windows ? nome + ".exe" : nome;

    /// <summary>catalina.bat / catalina.sh.</summary>
    public static string Catalina => Windows ? "catalina.bat" : "catalina.sh";

    /// <summary>npm.cmd / npm, mvn.cmd / mvn: o lançador em lote do Windows e o script do Linux.</summary>
    public static string Lancador(string nome) => Windows ? nome + ".cmd" : nome;

    /// <summary>Nome do processo do shell que dispara Tomcat, Maven e npm (o que o pit.pid guarda).</summary>
    public static string Shell => Windows ? "cmd" : "sh";

    /// <summary>Junta pastas para o PATH (; no Windows, : no Linux).</summary>
    public static string JuntarPath(IEnumerable<string> pastas) => string.Join(Path.PathSeparator, pastas.Where(p => !string.IsNullOrEmpty(p)));

    /// <summary>Põe pastas na frente do PATH do processo filho.</summary>
    public static void PrefixarPath(IDictionary<string, string?> env, params string?[] pastas)
    {
        env.TryGetValue("PATH", out var atual);
        env["PATH"] = JuntarPath(pastas.Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).Append(atual ?? ""));
    }

    /// <summary>
    /// Processo que roda uma linha de comando no shell do sistema: cmd.exe /d /s /c "linha" no Windows,
    /// /bin/sh -c "linha" no Linux. Caminhos com espaço vão entre aspas duplas (valem nos dois).
    /// </summary>
    public static ProcessStartInfo Linha(string linha, string cwd, bool redirecionar, Encoding? codificacao = null)
    {
        if (Windows)
        {
            var psi = Proc.Psi("cmd.exe", cwd, redirecionar, codificacao);
            psi.Arguments = "/d /s /c \"" + linha + "\"";
            return psi;
        }
        var sh = Proc.Psi("/bin/sh", cwd, redirecionar, codificacao);
        sh.ArgumentList.Add("-c");
        sh.ArgumentList.Add(linha);
        return sh;
    }

    /// <summary>Script de um programa instalado (catalina.sh, npm) pode ter vindo de um .zip sem o bit de execução.</summary>
    public static void GarantirExecutavel(string arquivo)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(arquivo)) return;
        try
        {
            var m = File.GetUnixFileMode(arquivo);
            const UnixFileMode x = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            if ((m & UnixFileMode.UserExecute) == 0) File.SetUnixFileMode(arquivo, m | x);
        }
        catch { /* sem permissão: a subida vai reclamar com a mensagem do shell */ }
    }

    /// <summary>
    /// Marca de início do processo que não muda entre leituras (pid + isso identifica o processo mesmo com pid
    /// reciclado). Windows: hora de início. Linux: o starttime cru de /proc/&lt;pid&gt;/stat (o Process.StartTime do .NET
    /// é recalculado a partir do relógio a cada leitura e varia alguns milissegundos).
    /// </summary>
    public static long Inicio(Process p)
    {
        if (Windows) return p.StartTime.ToUniversalTime().Ticks;
        var stat = File.ReadAllText("/proc/" + p.Id + "/stat");
        // campos depois de "(comm)": [0] = estado (campo 3) ... [19] = starttime (campo 22)
        var resto = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
        return long.Parse(resto[19]);
    }

    // ---------------------------------------------------------------- encerrar processo

    /// <summary>
    /// Encerra a árvore do processo. Sem <paramref name="forcar"/> pede para sair: no Windows, taskkill /T manda
    /// WM_CLOSE às janelas (o app fecha pelo caminho dele); no Linux, SIGTERM para o processo e os descendentes.
    /// Com <paramref name="forcar"/>: taskkill /T /F ou SIGKILL na árvore.
    /// </summary>
    public static void Encerrar(int pid, bool forcar)
    {
        if (Windows) { TaskKill(pid, forcar); return; }
        foreach (var p in ArvoreUnix(pid)) Sinal(p, forcar ? 9 : 15);
    }

    static void TaskKill(int pid, bool forcar)
    {
        var psi = new ProcessStartInfo("taskkill")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        psi.ArgumentList.Add("/T");
        if (forcar) psi.ArgumentList.Add("/F");
        psi.ArgumentList.Add("/PID");
        psi.ArgumentList.Add(pid.ToString());
        try
        {
            using var p = Process.Start(psi);
            p?.WaitForExit(15000);
        }
        catch { /* taskkill ausente/negado: o timeout do stop vai avisar */ }
    }

    /// <summary>pid e descendentes, filhos primeiro (lidos de /proc/&lt;pid&gt;/stat: o 4º campo é o pai).</summary>
    static List<int> ArvoreUnix(int raiz)
    {
        var filhos = new Dictionary<int, List<int>>();
        try
        {
            foreach (var d in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(d), out var pid)) continue;
                try
                {
                    var stat = File.ReadAllText(Path.Combine(d, "stat"));
                    // "pid (comm) estado ppid ...": o comm pode ter espaço e parêntese, então corta no último ')'
                    var resto = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                    if (resto.Length > 1 && int.TryParse(resto[1], out var pai))
                        (filhos.TryGetValue(pai, out var l) ? l : filhos[pai] = []).Add(pid);
                }
                catch { /* processo saiu durante a leitura */ }
            }
        }
        catch { /* sem /proc: fica só o próprio pid */ }
        var ordem = new List<int>();
        void Andar(int p)
        {
            if (filhos.TryGetValue(p, out var l)) foreach (var f in l) Andar(f);
            ordem.Add(p);
        }
        Andar(raiz);
        return ordem;
    }

    static void Sinal(int pid, int sinal)
    {
        try { _ = kill(pid, sinal); } catch { /* libc ausente: nada a fazer */ }
    }

    [DllImport("libc", SetLastError = true)]
    static extern int kill(int pid, int sig);
}
