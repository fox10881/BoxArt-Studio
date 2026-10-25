using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GameIconsStudio.Views;

public sealed partial class HomeView
{
    public MainViewModel VM { get; set; } = new();

    public event EventHandler<ScanMode>? ModeSelected;
    public event EventHandler? RepairRequested;

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
}
