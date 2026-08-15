using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

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

    /// <summary>
    /// Closes the DS Harness app window only — the standalone window we opened with
    /// <c>--app=</c>. It sends <c>WM_CLOSE</c> to top-level Chrome/Edge windows whose
    /// title mentions DeepSeek Harness but that are NOT regular browser tabs (those
    /// carry a " - Google Chrome" / " - Microsoft Edge" suffix). Other browser windows
    /// the user has open are left untouched.
    /// </summary>
    public static void CloseHarnessWindows()
    {
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            // Only consider Chrome/Edge top-level windows.
            GetWindowThreadProcessId(hwnd, out var pid);
            string processName;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                processName = process.ProcessName;
            }
            catch
            {
                return true; // process already gone — keep enumerating
            }
            if (!string.Equals(processName, "chrome", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(processName, "msedge", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var title = new StringBuilder(512);
            GetWindowText(hwnd, title, title.Capacity);
            var text = title.ToString();

            // App-mode window title is just the page title ("DeepSeek Harness").
            // Regular tabs append the browser name, e.g. "x - Google Chrome".
            var isAppWindow = text.Contains("DeepSeek Harness", StringComparison.OrdinalIgnoreCase)
                && !text.EndsWith(" - Google Chrome", StringComparison.OrdinalIgnoreCase)
                && !text.EndsWith(" - Microsoft Edge", StringComparison.OrdinalIgnoreCase)
                && !text.EndsWith(" - Chrome", StringComparison.OrdinalIgnoreCase);

            if (isAppWindow)
            {
                // Like clicking the window's × button — graceful, only this window.
                PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }

            return true;
        }, IntPtr.Zero);
    }

    // ------------------------------------------------------------------
    // Win32 interop for finding and closing the DS Harness window
    // ------------------------------------------------------------------

    private const uint WM_CLOSE = 0x0010;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
