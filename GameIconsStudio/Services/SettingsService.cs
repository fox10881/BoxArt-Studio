using System;
using System.IO;
using Windows.Storage;

namespace GameIconsStudio.Services;

/// <summary>
/// Persists small app settings. Primary store is
/// ApplicationData.Current.LocalSettings; if that API is unavailable
/// (unpackaged edge cases) we fall back to a plain file under
/// %LocalAppData%\GameIconsStudio so the key is never silently lost.
/// </summary>
public static class SettingsService
{
    private const string ApiKeySetting = "SteamGridApiKey";

    private static string FallbackDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameIconsStudio");

    private static string FallbackFile => Path.Combine(FallbackDir, "settings.txt");

    public static string? GetApiKey()
    {
        try
        {
            var v = ApplicationData.Current.LocalSettings.Values[ApiKeySetting] as string;
            if (!string.IsNullOrWhiteSpace(v)) return v;
        }
        catch { /* unpackaged fallback below */ }

        try
        {
            if (File.Exists(FallbackFile))
            {
                var v = File.ReadAllText(FallbackFile).Trim();
                return string.IsNullOrWhiteSpace(v) ? null : v;
            }
        }
        catch { }
        return null;
    }

    public static void SetApiKey(string? key)
    {
        key = key?.Trim() ?? string.Empty;
        var persisted = false;
        try
        {
            ApplicationData.Current.LocalSettings.Values[ApiKeySetting] = key;
            persisted = true;
        }
        catch { }

        // Always keep the fallback file in sync — cheap and makes the store
        // resilient regardless of which backend is live.
        try
        {
            Directory.CreateDirectory(FallbackDir);
            if (key.Length == 0) File.Delete(FallbackFile);
            else File.WriteAllText(FallbackFile, key);
            persisted = true;
        }
        catch { }

        if (!persisted)
            throw new InvalidOperationException("Could not persist the API key.");
    }

    /// <summary>Generic small-setting read: LocalSettings first, per-name file fallback.</summary>
    public static string? GetSetting(string name)
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values[name] is string v &&
                !string.IsNullOrWhiteSpace(v))
                return v;
        }
        catch { }

        try
        {
            var file = Path.Combine(FallbackDir, $"setting-{name}.txt");
            if (File.Exists(file))
            {
                var v = File.ReadAllText(file).Trim();
                if (v.Length > 0) return v;
            }
        }
        catch { }
        return null;
    }

    /// <summary>Generic small-setting write — LocalSettings + file fallback.</summary>
    public static void SetSetting(string name, string value)
    {
        try { ApplicationData.Current.LocalSettings.Values[name] = value; } catch { }
        try
        {
            Directory.CreateDirectory(FallbackDir);
            var file = Path.Combine(FallbackDir, $"setting-{name}.txt");
            if (value.Length == 0) File.Delete(file);
            else File.WriteAllText(file, value);
        }
        catch { }
    }

    public static string GetCoverCacheDir()
    {
        var dir = Path.Combine(FallbackDir, "covers");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
