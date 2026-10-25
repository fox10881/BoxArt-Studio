using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Windows.System;
using GameIconsStudio.Services;

namespace GameIconsStudio.Dialogs;

/// <summary>
/// Lets the user compose an AI cover prompt, copy the program's original
/// icon as a reference image, copy the generated prompt text, and jump to
/// Microsoft Copilot — all without leaving the clipboard guessing.
/// </summary>
public sealed partial class AiPromptDialog
{
    private Game? _game;

    /// <summary>Localized strings for this dialog (bound via x:Bind).</summary>
    public Localizer Loc => LocalizationService.Strings;

    public AiPromptDialog()
    {
        this.InitializeComponent();
        FlowDirection = LocalizationService.IsRtl
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        PrimaryButtonClick += async (_, _) =>
        {
            var encoded = Uri.EscapeDataString(BuildPrompt());
            await Launcher.LaunchUriAsync(new Uri($"https://www.bing.com/images/create?q={encoded}"));
        };
    }

    /// <summary>Bind the dialog to the selected game (pre-fills the name).</summary>
    public void Initialize(Game game)
    {
        _game = game;
        AiNameTextBox.Text = game.Name;
    }

    /// <summary>
    /// Copies the program's ORIGINAL icon to the clipboard as a bitmap.
    /// Resolves the shortcut target first so the user gets the real exe icon
    /// rather than our generated cover, then grabs a 256px shell thumbnail.
    /// </summary>
    private async void CopyIcon_Click(object sender, RoutedEventArgs e)
    {
        if (_game is null) return;
        try
        {
            var path = _game.ApplyPath;

            // .lnk → resolve the real target exe for the original icon.
            if (!_game.IsFolderTarget &&
                path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var lnk = ShellShortcut.Open(path);
                    if (lnk.TargetPath.Length > 0 && File.Exists(lnk.TargetPath))
                        path = lnk.TargetPath;
                }
                catch { /* fall back to the .lnk itself */ }
            }

            StorageItemThumbnail thumb;
            if (_game.IsFolderTarget)
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(path);
                thumb = await folder.GetThumbnailAsync(ThumbnailMode.SingleItem, 256);
            }
            else
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 256);
            }

            var dp = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            dp.SetBitmap(RandomAccessStreamReference.CreateFromStream(thumb));
            Clipboard.SetContent(dp);
            Clipboard.Flush();
            ShowCopied(Loc.AiCopiedIcon);
        }
        catch (Exception ex)
        {
            ShowCopied($"{Loc.AiCopyError}{ex.Message}", true);
            LogService.Error($"AI icon copy failed: {ex.Message}");
        }
    }

    /// <summary>Composes the full AI prompt from the two text boxes.
    /// Ends with an explicit 2:3 aspect instruction — Bing's URL can't
    /// toggle the aspect-ratio UI, so the prompt itself enforces it.</summary>
    private string BuildPrompt() =>
        $"Strictly 2D flat graphic design of a software front cover, front face view ONLY. The entire color palette, mood, and theme MUST be directly extracted and inspired by the attached logo image. Place the provided logo prominently in the center of the design, integrated with minimalist 2D flat UI elements, dashboard graphics, and tools that visually represent this specific function: {AiDescriptionTextBox.Text}. Positioned just slightly above this central logo and UI illustration, write the text '{AiNameTextBox.Text}' using a beautiful, elegant, and modern custom typography matching the logo's colors. Leave the very top area empty. Pure solid white isolated background, absolutely NO 3D, NO physical box, NO perspective. Vertical 2:3 aspect ratio.";

    /// <summary>Builds the AI prompt from the two text boxes and copies it.</summary>
    private void CopyPrompt_Click(object sender, RoutedEventArgs e)
    {
        var dp = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        dp.SetText(BuildPrompt());
        Clipboard.SetContent(dp);
        Clipboard.Flush();
        ShowCopied(Loc.AiCopiedPrompt);
    }

    private void ShowCopied(string msg, bool isError = false)
    {
        CopyStatus.Text = msg;
        CopyStatus.Foreground = isError
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BrushStatusWarn"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BrushPrimary"];
        CopyStatus.Visibility = Visibility.Visible;
    }
}
