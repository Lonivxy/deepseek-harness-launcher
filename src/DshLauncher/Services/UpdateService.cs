using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DshLauncher.Services;

public enum UpdateStatus
{
    Unknown,          // network error or local version unavailable
    UpToDate,
    UpdateAvailable,
    Checking,
}

/// <summary>
/// Compares the installed DeepSeek Harness version against the latest
/// GitHub release so the UI can tell the user whether they are current.
/// </summary>
public class UpdateService
{
    public const string HarnessRepo = "deepseek-ai/deepseek-harness";

    public async Task<(UpdateStatus Status, string? Latest, string? Local)> CheckAsync(
        string harnessPath,
        CancellationToken ct = default)
    {
        var local = GetLocalVersion(harnessPath);
        var latest = await FetchLatestVersionAsync(ct);

        if (string.IsNullOrWhiteSpace(local) || string.IsNullOrWhiteSpace(latest))
        {
            return (UpdateStatus.Unknown, latest, local);
        }

        return IsNewer(latest!, local!)
            ? (UpdateStatus.UpdateAvailable, latest, local)
            : (UpdateStatus.UpToDate, latest, local);
    }

    /// <summary>
    /// Fetches the newest harness version from the GitHub Releases API, with a
    /// jsDelivr CDN fallback for networks where api.github.com is blocked or
    /// rate-limited.
    /// </summary>
    private static async Task<string?> FetchLatestVersionAsync(CancellationToken ct)
    {
        // Primary source: jsDelivr CDN (works even where GitHub is blocked or
        // when the repo has no formal releases; tracks the default branch HEAD).
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(8);

            var json = await http.GetStringAsync(
                $"https://cdn.jsdelivr.net/gh/{HarnessRepo}@latest/package.json", ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("version", out var version))
            {
                return version.GetString();
            }
        }
        catch
        {
            // Fall through to the GitHub tags API below.
        }

        // Fallback: GitHub tags API (first tag is the newest).
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DSH-Launcher/1.0");
            http.Timeout = TimeSpan.FromSeconds(8);

            var json = await http.GetStringAsync(
                $"https://api.github.com/repos/{HarnessRepo}/tags", ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array
                && doc.RootElement.GetArrayLength() > 0)
            {
                var first = doc.RootElement[0];
                if (first.TryGetProperty("name", out var name))
                {
                    return name.GetString()?.TrimStart('v');
                }
            }
        }
        catch
        {
            // Both sources failed; the caller reports Unknown.
        }

        return null;
    }

    /// <summary>Reads the "version" field from the harness package.json.</summary>
    public static string? GetLocalVersion(string harnessPath)
    {
        try
        {
            var packageJson = Path.Combine(harnessPath, "package.json");
            if (!File.Exists(packageJson))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(packageJson));
            return doc.RootElement.TryGetProperty("version", out var version)
                ? version.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loose numeric semver comparison. Pre-release suffixes are ignored,
    /// which is fine for a "is there a newer release?" indicator.
    /// </summary>
    private static bool IsNewer(string latest, string local)
    {
        var a = ParseNumbers(latest);
        var b = ParseNumbers(local);
        var count = Math.Max(a.Count, b.Count);

        for (var i = 0; i < count; i++)
        {
            var x = i < a.Count ? a[i] : 0;
            var y = i < b.Count ? b[i] : 0;
            if (x > y) return true;
            if (x < y) return false;
        }
        return false;
    }

    private static List<int> ParseNumbers(string version)
        => version
            .Split(new[] { '.', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .TakeWhile(part => int.TryParse(part, out _))
            .Select(int.Parse)
            .ToList();
}
