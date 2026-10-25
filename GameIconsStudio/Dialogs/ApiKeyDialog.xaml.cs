using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GameIconsStudio.Services;

namespace GameIconsStudio.Dialogs;

public sealed partial class ApiKeyDialog : ContentDialog
{
    /// <summary>The key the user saved in this dialog, if any.</summary>
    public string? SavedKey { get; private set; }

    public ApiKeyDialog(string? currentKey)
    {
        this.InitializeComponent();
        ApiKeyBox.Text = currentKey ?? string.Empty;
        PrimaryButtonClick += OnSave;
    }

    private void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var key = ApiKeyBox.Text.Trim();
        try
        {
            SettingsService.SetApiKey(key);
            SavedKey = key.Length == 0 ? null : key;
        }
        catch (System.Exception ex)
        {
            // Keep the dialog open so the user can see the failure.
            args.Cancel = true;
            StatusText.Text = $"Could not save: {ex.Message}";
            StatusText.Visibility = Visibility.Visible;
        }
    }
}
