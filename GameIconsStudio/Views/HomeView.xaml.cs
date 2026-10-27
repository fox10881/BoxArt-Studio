using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace GameIconsStudio.Views;

public sealed partial class HomeView
{
    public MainViewModel VM { get; set; } = new();

    public event EventHandler<ScanMode>? ModeSelected;
    public event EventHandler? RepairRequested;
    /// <summary>Raised with the full path when a .lnk/.url is dropped on the drop zone.</summary>
    public event Action<string>? ShortcutDropped;

    public HomeView()
    {
        this.InitializeComponent();
    }

    private void ModeCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } &&
            Enum.TryParse<ScanMode>(tag, out var mode))
        {
            ModeSelected?.Invoke(this, mode);
        }
    }

    private void RepairCard_Click(object sender, RoutedEventArgs e) =>
        RepairRequested?.Invoke(this, EventArgs.Empty);

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
            e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        var path = items.OfType<StorageFile>()
            .Select(f => f.Path)
            .FirstOrDefault(IsShortcutFile);
        if (path is not null)
            ShortcutDropped?.Invoke(path);
    }

    private static bool IsShortcutFile(string path)
    {
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".url", StringComparison.OrdinalIgnoreCase);
    }
}
