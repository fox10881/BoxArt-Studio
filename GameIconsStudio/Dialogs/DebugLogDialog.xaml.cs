using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using GameIconsStudio.Services;

namespace GameIconsStudio.Dialogs;

/// <summary>
/// Live Debug Logs viewer — binds straight to LogService.Entries, keeps the
/// list pinned to the newest line, and offers copy/clear actions.
/// </summary>
public sealed partial class DebugLogDialog : ContentDialog
{
    public ObservableCollection<string> Entries => LogService.Entries;

    /// <summary>Localized strings — read once when the dialog loads.</summary>
    public Localizer Loc => LocalizationService.Strings;

    public DebugLogDialog()
    {
        this.InitializeComponent();
        FlowDirection = LocalizationService.IsRtl
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        Entries.CollectionChanged += Entries_CollectionChanged;
        Closed += (_, _) => Entries.CollectionChanged -= Entries_CollectionChanged;
        Loaded += (_, _) =>
        {
            UpdateCount();
            ScrollToEnd();
        };
        UpdateCount();
    }

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateCount();
        ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        if (Entries.Count > 0)
            LogList.ScrollIntoView(Entries[Entries.Count - 1]);
    }

    private void UpdateCount() => CountText.Text = $"{Entries.Count}";

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var pkg = new DataPackage();
        pkg.SetText(string.Join(Environment.NewLine, Entries));
        Clipboard.SetContent(pkg);
        Clipboard.Flush();
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => LogService.Clear();
}
