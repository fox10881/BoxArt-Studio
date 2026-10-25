using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GameIconsStudio;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Visible;
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is not Visibility.Visible;
}

public class StringToImageSourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is string s && !string.IsNullOrEmpty(s) ? new BitmapImage(new Uri(s)) : new BitmapImage();

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>Status -> status brush (emerald when matched, amber otherwise).</summary>
public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var ok = value is GameStatus.Matched;
        var key = ok ? "BrushStatusOk" : "BrushStatusWarn";
        return (Brush)Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>Status -> pill border brush.</summary>
public class StatusToBorderBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var ok = value is GameStatus.Matched;
        var key = ok ? "BrushOkBorder" : "BrushWarnBorder";
        return (Brush)Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>Status -> pill background tint.</summary>
public class StatusToTintBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var ok = value is GameStatus.Matched;
        var key = ok ? "BrushOkTint" : "BrushWarnTint";
        return (Brush)Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>bool + param "ResKeyTrue|ResKeyFalse" -> resource lookup.</summary>
public class BoolToResourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var parts = (parameter as string ?? string.Empty).Split('|');
        var key = value is true && parts.Length == 2 ? parts[0] : parts.Length > 1 ? parts[1] : parts[0];
        return Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>0-100 percent + param "trackWidth" -> pixel width for a progress fill.</summary>
public class PercentToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var track = double.TryParse(parameter as string, out var t) ? t : 112;
        var pct = value is int i ? i : value is double d ? d : 0;
        return Math.Clamp(track * pct / 100.0, 0, track);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>int count -> Visible when > 0; pass "invert" to flip.</summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var has = value is int i && i > 0;
        if (parameter is "invert") has = !has;
        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>bool + param "WhenTrue|WhenFalse" -> double. Used to switch the
/// preview frame between 1:1 (square) and 2:3 heights.</summary>
public class BoolToDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var parts = (parameter as string ?? "0|0").Split('|');
        var whenTrue = double.TryParse(parts[0], out var t) ? t : 0;
        var whenFalse = parts.Length > 1 && double.TryParse(parts[1], out var f) ? f : 0;
        return value is true ? whenTrue : whenFalse;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is not true;
}
