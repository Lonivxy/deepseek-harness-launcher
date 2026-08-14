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
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
