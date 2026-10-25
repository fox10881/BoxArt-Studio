using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using GameIconsStudio.Services;

namespace GameIconsStudio.Views;

public sealed partial class StudioView
{
    public MainViewModel VM { get; set; } = new();

    public ObservableCollection<Game> FilteredGames { get; } = new();

    public event EventHandler? BuildRequested;
    public event EventHandler? CoverReplaced;
    public event EventHandler<Game>? AlternativesRequested;
    public event EventHandler<Game>? WrongGameRequested;
    public event EventHandler? RemoveIconRequested;
    public event EventHandler? PasteFailed;

    public StudioView()
    {
        this.InitializeComponent();
        // Ctrl+V anywhere in the Studio pastes a clipboard image onto the cover.
        AddHandler(KeyDownEvent, new KeyEventHandler(OnStudioKeyDown), handledEventsToo: true);
    }

    private bool _syncingSelection;
    private bool _vmHooked;

    private void StudioView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_vmHooked)
        {
            _vmHooked = true;
            BuildStylePickers();
            VM.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.SelectedGame))
                {
                    HookSelectedGame();
                    SyncStyleControls();
                    if (GamesList.SelectedItem is not null &&
                        !ReferenceEquals(GamesList.SelectedItem, VM.SelectedGame))
                    {
                        _syncingSelection = true;
                        GamesList.SelectedItem = VM.SelectedGame;
                        _syncingSelection = false;
                    }
                }
                else if (e.PropertyName == nameof(MainViewModel.Loc))
                {
                    BuildStylePickers(); // re-localize picker labels
                }
            };
            VM.Games.CollectionChanged += (_, _) =>
            {
                Refilter(SearchBox.Text);
                SyncSelection();
            };
        }
        if (FilteredGames.Count == 0) Refilter(string.Empty);
        SyncSelection();
        HookSelectedGame();
        SyncStyleControls();
    }

    private void SyncSelection()
    {
        if (ReferenceEquals(GamesList.SelectedItem, VM.SelectedGame)) return;
        _syncingSelection = true;
        GamesList.SelectedItem = VM.SelectedGame;
        _syncingSelection = false;
    }

    private void GamesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        if (GamesList.SelectedItem is Game g && !ReferenceEquals(g, VM.SelectedGame))
            VM.SelectedGame = g;
    }

    private void Refilter(string query)
    {
        FilteredGames.Clear();
        foreach (var g in VM.Games)
            if (g.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                FilteredGames.Add(g);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        Refilter(SearchBox.Text);

    // ---------------- platform template + surface pickers ----------------

    /// <summary>One row in the platform ComboBox — thumbnail + label.
    /// <paramref name="CustomFile"/> is the file name inside the app's
    /// LocalAppData templates dir when <paramref name="Value"/> is
    /// <see cref="PlatformTemplate.Custom"/>.</summary>
    public sealed record PlatformOption(
        PlatformTemplate Value, string Label, string Thumb, string? CustomFile = null)
    {
        public bool ShowGlyph => string.IsNullOrEmpty(Thumb);
    }

    private ObservableCollection<PlatformOption> _platformOptions = [];
    /// <summary>Imported custom templates — re-added after each rebuild
    /// (e.g. a language switch) so they never vanish from the ComboBox.</summary>
    private readonly List<PlatformOption> _customOptions = [];
    private bool _syncingStyle;
    private Game? _hookedGame;

    private void BuildStylePickers()
    {
        _platformOptions = new ObservableCollection<PlatformOption>(PlatformAssets.All
            .Select(p => new PlatformOption(
                p,
                p == PlatformTemplate.None ? VM.Loc.PlatformNone : PlatformAssets.LabelFor(p),
                PlatformAssets.FileFor(p) is { } f ? $"ms-appx:///Assets/Templates/{f}" : string.Empty)));
        foreach (var c in _customOptions)
            _platformOptions.Add(c);

        _syncingStyle = true;
        try
        {
            PlatformCombo.ItemsSource = _platformOptions;
            SurfaceButtons.ItemsSource = new List<string>
            {
                VM.Loc.SurfaceNone, VM.Loc.SurfaceGlossy, VM.Loc.SurfaceMatte,
                VM.Loc.SurfaceHolo, VM.Loc.SurfaceVintage,
            };
        }
        finally { _syncingStyle = false; }
        SyncStyleControls();
    }

    /// <summary>Reflect the model's Platform/Surface into the pickers —
    /// called on selection change, language switch, and after the
    /// remove-icon deep wipe resets both to None.</summary>
    private void SyncStyleControls()
    {
        _syncingStyle = true;
        try
        {
            var g = VM.SelectedGame;
            var p = g?.Platform ?? PlatformTemplate.None;
            if (p == PlatformTemplate.Custom && !string.IsNullOrEmpty(g?.CustomTemplate))
                EnsureCustomOption(g.CustomTemplate);
            PlatformCombo.SelectedItem = _platformOptions.FirstOrDefault(o =>
                o.Value == p && (p != PlatformTemplate.Custom || o.CustomFile == g!.CustomTemplate));
            SurfaceButtons.SelectedIndex = g is not null ? (int)g.Surface : -1;
        }
        finally { _syncingStyle = false; }
    }

    private void PlatformCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingStyle) return;
        if (PlatformCombo.SelectedItem is PlatformOption opt && VM.SelectedGame is { } g)
        {
            g.Platform = opt.Value;
            if (opt.Value == PlatformTemplate.Custom && !string.IsNullOrEmpty(opt.CustomFile))
                g.CustomTemplate = opt.CustomFile;
        }
    }

    /// <summary>Find-or-create a ComboBox option for a stored custom
    /// template file (used when a saved game's choice is restored).</summary>
    private void EnsureCustomOption(string file, string? label = null)
    {
        var existing = _platformOptions.FirstOrDefault(o => o.CustomFile == file);
        if (existing is not null) return;
        var opt = new PlatformOption(
            PlatformTemplate.Custom,
            label ?? $"Custom · {Path.GetFileNameWithoutExtension(file)}",
            new Uri(Path.Combine(GameChoicesService.CustomTemplatesDir, file)).AbsoluteUri,
            file);
        _platformOptions.Add(opt);
        _customOptions.Add(opt);
    }

    /// <summary>Import a user-supplied transparent PNG/WebP as a template —
    /// copied to LocalAppData, added to the ComboBox, auto-selected.</summary>
    private async void BrowseTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedGame is null) return;
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Hwnd);
        picker.FileTypeFilter.Add(".png");   // alpha channel required —
        picker.FileTypeFilter.Add(".webp");  // no jpg/bmp/gif allowed
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;

        var picked = await picker.PickSingleFileAsync();
        if (picked is null) return;

        // Copy into the app templates dir under a unique safe name so the
        // template survives the user moving/deleting the original file.
        var ext = Path.GetExtension(picked.Name);
        var safeName = $"custom_template_{Guid.NewGuid():N}{ext}";
        var dest = Path.Combine(GameChoicesService.CustomTemplatesDir, safeName);
        File.Copy(picked.Path, dest);

        EnsureCustomOption(safeName, $"Custom · {picked.DisplayName}");
        VM.SelectedGame.Platform = PlatformTemplate.Custom;
        VM.SelectedGame.CustomTemplate = safeName;
        var opt = _platformOptions.FirstOrDefault(o => o.CustomFile == safeName);
        _syncingStyle = true;
        try { PlatformCombo.SelectedItem = opt; }
        finally { _syncingStyle = false; }
    }

    private void SurfaceButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingStyle) return;
        if (VM.SelectedGame is { } g && SurfaceButtons.SelectedIndex >= 0)
            g.Surface = (SurfaceFilter)SurfaceButtons.SelectedIndex;
    }

    /// <summary>Watch Platform/Surface mutations on the game itself (e.g.
    /// the remove-icon deep wipe) so the pickers stay truthful.</summary>
    private void HookSelectedGame()
    {
        if (_hookedGame is not null) _hookedGame.PropertyChanged -= SelectedGame_PropertyChanged;
        _hookedGame = VM.SelectedGame;
        if (_hookedGame is not null) _hookedGame.PropertyChanged += SelectedGame_PropertyChanged;
    }

    private void SelectedGame_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Game.Platform) or nameof(Game.Surface))
            SyncStyleControls();
    }

    private void MarkWrong_Click(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedGame is { } g)
            WrongGameRequested?.Invoke(this, g);
    }

    private void Build_Click(object sender, RoutedEventArgs e) =>
        BuildRequested?.Invoke(this, EventArgs.Empty);

    private void RemoveIcon_Click(object sender, RoutedEventArgs e) =>
        RemoveIconRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// "Choose different cover" — asks MainWindow to (re)fetch the SteamGridDB
    /// alternative-cover gallery for the selected game.
    /// </summary>
    private void ChooseCover_Click(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedGame is { } g)
            AlternativesRequested?.Invoke(this, g);
    }

    /// <summary>Clicking a gallery thumbnail instantly applies it as the cover.</summary>
    private void AltCovers_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ArtworkCandidate art && VM.SelectedGame is { } g)
        {
            g.Cover = art.Url;
            g.Source = $"SteamGridDB · {art.Width}×{art.Height}";
            g.Status = GameStatus.Matched;
            if (g.Confidence == 0) g.Confidence = 100;
            VM.NotifyGamesChanged();
            CoverReplaced?.Invoke(this, EventArgs.Empty);
        }
    }

    // ---------------- clipboard / local file covers ----------------

    private void OnStudioKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.V) return;
        var state = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        if (!state.HasFlag(VirtualKeyStates.Down)) return;
        e.Handled = true;
        _ = PasteCoverAsync();
    }

    private async void PasteCover_Click(object sender, RoutedEventArgs e) =>
        await PasteCoverAsync();

    /// <summary>
    /// Reads the clipboard: a bitmap ("Copy image" in a browser) or a copied
    /// image file. Returns true when a cover was applied.
    /// </summary>
    private async Task<bool> PasteCoverAsync()
    {
        if (VM.SelectedGame is not { } g) return false;
        try
        {
            var content = Clipboard.GetContent();
            string? fileUri = null;
            var source = "Pasted image";

            if (content.Contains(StandardDataFormats.Bitmap))
            {
                var bmp = await content.GetBitmapAsync();
                using var ras = await bmp.OpenReadAsync();
                var file = Path.Combine(SettingsService.GetCoverCacheDir(),
                    $"paste-{Guid.NewGuid():N}.png");
                await using (var fs = File.Create(file))
                    await ras.AsStream().CopyToAsync(fs);
                fileUri = new Uri(file).AbsoluteUri;
            }
            else if (content.Contains(StandardDataFormats.StorageItems))
            {
                var items = await content.GetStorageItemsAsync();
                var file = items?.OfType<StorageFile>().FirstOrDefault(f =>
                    f.FileType.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                    f.FileType.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    f.FileType.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                    f.FileType.Equals(".webp", StringComparison.OrdinalIgnoreCase));
                if (file is not null)
                {
                    fileUri = new Uri(file.Path).AbsoluteUri;
                    source = $"Pasted file · {file.Name}";
                }
            }

            if (fileUri is null)
            {
                PasteFailed?.Invoke(this, EventArgs.Empty);
                return false;
            }

            g.Cover = fileUri;
            g.Source = source;
            g.Status = GameStatus.Matched;
            g.Confidence = 100;
            VM.NotifyGamesChanged();
            CoverReplaced?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch
        {
            PasteFailed?.Invoke(this, EventArgs.Empty);
            return false;
        }
    }

    /// <summary>Old file picker — kept as the explicit "Browse PC" action.</summary>
    private async void BrowseCover_Click(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedGame is null) return;
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Hwnd);
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".webp");
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        VM.SelectedGame.Cover = new Uri(file.Path).AbsoluteUri;
        VM.SelectedGame.Source = $"Custom · {file.Name}";
        VM.SelectedGame.Status = GameStatus.Matched;
        VM.SelectedGame.Confidence = 100;
        VM.NotifyGamesChanged();
        CoverReplaced?.Invoke(this, EventArgs.Empty);
    }

    private async void AiGenerate_Click(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedGame is null) return;
        var dlg = new Dialogs.AiPromptDialog { XamlRoot = this.XamlRoot };
        dlg.Initialize(VM.SelectedGame);
        await dlg.ShowAsync();
    }

    private async void GoogleSearch_Click(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedGame is null) return;
        var q = Uri.EscapeDataString($"{VM.SelectedGame.Name} vertical game cover art");
        await Windows.System.Launcher.LaunchUriAsync(
            new Uri($"https://www.google.com/search?tbm=isch&q={q}"));
    }
}
