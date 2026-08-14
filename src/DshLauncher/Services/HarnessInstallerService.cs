using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DshLauncher.Services;

/// <summary>
/// One-click installer for DeepSeek Harness.
///
/// A brand-new user should be able to run the launcher, answer a few simple
/// prompts, and have the whole harness cloned, dependency-installed, and
/// built without ever opening a terminal. Every step streams its output to
/// the GUI log panel.
/// </summary>
public class HarnessInstallerService
{
    public const string HarnessRepoUrl = "https://github.com/deepseek-ai/deepseek-harness.git";

    public event Action<string>? LogLine;

    /// <summary>True when the harness checkout exists and looks built (node_modules present).</summary>
    public static bool IsInstalled(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path)
                && File.Exists(Path.Combine(path, "package.json"))
                && Directory.Exists(Path.Combine(path, "node_modules"));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Clones (or updates) the harness repo, installs dependencies and builds it.
    /// Returns false on failure; detailed progress arrives via <see cref="LogLine"/>.
    /// </summary>
    public async Task<bool> InstallAsync(string path, CancellationToken ct = default)
    {
        var overall = Stopwatch.StartNew();
        try
        {
            Directory.CreateDirectory(path);

            var hasCheckout = Directory.Exists(Path.Combine(path, ".git"));
            var step = Stopwatch.StartNew();
            if (!hasCheckout)
            {
                Emit("Step 1/3: downloading DeepSeek Harness (git clone)...");
                await RunAsync("git",
                    $"clone --depth 1 \"{HarnessRepoUrl}\" \"{path}\"", path, ct);
                Emit($"Step 1/3 done ({step.Elapsed.TotalSeconds:F0}s).");
            }
            else
            {
                Emit("Step 1/3: existing checkout found, updating (git pull)...");
                await RunAsync("git", "pull --ff-only", path, ct);
                Emit($"Step 1/3 done ({step.Elapsed.TotalSeconds:F0}s).");
            }

            if (!await EnsurePnpmAsync(ct))
            {
                return false;
            }

            step.Restart();
            Emit("Step 2/3: downloading packages (pnpm install — the biggest step)...");
            var registryFlag = UseChinaMirror()
                ? " --registry=https://registry.npmmirror.com"
                : "";
            await RunAsync("cmd.exe", $"/c pnpm install --frozen-lockfile{registryFlag}", path, ct);
            Emit($"Step 2/3 done ({step.Elapsed.TotalSeconds:F0}s).");

            step.Restart();
            Emit("Step 3/3: building the project (pnpm run build)...");
            await RunAsync("cmd.exe", "/c pnpm run build", path, ct);
            Emit($"Step 3/3 done ({step.Elapsed.TotalSeconds:F0}s).");

            Emit($"DeepSeek Harness installed successfully (total {overall.Elapsed.TotalMinutes:F1} min).");
            return true;
        }
        catch (OperationCanceledException)
        {
            Emit("Installation cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            Emit("Installation failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Chinese-locale systems get the npmmirror registry automatically because
    /// the official npm registry is frequently slow or blocked there. This shaves
    /// minutes off the package-download step without changing behavior elsewhere.
    /// </summary>
    private static bool UseChinaMirror()
        => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";

    /// <summary>
    /// Makes sure pnpm exists, installing it globally via npm when needed.
    /// </summary>
    public async Task<bool> EnsurePnpmAsync(CancellationToken ct = default)
    {
        if (CommandWorks("pnpm --version"))
        {
            return true;
        }

        if (!CommandWorks("npm --version"))
        {
            Emit("npm is not available — please install Node.js first.");
            return false;
        }

        Emit("pnpm not found — installing it globally via npm (npm install -g pnpm)...");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await RunAsync("cmd.exe", "/c npm install -g pnpm", home, ct);
        return CommandWorks("pnpm --version");
    }

    private static bool CommandWorks(string command)
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

    private async Task RunAsync(string exe, string args, string cwd, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo.FileName = exe;
        process.StartInfo.Arguments = args;
        process.StartInfo.WorkingDirectory = cwd;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) Emit(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) Emit(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // If the user closes the app mid-install, kill the whole tree.
        using var killOnCancel = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process already gone.
            }
        });

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{exe} exited with code {process.ExitCode}.");
        }
    }

    private void Emit(string line) => LogLine?.Invoke(line);
}
