using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using GameIconsStudio.Controls;
using GameIconsStudio.Dialogs;
using GameIconsStudio.Services;
using GameIconsStudio.Views;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GameIconsStudio;

public sealed partial class MainWindow : Window
{
    /// <summary>HWND of the main window — needed to initialize native pickers/dialogs.</summary>
    public static IntPtr Hwnd { get; private set; }

    private readonly MainViewModel _vm = new();
    /// <summary>x:Bind source for localized title-bar strings.</summary>
    public MainViewModel VM => _vm;
    private readonly AppWindow _appWindow;
    private readonly HomeView _home;
    private readonly StudioView _studio;
    private readonly IconBuilderService _iconBuilder = new();
    private readonly GameScanService _scanner = new();
    private readonly SteamGridService _sgdb = new();
    private DispatcherTimer? _progressTimer;
    private DispatcherTimer? _toastTimer;
    private double _progressStep = 3;

    public MainWindow()
    {
        this.InitializeComponent();

        var hwnd = WindowNative.GetWindowHandle(this);
        Hwnd = hwnd;
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Resize(new Windows.Graphics.SizeInt32(1440, 900));
        _appWindow.Title = Services.LocalizationService.Strings.AppTitle;
        try { _appWindow.SetIcon("Assets\\app.ico"); } catch { }

        // Custom title bar + Mica (fall back to Acrylic if Mica can't be applied).
        // Both flags are needed: without the AppWindow-level collapse the
        // native caption strip (min/max/close) keeps rendering underneath
        // our custom buttons → the "double buttons" glitch.
        ExtendsContentIntoTitleBar = true;
        _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        _appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
        SetTitleBar(TitleBarDragArea);
        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch
        {
            try { SystemBackdrop = new DesktopAcrylicBackdrop(); } catch { /* no backdrop */ }
        }

        _home = new HomeView { VM = _vm };
        _home.ModeSelected += OnModeSelected;
        _home.RepairRequested += OnRepairRequested;
        _home.ShortcutDropped += path => _ = QuickLoadShortcutAsync(path);

        _studio = new StudioView { VM = _vm };
        _studio.BuildRequested += OnBuildRequested;
        _studio.CoverReplaced += OnCoverReplaced;
        _studio.AlternativesRequested += (_, g) => _ = LoadAlternativesAsync(g, toastOnDone: true);
        _studio.WrongGameRequested += (_, g) => _ = CorrectGameAsync(g);
        _studio.RemoveIconRequested += OnRemoveIconRequested;
        _studio.PasteFailed += (_, _) => ShowToast(IconKind.Clipboard, ToastTone.Warn,
            "Nothing to paste", "The clipboard doesn't contain an image or image file.");

        ContentHost.Content = _home;

        LogService.Init(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        LogService.Info("BoxArt Studio started");
        if (SettingsService.GetApiKey() is { } savedKey)
            _sgdb.Configure(savedKey);
        else
            LogService.Warn("SteamGridDB API key not configured");

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Progress) or nameof(MainViewModel.IsBuilding))
                UpdateProgressVisual();
            if (e.PropertyName == nameof(MainViewModel.ScanModeTitle))
                ModeChipText.Text = _vm.ScanModeTitle;
        };

        // Restore persisted language + mirror layout for RTL.
        LocalizationService.Init();
        LocalizationService.LanguageChanged += (_, _) => ApplyLanguage();
        ApplyLanguage();
    }

    /// <summary>Flip FlowDirection + retitle the window for the current language.</summary>
    private void ApplyLanguage()
    {
        Root.FlowDirection = LocalizationService.IsRtl
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        _appWindow.Title = LocalizationService.T("AppTitle");
        ModeChipText.Text = _vm.ScanModeTitle;
    }

    // ---------- navigation ----------

    private async void OnModeSelected(object? sender, ScanMode mode)
    {
        if (_picking) return;
        _picking = true;
        try
        {
            var scan = await PickAndScanAsync(mode);
            if (scan is null) return; // cancelled — stay on Home

            _vm.ScanMode = mode;
            ModeChipText.Text = _vm.ScanModeTitle;
            PopulateGames(scan.Value.Items, mode, scan.Value.Root);
            ShowStudio();
            _ = FetchCoversAsync();
        }
        finally
        {
            _picking = false;
        }
    }

    private bool _picking;
    private CancellationTokenSource? _fetchCts;

    /// <summary>
    /// Runs the correct picker for the mode, scans what was selected, and
    /// returns the parsed shortcuts + the picked root folder (null for the
    /// file-picker mode) — or null when the user cancels.
    /// </summary>
    private async Task<(IReadOnlyList<ShortcutInfo> Items, string? Root)?> PickAndScanAsync(ScanMode mode)
    {
        if (mode == ScanMode.SpecificShortcuts)
        {
            var files = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Desktop };
            InitializeWithWindow.Initialize(files, Hwnd);
            files.FileTypeFilter.Add(".lnk");
            var picked = await files.PickMultipleFilesAsync();
            if (picked is null || picked.Count == 0)
            {
                ShowToast(IconKind.FileStack, ToastTone.Warn,
                    "No shortcuts selected", "Pick one or more .lnk files to continue.");
                return null;
            }
            _vm.Progress = 15;
            var list = await _scanner.FromPathsAsync(picked.Select(f => f.Path));
            _vm.Progress = 0;
            return (list, null);
        }

        var folders = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        InitializeWithWindow.Initialize(folders, Hwnd);
        folders.FileTypeFilter.Add("*");
        var folder = await folders.PickSingleFolderAsync();
        if (folder is null)
        {
            ShowToast(IconKind.FolderOpen, ToastTone.Warn,
                "No folder selected", "Pick a folder to continue into the Studio.");
            return null;
        }

        _vm.Progress = 15;
        IReadOnlyList<ShortcutInfo> results = mode switch
        {
            // Folder modes: the folders themselves are the games.
            ScanMode.SpecificFolder => new List<ShortcutInfo> { _scanner.FolderEntry(folder.Path) },
            ScanMode.AllSubFolders => await _scanner.ScanSubFoldersAsync(folder.Path),
            // Shortcuts Folder: recursive .lnk scan.
            _ => await _scanner.ScanFolderAsync(folder.Path, recursive: true),
        };
        _vm.Progress = 0;

        if (results.Count == 0)
            ShowToast(IconKind.FolderOpen, ToastTone.Warn,
                "Nothing found",
                mode == ScanMode.AllSubFolders
                    ? "That folder has no sub-folders."
                    : "No .lnk shortcuts were found in that folder.");
        return (results, folder.Path);
    }

    /// <summary>Loads one dropped/picked shortcut into the Studio — same pipeline as the file picker.</summary>
    private async Task QuickLoadShortcutAsync(string path)
    {
        if (_picking) return;
        _picking = true;
        try
        {
            _vm.Progress = 15;
            var list = await _scanner.FromPathsAsync(new[] { path });
            _vm.Progress = 0;
            if (list.Count == 0)
            {
                ShowToast(IconKind.FileStack, ToastTone.Warn,
                    "Shortcut unreadable", "That shortcut could not be loaded.");
                return;
            }

            _vm.ScanMode = ScanMode.SpecificShortcuts;
            ModeChipText.Text = _vm.ScanModeTitle;
            PopulateGames(list, ScanMode.SpecificShortcuts, root: null);
            ShowStudio();
            _ = FetchCoversAsync();
        }
        finally
        {
            _picking = false;
        }
    }

    private void PopulateGames(IReadOnlyList<ShortcutInfo> items, ScanMode mode, string? root)
    {
        var folderTarget = mode is ScanMode.AllSubFolders or ScanMode.SpecificFolder;
        _vm.Games.Clear();
        var restored = 0;
        foreach (var s in items)
        {
            var display = string.IsNullOrWhiteSpace(s.NormalizedName) ? s.Name : s.NormalizedName;
            var applyPath = s.Path; // folder modes: s.Path IS the folder; shortcut modes: the .lnk
            var game = new Game
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = display,
                Shortcut = s.Path,
                SearchName = display,
                SteamAppId = s.SteamAppId,
                Year = s.Year,
                Status = GameStatus.Attention,
                Confidence = 0,
                Source = "Awaiting cover fetch",
                Platform = PlatformTemplate.None,
                ApplyPath = applyPath,
                IsFolderTarget = folderTarget,
            };

            // Cache-first: a saved choice restores the full customization and
            // marks the game so FetchCoversAsync skips it entirely.
            var saved = GameChoicesService.FindByName(display);
            if (saved is not null && TryRestoreChoice(game, saved))
            {
                game.IsSavedChoice = true;
                restored++;
            }
            _vm.Games.Add(game);
        }
        _vm.SelectedGame = _vm.Games.FirstOrDefault();
        _vm.NotifyGamesChanged();
        if (restored > 0)
            IconBuilderService.Log($"{restored} game(s) restored from choices.json");
    }

    /// <summary>
    /// Apply a saved choice to a freshly scanned game. Returns false when the
    /// cached cover file is gone — the game then goes through the normal
    /// SteamGridDB fetch like any new entry.
    /// </summary>
    private static bool TryRestoreChoice(Game g, GameChoice c)
    {
        if (Uri.TryCreate(c.Url, UriKind.Absolute, out var u) &&
            u.Scheme == "file" && !File.Exists(u.LocalPath))
        {
            IconBuilderService.Log($"saved cover missing for {g.Name} ({u.LocalPath}) — re-fetching");
            return false;
        }
        if (string.IsNullOrWhiteSpace(c.Url)) return false;

        g.Cover = c.Url;
        g.Platform = GameChoicesService.ParsePlatform(c.PlatformTemplate);
        if (g.Platform == PlatformTemplate.Custom)
        {
            var customPath = Path.Combine(GameChoicesService.CustomTemplatesDir, c.PlatformTemplate!);
            if (File.Exists(customPath))
                g.CustomTemplate = c.PlatformTemplate!;
            else
            {
                IconBuilderService.Log($"custom template missing for {g.Name} ({customPath}) — using None");
                g.Platform = PlatformTemplate.None;
            }
        }
        g.Surface = Enum.TryParse<SurfaceFilter>(c.SurfaceFilter, out var s)
            ? s : SurfaceFilter.None;
        g.StretchSquare = c.IsStretchedToSquare;
        g.StickerPcCdrom = c.HasPcCdrom;
        g.StickerDvd = c.HasDvd;
        g.StickerEsrb = c.HasEsrb;
        g.StickerPegi18 = c.HasPegi18;
        g.StickerSeal = c.HasSeal;
        g.SgdbGameId = c.SgdbGameId;
        g.Source = "Saved choice";
        g.Status = GameStatus.Matched;
        g.Confidence = 100;
        return true;
    }

    /// <summary>
    /// Real pipeline: resolve each scanned shortcut against SteamGridDB,
    /// download the best 2:3 cover into the local cache, and update the UI.
    /// </summary>
    private async Task FetchCoversAsync()
    {
        _fetchCts?.Cancel();
        var cts = _fetchCts = new CancellationTokenSource();

        // Cache-first: games restored from choices.json never touch the API.
        var games = _vm.Games.Where(g => !g.IsSavedChoice).ToList();
        var savedCount = _vm.Games.Count - games.Count;
        if (savedCount > 0)
            IconBuilderService.Log($"skipping {savedCount} saved game(s) — no SteamGridDB calls");
        if (games.Count == 0)
        {
            if (savedCount > 0)
                ShowToast(IconKind.CheckCircle, ToastTone.Success,
                    "Covers restored",
                    $"{savedCount} game(s) loaded from your saved choices — nothing to fetch.");
            return;
        }

        var key = SettingsService.GetApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            ShowToast(IconKind.Key, ToastTone.Warn,
                "SteamGridDB key needed",
                "Open the gear button in the header and paste your API key to fetch real covers.");
            key = await PromptApiKeyAsync();      // auto-open the settings dialog
            if (string.IsNullOrWhiteSpace(key))
            {
                foreach (var g in games)
                    g.Source = "No API key — open the gear settings";
                _vm.NotifyGamesChanged();
                return;
            }
        }
        _sgdb.Configure(key);

        var total = games.Count;
        var done = 0;
        var matched = 0;
        _vm.Progress = 3;
        try
        {
            await Parallel.ForEachAsync(games,
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cts.Token },
                async (g, ct) =>
                {
                    try
                    {
                        var match = await _sgdb.ResolveGameMatchAsync(
                            g.SearchName, g.Year, g.SteamAppId, ct: ct);
                        if (match is null || match.GameId <= 0)
                        {
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                g.Source = "No match on SteamGridDB";
                                g.Status = GameStatus.Attention;
                            });
                            return;
                        }

                        var cands = await GetCoverCandidatesAsync(match.GameId, 12, ct);
                        var art = cands.Count > 0 ? cands[0] : null;
                        if (art is null)
                        {
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                g.SgdbGameId = match.GameId;
                                g.Source = $"Matched: {match.GameName} — no artwork";
                                g.Confidence = (int)Math.Round(match.Score * 100);
                                g.Status = GameStatus.Attention;
                            });
                            return;
                        }

                        var bytes = await _sgdb.DownloadImageAsync(art.Url, ct);
                        var ext = Path.GetExtension(new Uri(art.Url).AbsolutePath).ToLowerInvariant();
                        if (ext is not ".png" and not ".jpg" and not ".jpeg" and not ".webp")
                            ext = ".png";
                        var file = Path.Combine(SettingsService.GetCoverCacheDir(),
                            $"{match.GameId}{ext}");
                        await File.WriteAllBytesAsync(file, bytes, ct);
                        var fileUri = new Uri(file).AbsoluteUri;

                        DispatcherQueue.TryEnqueue(() =>
                        {
                            g.SgdbGameId = match.GameId;
                            g.Cover = fileUri;
                            g.Source = "SteamGridDB";
                            g.Confidence = (int)Math.Round(match.Score * 100);
                            g.Status = match.Ambiguous ? GameStatus.Attention : GameStatus.Matched;
                            g.CoverOptions.Clear();
                            foreach (var a in cands) g.CoverOptions.Add(a);
                            g.RefreshCoverOptions();
                        });
                        Interlocked.Increment(ref matched);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            g.Source = $"Error: {ex.Message}";
                            g.Status = GameStatus.Attention;
                        });
                    }
                    finally
                    {
                        var d = Interlocked.Increment(ref done);
                        DispatcherQueue.TryEnqueue(() => _vm.Progress = Math.Max(3, d * 100.0 / total));
                    }
                });

            _vm.NotifyGamesChanged();
            if (!cts.IsCancellationRequested)
                ShowToast(IconKind.CloudDownload, ToastTone.Success,
                    "Covers fetched",
                    $"{matched} of {total} games matched on SteamGridDB.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowToast(IconKind.AlertTriangle, ToastTone.Warn, "Cover fetch failed", ex.Message);
        }
        finally
        {
            var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            settle.Tick += (_, _) => { settle.Stop(); _vm.Progress = 0; };
            settle.Start();
        }
    }

    /// <summary>
    /// Cover candidates for a game: prefers 600x900 (2:3) vertical grids,
    /// falls back to any grid, then square.
    /// </summary>
    private async Task<IReadOnlyList<ArtworkCandidate>> GetCoverCandidatesAsync(
        int gameId, int top, CancellationToken ct = default)
    {
        foreach (var kind in new[] { ArtworkKind.VerticalGrid, ArtworkKind.Grid, ArtworkKind.Square })
        {
            var cands = await _sgdb.GetArtworkCandidatesAsync(gameId, kind, top, ct);
            if (cands.Count > 0) return cands;
        }
        return Array.Empty<ArtworkCandidate>();
    }

    /// <summary>
    /// "Choose different cover": populate the alternative-covers gallery from
    /// SteamGridDB for the given (already matched) game.
    /// </summary>
    private async Task LoadAlternativesAsync(Game g, bool toastOnDone)
    {
        if (g.SgdbGameId <= 0)
        {
            ShowToast(IconKind.Flag, ToastTone.Warn,
                "No game matched yet",
                "Use 'Mark as wrong game' to find the right game, then pick a cover.");
            return;
        }
        try
        {
            var cands = await GetCoverCandidatesAsync(g.SgdbGameId, 12);
            g.CoverOptions.Clear();
            foreach (var a in cands) g.CoverOptions.Add(a);
            g.RefreshCoverOptions();

            if (toastOnDone)
                ShowToast(IconKind.CloudDownload, cands.Count > 0 ? ToastTone.Success : ToastTone.Warn,
                    cands.Count > 0 ? "Covers loaded" : "No covers found",
                    cands.Count > 0
                        ? $"{cands.Count} alternative covers for {g.Name}."
                        : "SteamGridDB has no grid artwork for this game.");
        }
        catch (Exception ex)
        {
            ShowToast(IconKind.AlertTriangle, ToastTone.Warn, "Cover fetch failed", ex.Message);
        }
    }

    /// <summary>
    /// "Mark as wrong game": SteamGridDB autocomplete dialog -> new GameId ->
    /// refreshed covers + alternative gallery.
    /// </summary>
    private async Task CorrectGameAsync(Game g)
    {
        var key = SettingsService.GetApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            ShowToast(IconKind.Key, ToastTone.Warn,
                "SteamGridDB key needed",
                "Open the gear button in the header and paste your API key first.");
            key = await PromptApiKeyAsync();      // auto-open the settings dialog
            if (string.IsNullOrWhiteSpace(key)) return;
        }
        _sgdb.Configure(key);

        GameSearchDialog dialog;
        try
        {
            dialog = new GameSearchDialog(_sgdb, g.SearchName)
            {
                XamlRoot = Root.XamlRoot,
                FlowDirection = Root.FlowDirection,
            };
        }
        catch (Exception ex)
        {
            ShowToast(IconKind.AlertTriangle, ToastTone.Warn, "Search", ex.Message);
            return;
        }

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Selected is null)
            return;

        var c = dialog.Selected;
        g.SgdbGameId = c.GameId;
        g.Source = $"Manual match · {c.Name}";
        g.Confidence = 100;
        g.Status = GameStatus.Matched;

        try
        {
            var cands = await GetCoverCandidatesAsync(c.GameId, 12);
            g.CoverOptions.Clear();
            foreach (var a in cands) g.CoverOptions.Add(a);
            g.RefreshCoverOptions();

            if (cands.Count > 0)
            {
                var bytes = await _sgdb.DownloadImageAsync(cands[0].Url);
                var ext = Path.GetExtension(new Uri(cands[0].Url).AbsolutePath).ToLowerInvariant();
                if (ext is not ".png" and not ".jpg" and not ".jpeg" and not ".webp")
                    ext = ".png";
                var file = Path.Combine(SettingsService.GetCoverCacheDir(), $"{c.GameId}{ext}");
                await File.WriteAllBytesAsync(file, bytes);
                g.Cover = new Uri(file).AbsoluteUri;
                g.Source = "SteamGridDB";
            }
            _vm.NotifyGamesChanged();
            ShowToast(IconKind.CheckCircle, ToastTone.Success,
                "Game corrected",
                $"Now matched to {c.Name} — {cands.Count} covers available below.");
        }
        catch (Exception ex)
        {
            _vm.NotifyGamesChanged();
            ShowToast(IconKind.AlertTriangle, ToastTone.Warn, "Cover fetch failed", ex.Message);
        }
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e) =>
        await PromptApiKeyAsync();

    private async void DebugButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DebugLogDialog { XamlRoot = Root.XamlRoot };
        await dialog.ShowAsync();
    }

    /// <summary>
    /// Opens the SteamGridDB API-key dialog. Returns the saved key (or null
    /// when cancelled/cleared) so callers can immediately continue a flow
    /// that needed a key.
    /// </summary>
    private async Task<string?> PromptApiKeyAsync()
    {
        try
        {
            var dialog = new ApiKeyDialog(SettingsService.GetApiKey())
            {
                XamlRoot = Root.XamlRoot,
                FlowDirection = Root.FlowDirection,
            };
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return null;

            if (dialog.SavedKey is { } key)
            {
                _sgdb.Configure(key);
                ShowToast(IconKind.CheckCircle, ToastTone.Success,
                    "API key saved", "SteamGridDB cover matching is enabled.");
                return key;
            }

            ShowToast(IconKind.Key, ToastTone.Warn,
                "API key cleared", "Cover fetching is disabled until you save a key.");
        }
        catch (Exception ex)
        {
            ShowToast(IconKind.AlertTriangle, ToastTone.Warn, "Settings", ex.Message);
        }
        return null;
    }

    /// <summary>True while a remove-icon deep wipe is running — guards any
    /// side-effect handlers that might re-trigger cover/apply work.</summary>
    private bool _isClearing;

    /// <summary>"Remove Custom Icon": revert desktop.ini / IconLocation for the
    /// selected target, then wipe the game's customization state so the UI
    /// (preview, overlay, badges, confidence) reflects the cleared icon.</summary>
    private async void OnRemoveIconRequested(object? sender, EventArgs e)
    {
        if (_vm.SelectedGame is not { } g || _isClearing) return;
        _isClearing = true; // suspend auto-triggers while we wipe
        try
        {
            await _iconBuilder.RemoveIconAsync(g);

            // Deep UI state wipe — the icon is gone, so nothing should keep
            // showing the old cover/template/badges.
            g.Cover = string.Empty;                 // preview -> placeholder
            g.Platform = PlatformTemplate.None;     // template -> None
            g.Surface = SurfaceFilter.None;         // finish -> Natural
            g.StretchSquare = false;                // shape -> 2:3 letterbox
            g.StickerPcCdrom = false;
            g.StickerDvd = false;
            g.StickerEsrb = false;
            g.StickerPegi18 = false;
            g.StickerSeal = false;
            g.Confidence = 0;
            g.Source = string.Empty;
            g.Status = GameStatus.Attention;
            g.IsSavedChoice = false;
            GameChoicesService.Forget(g);           // choices.json entry removed
            g.CoverOptions.Clear();
            g.RefreshCoverOptions();
            _vm.NotifyGamesChanged();

            ShowToast(IconKind.RotateCcw, ToastTone.Primary,
                LocalizationService.T("ToastIconRemoved"),
                string.Format(LocalizationService.T("ToastIconRemovedBody"), g.Name));
        }
        catch (Exception ex)
        {
            ShowToast(IconKind.AlertTriangle, ToastTone.Warn, "Remove failed", ex.Message);
        }
        finally
        {
            _isClearing = false;
        }
    }

    private void ShowStudio()
    {
        ContentHost.Content = _studio;
        BackButton.Visibility = Visibility.Visible;
        BackDivider.Visibility = Visibility.Visible;
        ModeChip.Visibility = Visibility.Visible;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        ContentHost.Content = _home;
        BackButton.Visibility = Visibility.Collapsed;
        BackDivider.Visibility = Visibility.Collapsed;
        ModeChip.Visibility = Visibility.Collapsed;
    }

    // ---------- actions ----------

    private async void OnBuildRequested(object? sender, EventArgs e)
    {
        if (_vm.IsBuilding) return;
        _vm.IsBuilding = true;
        _vm.Progress = 4;
        var progress = new Progress<BuildProgress>(p => _vm.Progress = Math.Max(4, p.Fraction * 100));
        try
        {
            var result = await _iconBuilder.BuildIconsAsync(_vm.Games.ToList(), progress);

            // Persist every customized game so the next scan restores it
            // instantly — cover (from the local cache), template, finish, shape.
            if (result.Applied > 0)
                foreach (var g in _vm.Games)
                    if (!string.IsNullOrWhiteSpace(g.Cover))
                    {
                        GameChoicesService.Save(g);
                        g.IsSavedChoice = true;
                    }

            var detail = $"{result.Applied} icon(s) applied";
            if (result.Skipped > 0) detail += $" · {result.Skipped} skipped (no cover)";
            if (result.Failed > 0) detail += $" · {result.Failed} failed";
            ShowToast(
                result.Failed > 0 ? IconKind.AlertTriangle : IconKind.CheckCircle,
                result.Failed > 0 ? ToastTone.Warn : ToastTone.Success,
                "Build finished", detail);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowToast(IconKind.AlertTriangle, ToastTone.Warn, "Build failed", ex.Message);
        }
        finally
        {
            _vm.IsBuilding = false;
            _vm.Progress = 0;
        }
    }

    private void OnCoverReplaced(object? sender, EventArgs e)
    {
        if (_vm.SelectedGame is null) return;
        ShowToast(IconKind.ImageUp, ToastTone.Glow,
            "Cover replaced",
            $"A custom cover is now used for {_vm.SelectedGame.Name}.");
    }

    private void RepairButton_Click(object sender, RoutedEventArgs e) => _ = RepairAsync();

    private void OnRepairRequested(object? sender, EventArgs e) => _ = RepairAsync();

    private async Task RepairAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = LocalizationService.T("RepairDialogTitle"),
            Content = LocalizationService.T("RepairDialogBody"),
            PrimaryButtonText = LocalizationService.T("RepairDialogPrimary"),
            CloseButtonText = LocalizationService.T("Cancel"),
            FlowDirection = Root.FlowDirection,
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        _vm.Progress = 4;
        try
        {
            await _iconBuilder.RefreshIconsAsync();
            _vm.Progress = 100;
            ShowToast(IconKind.Wrench, ToastTone.Primary,
                "Icon cache rebuilt",
                "Explorer was refreshed. New covers will show up instantly.");
        }
        catch (Exception ex)
        {
            ShowToast(IconKind.AlertTriangle, ToastTone.Warn, "Repair failed", ex.Message);
        }
        finally
        {
            var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            settle.Tick += (_, _) => { settle.Stop(); _vm.Progress = 0; };
            settle.Start();
        }
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        LocalizationService.SetLanguage(LocalizationService.IsRtl
            ? LocalizationService.English
            : LocalizationService.Arabic);
        ShowToast(IconKind.Globe, ToastTone.Glow,
            LocalizationService.T("LangToastTitle"),
            LocalizationService.T("LangToastBody"));
    }


    private void RunProgress(double step, Action onDone)
    {
        _progressStep = step;
        _vm.Progress = 4;
        _progressTimer?.Stop();
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _progressTimer.Tick += (_, _) =>
        {
            var next = Math.Min(100, _vm.Progress + _progressStep);
            _vm.Progress = next;
            if (next >= 100)
            {
                _progressTimer?.Stop();
                var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                settle.Tick += (_, _) =>
                {
                    settle.Stop();
                    onDone();
                    _vm.Progress = 0;
                };
                settle.Start();
            }
        };
        _progressTimer.Start();
    }

    private void UpdateProgressVisual()
    {
        var visible = _vm.IsProgressVisible;
        ProgressFill.Opacity = visible ? 1 : 0;
        ProgressFill.Width = visible ? Root.ActualWidth * _vm.Progress / 100.0 : 0;
    }

    // ---------- toast ----------

    private enum ToastTone { Success, Warn, Primary, Glow }

    private void ShowToast(IconKind icon, ToastTone tone, string title, string body)
    {
        var (tint, brush) = tone switch
        {
            ToastTone.Success => ("BrushOkTint", "BrushStatusOk"),
            ToastTone.Warn => ("BrushWarnTint", "BrushStatusWarn"),
            ToastTone.Glow => ("BrushGlowTint", "BrushGlow"),
            _ => ("BrushPrimaryTint", "BrushPrimary"),
        };
        ToastIconWrap.Background = (Brush)Application.Current.Resources[tint];
        ToastIcon.Stroke = (Brush)Application.Current.Resources[brush];
        ToastIcon.Kind = icon;
        ToastTitle.Text = title;
        ToastBody.Text = body;
        ToastHost.Visibility = Visibility.Visible;

        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer?.Stop();
            ToastHost.Visibility = Visibility.Collapsed;
        };
        _toastTimer.Start();
    }

    private void ToastDismiss_Click(object sender, RoutedEventArgs e) =>
        ToastHost.Visibility = Visibility.Collapsed;

    // ---------- theme ----------

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var dark = Root.RequestedTheme != ElementTheme.Dark;
        Root.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        ThemeIcon.Kind = dark ? IconKind.Sun : IconKind.Moon;
    }

    // ---------- caption buttons ----------

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        (_appWindow.Presenter as OverlappedPresenter)?.Minimize();

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (_appWindow.Presenter is not OverlappedPresenter p) return;
        if (p.State == OverlappedPresenterState.Maximized) p.Restore();
        else p.Maximize();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
