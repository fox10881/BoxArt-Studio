using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameIconsStudio;
using GameIconsStudio.Services;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;

public static class Program
{
    private static void Sta(Action a)
    {
        var t = new System.Threading.Thread(() => a());
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start(); t.Join();
    }

    private static void CreateLnk(string p) => Sta(() =>
    {
        dynamic wsh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic l = wsh.CreateShortcut(p);
        l.TargetPath = @"C:\Windows\notepad.exe";
        l.Save();
    });

    private static string GetLnkIcon(string p)
    {
        var icon = "";
        Sta(() =>
        {
            dynamic wsh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic l = wsh.CreateShortcut(p);
            icon = (string)l.IconLocation;
        });
        return icon;
    }

    /// <summary>Extract the largest PNG frame from an .ico file.</summary>
    private static byte[] ExtractLargestPng(string icoPath)
    {
        var data = File.ReadAllBytes(icoPath);
        var count = BitConverter.ToUInt16(data, 4);
        int bestOff = 0, bestLen = 0, bestDim = 0;
        for (var i = 0; i < count; i++)
        {
            var e = 6 + i * 16;
            var dim = data[e] == 0 ? 256 : data[e];
            var len = BitConverter.ToInt32(data, e + 8);
            var off = BitConverter.ToInt32(data, e + 12);
            if (dim > bestDim) { bestDim = dim; bestLen = len; bestOff = off; }
        }
        var png = new byte[bestLen];
        Array.Copy(data, bestOff, png, 0, bestLen);
        return png;
    }

    private static async Task AnalyzeIcoAsync(string icoPath)
    {
        var png = ExtractLargestPng(icoPath);
        using var s = new InMemoryRandomAccessStream();
        await s.WriteAsync(png.AsBuffer());
        s.Seek(0);
        var dec = await BitmapDecoder.CreateAsync(s);
        var d = await dec.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        var px = d.DetachPixelData();
        int w = (int)dec.PixelWidth, h = (int)dec.PixelHeight;

        int minX = w, maxX = -1, minY = h, maxY = -1;
        int opaque = 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var a = px[(y * w + x) * 4 + 3];
                if (a > 0)
                {
                    opaque++;
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
            }
        var name = Path.GetFileName(icoPath);
        Console.WriteLine($"{name}: {w}x{h} opaque={opaque} bbox=({minX},{minY})-({maxX},{maxY}) art={maxX - minX + 1}x{maxY - minY + 1} marginLR={minX}/{w - 1 - maxX}");

        // sample: left pillar column should be fully transparent; center opaque
        var leftA = px[(h / 2 * w + 2) * 4 + 3];
        var ctrA = px[(h / 2 * w + w / 2) * 4 + 3];
        // top of art for banner detection (center column)
        var topI = (minY + 2) * w + w / 2;
        Console.WriteLine($"   leftpillar alpha={leftA} center alpha={ctrA} topArtPixel=BGRA({px[topI * 4]},{px[topI * 4 + 1]},{px[topI * 4 + 2]},{px[topI * 4 + 3]})");
    }

