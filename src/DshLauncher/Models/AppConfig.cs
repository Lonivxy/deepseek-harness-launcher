namespace DshLauncher.Models;

/// <summary>
/// User settings persisted to %APPDATA%\DshLauncher\config.json.
/// Plain-old-data so it serialises cleanly with System.Text.Json.
/// </summary>
public class AppConfig
{
    /// <summary>Browser used to open the DS Harness interface: "Chrome" or "Edge".</summary>
    public string BrowserChoice { get; set; } = "Chrome";

    /// <summary>Whether the first-run wizard's browser choice should be remembered.</summary>
    public bool RememberBrowserChoice { get; set; } = true;

    /// <summary>True once the first-run wizard has been completed at least once.</summary>
    public bool WizardCompleted { get; set; }

    /// <summary>Absolute path to the DeepSeek Harness repository checkout.</summary>
    public string HarnessPath { get; set; } = @"D:\dsh";

    /// <summary>Local URL served by the harness web profile.</summary>
    public string HarnessUrl { get; set; } = "http://127.0.0.1:3080";

    /// <summary>Whether the interface should open automatically once the engine is ready.</summary>
    public bool AutoOpenHarness { get; set; } = true;

    /// <summary>Path of the .env file that stores the API key for the harness.</summary>
    public string ApiKeyPath { get; set; } = "";

    /// <summary>Environment variable name used for the API key inside the .env file.</summary>
    public string ApiKeyVarName { get; set; } = "DEEPSEEK_API_KEY";
}
