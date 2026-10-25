using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace GameIconsStudio.Controls;

public enum IconKind
{
    ArrowLeft, Globe, Minus, Square, Sun, Moon, Wrench, Close, Search, Check,
    CheckCircle, AlertTriangle, TrendingUp, Zap, Loader, Flag, ImageUp,
    Gamepad, Sparkles, ArrowUpRight, FolderSymlink, FolderTree, FolderOpen,
    FileStack, Play, Layers, Settings, Key, ExternalLink, CloudDownload, Clipboard,
    RotateCcw, Bug, Trash2
}

/// <summary>
/// Renders Lucide-style 24x24 stroke icons as native XAML paths.
/// </summary>
public class LucideIcon : UserControl
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(IconKind), typeof(LucideIcon),
        new PropertyMetadata(IconKind.Search, (s, _) => ((LucideIcon)s).Rebuild()));

    public IconKind Kind
    {
        get => (IconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(LucideIcon),
        new PropertyMetadata(null, (s, _) => ((LucideIcon)s).Rebuild()));

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(LucideIcon),
        new PropertyMetadata(2.0, (s, _) => ((LucideIcon)s).Rebuild()));

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    private readonly Viewbox _viewbox;

    public LucideIcon()
    {
        _viewbox = new Viewbox { Stretch = Stretch.Uniform };
        Content = _viewbox;
        DefaultStyleKey = typeof(LucideIcon);
        Rebuild();
    }

    private void Rebuild()
    {
        var stroke = Stroke ?? new SolidColorBrush(Microsoft.UI.Colors.White);
        var (figures, filled) = Icons.Data[Kind];

        var canvas = new Grid { Width = 24, Height = 24 };
        foreach (var data in figures)
        {
            // XamlReader.Load is not implemented in WinUI 3 — parse geometry via XamlBindingHelper instead.
            var geometry = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);
            var path = new Path { Data = geometry, Stretch = Stretch.None };
            if (filled)
            {
                path.Fill = stroke;
            }
            else
            {
                path.Stroke = stroke;
                path.StrokeThickness = StrokeThickness;
                path.StrokeStartLineCap = PenLineCap.Round;
                path.StrokeEndLineCap = PenLineCap.Round;
                path.StrokeLineJoin = PenLineJoin.Round;
            }
            canvas.Children.Add(path);
        }
        _viewbox.Child = canvas;
    }

    private static class Icons
    {
        // Each entry: one or more 24x24 path figures + whether it's fill-based.
        public static readonly Dictionary<IconKind, (string[] Figures, bool Filled)> Data = new()
        {
            [IconKind.ArrowLeft] = (new[] { "M19 12H5 M12 19L5 12L12 5" }, false),
            [IconKind.Globe] = (new[]
            {
                "M22 12 A10 10 0 1 0 2 12 A10 10 0 1 0 22 12 Z",
                "M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20",
                "M2 12h20"
            }, false),
            [IconKind.Minus] = (new[] { "M5 12h14" }, false),
            [IconKind.Square] = (new[] { "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2Z" }, false),
            [IconKind.Sun] = (new[]
            {
                "M16 12 A4 4 0 1 0 8 12 A4 4 0 1 0 16 12 Z",
                "M12 2v2 M12 20v2 M4.93 4.93l1.41 1.41 M17.66 17.66l1.41 1.41 M2 12h2 M20 12h2 M6.34 17.66l-1.41 1.41 M19.07 4.93l-1.41 1.41"
            }, false),
            [IconKind.Moon] = (new[] { "M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z" }, false),
            [IconKind.Wrench] = (new[] { "M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76Z" }, false),
            [IconKind.Close] = (new[] { "M18 6L6 18 M6 6l12 12" }, false),
            [IconKind.Search] = (new[]
            {
                "M19 11 A8 8 0 1 0 3 11 A8 8 0 1 0 19 11 Z",
                "M21 21l-4.35-4.35"
            }, false),
            [IconKind.Check] = (new[] { "M20 6L9 17l-5-5" }, false),
            [IconKind.CheckCircle] = (new[]
            {
                "M22 12 A10 10 0 1 0 2 12 A10 10 0 1 0 22 12 Z",
                "m9 12 2 2 4-4"
            }, false),
            [IconKind.AlertTriangle] = (new[]
            {
                "m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z",
                "M12 9v4 M12 17h.01"
            }, false),
            [IconKind.TrendingUp] = (new[]
            {
                "m22 7-8.5 8.5-5-5L2 17",
                "M16 7h6v6"
            }, false),
            [IconKind.Zap] = (new[] { "M13 2L3 14h9l-1 8L21 10h-9l1-8Z" }, true),
            [IconKind.Loader] = (new[] { "M21 12a9 9 0 1 1-6.219-8.56" }, false),
            [IconKind.Flag] = (new[]
            {
                "M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1Z",
                "M4 22v-7"
            }, false),
            [IconKind.ImageUp] = (new[]
            {
                "M10.3 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v10l-3.1-3.1a2 2 0 0 0-2.814-.014L6 21",
                "M11 9 A2 2 0 1 0 7 9 A2 2 0 1 0 11 9 Z",
                "m14 19.5 3-3 3 3 M17 22v-5.5"
            }, false),
            [IconKind.Gamepad] = (new[]
            {
                "M17.32 5H6.68a4 4 0 0 0-3.978 3.59c-.006.052-.01.101-.017.152C2.604 9.416 2 14.456 2 16a3 3 0 0 0 3 3c1 0 1.5-.5 2-1l1.414-1.414A2 2 0 0 1 9.828 16h4.344a2 2 0 0 1 1.414.586L17 18c.5.5 1 1 2 1a3 3 0 0 0 3-3c0-1.545-.604-6.584-.685-7.258-.007-.05-.011-.1-.017-.151A4 4 0 0 0 17.32 5Z",
                "M6 11h4 M8 9v4 M15 12h.01 M18 10h.01"
            }, false),
            [IconKind.Sparkles] = (new[]
            {
                "m12 3-1.9 5.8a2 2 0 0 1-1.29 1.29L3 12l5.81 1.91a2 2 0 0 1 1.29 1.29L12 21l1.9-5.81a2 2 0 0 1 1.29-1.29L21 12l-5.81-1.91a2 2 0 0 1-1.29-1.29Z",
                "M5 3v4 M19 17v4 M3 5h4 M17 19h4"
            }, false),
            [IconKind.ArrowUpRight] = (new[] { "M7 7h10v10 M7 17L17 7" }, false),
            [IconKind.FolderSymlink] = (new[]
            {
                "M2 9V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H20a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2Z",
                "m9 16 3-3-3-3 M12 13h6"
            }, false),
            [IconKind.FolderTree] = (new[]
            {
                "M13 4h5.5a1.5 1.5 0 0 1 1.5 1.5v1a1.5 1.5 0 0 1-1.5 1.5H13A1.5 1.5 0 0 1 11.5 6.5v-1A1.5 1.5 0 0 1 13 4Z",
                "M13 16h5.5a1.5 1.5 0 0 1 1.5 1.5v1a1.5 1.5 0 0 1-1.5 1.5H13a1.5 1.5 0 0 1-1.5-1.5v-1A1.5 1.5 0 0 1 13 16Z",
                "M11 6H8.5 M11 18H8.5 M8.5 6v12",
                "M8.5 12H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h2.1a2 2 0 0 1 1.69.9l.4.6"
            }, false),
            [IconKind.FolderOpen] = (new[]
            {
                "m6 14 1.45-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.55 6a2 2 0 0 1-1.94 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9h8.72a2 2 0 0 1 2 2v2"
            }, false),
            [IconKind.FileStack] = (new[]
            {
                "M21 7h-3a2 2 0 0 1-2-2V2",
                "M21 6v6.5c0 .83-.67 1.5-1.5 1.5h-7c-.83 0-1.5-.67-1.5-1.5v-9c0-.83.67-1.5 1.5-1.5H17Z",
                "M8 8.5V19c0 .83.67 1.5 1.5 1.5h7c.83 0 1.5-.67 1.5-1.5v-1"
            }, false),
            [IconKind.Play] = (new[] { "M6 3l14 9-14 9V3Z" }, true),
            [IconKind.Layers] = (new[]
            {
                "M12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83Z",
                "m22 12.18-8.58 3.91a2 2 0 0 1-1.66 0L2.6 11.92",
                "m22 17.18-8.58 3.91a2 2 0 0 1-1.66 0L2.6 16.92"
            }, false),
            [IconKind.Settings] = (new[]
            {
                "M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z",
                "M15 12 A3 3 0 1 0 9 12 A3 3 0 1 0 15 12 Z"
            }, false),
            [IconKind.Key] = (new[]
            {
                "M21 2l-9.6 9.6",
                "M15.5 7.5l3 3L22 7l-3-3",
                "M13 15.5 A5.5 5.5 0 1 0 2 15.5 A5.5 5.5 0 1 0 13 15.5 Z"
            }, false),
            [IconKind.ExternalLink] = (new[]
            {
                "M15 3h6v6",
                "M10 14L21 3",
                "M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"
            }, false),
            [IconKind.CloudDownload] = (new[]
            {
                "M4 14.899A7 7 0 1 1 15.71 8h1.79a4.5 4.5 0 0 1 2.5 8.242",
                "M12 12v9",
                "m8 17 4 4 4-4"
            }, false),
            [IconKind.Clipboard] = (new[]
            {
                "M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2",
                "M9 2h6a1 1 0 0 1 1 1v2a1 1 0 0 1-1 1H9a1 1 0 0 1-1-1V3a1 1 0 0 1 1-1Z"
            }, false),
            [IconKind.RotateCcw] = (new[]
            {
                "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8",
                "M3 3v5h5"
            }, false),
            [IconKind.Bug] = (new[]
            {
                "m8 2 1.88 1.88",
                "M14.12 3.88 16 2",
                "M9 7.13v-1a3.003 3.003 0 1 1 6 0v1",
                "M12 20c-3.3 0-6-2.7-6-6v-3a4 4 0 0 1 4-4h4a4 4 0 0 1 4 4v3c0 3.3-2.7 6-6 6",
                "M12 20v-9",
                "M6.53 9C4.6 8.8 3 7.1 3 5",
                "M6 13H2",
                "M3 21c0-2.1 1.7-3.9 3.8-4",
                "M20.97 5c0 2.1-1.6 3.8-3.5 4",
                "M22 13h-4",
                "M17.2 17c2.1.1 3.8 1.9 3.8 4"
            }, false),
            [IconKind.Trash2] = (new[]
            {
                "M3 6h18",
                "M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6",
                "M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2",
                "M10 11v6 M14 11v6"
            }, false),
        };
    }
}
