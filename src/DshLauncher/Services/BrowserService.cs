using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DshLauncher.Services;

/// <summary>
/// Opens the DS Harness UI in a standalone app-style browser window
/// (no tabs, no address bar) using the user's preferred browser.
/// </summary>
public static class BrowserService
{
    /// <summary>Common install locations, most likely first.</summary>
    private static readonly (string Name, string Path)[] Candidates =
    {
        ("Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe"),
        ("Chrome", @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"),
        ("Edge",   @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"),
        ("Edge",   @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"),
    };

    /// <summary>Returns the exe path for a browser name, or null when not installed.</summary>
    public static string? FindBrowser(string name)
    {
        foreach (var candidate in Candidates)
        {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)
                && File.Exists(candidate.Path))
            {
                return candidate.Path;
            }
        }
        return null;
    }

    /// <summary>Names of browsers actually installed on this machine.</summary>
    public static List<string> InstalledNames()
        => Candidates.Where(c => File.Exists(c.Path)).Select(c => c.Name).Distinct().ToList();

    /// <summary>
    /// Opens <paramref name="url"/> in an app window using the preferred browser,
    /// falling back to any installed browser, then the default browser.
    /// </summary>
    public static void OpenApp(string url, string? preferred = null)
    {
        // Preferred browser first.
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var path = FindBrowser(preferred);
            if (path != null)
            {
                LaunchAppWindow(path, url);
                return;
            }
        }

        // Any installed browser.
        foreach (var name in new[] { "Chrome", "Edge" })
        {
            var path = FindBrowser(name);
            if (path != null)
            {
                LaunchAppWindow(path, url);
                return;
            }
        }

        // Last resort: the OS default browser (regular tab).
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    private static void LaunchAppWindow(string browserPath, string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = browserPath,
            Arguments = "--app=" + url,
            UseShellExecute = false,
        });
    }
}
