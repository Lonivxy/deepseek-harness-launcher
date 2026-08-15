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

        // Headless Node.js environment installer: --install-node
        // Installs nvm-windows + Node.js LTS without opening the GUI.
        if (args.Length >= 1 && args[0] == "--install-node")
        {
            var installer = new Services.NodeEnvInstallerService();
            installer.LogLine += Console.WriteLine;
            var ok = installer.InstallAsync().GetAwaiter().GetResult();
            Environment.Exit(ok ? 0 : 1);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
