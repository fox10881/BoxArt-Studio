using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GameIconsStudio.Services;

/// <summary>One saved customization — written to choices.json on Apply.</summary>
public sealed class GameChoice
{
    /// <summary>Cover image URI — usually a file:// path into the local
    /// covers cache, but may be a remote https URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Template PNG file name (e.g. "template_ps5_blue.png") or "None".</summary>
    public string PlatformTemplate { get; set; } = "None";

    /// <summary>SurfaceFilter enum name ("Glossy"…) or "None".</summary>
    public string SurfaceFilter { get; set; } = "None";

    public bool IsStretchedToSquare { get; set; }

    // Case badges (sticker checkboxes)
    public bool HasPcCdrom { get; set; }
    public bool HasDvd { get; set; }
    public bool HasEsrb { get; set; }
    public bool HasPegi18 { get; set; }
    public bool HasSeal { get; set; }

    /// <summary>Remembered so the alternatives gallery works without re-matching.</summary>
    public int SgdbGameId { get; set; }
}

/// <summary>
/// Cache-first persistence: %LocalAppData%\GameIconsStudio\choices.json maps a
/// normalized game name to its saved customization. A game found here is
/// fully restored at scan time and never re-queried against SteamGridDB.
/// The file is kept in memory and written through on every change.
/// </summary>
public static class GameChoicesService
{
    private static readonly object Gate = new();
    private static Dictionary<string, GameChoice>? _choices;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameIconsStudio", "choices.json");

    /// <summary>%LocalAppData%\GameIconsStudio\templates — where imported
    /// custom template PNGs/WebPs are copied so they survive source moves.</summary>
    public static string CustomTemplatesDir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GameIconsStudio", "templates");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Stable key — normalized name, case-insensitive.</summary>
    public static string KeyFor(string name) => (name ?? string.Empty).Trim();

    public static string KeyFor(Game g) =>
        KeyFor(string.IsNullOrWhiteSpace(g.SearchName) ? g.Name : g.SearchName);

    private static Dictionary<string, GameChoice> Load()
    {
        if (_choices is not null) return _choices;
        _choices = new Dictionary<string, GameChoice>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(FilePath))
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, GameChoice>>(
                    File.ReadAllText(FilePath));
                if (data is not null)
                    _choices = new Dictionary<string, GameChoice>(data, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) { IconBuilderService.Log($"choices.json load failed: {ex.Message}"); }
        return _choices;
    }

    private static void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(_choices, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { IconBuilderService.Log($"choices.json save failed: {ex.Message}"); }
    }

    public static GameChoice? Find(Game g) => FindByName(KeyFor(g));

    public static GameChoice? FindByName(string name)
    {
        lock (Gate)
            return Load().TryGetValue(KeyFor(name), out var c) ? c : null;
    }

    /// <summary>Persist the game's current cover + style state.</summary>
    public static void Save(Game g)
    {
        if (string.IsNullOrWhiteSpace(g.Cover)) return;
        lock (Gate)
        {
            Load()[KeyFor(g)] = new GameChoice
            {
                Url = g.Cover,
                PlatformTemplate = g.Platform == PlatformTemplate.Custom
                    ? g.CustomTemplate          // custom file name, e.g. custom_xxx.png
                    : PlatformAssets.FileFor(g.Platform) ?? "None",
                SurfaceFilter = g.Surface.ToString(),
                IsStretchedToSquare = g.StretchSquare,
                HasPcCdrom = g.StickerPcCdrom,
                HasDvd = g.StickerDvd,
                HasEsrb = g.StickerEsrb,
                HasPegi18 = g.StickerPegi18,
                HasSeal = g.StickerSeal,
                SgdbGameId = g.SgdbGameId,
            };
            Persist();
        }
    }

    /// <summary>Drop a saved choice (deep wipe / remove custom icon).</summary>
    public static void Forget(Game g)
    {
        lock (Gate)
        {
            if (Load().Remove(KeyFor(g))) Persist();
        }
    }

    /// <summary>Map a stored template file name back to its enum value.
    /// Unknown (custom) file names map to <see cref="PlatformTemplate.Custom"/>;
    /// the caller then sets <c>Game.CustomTemplate</c> to the file name.</summary>
    public static PlatformTemplate ParsePlatform(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || stored == "None")
            return PlatformTemplate.None;
        foreach (var p in PlatformAssets.All)
            if (PlatformAssets.FileFor(p) == stored)
                return p;
        return PlatformTemplate.Custom;
    }
}