    /// <summary>Decode any WIC format, process pixels, write BGRA PNG.</summary>
    private static async Task BakeStickerAsync(string src, string dst, string mode)
    {
        var raw = File.ReadAllBytes(src);
        BitmapDecoder dec;
        using (var s = new InMemoryRandomAccessStream())
        {
            await s.WriteAsync(raw.AsBuffer());
            s.Seek(0);
            dec = await BitmapDecoder.CreateAsync(s);
            var pd = await dec.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            var px = pd.DetachPixelData();
            int w = (int)dec.PixelWidth, h = (int)dec.PixelHeight;

            if (mode == "emblem")
            {
                // dark ink -> opaque white emblem; white bg -> transparent
                for (var i = 0; i < px.Length; i += 4)
                {
                    var lum = Math.Max(px[i], Math.Max(px[i + 1], px[i + 2]));
                    var a = (byte)(255 - lum);
                    px[i] = px[i + 1] = px[i + 2] = px[i + 3] = a;
                }
            }
            else if (mode == "edgekey")
            {
                // remove the white *background* only (flood fill from borders);
                // enclosed whites inside the artwork are preserved
                var bg = new bool[w * h];
                var q = new Queue<int>();
                void Enq(int idx)
                {
                    if (bg[idx]) return;
                    var o = idx * 4;
                    if (px[o] > 240 && px[o + 1] > 240 && px[o + 2] > 240) { bg[idx] = true; q.Enqueue(idx); }
                }
                for (var x = 0; x < w; x++) { Enq(x); Enq((h - 1) * w + x); }
                for (var y = 0; y < h; y++) { Enq(y * w); Enq(y * w + w - 1); }
                while (q.Count > 0)
                {
                    var idx = q.Dequeue();
                    var x = idx % w; var y = idx / w;
                    if (x > 0) Enq(idx - 1);
                    if (x < w - 1) Enq(idx + 1);
                    if (y > 0) Enq(idx - w);
                    if (y < h - 1) Enq(idx + w);
                }
                for (var i = 0; i < bg.Length; i++)
                    if (bg[i]) { px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = 0; px[i * 4 + 3] = 0; }
            }

            using var output = new InMemoryRandomAccessStream();
            var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            enc.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                (uint)w, (uint)h, 96, 96, px);
            await enc.FlushAsync();
            output.Seek(0);
            await using var f = File.Create(dst);
            await output.AsStreamForRead().CopyToAsync(f);
            Console.WriteLine($"baked {Path.GetFileName(dst)} {w}x{h} mode={mode}");
        }
    }

    public static async Task<int> Main()
    {
        var stick = @"C:\BoxArt-Studio\GameIconsStudio\Assets\stickers";
        await BakeStickerAsync(Path.Combine(stick, "sticker_dvd.jpg"), Path.Combine(stick, "sticker_dvd.png"), "emblem");
        await BakeStickerAsync(Path.Combine(stick, "sticker_pccdrom.webp"), Path.Combine(stick, "sticker_pccdrom.png"), "emblem");
        await BakeStickerAsync(Path.Combine(stick, "sticker_seal.webp"), Path.Combine(stick, "sticker_seal.png"), "edgekey");
        await BakeStickerAsync(Path.Combine(stick, "sticker_esrb.png"), Path.Combine(stick, "sticker_esrb.png"), "keep");
        await BakeStickerAsync(Path.Combine(stick, "sticker_pegi18.jpg"), Path.Combine(stick, "sticker_pegi18.png"), "edgekey");

        var dir = Path.Combine(Path.GetTempPath(), "icotest");
        Directory.CreateDirectory(dir);
        var cover = @"C:\BoxArt-Studio\GameIconsStudio\Assets\covers\showcase-base.png";
        var coverUri = new Uri(cover).AbsoluteUri;
        var lnk = Path.Combine(dir, "X.lnk");
        CreateLnk(lnk);

        using var svc = new IconBuilderService();
        var games = new[]
        {
            new Game { Id = "v-none", Name = "None", Cover = coverUri, ApplyPath = lnk },
            new Game { Id = "v-ps5", Name = "PS5 Glossy", Cover = coverUri, ApplyPath = lnk,
                Platform = PlatformTemplate.Ps5Blue, Surface = SurfaceFilter.Glossy,
                StickerPcCdrom = true, StickerDvd = true, StickerEsrb = true, StickerSeal = true },
            new Game { Id = "v-xbox", Name = "Xbox Holo", Cover = coverUri, ApplyPath = lnk,
                Platform = PlatformTemplate.XboxSeries, Surface = SurfaceFilter.Holographic,
                StickerPcCdrom = true, StickerDvd = true, StickerEsrb = true, StickerSeal = true, StickerPegi18 = true },
            new Game { Id = "v-epic", Name = "Epic Vintage", Cover = coverUri, ApplyPath = lnk,
                Platform = PlatformTemplate.Epic, Surface = SurfaceFilter.Vintage,
                StickerPcCdrom = true, StickerDvd = true, StickerEsrb = true, StickerSeal = true, StickerPegi18 = true },
            new Game { Id = "v-matte", Name = "GFW Matte", Cover = coverUri, ApplyPath = lnk,
                Platform = PlatformTemplate.GfwModern, Surface = SurfaceFilter.Matte },
            new Game { Id = "v-win11", Name = "Win11 Glossy", Cover = coverUri, ApplyPath = lnk,
                Platform = PlatformTemplate.Windows11, Surface = SurfaceFilter.Glossy,
                StickerPcCdrom = true, StickerSeal = true },
            new Game { Id = "v-stretch", Name = "PS5 Square", Cover = coverUri, ApplyPath = lnk,
                Platform = PlatformTemplate.Ps5Black, Surface = SurfaceFilter.Glossy,
                StretchSquare = true,
                StickerPcCdrom = true, StickerDvd = true, StickerEsrb = true, StickerSeal = true },
        };

        // Custom-template path: copy a PNG into the LocalAppData templates
        // dir and render a game using it, plus a round-trip save.
        var customFile = $"custom_template_{Guid.NewGuid():N}.png";
        var customDest = Path.Combine(GameChoicesService.CustomTemplatesDir, customFile);
        File.Copy(@"C:\BoxArt-Studio\GameIconsStudio\Assets\Templates\template_ps5_blue.png", customDest);
        var customGame = new Game { Id = "v-custom", Name = "Custom Tpl", Cover = coverUri, ApplyPath = lnk,
            Platform = PlatformTemplate.Custom, CustomTemplate = customFile, Surface = SurfaceFilter.Glossy };
        var customList = new[] { customGame }.Concat(games).ToArray();
        var r = await svc.BuildIconsAsync(customList);
        Console.WriteLine($"applied={r.Applied} failed={r.Failed}");

        var iconsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameIconsStudio", "icons");
        foreach (var g in customList)
        {
            var ico = Path.Combine(iconsDir, $"{g.Id}.ico");
            await AnalyzeIcoAsync(ico);
            File.WriteAllBytes(Path.Combine(dir, $"{g.Id}.png"), ExtractLargestPng(ico));
        }
        Console.WriteLine($"frames dumped to {dir}");

        // ---- choices.json round-trip ----
        var cg = new Game { Id = "choice", Name = "Choice Game", SearchName = "Choice Game",
            Cover = coverUri, Platform = PlatformTemplate.SteamBlue,
            Surface = SurfaceFilter.Holographic, StretchSquare = true, SgdbGameId = 4242,
            StickerPcCdrom = true, StickerDvd = true, StickerPegi18 = true };
        GameChoicesService.Save(cg);
        var found = GameChoicesService.Find(cg);
        Console.WriteLine($"choices: found={found is not null} url={found?.Url} tpl={found?.PlatformTemplate} " +
            $"surf={found?.SurfaceFilter} stretch={found?.IsStretchedToSquare} sgdb={found?.SgdbGameId} " +
            $"stickers pc={found?.HasPcCdrom} dvd={found?.HasDvd} esrb={found?.HasEsrb} pegi={found?.HasPegi18} seal={found?.HasSeal}");
        // Custom template persists as its file name in PlatformTemplate.
        GameChoicesService.Save(customGame);
        var foundCustom = GameChoicesService.Find(customGame);
        var parsed = GameChoicesService.ParsePlatform(foundCustom?.PlatformTemplate);
        Console.WriteLine($"custom choice: tpl={foundCustom?.PlatformTemplate} parsed={parsed}");
        GameChoicesService.Forget(customGame);
        File.Delete(customDest);

        GameChoicesService.Forget(cg);
        Console.WriteLine($"choices after forget: found={GameChoicesService.Find(cg) is not null}");
        var choicesFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameIconsStudio", "choices.json");
        Console.WriteLine($"choices.json exists={File.Exists(choicesFile)}");

        // ---- removal test: folder + shortcut ----
        var folderGame = new Game
        {
            Id = "rm", Name = "Rm", Cover = coverUri,
            ApplyPath = Path.Combine(dir, "RmFolder"), IsFolderTarget = true,
        };
        Directory.CreateDirectory(folderGame.ApplyPath);
        await svc.BuildIconsAsync(new[] { folderGame });
        await svc.RemoveIconAsync(folderGame);
        Console.WriteLine($"folder: ini-exists-after-remove={File.Exists(Path.Combine(folderGame.ApplyPath, "desktop.ini"))} " +
            $"readonly={(new DirectoryInfo(folderGame.ApplyPath).Attributes & FileAttributes.ReadOnly) != 0}");
        await svc.RemoveIconAsync(games[0]);
        Console.WriteLine($"lnk IconLocation after remove: '{GetLnkIcon(lnk)}'");

        // ---- Arabic-path test: .lnk inside a Unicode folder ----
        var arabDir = Path.Combine(dir, "بعد");
        Directory.CreateDirectory(arabDir);
        var arabLnk = Path.Combine(arabDir, "Arabic Game.lnk");
        using (var s = GameIconsStudio.Services.ShellShortcut.Create())
        {
            s.TargetPath = @"C:\Windows\notepad.exe";
            s.SaveAs(arabLnk);
        }
        var arabGame = new Game
        {
            Id = "arab", Name = "Arabic Game", Cover = coverUri,
            ApplyPath = arabLnk,
        };
        var rr = await svc.BuildIconsAsync(new[] { arabGame });
        Console.WriteLine($"arabic build: applied={rr.Applied} failed={rr.Failed}");
        using (var s = GameIconsStudio.Services.ShellShortcut.Open(arabLnk))
            Console.WriteLine($"arabic icon after build: '{s.IconLocation}'");
        await svc.RemoveIconAsync(arabGame);
        using (var s = GameIconsStudio.Services.ShellShortcut.Open(arabLnk))
            Console.WriteLine($"arabic icon after remove: '{s.IconLocation}'");
        return 0;
    }
}
