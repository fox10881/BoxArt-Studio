using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GameIconsStudio.Controls;

public sealed partial class CoverFrame
{
    public static readonly DependencyProperty PlatformProperty = DependencyProperty.Register(
        nameof(Platform), typeof(PlatformTemplate), typeof(CoverFrame),
        new PropertyMetadata(PlatformTemplate.None, (s, _) => ((CoverFrame)s).ApplyPlatform()));

    public PlatformTemplate Platform
    {
        get => (PlatformTemplate)GetValue(PlatformProperty);
        set => SetValue(PlatformProperty, value);
    }

    public static readonly DependencyProperty SurfaceProperty = DependencyProperty.Register(
        nameof(Surface), typeof(SurfaceFilter), typeof(CoverFrame),
        new PropertyMetadata(SurfaceFilter.None, (s, _) => ((CoverFrame)s).ApplySurface()));

    public SurfaceFilter Surface
    {
        get => (SurfaceFilter)GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    /// <summary>True = preview squashes to a square like the final 1:1 icon;
    /// false = the standard 2:3 letterbox preview.</summary>
    public static readonly DependencyProperty StretchSquareProperty = DependencyProperty.Register(
        nameof(StretchSquare), typeof(bool), typeof(CoverFrame),
        new PropertyMetadata(false, (s, _) => ((CoverFrame)s).ApplyStretch()));

    public bool StretchSquare
    {
        get => (bool)GetValue(StretchSquareProperty);
        set => SetValue(StretchSquareProperty, value);
    }

    /// <summary>file:// URI of an imported custom template — used when
    /// <see cref="Platform"/> is <see cref="PlatformTemplate.Custom"/>.</summary>
    public static readonly DependencyProperty CustomTemplateUriProperty = DependencyProperty.Register(
        nameof(CustomTemplateUri), typeof(string), typeof(CoverFrame),
        new PropertyMetadata(string.Empty, (s, _) => ((CoverFrame)s).ApplyPlatform()));

    public string CustomTemplateUri
    {
        get => (string)GetValue(CustomTemplateUriProperty);
        set => SetValue(CustomTemplateUriProperty, value);
    }

    public static readonly DependencyProperty CoverUriProperty = DependencyProperty.Register(
        nameof(CoverUri), typeof(string), typeof(CoverFrame),
        new PropertyMetadata(string.Empty, (s, _) => ((CoverFrame)s).ApplyCover()));

    public string CoverUri
    {
        get => (string)GetValue(CoverUriProperty);
        set => SetValue(CoverUriProperty, value);
    }

    public CoverFrame()
    {
        this.InitializeComponent();
        ApplyPlatform();
        ApplySurface();
        ApplyStretch();
        ApplyCover();
    }

    private void ApplyStretch()
    {
        if (ContentBox is null) return;
        // Swap the design space aspect — Stretch="Fill" on every layer
        // (art, template, filters) does the squashing automatically.
        var square = StretchSquare;
        ContentBox.Width = square ? 300 : 200;
        ContentBox.Height = 300;
    }

    private void ApplyPlatform()
    {
        if (TemplateImage is null) return;
        if (Platform == PlatformTemplate.Custom)
        {
            var uri = CustomTemplateUri;
            TemplateImage.Source = string.IsNullOrEmpty(uri) ? null : new BitmapImage(new Uri(uri));
            return;
        }
        var file = PlatformAssets.FileFor(Platform);
        TemplateImage.Source = file is null
            ? null
            : new BitmapImage(new Uri($"ms-appx:///Assets/Templates/{file}"));
    }

    private void ApplySurface()
    {
        if (SurfaceGlossy is null) return;
        SurfaceGlossy.Visibility = Surface == SurfaceFilter.Glossy ? Visibility.Visible : Visibility.Collapsed;
        SurfaceMatte.Visibility = Surface == SurfaceFilter.Matte ? Visibility.Visible : Visibility.Collapsed;
        SurfaceHolo.Visibility = Surface == SurfaceFilter.Holographic ? Visibility.Visible : Visibility.Collapsed;
        SurfaceVintage.Visibility = Surface == SurfaceFilter.Vintage ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyCover()
    {
        if (ArtBrush is null) return;
        var uri = CoverUri;
        ArtBrush.ImageSource = string.IsNullOrEmpty(uri) ? null : new BitmapImage(new Uri(uri));
        if (EmptyOverlay is not null)
            EmptyOverlay.Visibility = ArtBrush.ImageSource is null ? Visibility.Visible : Visibility.Collapsed;
    }
}
