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

        ApplicationConfiguration.Initialize();
        Application.Run(new SetupForm());
        return 0;
    }
}
