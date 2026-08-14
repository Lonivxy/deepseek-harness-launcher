using System;
using System.Windows.Forms;
using DshLauncher.Forms;

namespace DshLauncher;

/// <summary>
/// Application entry point. WinForms apps must run on a single STA thread.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Headless installer mode: DeepSeekHarnessLauncher.exe --install-harness <path>
        // Used by power users and CI; the GUI uses the same service.
        if (args.Length >= 2 && args[0] == "--install-harness")
        {
            var installer = new Services.HarnessInstallerService();
            installer.LogLine += Console.WriteLine;
            var ok = installer.InstallAsync(args[1]).GetAwaiter().GetResult();
            Environment.Exit(ok ? 0 : 1);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
