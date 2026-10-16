using System.Diagnostics;

namespace Pitstop.Updater;

/// <summary>Diagnóstico seguro e conservador de módulos carregados de uma instalação.
/// Não termina processos: solicita que o usuário feche bloqueadores externos.</summary>
internal static class WindowsBlockers
{
    public static string[] Listar(string caminho)
    {
        try
        {
            var alvo = Path.GetFullPath(caminho).TrimEnd(Path.DirectorySeparatorChar);
            var pasta = Directory.Exists(alvo);
            if (!pasta && !File.Exists(alvo)) return [];
            var encontrados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    if (p.Id == Environment.ProcessId) continue;
                    try
                    {
                        var principal = p.MainModule?.FileName;
                        var emUso = !string.IsNullOrWhiteSpace(principal) && NoAlvo(principal, alvo, pasta);
                        if (!emUso)
                        {
                            try
                            {
                                foreach (ProcessModule modulo in p.Modules)
                                    if (NoAlvo(modulo.FileName, alvo, pasta)) { emUso = true; break; }
                            }
                            catch { /* processador diferente, processo protegido ou já encerrado */ }
                        }
                        if (emUso) encontrados.Add(p.ProcessName + " (PID " + p.Id + ")");
                    }
                    catch { /* acesso negado ou processo encerrado */ }
                }
            }
            return encontrados.ToArray();
        }
        catch { return []; }
    }

    static bool NoAlvo(string arquivo, string alvo, bool pasta)
    {
        var caminho = Path.GetFullPath(arquivo);
        return pasta
            ? caminho.StartsWith(alvo + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            : string.Equals(caminho, alvo, StringComparison.OrdinalIgnoreCase);
    }
}
