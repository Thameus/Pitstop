namespace Pitstop.Setup;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Any(a => a.Equals("--smoke", StringComparison.OrdinalIgnoreCase)))
            return InstallerEngine.SmokePayload();
        if (args.Any(a => a.Equals("--smoke-install", StringComparison.OrdinalIgnoreCase)))
            return InstallerEngine.SmokeInstall();
        if (args.Any(a => a.Equals("--smoke-root-path", StringComparison.OrdinalIgnoreCase)))
            return InstallerEngine.SmokeRootPath();
        if (args.Any(a => a.Equals("--smoke-update", StringComparison.OrdinalIgnoreCase)))
            return InstallerEngine.SmokeUpdate();
        if (args.Any(a => a.Equals("--update", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                InstallerEngine.UpdateExisting(args);
                return 0;
            }
            catch (Exception ex)
            {
                ApplicationConfiguration.Initialize();
                MessageBox.Show("A atualização do Pitstop falhou.\n\n" + ex.Message,
                    "Pitstop - atualização", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 20;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new SetupForm());
        return 0;
    }
}
