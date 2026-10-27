using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace GameIconsStudio.Services;

public sealed record BuildProgress(int Done, int Total)
{
    public double Fraction => Total == 0 ? 0 : (double)Done / Total;
}

public sealed record BuildResult(int Applied, int Skipped, int Failed, IReadOnlyList<string> Errors)
{
    public BuildResult(int applied, int skipped, int failed)
        : this(applied, skipped, failed, Array.Empty<string>()) { }
}

/// <summary>
/// Builds real .ico files and applies them to Windows targets.
///
/// Icon canvas spec (zero-crop letterbox):
///   Every game renders as a centered 2:3 poster on a transparent 1:1
///   canvas — the ENTIRE cover is stretched into the box, never cropped.
///   An optional platform template PNG (PS5/Xbox/Steam/Epic/GFW) is drawn
///   over the same rect (its center window is transparent), followed by an
///   optional surface finish (Glossy/Matte/Holographic/Vintage) and the
///   bottom sticker row, all clipped to a rounded-corner geometry.
///
/// Apply targets:
///   Shortcut (.lnk)  -> IShellLinkW IconLocation = "ico,0" + Save()
///   Folder           -> desktop.ini IconResource + folder ReadOnly attribute
///                       (Windows ignores desktop.ini without it)
///   After each apply -> SHCNE_UPDATEITEM for the path; at the end of a run
///   -> SHCNE_ASSOCCHANGED broadcast + ie4uinit -show.
/// </summary>
public sealed class IconBuilderService : IDisposable
{
    private static readonly int[] IcoSizes = [16, 32, 48, 64, 128, 256];
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNE_UPDATEITEM = 0x00002000;
    private const int SHCNE_UPDATEDIR = 0x00001000;
    private const uint SHCNF_IDLIST = 0x0000;
    private const uint SHCNF_PATHW = 0x0005;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private static string IconsDir
    {
        get
        {
            var d = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GameIconsStudio", "icons");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// <summary>Append a diagnostic line to %LocalAppData%\GameIconsStudio\debug_log.txt
    /// and tee it into the in-app Debug Logs viewer.</summary>
    internal static void Log(string message)
    {
        LogService.Info(message);
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GameIconsStudio");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "debug_log.txt"),
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>
    /// Real build: for each game, load its cover, composite per variant,
    /// write the .ico, and apply it to the shortcut or folder target.
    /// </summary>
    public Task<BuildResult> BuildIconsAsync(
        IReadOnlyList<Game> games,
        IProgress<BuildProgress>? progress = null,
        CancellationToken ct = default) =>
        Task.Run(async () =>
        {
            var applied = 0;
            var skipped = 0;
            var failed = 0;
            var errors = new List<string>();
            var total = games.Count;

            CanvasDevice? device = null;
            try { device = new CanvasDevice(); }
            catch (Exception ex) { Log($"Win2D device unavailable — legacy compositor: {ex.Message}"); }

            for (var i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var g = games[i];
                try
                {
                    var bytes = await LoadCoverBytesAsync(g, ct);
                    if (bytes is null || bytes.Length == 0 || string.IsNullOrWhiteSpace(g.ApplyPath))
                    {
                        Log($"SKIP {g.Name} — cover='{g.Cover}' apply='{g.ApplyPath}'");
                        skipped++;
                    }
                    else
                    {
                        Log($"BUILD {g.Name} platform={g.Platform} surface={g.Surface} target={g.ApplyPath} folder={g.IsFolderTarget}");
                        var frames = await ComposeIcoFramesAsync(device, bytes, g);
                        var ico = Path.Combine(IconsDir, $"{g.Id}.ico");
                        WriteIco(ico, frames);
                        ApplyIcon(g, ico);
                        applied++;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failed++;
                    var msg = $"{g.Name} ({g.ApplyPath}): {ex}";
                    errors.Add(msg);
                    Log($"FAIL {msg}");
                }
                progress?.Report(new BuildProgress(i + 1, total));
            }

            NotifyShellIconsChanged();
            RunIe4UInit("-ClearIconCache");
            RunIe4UInit("-show");
            device?.Dispose();
            return new BuildResult(applied, skipped, failed, errors);
        }, ct);

    // ---------------- icon composition ----------------

    /// <summary>
    /// Render every ICO frame. Win2D path: decode once, draw per size with
    /// antialiased geometry, gradients, and the platform template + surface
    /// filter. Falls back to the pure-software compositor if no CanvasDevice
    /// is available.
    /// </summary>
    private static async Task<List<(int Size, byte[] Png)>> ComposeIcoFramesAsync(
        CanvasDevice? device, byte[] imageBytes, Game g)
    {
        var frames = new List<(int, byte[])>(IcoSizes.Length);
        if (device is not null)
        {
            Dictionary<string, CanvasBitmap>? stickers = null;
            CanvasBitmap? template = null;
            CanvasBitmap? noise = null;
            try
            {
                using var stream = new MemoryStream(imageBytes);
                var bmp = await CanvasBitmap.LoadAsync(device, stream.AsRandomAccessStream());
                if (g.StickerPcCdrom || g.StickerDvd || g.StickerEsrb || g.StickerSeal || g.StickerPegi18)
                    stickers = await LoadStickerBitmapsAsync(device);
                if (g.Platform != PlatformTemplate.None)
                    template = await LoadTemplateBitmapAsync(device, g);
                if (g.Surface == SurfaceFilter.Matte)
                    noise = CreateNoiseBitmap(device);
                foreach (var size in IcoSizes)
                    frames.Add((size, await RenderFrameWin2DAsync(device, bmp, g, size, stickers, template, noise)));
                return frames;
            }
            catch (Exception ex)
            {
                frames.Clear();
                Log($"Win2D render failed — legacy fallback: {ex.Message}");
            }
            finally
            {
                if (stickers is not null)
                    foreach (var s in stickers.Values) s.Dispose();
                template?.Dispose();
                noise?.Dispose();
            }
        }
        foreach (var size in IcoSizes)
            frames.Add((size, await RenderFrameAsync(imageBytes, size, g.StretchSquare)));
        return frames;
    }

    // ================= Win2D compositor =================

    private static Color Rgba(byte r, byte g, byte b, byte a) => Color.FromArgb(a, r, g, b);

    // ---------------- case badges / stickers ----------------

    private static readonly (string Key, string File)[] StickerFiles =
    [
        ("pccdrom", "sticker_pccdrom.png"),
        ("dvd", "sticker_dvd.png"),
        ("esrb", "sticker_esrb.png"),
        ("pegi18", "sticker_pegi18.png"),
        ("seal", "sticker_seal.png"),
    ];

    /// <summary>
    /// Locate Assets\stickers — next to the exe in the deployed app, or inside
    /// the project tree when running from a test harness.
    /// </summary>
    private static string? FindStickersDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; dir is not null && i < 8; i++, dir = dir.Parent)
        {
            var direct = Path.Combine(dir.FullName, "Assets", "stickers");
            if (Directory.Exists(direct)) return direct;
            var proj = Path.Combine(dir.FullName, "GameIconsStudio", "Assets", "stickers");
            if (Directory.Exists(proj)) return proj;
        }
        return null;
    }

    private static async Task<Dictionary<string, CanvasBitmap>> LoadStickerBitmapsAsync(CanvasDevice device)
    {
        var map = new Dictionary<string, CanvasBitmap>();
        var dir = FindStickersDir();
        if (dir is null) { Log("stickers dir not found"); return map; }
        foreach (var (key, file) in StickerFiles)
        {
            var path = Path.Combine(dir, file);
            if (!File.Exists(path)) { Log($"sticker missing: {path}"); continue; }
            try
            {
                // Decode via WIC first so every format (png/jpg/webp) lands in
                // predictable straight-alpha BGRA, then hand it to Win2D with an
                // explicit alpha mode — prevents "black box" premultiply bugs.
                var bytes = await File.ReadAllBytesAsync(path);
                using var ras = new InMemoryRandomAccessStream();
                await ras.WriteAsync(bytes.AsBuffer());
                ras.Seek(0);
                var dec = await BitmapDecoder.CreateAsync(ras);
                var pd = await dec.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage);
                map[key] = CanvasBitmap.CreateFromBytes(device, pd.DetachPixelData(),
                    (int)dec.PixelWidth, (int)dec.PixelHeight,
                    Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized);
            }
            catch (Exception ex) { Log($"sticker {file}: {ex.Message}"); }
        }
        return map;
    }

    /// <summary>
    /// Physical-box sticker row along the bottom edge of the art:
    /// ratings anchored bottom-left, seal bottom-right, platform logos
    /// centered between them — all scaled to a uniform badge height.
    /// </summary>
    private static void PaintStickers(CanvasDrawingSession ds,
        Dictionary<string, CanvasBitmap>? stickers, Game g, Rect artRect, float k)
    {
        if (stickers is null || stickers.Count == 0) return;

        var bh = Math.Max(6f, 33f * k);          // uniform badge height
        var gap = Math.Max(2f, 5f * k);
        var y = (float)(artRect.Bottom - 7 * k - bh);
        var leftEdge = (float)(artRect.X + 7 * k);
        var rightEdge = (float)(artRect.Right - 7 * k);

        // ratings — bottom-left, stacked vertically (PEGI above ESRB)
        var stackGap = Math.Max(1f, 2f * k);
        if (g.StickerEsrb && stickers.TryGetValue("esrb", out var esrb))
        {
            var w = bh * esrb.SizeInPixels.Width / esrb.SizeInPixels.Height;
            DrawBadge(ds, esrb, new Rect(leftEdge, y, w, bh));
        }
        if (g.StickerPegi18 && stickers.TryGetValue("pegi18", out var pegi))
        {
            var w = bh * pegi.SizeInPixels.Width / pegi.SizeInPixels.Height;
            var py = g.StickerEsrb && stickers.ContainsKey("esrb")
                ? y - bh - stackGap   // directly above ESRB
                : y;                  // alone — sits at the bottom
            DrawBadge(ds, pegi, new Rect(leftEdge, py, w, bh));
        }

        // PC CD-ROM logo — bottom-right corner
        if (g.StickerPcCdrom && stickers.TryGetValue("pccdrom", out var pc))
        {
            var w = bh * pc.SizeInPixels.Width / pc.SizeInPixels.Height;
            DrawBadge(ds, pc, new Rect(rightEdge - w, y, w, bh));
        }

        // centered group — quality seal + DVD
        var center = new List<CanvasBitmap>(2);
        if (g.StickerSeal && stickers.TryGetValue("seal", out var seal)) center.Add(seal);
        if (g.StickerDvd && stickers.TryGetValue("dvd", out var dvd)) center.Add(dvd);
        if (center.Count > 0)
        {
            var tw = 0f;
            foreach (var b in center) tw += bh * b.SizeInPixels.Width / b.SizeInPixels.Height;
            tw += gap * (center.Count - 1);
            var xc = (float)(artRect.X + artRect.Width / 2 - tw / 2);
            foreach (var b in center)
            {
                var w = bh * b.SizeInPixels.Width / b.SizeInPixels.Height;
                DrawBadge(ds, b, new Rect(xc, y, w, bh));
                xc += w + gap;
            }
        }
    }

    /// <summary>Draw a single badge with a soft drop shadow.</summary>
    private static void DrawBadge(CanvasDrawingSession ds, CanvasBitmap bmp, Rect dest)
    {
        var src = new Rect(0, 0, bmp.SizeInPixels.Width, bmp.SizeInPixels.Height);
        try
        {
            // Inflate source/dest so the shadow's blurred bleed isn't clipped —
            // the badge itself still lands exactly on `dest`.
            var blur = Math.Max(1f, (float)dest.Height * 0.06f);
            var pad = blur * 4f;
            var srcPadX = (float)(pad * src.Width / dest.Width);
            var srcPadY = (float)(pad * src.Height / dest.Height);
            var srcInf = new Rect(-srcPadX, -srcPadY,
                src.Width + 2 * srcPadX, src.Height + 2 * srcPadY);
            var dstInf = new Rect(dest.X - pad, dest.Y - pad,
                dest.Width + 2 * pad, dest.Height + 2 * pad);
            using var fx = new ShadowEffect
            {
                Source = bmp,
                ShadowColor = Rgba(0, 0, 0, 140),
                BlurAmount = blur,
            };
            ds.DrawImage(fx, dstInf, srcInf, 1f, CanvasImageInterpolation.HighQualityCubic);
        }
        catch { }
        // ShadowEffect emits only the shadow — the badge itself must be
        // drawn afterwards or it renders as a faint dark box.
        ds.DrawImage(bmp, dest, src, 1f, CanvasImageInterpolation.HighQualityCubic);
    }

    /// <summary>
    /// Zero-crop pipeline:
    ///   1. transparent 1:1 canvas
    ///   2. art rect = centered 2:3 (pillarbox, e.g. 170x256 @ x=43), or the
    ///      FULL canvas when the game opted into "Stretch to Square"
    ///   3. ENTIRE poster stretched into that rect — no center crop, ever
    ///   4. platform template PNG drawn over the same rect (transparent window)
    ///   5. surface filter blended on top
    ///   6. bottom sticker row
    /// </summary>
    private static async Task<byte[]> RenderFrameWin2DAsync(
        CanvasDevice device, CanvasBitmap bmp, Game g, int size,
        Dictionary<string, CanvasBitmap>? stickers, CanvasBitmap? template,
        CanvasBitmap? noise)
    {
        using var rt = new CanvasRenderTarget(device, size, size, 96);
        using (var ds = rt.CreateDrawingSession())
        {
            ds.Clear(Color.FromArgb(0, 0, 0, 0));
            ds.Antialiasing = CanvasAntialiasing.Antialiased;
            ds.TextAntialiasing = CanvasTextAntialiasing.Auto;
            ds.Blend = CanvasBlend.SourceOver;

            var sw = (float)bmp.SizeInPixels.Width;
            var sh = (float)bmp.SizeInPixels.Height;
            // Stretch mode fills the whole square; default keeps the 2:3 pillarbox.
            var artW = g.StretchSquare ? size : Math.Max(1, (int)Math.Round(size * 2.0 / 3.0));
            var artRect = new Rect((size - artW) / 2.0, 0, artW, size);
            var radius = (float)(artW / 12.0);
            var k = size / 256f; // decorations scale with frame size
            var srcFull = new Rect(0, 0, sw, sh);   // never crop the source

            using var artGeom = CanvasGeometry.CreateRoundedRectangle(device, artRect, radius, radius);
            using (var layer = ds.CreateLayer(1f, artGeom))
            {
                // the whole poster squishes into the 2:3 box
                ds.DrawImage(bmp, artRect, srcFull, 1f, CanvasImageInterpolation.HighQualityCubic);

                // platform case template — its center is transparent so the
                // art shows through the window
                if (template is not null)
                {
                    var tsrc = new Rect(0, 0, template.SizeInPixels.Width, template.SizeInPixels.Height);
                    ds.DrawImage(template, artRect, tsrc, 1f, CanvasImageInterpolation.HighQualityCubic);
                }

                PaintSurfaceFilter(ds, g.Surface, artRect, noise, k);
            }

            ds.DrawGeometry(artGeom, Rgba(255, 255, 255, 36), Math.Max(1f, 1.5f * k));

            // case badges — physical-box bottom row
            PaintStickers(ds, stickers, g, artRect, k);
        }
        return await EncodePngAsync(rt.GetPixelBytes(), (uint)size, (uint)size);
    }

    // ---------------- platform templates ----------------

    private static string? FindTemplatesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; dir is not null && i < 8; i++, dir = dir.Parent)
        {
            var direct = Path.Combine(dir.FullName, "Assets", "Templates");
            if (Directory.Exists(direct)) return direct;
            var proj = Path.Combine(dir.FullName, "GameIconsStudio", "Assets", "Templates");
            if (Directory.Exists(proj)) return proj;
        }
        return null;
    }

    /// <summary>WIC-decode a template PNG to premultiplied BGRA — same path
    /// as the sticker loader so alpha is always correct. Custom templates
    /// resolve from the LocalAppData templates dir.</summary>
    private static async Task<CanvasBitmap?> LoadTemplateBitmapAsync(
        CanvasDevice device, Game g)
    {
        var custom = g.Platform == PlatformTemplate.Custom;
        var file = custom ? g.CustomTemplate : PlatformAssets.FileFor(g.Platform);
        var dir = custom ? GameChoicesService.CustomTemplatesDir
                         : file is null ? null : FindTemplatesDir();
        if (string.IsNullOrEmpty(file) || dir is null) { Log("templates dir not found"); return null; }
        var path = Path.Combine(dir, file);
        if (!File.Exists(path)) { Log($"template missing: {path}"); return null; }
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            using var ras = new InMemoryRandomAccessStream();
            await ras.WriteAsync(bytes.AsBuffer());
            ras.Seek(0);
            var dec = await BitmapDecoder.CreateAsync(ras);
            var pd = await dec.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);
            return CanvasBitmap.CreateFromBytes(device, pd.DetachPixelData(),
                (int)dec.PixelWidth, (int)dec.PixelHeight,
                Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized);
        }
        catch (Exception ex) { Log($"template {file}: {ex.Message}"); return null; }
    }

    // ---------------- surface filters ----------------

    /// <summary>48x48 mono noise tile for the Matte finish.</summary>
    private static CanvasBitmap CreateNoiseBitmap(CanvasDevice device)
    {
        const int n = 48;
        var px = new byte[n * n * 4];
        var rng = new Random(0x5EED);
        for (var i = 0; i < px.Length; i += 4)
        {
            var v = (byte)rng.Next(140, 256);          // light gray speckle
            var a = (byte)rng.Next(0, 22);             // very low alpha
            px[i] = px[i + 1] = px[i + 2] = (byte)(v * a / 255); // premultiplied
            px[i + 3] = a;
        }
        return CanvasBitmap.CreateFromBytes(device, px, n, n,
            Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized);
    }

    /// <summary>
    /// Surface finish painted inside the art clip (order: art → template →
    /// this). All effects are low-alpha so the cover stays readable.
    /// </summary>
    private static void PaintSurfaceFilter(CanvasDrawingSession ds,
        SurfaceFilter filter, Rect artRect, CanvasBitmap? noise, float k)
    {
        switch (filter)
        {
            case SurfaceFilter.Glossy:
            {
                // plastic glare — soft white diagonal from the top corner
                using var glare = new CanvasLinearGradientBrush(ds,
                [
                    new CanvasGradientStop { Position = 0f, Color = Rgba(255, 255, 255, 130) },
                    new CanvasGradientStop { Position = 0.3f, Color = Rgba(255, 255, 255, 46) },
                    new CanvasGradientStop { Position = 0.58f, Color = Rgba(255, 255, 255, 0) },
                ]);
                glare.StartPoint = new Vector2((float)artRect.X, (float)artRect.Y);
                glare.EndPoint = new Vector2((float)artRect.Right, (float)(artRect.Y + artRect.Height * 0.85));
                ds.FillRectangle(artRect, glare);
                break;
            }
            case SurfaceFilter.Matte:
            {
                // matte paper — desaturating wash + faint speckled tooth
                ds.FillRectangle(artRect, Rgba(200, 200, 200, 14));
                if (noise is not null)
                {
                    using var brush = new CanvasImageBrush(ds, noise)
                    {
                        ExtendX = CanvasEdgeBehavior.Wrap,
                        ExtendY = CanvasEdgeBehavior.Wrap,
                        Interpolation = CanvasImageInterpolation.NearestNeighbor,
                        Opacity = 0.9f,
                    };
                    var scale = Math.Max(1f, 3f * k);
                    brush.Transform = Matrix3x2.CreateScale(scale, scale) *
                        Matrix3x2.CreateTranslation((float)artRect.X, (float)artRect.Y);
                    ds.FillRectangle(artRect, brush);
                }
                break;
            }
            case SurfaceFilter.Holographic:
            {
                // foil rainbow — heavily transparent so the art stays dominant
                using var holo = new CanvasLinearGradientBrush(ds,
                [
                    new CanvasGradientStop { Position = 0f, Color = Rgba(255, 90, 90, 52) },
                    new CanvasGradientStop { Position = 0.18f, Color = Rgba(255, 200, 90, 46) },
                    new CanvasGradientStop { Position = 0.34f, Color = Rgba(255, 244, 90, 44) },
                    new CanvasGradientStop { Position = 0.5f, Color = Rgba(90, 255, 140, 46) },
                    new CanvasGradientStop { Position = 0.66f, Color = Rgba(90, 200, 255, 44) },
                    new CanvasGradientStop { Position = 0.82f, Color = Rgba(140, 90, 255, 46) },
                    new CanvasGradientStop { Position = 1f, Color = Rgba(255, 90, 200, 52) },
                ]);
                holo.StartPoint = new Vector2((float)artRect.X, (float)artRect.Y);
                holo.EndPoint = new Vector2((float)artRect.Right, (float)artRect.Bottom);
                ds.FillRectangle(artRect, holo);
                break;
            }
            case SurfaceFilter.Vintage:
            {
                // worn jacket — dark vignette + a few grunge scratches
                using var vig = new CanvasRadialGradientBrush(ds,
                [
                    new CanvasGradientStop { Position = 0.5f, Color = Rgba(26, 18, 8, 10) },
                    new CanvasGradientStop { Position = 1f, Color = Rgba(0, 0, 0, 100) },
                ]);
                vig.Center = new Vector2((float)(artRect.X + artRect.Width / 2),
                    (float)(artRect.Y + artRect.Height / 2));
                vig.RadiusX = (float)(artRect.Width * 0.75);
                vig.RadiusY = (float)(artRect.Height * 0.62);
                ds.FillRectangle(artRect, vig);

                var rng = new Random(0xC0FFEE);
                var scratchColor = Rgba(10, 8, 5, 60);
                for (var i = 0; i < 7; i++)
                {
                    // short dark strokes clustered near the edges
                    var nearVertical = rng.Next(2) == 0;
                    float x, y, len, ang;
                    if (nearVertical)
                    {
                        x = (float)(artRect.X + artRect.Width * (rng.NextDouble() < 0.5 ? rng.NextDouble() * 0.14 : 0.86 + rng.NextDouble() * 0.14));
                        y = (float)(artRect.Y + artRect.Height * rng.NextDouble());
                        len = (float)(artRect.Height * (0.06 + rng.NextDouble() * 0.12));
                        ang = (float)((rng.NextDouble() - 0.5) * 0.5);
                    }
                    else
                    {
                        x = (float)(artRect.X + artRect.Width * rng.NextDouble());
                        y = (float)(artRect.Y + artRect.Height * (rng.NextDouble() < 0.5 ? rng.NextDouble() * 0.12 : 0.88 + rng.NextDouble() * 0.12));
                        len = (float)(artRect.Width * (0.08 + rng.NextDouble() * 0.14));
                        ang = (float)(Math.PI / 2 + (rng.NextDouble() - 0.5) * 0.5);
                    }
                    var dx = (float)Math.Sin(ang) * len;
                    var dy = (float)Math.Cos(ang) * len;
                    ds.DrawLine(new Vector2(x, y), new Vector2(x + dx, y + dy),
                        scratchColor, Math.Max(1f, 1.4f * k));
                }
                break;
            }
        }
    }

    private static async Task<byte[]> EncodePngAsync(byte[] px, uint w, uint h)
    {
        using var output = new InMemoryRandomAccessStream();
        var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        enc.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, w, h, 96, 96, px);
        await enc.FlushAsync();
        output.Seek(0);
        using var ms = new MemoryStream();
        await output.AsStreamForRead().CopyToAsync(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Software fallback (no Win2D device): same zero-crop pipeline — the
    /// entire poster is box-resampled into a centered 2:3 rect (or the full
    /// square in stretch mode) on a transparent canvas. Platform templates /
    /// surface filters are Win2D-only decorations; the fallback still
    /// produces a correct icon.
    /// </summary>
    private static async Task<byte[]> RenderFrameAsync(byte[] imageBytes, int size, bool stretchSquare)
    {
        using var input = new InMemoryRandomAccessStream();
        await input.WriteAsync(imageBytes.AsBuffer());
        input.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(input);

        var srcW = (int)decoder.PixelWidth;
        var srcH = (int)decoder.PixelHeight;
        var artH = size;
        var artW = stretchSquare ? size : Math.Max(1, (int)Math.Round(size * 2.0 / 3.0));

        // Full source — no Bounds crop, ever.
        var pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        var px = BoxDownscale(pixelData.DetachPixelData(), srcW, srcH, artW, artH);

        var canvas = new byte[size * size * 4]; // transparent
        var x0 = (size - artW) / 2;
        for (var y = 0; y < artH; y++)
            System.Buffer.BlockCopy(px, y * artW * 4, canvas, (y * size + x0) * 4, artW * 4);
        ApplyRoundedCorners(canvas, size, x0, 0, artW, artH, Math.Max(2, artW / 14));

        using var output = new InMemoryRandomAccessStream();
        var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        enc.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)size, (uint)size, 96, 96, canvas);
        await enc.FlushAsync();
        output.Seek(0);
        using var ms = new MemoryStream();
        await output.AsStreamForRead().CopyToAsync(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Area-average (box) downscale of BGRA premultiplied pixels — the right
    /// filter for shrinking covers into icon sizes. Falls back to nearest for
    /// any accidental upscale.
    /// </summary>
    private static byte[] BoxDownscale(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        var dst = new byte[dstW * dstH * 4];
        if (dstW >= srcW || dstH >= srcH)
        {
            for (var y = 0; y < dstH; y++)
            {
                var sy = Math.Min(srcH - 1, y * srcH / dstH);
                for (var x = 0; x < dstW; x++)
                {
                    var sx = Math.Min(srcW - 1, x * srcW / dstW);
                    var si = (sy * srcW + sx) * 4;
                    var di = (y * dstW + x) * 4;
                    dst[di] = src[si]; dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2]; dst[di + 3] = src[si + 3];
                }
            }
            return dst;
        }

        for (var dy = 0; dy < dstH; dy++)
        {
            var sy0 = dy * srcH / dstH;
            var sy1 = Math.Max(sy0 + 1, (dy + 1) * srcH / dstH);
            for (var dx = 0; dx < dstW; dx++)
            {
                var sx0 = dx * srcW / dstW;
                var sx1 = Math.Max(sx0 + 1, (dx + 1) * srcW / dstW);
                long b = 0, g = 0, r = 0, a = 0, n = 0;
                for (var sy = sy0; sy < sy1; sy++)
                {
                    for (var sx = sx0; sx < sx1; sx++)
                    {
                        var i = (sy * srcW + sx) * 4;
                        b += src[i]; g += src[i + 1]; r += src[i + 2]; a += src[i + 3];
                        n++;
                    }
                }
                var di = (dy * dstW + dx) * 4;
                dst[di] = (byte)(b / n); dst[di + 1] = (byte)(g / n);
                dst[di + 2] = (byte)(r / n); dst[di + 3] = (byte)(a / n);
            }
        }
        return dst;
    }

    /// <summary>Zero out pixels outside the corner arcs (premultiplied => all 4 bytes = 0).</summary>
    private static void ApplyRoundedCorners(byte[] px, int stride, int x0, int y0, int w, int h, int r)
    {
        for (var cy = 0; cy < r; cy++)
        {
            for (var cx = 0; cx < r; cx++)
            {
                var dx = r - cx - 1;
                var dy = r - cy - 1;
                if (dx * dx + dy * dy <= r * r) continue;
                ClearPixel(px, stride, x0 + cx, y0 + cy);
                ClearPixel(px, stride, x0 + w - 1 - cx, y0 + cy);
                ClearPixel(px, stride, x0 + cx, y0 + h - 1 - cy);
                ClearPixel(px, stride, x0 + w - 1 - cx, y0 + h - 1 - cy);
            }
        }
    }

    private static void ClearPixel(byte[] px, int stride, int x, int y)
    {
        var i = (y * stride + x) * 4;
        px[i] = px[i + 1] = px[i + 2] = px[i + 3] = 0;
    }

    // ---------------- apply to Windows ----------------

    private static void ApplyIcon(Game g, string icoPath)
    {
        if (g.IsFolderTarget) ApplyFolderIcon(g.ApplyPath, icoPath);
        else SetShortcutIcon(g.ApplyPath, icoPath);
        NotifyItemChanged(g.ApplyPath);
    }

    /// <summary>Overwrite a .lnk's IconLocation via IShellLinkW and save.</summary>
    private static void SetShortcutIcon(string lnkPath, string icoPath)
    {
        try
        {
            // Shell COM objects are kept on a dedicated STA thread.
            RunOnSta(() =>
            {
                using var lnk = ShellShortcut.Open(lnkPath);
                lnk.IconLocation = $"{icoPath},0";
                lnk.Save();
            });
        }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            SaveShortcutElevated(lnkPath, lnk => lnk.IconLocation = $"{icoPath},0");
        }
    }

    /// <summary>
    /// Per-operation elevation for .lnk files in admin-only locations
    /// (e.g. Public Desktop): rebuild the modified shortcut in %TEMP%, then
    /// a one-shot elevated `copy` puts it back. UAC fires only at this
    /// moment — the app itself stays unelevated so drag-and-drop works.
    /// </summary>
    private static void SaveShortcutElevated(string lnkPath, Action<ShellShortcut> mutate)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"BoxArtStudio_{Guid.NewGuid():N}.lnk");
        try
        {
            // Read access is enough to copy the protected .lnk out; mutate
            // and save the temp copy so any Save() inside `mutate` writes
            // to a user-writable path.
            File.Copy(lnkPath, tempPath, overwrite: true);
            RunOnSta(() =>
            {
                using var lnk = ShellShortcut.Open(tempPath);
                mutate(lnk);
                lnk.Save();
            });

            Process proc;
            try
            {
                proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c copy /Y \"{tempPath}\" \"{lnkPath}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                }) ?? throw new IOException("Elevated copy could not be started.");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                throw new UnauthorizedAccessException("Elevation declined by the user.", ex);
            }

            proc.WaitForExit();
            if (proc.ExitCode != 0)
                throw new IOException($"Elevated copy failed (exit code {proc.ExitCode})");
            Log($"lnk saved via elevation: {lnkPath}");
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    /// <summary>Access-denied from either the file system or COM HRESULT.</summary>
    private static bool IsAccessDenied(Exception ex) =>
        ex is UnauthorizedAccessException ||
        ex is COMException { HResult: unchecked((int)0x80070005) or unchecked((int)0x80030005) };

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        t.Join();
        if (error is not null) throw error;
    }

    /// <summary>
    /// desktop.ini approach — the four rules Windows demands:
    ///   1. the TARGET FOLDER carries ReadOnly (Explorer ignores the ini without it)
    ///   2. an existing ini is stripped of Hidden/System/ReadOnly before any write
    ///   3. content: [.ShellClassInfo] + IconResource=<abs ico>,0 — written as
    ///      UTF-16 LE w/ BOM (the encoding Explorer reliably parses; UTF-8
    ///      without BOM is silently ignored on many builds)
    ///   4. the ini itself is re-flagged Hidden+System afterwards
    /// </summary>
    private static void ApplyFolderIcon(string folder, string icoPath)
    {
        try
        {
            var ini = Path.Combine(folder, "desktop.ini");

            // Rule 2: strip attributes on a pre-existing ini before writing.
            if (File.Exists(ini))
            {
                File.SetAttributes(ini, FileAttributes.Normal);
                Log($"folder-ini stripped attrs: {ini}");
            }

            var lines = File.Exists(ini)
                ? File.ReadAllLines(ini).ToList()
                : new List<string>();

            var shellIdx = lines.FindIndex(l => l.Trim() == "[.ShellClassInfo]");
            if (shellIdx < 0)
            {
                lines.Add("[.ShellClassInfo]");
                shellIdx = lines.Count - 1;
            }

            // Remove previous icon keys inside that section, then re-add ours.
            var insert = shellIdx + 1;
            for (var i = insert; i < lines.Count && !lines[i].StartsWith("[", StringComparison.Ordinal); i++)
            {
                if (lines[i].StartsWith("IconResource", StringComparison.Ordinal) ||
                    lines[i].StartsWith("IconFile", StringComparison.Ordinal) ||
                    lines[i].StartsWith("IconIndex", StringComparison.Ordinal))
                {
                    lines.RemoveAt(i);
                    i--;
                }
            }
            lines.Insert(insert, $"IconResource={icoPath},0");
            lines.Insert(insert + 1, $"IconFile={icoPath}");
            lines.Insert(insert + 2, "IconIndex=0");

            // Rule 3: UTF-16 LE with BOM — the safest desktop.ini encoding.
            File.WriteAllLines(ini, lines, Encoding.Unicode);
            Log($"folder-ini written: {ini} -> {icoPath}");

            // Rule 4: ini is Hidden + System.
            File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);

            // Rule 1: the FOLDER itself must carry ReadOnly (or System) —
            // this is the switch that makes Explorer consult the ini.
            new DirectoryInfo(folder).Attributes |= FileAttributes.ReadOnly;
            Log($"folder attrs set ReadOnly: {folder} -> {new DirectoryInfo(folder).Attributes}");

            // Nudge the parent dir listing too so the icon repaints now.
            var parent = Path.GetDirectoryName(folder);
            if (parent is not null) NotifyDirChanged(parent);
        }
        catch (Exception ex)
        {
            Log($"folder-ini FAIL {folder}: {ex}");
            throw;
        }
    }

    /// <summary>
    /// Revert a custom icon: folder targets lose desktop.ini + the ReadOnly
    /// attribute; .lnk targets get an empty IconLocation. Explorer is
    /// refreshed so the stock icon repaints immediately.
    /// </summary>
    public Task RemoveIconAsync(Game g, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            Log($"REMOVE {g.Name} target={g.ApplyPath} folder={g.IsFolderTarget}");
            try
            {
                if (g.IsFolderTarget) RemoveFolderIcon(g.ApplyPath);
                else ClearShortcutIcon(g.ApplyPath);
            }
            catch (Exception ex)
            {
                Log($"REMOVE FAIL {g.ApplyPath}: {ex}");
                throw;
            }

            NotifyItemChanged(g.ApplyPath);
            var parent = Path.GetDirectoryName(g.ApplyPath);
            if (parent is not null) NotifyDirChanged(parent);
            NotifyShellIconsChanged();
            RunIe4UInit("-ClearIconCache");
            RunIe4UInit("-show");
            Log($"REMOVE done: {g.Name}");
        }, ct);

    /// <summary>Delete desktop.ini and drop ReadOnly from the folder.</summary>
    private static void RemoveFolderIcon(string folder)
    {
        try
        {
            var ini = Path.Combine(folder, "desktop.ini");
            if (File.Exists(ini))
            {
                // Rule: strip Hidden/System/ReadOnly BEFORE deleting — a plain
                // File.Delete on a flagged ini throws UnauthorizedAccessException.
                File.SetAttributes(ini, FileAttributes.Normal);
                File.Delete(ini);
                Log($"folder-ini deleted: {ini}");
            }
            var di = new DirectoryInfo(folder);
            di.Attributes &= ~FileAttributes.ReadOnly;
            Log($"folder icon removed: {folder} -> {new DirectoryInfo(folder).Attributes}");
        }
        catch (Exception ex)
        {
            Log($"folder-ini REMOVE FAIL {folder}: {ex}");
            throw;
        }
    }

    /// <summary>
    /// Revert a .lnk to its target's default icon. An empty IconLocation may
    /// be rejected, so the reliable "remove" is pointing it back at the
    /// shortcut's own target executable.
    /// </summary>
    private static void ClearShortcutIcon(string lnkPath)
    {
        try
        {
            RunOnSta(() =>
            {
                using var lnk = ShellShortcut.Open(lnkPath);
                ClearIconLocation(lnk, lnkPath);
            });
        }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            SaveShortcutElevated(lnkPath, lnk => ClearIconLocation(lnk, lnkPath));
        }
    }

    private static void ClearIconLocation(ShellShortcut lnk, string lnkPath)
    {
        var target = lnk.TargetPath;

        // The setter validates the value: "" may throw E_INVALIDARG, and
        // URL-style shortcuts have an empty TargetPath. Try the cleanest
        // revert first, then degrade.
        var attempts = new List<string>
        {
            "",                                                   // true clear
            $"{target},0",                                        // target's own icon
        };
        if (string.IsNullOrWhiteSpace(target))
            attempts.Add(@"%SystemRoot%\system32\shell32.dll,0"); // stock document icon

        Exception? lastError = null;
        foreach (var attempt in attempts)
        {
            try
            {
                lnk.IconLocation = attempt;
                lnk.Save();
                Log($"lnk icon reverted: {lnkPath} -> '{attempt}'");
                return;
            }
            catch (Exception ex) { lastError = ex; }
        }
        throw lastError ?? new InvalidOperationException("IconLocation could not be cleared.");
    }

    // ---------------- shell refresh ----------------

    private static void NotifyShellIconsChanged()
    {
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Immediate per-path refresh so Explorer repaints without waiting.</summary>
    private static void NotifyItemChanged(string path)
    {
        var ptr = Marshal.StringToHGlobalUni(path);
        try { SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, ptr, IntPtr.Zero); }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    /// <summary>Notify that a directory's contents changed (repaints its listing).</summary>
    private static void NotifyDirChanged(string path)
    {
        var ptr = Marshal.StringToHGlobalUni(path);
        try { SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, ptr, IntPtr.Zero); }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    /// <summary>
    /// Gentle refresh: SHCNE_ASSOCCHANGED broadcast + ie4uinit -show.
    /// Does not restart Explorer or delete cache files.
    /// </summary>
    public Task RefreshIconsAsync(CancellationToken ct = default) =>
        Task.Run(() =>
        {
            NotifyShellIconsChanged();
            RunIe4UInit();
        }, ct);

    private static void RunIe4UInit(string args = "-show")
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ie4uinit.exe",
                Arguments = args,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            });
        }
        catch { /* ie4uinit missing is non-fatal */ }
    }

    /// <summary>
    /// Full icon-cache repair: shell notify, delete iconcache*.db under
    /// %LocalAppData%\Microsoft\Windows\Explorer (plus the legacy
    /// IconCache.db), then restart Explorer so Windows regenerates them.
    /// Only cache DBs are deleted — never user data.
    /// </summary>
    public Task<int> RepairIconCacheAsync(bool restartExplorer, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var deleted = 0;
            NotifyShellIconsChanged();

            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var explorerCache = Path.Combine(localApp, @"Microsoft\Windows\Explorer");
            if (Directory.Exists(explorerCache))
            {
                foreach (var f in Directory.EnumerateFiles(explorerCache, "iconcache*.db"))
                {
                    try { File.Delete(f); deleted++; } catch { /* locked while Explorer runs */ }
                }
            }
            var legacy = Path.Combine(localApp, "IconCache.db");
            if (File.Exists(legacy)) { try { File.Delete(legacy); deleted++; } catch { } }

            if (restartExplorer)
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName("explorer")) p.Kill();
                    Thread.Sleep(400);
                    // retry deletion while the locks are released
                    if (Directory.Exists(explorerCache))
                        foreach (var f in Directory.EnumerateFiles(explorerCache, "iconcache*.db"))
                        {
                            try { File.Delete(f); deleted++; } catch { }
                        }
                    Thread.Sleep(300);
                    if (Process.GetProcessesByName("explorer").Length == 0)
                        Process.Start("explorer.exe");
                }
                catch
                {
                    try { Process.Start("explorer.exe"); } catch { }
                }
            }
            else
            {
                RunIe4UInit();
            }
            return deleted;
        }, ct);

    // ---------------- cover loading ----------------

    private async Task<byte[]?> LoadCoverBytesAsync(Game g, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(g.Cover)) return null;
        if (!Uri.TryCreate(g.Cover, UriKind.Absolute, out var uri)) return null;
        try
        {
            switch (uri.Scheme)
            {
                case "file":
                    return await File.ReadAllBytesAsync(uri.LocalPath, ct);
                case "ms-appx":
                    var rel = uri.LocalPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                    return await File.ReadAllBytesAsync(
                        Path.Combine(AppContext.BaseDirectory, rel), ct);
                default:
                    return await _http.GetByteArrayAsync(uri, ct);
            }
        }
        catch { return null; }
    }

    // ---------------- ICO writer ----------------

    /// <summary>
    /// Write a multi-size PNG-compressed ICO container. Caller supplies
    /// already-rendered PNG frames; 256px uses dimension byte 0.
    /// </summary>
    public static void WriteIco(string outputPath, IReadOnlyList<(int Size, byte[] Png)> frames)
    {
        if (frames.Count == 0) throw new ArgumentException("At least one frame is required.", nameof(frames));

        using var fs = File.Open(outputPath, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);

        w.Write((ushort)0);              // reserved
        w.Write((ushort)1);              // type: icon
        w.Write((ushort)frames.Count);

        var offset = 6 + 16 * frames.Count;
        foreach (var (size, png) in frames)
        {
            var dim = size >= 256 ? (byte)0 : (byte)size;
            w.Write(dim);                // width
            w.Write(dim);                // height
            w.Write((byte)0);            // palette
            w.Write((byte)0);            // reserved
            w.Write((ushort)1);          // planes
            w.Write((ushort)32);         // bpp
            w.Write((uint)png.Length);   // bytes in resource
            w.Write((uint)offset);       // image offset
            offset += png.Length;
        }
        foreach (var (_, png) in frames) w.Write(png);
    }

    /// <summary>Valid ICO magic bytes: 00 00 01 00.</summary>
    public static bool IsIcoFile(string path)
    {
        try
        {
            var head = new byte[4];
            using var fs = File.OpenRead(path);
            return fs.Read(head, 0, 4) == 4 && head[0] == 0 && head[1] == 0 && head[2] == 1 && head[3] == 0;
        }
        catch { return false; }
    }

    /// <summary>Default size ladder used for generated icons.</summary>
    public static IReadOnlyList<int> DefaultIcoSizes => IcoSizes;

    public void Dispose() => _http.Dispose();
}
