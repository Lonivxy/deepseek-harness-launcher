using System;
using System.Diagnostics;

namespace DshLauncher.Services;

/// <summary>
/// Pre-flight checks for new users: the engine is launched through
/// pnpm (which requires Node.js), so both must be installed.
/// </summary>
public class PrerequisitesService
{
    public record CheckResult(
        bool NodeInstalled,
        bool NpmInstalled,
        bool PnpmInstalled,
        bool GitInstalled);

    public CheckResult Check()
        => new(
            RunVersionCommand("node --version"),
            RunVersionCommand("npm --version"),
            RunVersionCommand("pnpm --version"),
            RunVersionCommand("git --version"));

    /// <summary>Runs a tiny version command and reports whether it produced output.</summary>
    private static bool RunVersionCommand(string command)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = "/c " + command;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;

            process.Start();
            var output = process.StandardOutput.ReadToEnd()
                         + process.StandardError.ReadToEnd();
            return process.WaitForExit(10_000) && !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            return false;
        }
    }
}
