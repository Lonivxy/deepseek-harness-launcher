using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DshLauncher.Models;

namespace DshLauncher.Services;

/// <summary>
/// Loads and saves app settings, and reads/writes the API key in a
/// standard .env file so external automation and CI scripts can use it too.
/// </summary>
/// <remarks>
/// Security note: the .env file stores the key in plain text on the user's own
/// machine (standard practice for local automation). The key is masked in the
/// GUI, and the config.json never contains the key itself.
/// </remarks>
public class ConfigService
{
    public string ConfigDir { get; }
    public string ConfigPath { get; }
    public AppConfig Config { get; private set; } = new();

    public ConfigService()
    {
        ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DshLauncher");
        ConfigPath = Path.Combine(ConfigDir, "config.json");
    }

    /// <summary>Loads config.json, falling back to defaults when missing or corrupt.</summary>
    public void Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                Config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
        }
        catch
        {
            // Corrupt config should never brick the app; start fresh.
            Config = new AppConfig();
        }

        Normalize();
    }

    /// <summary>Persists current settings to config.json.</summary>
    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }

    /// <summary>Fills derived defaults (e.g. .env location) after load.</summary>
    private void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Config.ApiKeyPath))
        {
            Config.ApiKeyPath = Path.Combine(Config.HarnessPath, ".env");
        }
        if (string.IsNullOrWhiteSpace(Config.BrowserChoice))
        {
            Config.BrowserChoice = "Chrome";
        }
    }

    public bool HasApiKey() => !string.IsNullOrWhiteSpace(ReadApiKey());

    /// <summary>Returns the stored key, or null when not configured.</summary>
    public string? ReadApiKey()
    {
        try
        {
            if (!File.Exists(Config.ApiKeyPath))
            {
                return null;
            }

            var prefix = Config.ApiKeyVarName + "=";
            foreach (var line in File.ReadAllLines(Config.ApiKeyPath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed[prefix.Length..].Trim().Trim('"');
                }
            }
        }
        catch
        {
            // Ignore read errors; the caller treats an unreadable key as "not set".
        }
        return null;
    }

    /// <summary>Adds or updates the key inside the .env file without touching other entries.</summary>
    public void WriteApiKey(string key)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Config.ApiKeyPath)!);

        var lines = File.Exists(Config.ApiKeyPath)
            ? new List<string>(File.ReadAllLines(Config.ApiKeyPath))
            : new List<string>();

        var prefix = Config.ApiKeyVarName + "=";
        var replaced = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = prefix + key;
                replaced = true;
                break;
            }
        }

        if (!replaced)
        {
            lines.Add(prefix + key);
        }

        File.WriteAllLines(Config.ApiKeyPath, lines);
    }
}
