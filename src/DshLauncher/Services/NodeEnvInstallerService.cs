using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DshLauncher.Services;

/// <summary>
/// One-click installer for the Node.js runtime used by DeepSeek Harness.
///
/// A brand-new user who has never installed Node.js should be able to click
/// one button and get nvm-windows + the latest LTS Node.js installed
/// per-user (no admin rights, no manual steps). Progress streams to the GUI
/// log panel. After install the runtime is on the user PATH, and processes
/// spawned from the launcher get it prepended immediately via
/// <see cref="PrependNodeToPath"/>.
/// </summary>
public class NodeEnvInstallerService
{
    public event Action<string>? LogLine;

    // Per-user install locations. Respect an existing NVM_HOME if the user already
    // has nvm-windows configured; otherwise default to %APPDATA%\nvm (nvm-windows default).
    public static string NvmHome
        => Environment.GetEnvironmentVariable("NVM_HOME")
           ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nvm");

    /// <summary>Junction dir where the active Node.js binaries live.</summary>
    public static string NodeBinDir
        => Path.Combine(NvmHome, "nodejs");

    /// <summary>True when a Node runtime is already usable (nvm install present or on PATH).</summary>
    public static bool IsNodeInstalled()
    {
        try
        {
            if (File.Exists(Path.Combine(NodeBinDir, "node.exe")))
            {
                return true;
            }
            return CommandWorks("node --version");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Prepends our per-user nvm/node dirs to a spawned process's PATH when they exist,
    /// so an engine/harness command started right after install finds node &amp; pnpm.
    /// </summary>
    public static void PrependNodeToPath(ProcessStartInfo psi)
    {
        var path = psi.Environment.TryGetValue("PATH", out var existing) && !string.IsNullOrEmpty(existing)
            ? existing
            : Environment.GetEnvironmentVariable("PATH") ?? "";

        foreach (var dir in new[] { NodeBinDir, NvmHome })
        {
            if (Directory.Exists(dir) && !path.Contains(dir, StringComparison.OrdinalIgnoreCase))
            {
                path = dir + ";" + path;
            }
        }

        psi.Environment["PATH"] = path;
    }

    /// <summary>
    /// Downloads nvm-windows, configures it for the current user, then installs and
    /// activates the latest LTS Node.js. Returns false on failure; progress arrives
    /// via <see cref="LogLine"/>.
    /// </summary>
    public async Task<bool> InstallAsync(CancellationToken ct = default)
    {
        try
        {
            // Already have Node? Nothing to do — safe for --install-node on any machine.
            if (IsNodeInstalled())
            {
                Emit("Node.js is already installed (" + NodeVersion() + ") — nothing to do.");
                return true;
            }

            Emit("Node.js not found — installing nvm-windows + Node.js LTS (per-user, no admin)...");

            Directory.CreateDirectory(NvmHome);

            var nvmExe = Path.Combine(NvmHome, "nvm.exe");
            if (!File.Exists(nvmExe))
            {
                Emit("Step 1/3: downloading nvm-windows...");
                var zip = Path.Combine(Path.GetTempPath(), "nvm-noinstall.zip");
                if (!await DownloadNvmAsync(zip, ct))
                {
                    Emit("Failed to download nvm-windows (both GitHub and npmmirror).");
                    return false;
                }

                Emit("Step 2/3: extracting nvm-windows...");
                if (Directory.Exists(NvmHome))
                {
                    // Extract over the top so a re-run doesn't duplicate nvm.exe.
                    ZipFile.ExtractToDirectory(zip, NvmHome, overwriteFiles: true);
                }
                File.Delete(zip);

                WriteSettings();
                SetUserEnvironment();
            }
            else
            {
                Emit("nvm-windows already present, skipping download.");
                // Make sure settings/env still match in case of a partial prior run.
                WriteSettings();
                SetUserEnvironment();
            }

            Emit("Step 3/3: installing Node.js LTS (nvm install lts) — this downloads ~30 MB...");
            var installed = await RunNvmAsync("install lts", ct);
            if (!installed)
            {
                Emit("nvm install lts failed.");
                return false;
            }

            Emit("Activating Node.js LTS (nvm use lts)...");
            await RunNvmAsync("use lts", ct);

            if (!IsNodeInstalled())
            {
                Emit("Node.js was not found after install.");
                return false;
            }

            Emit("Node.js LTS installed successfully. " + NodeVersion());
            return true;
        }
        catch (OperationCanceledException)
        {
            Emit("Node.js installation cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            Emit("Node.js installation failed: " + ex.Message);
            return false;
        }
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

    private static string NodeVersion()
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = "/c \"node --version\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            PrependNodeToPath(process.StartInfo);

            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5_000);
            return string.IsNullOrWhiteSpace(output) ? "" : output;
        }
        catch
        {
            return "";
        }
    }

    private async Task<bool> DownloadNvmAsync(string destZip, CancellationToken ct)
    {
        // nvm-windows is small (~6 MB) and npmmirror does not mirror it, so GitHub
        // is the source. Node itself (the big ~30 MB download) is served by the
        // npmmirror node_mirror configured in settings.txt for zh-locale users.
        const string github = "https://github.com/coreybutler/nvm-windows/releases/latest/download/nvm-noinstall.zip";
        var urls = new[] { github };

        foreach (var url in urls)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                Emit("  downloading " + url);
                var bytes = await http.GetByteArrayAsync(url, ct);
                await File.WriteAllBytesAsync(destZip, bytes, ct);
                return true;
            }
            catch (Exception ex)
            {
                Emit("  download failed: " + ex.Message);
            }
        }
        return false;
    }

    /// <summary>Writes nvm-windows settings.txt pointing at the per-user install.</summary>
    private void WriteSettings()
    {
        var isZh = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
        var arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";

        var lines = new System.Collections.Generic.List<string>
        {
            "root: " + NvmHome,
            "path: " + NodeBinDir,
            "arch: " + arch,
            "proxy: none",
        };
        if (isZh)
        {
            // Speed up Node downloads for Chinese users (official site is often slow).
            lines.Add("node_mirror: https://npmmirror.com/mirrors/node/");
            lines.Add("npm_mirror: https://npmmirror.com/mirrors/npm/");
        }

        File.WriteAllText(Path.Combine(NvmHome, "settings.txt"), string.Join("\r\n", lines) + "\r\n");
    }

    /// <summary>Persists NVM_HOME / NVM_SYMLINK and adds the node dirs to the user PATH.</summary>
    private static void SetUserEnvironment()
    {
        Environment.SetEnvironmentVariable("NVM_HOME", NvmHome, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("NVM_SYMLINK", NodeBinDir, EnvironmentVariableTarget.User);

        var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
        foreach (var dir in new[] { NvmHome, NodeBinDir })
        {
            if (!userPath.Contains(dir, StringComparison.OrdinalIgnoreCase))
            {
                userPath = userPath.TrimEnd(';') + ";" + dir;
            }
        }
        Environment.SetEnvironmentVariable("PATH", userPath, EnvironmentVariableTarget.User);

        // Also update the current process PATH so subsequent commands in this session work.
        var curPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in new[] { NvmHome, NodeBinDir })
        {
            if (!curPath.Contains(dir, StringComparison.OrdinalIgnoreCase))
            {
                curPath = dir + ";" + curPath;
            }
        }
        Environment.SetEnvironmentVariable("PATH", curPath);
    }

    private async Task<bool> RunNvmAsync(string args, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo.FileName = Path.Combine(NvmHome, "nvm.exe");
        process.StartInfo.Arguments = args;
        process.StartInfo.WorkingDirectory = NvmHome;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.Environment["NVM_HOME"] = NvmHome;
        process.StartInfo.Environment["NVM_SYMLINK"] = NodeBinDir;
        PrependNodeToPath(process.StartInfo);

        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Emit(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Emit(e.Data); };

        using var killOnCancel = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { }
        });

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);
        return process.ExitCode == 0;
    }

    private void Emit(string line) => LogLine?.Invoke(line);
}
