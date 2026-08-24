namespace SephiriaAutoRetry.Installer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                InstallerEngine.RunSelfTest(args[1]);
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    Directory.CreateDirectory(args[1]);
                    File.WriteAllText(Path.Combine(args[1], "self-test-error.txt"), ex.ToString());
                }
                catch
                {
                    // Preserve the original failure exit code.
                }

                return 1;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
