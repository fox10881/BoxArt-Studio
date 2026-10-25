using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GameIconsStudio.Services;

namespace GameIconsStudio;

public enum PlatformTemplate
{
    None,
    Ps5Blue, Ps5Black,
    XboxSeries, XboxOne, Xbox360,
    SteamBlue, SteamMinimal,
    Epic,
    GfwClassic, GfwModern,
    Windows11,
    Custom,
}
public enum SurfaceFilter { None, Glossy, Matte, Holographic, Vintage }
public enum GameStatus { Matched, Attention }
public enum ScanMode { ShortcutsFolder, AllSubFolders, SpecificFolder, SpecificShortcuts }

public class Game : INotifyPropertyChanged
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Shortcut { get; set; } = string.Empty;

    // Scan/match metadata used by the real fetch pipeline
    public string SearchName { get; set; } = string.Empty;   // normalized name sent to SGDB
    public long SteamAppId { get; set; }
    public int Year { get; set; }
    public int SgdbGameId { get; set; }

    /// <summary>True when this game's state was restored from choices.json —
    /// the cover-fetch pipeline skips it (cache-first, no API call).</summary>
    public bool IsSavedChoice { get; set; }

    // What the built icon gets applied to:
    //  - .lnk path for shortcut modes (IconLocation via IShellLinkW)
    //  - folder path for folder modes (desktop.ini + ReadOnly attribute)
    public string ApplyPath { get; set; } = string.Empty;
    public bool IsFolderTarget { get; set; }

    /// <summary>Alternative covers fetched from SteamGridDB for this game.</summary>
    public ObservableCollection<ArtworkCandidate> CoverOptions { get; } = new();
    public int CoverOptionsCount => CoverOptions.Count;
    public void RefreshCoverOptions() => OnChanged(nameof(CoverOptionsCount));

    private string _cover = string.Empty;
    public string Cover
    {
        get => _cover;
        set { _cover = value; OnChanged(); }
    }

    private string _source = string.Empty;
    public string Source
    {
        get => _source;
        set { _source = value; OnChanged(); }
    }

    private GameStatus _status;
    public GameStatus Status
    {
        get => _status;
        set { _status = value; OnChanged(); OnChanged(nameof(StatusLabel)); OnChanged(nameof(IsMatched)); }
    }

    private int _confidence;
    public int Confidence
    {
        get => _confidence;
        set { _confidence = value; OnChanged(); }
    }

    private PlatformTemplate _platform;
    public PlatformTemplate Platform
    {
        get => _platform;
        set { _platform = value; OnChanged(); OnChanged(nameof(OverlayLabel)); }
    }

    private SurfaceFilter _surface;
    public SurfaceFilter Surface
    {
        get => _surface;
        set { _surface = value; OnChanged(); OnChanged(nameof(OverlayLabel)); }
    }

    /// <summary>When true the icon fills the whole 1:1 canvas (art squashes,
    /// never crops); when false it keeps the pillarboxed 2:3 letterbox.</summary>
    private bool _stretchSquare;
    public bool StretchSquare
    {
        get => _stretchSquare;
        set { _stretchSquare = value; OnChanged(); OnChanged(nameof(OverlayLabel)); }
    }

    /// <summary>File name (inside the app templates dir) when
    /// <see cref="Platform"/> is <see cref="PlatformTemplate.Custom"/>.</summary>
    private string _customTemplate = string.Empty;
    public string CustomTemplate
    {
        get => _customTemplate;
        set { _customTemplate = value; OnChanged(); OnChanged(nameof(CustomTemplateUri)); OnChanged(nameof(OverlayLabel)); }
    }

    /// <summary>file:// URI of the imported custom template, for the preview.</summary>
    public string CustomTemplateUri =>
        string.IsNullOrEmpty(CustomTemplate)
            ? string.Empty
            : new Uri(Path.Combine(GameChoicesService.CustomTemplatesDir, CustomTemplate)).AbsoluteUri;

    // Optional case badges composited on top of the chosen template
    private bool _stickerPcCdrom;
    public bool StickerPcCdrom { get => _stickerPcCdrom; set { _stickerPcCdrom = value; OnChanged(); } }

    private bool _stickerDvd;
    public bool StickerDvd { get => _stickerDvd; set { _stickerDvd = value; OnChanged(); } }

    private bool _stickerEsrb;
    public bool StickerEsrb { get => _stickerEsrb; set { _stickerEsrb = value; OnChanged(); } }

    private bool _stickerPegi18;
    public bool StickerPegi18 { get => _stickerPegi18; set { _stickerPegi18 = value; OnChanged(); } }

    private bool _stickerSeal;
    public bool StickerSeal { get => _stickerSeal; set { _stickerSeal = value; OnChanged(); } }

    public bool IsMatched => Status == GameStatus.Matched;
    public string StatusLabel =>
        LocalizationService.T(IsMatched ? "StatusMatched" : "StatusAttention");

    /// <summary>Sidebar subtitle — e.g. "PS5 (Blue) · Glossy", or "Original".</summary>
    public string OverlayLabel
    {
        get
        {
            var parts = new List<string>();
            if (Platform != PlatformTemplate.None) parts.Add(PlatformAssets.LabelFor(Platform));
            if (Surface != SurfaceFilter.None) parts.Add(Surface switch
            {
                SurfaceFilter.Glossy => LocalizationService.T("SurfaceGlossy"),
                SurfaceFilter.Matte => LocalizationService.T("SurfaceMatte"),
                SurfaceFilter.Holographic => LocalizationService.T("SurfaceHolo"),
                SurfaceFilter.Vintage => LocalizationService.T("SurfaceVintage"),
                _ => LocalizationService.T("SurfaceNone"),
            });
            return parts.Count == 0 ? LocalizationService.T("OverlayOriginal") : string.Join(" · ", parts);
        }
    }

    /// <summary>Re-raise localized labels after a language switch.</summary>
    public void RefreshLocalized()
    {
        OnChanged(nameof(OverlayLabel));
        OnChanged(nameof(StatusLabel));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<Game> Games { get; } = new();

    private Game? _selectedGame;
    public Game? SelectedGame
    {
        get => _selectedGame;
        set { _selectedGame = value; OnChanged(); OnChanged(nameof(SelectedGameName)); }
    }

    public string SelectedGameName => SelectedGame?.Name ?? string.Empty;

    private ScanMode _scanMode = ScanMode.ShortcutsFolder;
    public ScanMode ScanMode
    {
        get => _scanMode;
        set { _scanMode = value; OnChanged(); OnChanged(nameof(ScanModeTitle)); }
    }

    public string ScanModeTitle => ScanMode switch
    {
        ScanMode.AllSubFolders => LocalizationService.T("ModeSubFolders"),
        ScanMode.SpecificFolder => LocalizationService.T("ModeFolder"),
        ScanMode.SpecificShortcuts => LocalizationService.T("ModePicks"),
        _ => LocalizationService.T("ModeShortcuts"),
    };

    /// <summary>Localized string table — bindings re-evaluate on language change.</summary>
    public Localizer Loc => LocalizationService.Strings;

    private double _progress;
    public double Progress
    {
        get => _progress;
        set { _progress = value; OnChanged(); OnChanged(nameof(IsProgressVisible)); }
    }

    public bool IsProgressVisible => IsBuilding || Progress > 0;

    private bool _isBuilding;
    public bool IsBuilding
    {
        get => _isBuilding;
        set { _isBuilding = value; OnChanged(); OnChanged(nameof(IsProgressVisible)); }
    }

    public int Processed => Games.Count;
    public int Matched => CountMatched();
    public int AttentionCount => Games.Count - Matched;
    public int SuccessRate => Processed == 0 ? 0 : (int)System.Math.Round(Matched * 100.0 / Processed);
    public string SuccessRateLabel => $"{SuccessRate}%";
    public string GamesCountLabel => $"{Games.Count}{LocalizationService.T("GamesSuffix")}";

    public MainViewModel()
    {
        foreach (var game in Seed()) Games.Add(game);
        Games.CollectionChanged += (_, _) => RefreshCounts();
        SelectedGame = Games[0];
        // Language flips: re-evaluate all VM.Loc.* bindings + localized labels.
        LocalizationService.LanguageChanged += (_, _) =>
        {
            OnChanged(nameof(Loc));
            OnChanged(nameof(ScanModeTitle));
            OnChanged(nameof(GamesCountLabel));
            foreach (var g in Games) g.RefreshLocalized();
        };
    }

    public void NotifyGamesChanged() => RefreshCounts();

    private int CountMatched()
    {
        var n = 0;
        foreach (var g in Games) if (g.Status == GameStatus.Matched) n++;
        return n;
    }

    private void RefreshCounts()
    {
        OnChanged(nameof(Processed));
        OnChanged(nameof(Matched));
        OnChanged(nameof(AttentionCount));
        OnChanged(nameof(SuccessRate));
        OnChanged(nameof(SuccessRateLabel));
        OnChanged(nameof(GamesCountLabel));
    }

    private static Game[] Seed() =>
    [
        new() { Id = "eclipse-vanguard", Name = "Eclipse Vanguard", Shortcut = @"D:\Games\Eclipse Vanguard\Vanguard.lnk", Cover = "ms-appx:///Assets/covers/showcase-base.png", Status = GameStatus.Matched, Confidence = 98, Source = "SteamGridDB", Platform = PlatformTemplate.Ps5Blue, Surface = SurfaceFilter.Glossy },
        new() { Id = "ashen-crown", Name = "Ashen Crown", Shortcut = @"D:\Games\Ashen Crown\AshenCrown.lnk", Cover = "ms-appx:///Assets/covers/ashen-crown.png", Status = GameStatus.Matched, Confidence = 96, Source = "SteamGridDB", Platform = PlatformTemplate.GfwClassic },
        new() { Id = "neon-runner", Name = "Neon Runner 2077", Shortcut = @"D:\Games\Neon Runner\NeonRunner.lnk", Cover = "ms-appx:///Assets/covers/neon-runner.png", Status = GameStatus.Matched, Confidence = 94, Source = "IGDB", Platform = PlatformTemplate.XboxSeries, Surface = SurfaceFilter.Holographic },
        new() { Id = "void-drift", Name = "Void Drift", Shortcut = @"E:\Library\Void Drift\VoidDrift.lnk", Cover = "ms-appx:///Assets/covers/void-drift.png", Status = GameStatus.Matched, Confidence = 91, Source = "SteamGridDB" },
        new() { Id = "frostbound", Name = "Frostbound Saga", Shortcut = @"E:\Library\Frostbound\Frostbound.lnk", Cover = "ms-appx:///Assets/covers/frostbound.png", Status = GameStatus.Matched, Confidence = 89, Source = "IGDB" },
        new() { Id = "iron-tide", Name = "Iron Tide", Shortcut = @"E:\Library\IronTide\IT_Launcher.lnk", Cover = "ms-appx:///Assets/covers/iron-tide.png", Status = GameStatus.Attention, Confidence = 62, Source = "Web match" },
        new() { Id = "spectral-hollow", Name = "Spectral Hollow", Shortcut = @"C:\Users\Player\Desktop\Hollow.lnk", Cover = "ms-appx:///Assets/covers/spectral-hollow.png", Status = GameStatus.Attention, Confidence = 54, Source = "Web match" },
    ];

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Maps a <see cref="PlatformTemplate"/> to its PNG under Assets\Templates
/// (transparent-center box-art frames) and its display label. Shared by the
/// XAML preview (<see cref="Controls.CoverFrame"/>) and the Win2D compositor
/// (<see cref="Services.IconBuilderService"/>).
/// </summary>
public static class PlatformAssets
{
    public static string? FileFor(PlatformTemplate t) => t switch
    {
        PlatformTemplate.Ps5Blue => "template_ps5_blue.png",
        PlatformTemplate.Ps5Black => "template_ps5_black.png",
        PlatformTemplate.XboxSeries => "template_xbox_series.png",
        PlatformTemplate.XboxOne => "template_xbox_one.png",
        PlatformTemplate.Xbox360 => "template_xbox_360.png",
        PlatformTemplate.SteamBlue => "template_steam_blue.png",
        PlatformTemplate.SteamMinimal => "template_steam_minimal.png",
        PlatformTemplate.Epic => "template_epic.png",
        PlatformTemplate.GfwClassic => "template_gfw_classic.png",
        PlatformTemplate.GfwModern => "template_gfw_modern.png",
        PlatformTemplate.Windows11 => "template_windows_11.png",
        _ => null,
    };

    /// <summary>Brand names stay English in every language.</summary>
    public static string LabelFor(PlatformTemplate t) => t switch
    {
        PlatformTemplate.Ps5Blue => "PS5 (Blue)",
        PlatformTemplate.Ps5Black => "PS5 (Black)",
        PlatformTemplate.XboxSeries => "Xbox Series",
        PlatformTemplate.XboxOne => "Xbox One",
        PlatformTemplate.Xbox360 => "Xbox 360",
        PlatformTemplate.SteamBlue => "Steam (Blue)",
        PlatformTemplate.SteamMinimal => "Steam (Minimal)",
        PlatformTemplate.Epic => "Epic Games",
        PlatformTemplate.GfwClassic => "GFW (Classic)",
        PlatformTemplate.GfwModern => "GFW (Modern)",
        PlatformTemplate.Windows11 => "Windows 11",
        PlatformTemplate.Custom => "Custom",
        _ => string.Empty,
    };

    /// <summary>Ordered list for the platform ComboBox (index == enum value).</summary>
    public static IReadOnlyList<PlatformTemplate> All { get; } =
    [
        PlatformTemplate.None,
        PlatformTemplate.Ps5Blue, PlatformTemplate.Ps5Black,
        PlatformTemplate.XboxSeries, PlatformTemplate.XboxOne, PlatformTemplate.Xbox360,
        PlatformTemplate.SteamBlue, PlatformTemplate.SteamMinimal,
        PlatformTemplate.Epic,
        PlatformTemplate.GfwClassic, PlatformTemplate.GfwModern,
        PlatformTemplate.Windows11,
    ];
}
