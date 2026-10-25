namespace GameIconsStudio.Services
{
    /// <summary>Harness stub for the in-app log viewer.</summary>
    public static class LogService
    {
        public static void Info(string message) => System.Console.WriteLine($"[log] {message}");
        public static void Warn(string message) => System.Console.WriteLine($"[warn] {message}");
        public static void Error(string message) => System.Console.WriteLine($"[err] {message}");
    }
}

namespace GameIconsStudio
{
    public enum PlatformTemplate
    {
        None,
        Ps5Blue, Ps5Black,
        XboxSeries, XboxOne, Xbox360,
        SteamBlue, SteamMinimal,
        Epic,
        GfwClassic, GfwModern,
        Windows11,
        Custom,
    }
    public enum SurfaceFilter { None, Glossy, Matte, Holographic, Vintage }

    /// <summary>Stub of Models.cs PlatformAssets — only what the renderer needs.</summary>
    public static class PlatformAssets
    {
        public static string? FileFor(PlatformTemplate t) => t switch
        {
            PlatformTemplate.Ps5Blue => "template_ps5_blue.png",
            PlatformTemplate.Ps5Black => "template_ps5_black.png",
            PlatformTemplate.XboxSeries => "template_xbox_series.png",
            PlatformTemplate.XboxOne => "template_xbox_one.png",
            PlatformTemplate.Xbox360 => "template_xbox_360.png",
            PlatformTemplate.SteamBlue => "template_steam_blue.png",
            PlatformTemplate.SteamMinimal => "template_steam_minimal.png",
            PlatformTemplate.Epic => "template_epic.png",
            PlatformTemplate.GfwClassic => "template_gfw_classic.png",
            PlatformTemplate.GfwModern => "template_gfw_modern.png",
            PlatformTemplate.Windows11 => "template_windows_11.png",
            _ => null,
        };

        public static System.Collections.Generic.IReadOnlyList<PlatformTemplate> All { get; } =
        [
            PlatformTemplate.None,
            PlatformTemplate.Ps5Blue, PlatformTemplate.Ps5Black,
            PlatformTemplate.XboxSeries, PlatformTemplate.XboxOne, PlatformTemplate.Xbox360,
            PlatformTemplate.SteamBlue, PlatformTemplate.SteamMinimal,
            PlatformTemplate.Epic,
            PlatformTemplate.GfwClassic, PlatformTemplate.GfwModern,
            PlatformTemplate.Windows11,
        ];
    }

    public class Game
    {
        public string Id { get; set; } = "test";
        public string Name { get; set; } = "Test";
        public string Cover { get; set; } = "";
        public PlatformTemplate Platform { get; set; }
        public string CustomTemplate { get; set; } = "";
        public SurfaceFilter Surface { get; set; }
        public bool StretchSquare { get; set; }
        public string SearchName { get; set; } = "";
        public int SgdbGameId { get; set; }
        public bool IsSavedChoice { get; set; }
        public string ApplyPath { get; set; } = "";
        public bool IsFolderTarget { get; set; }
        public bool StickerPcCdrom { get; set; }
        public bool StickerDvd { get; set; }
        public bool StickerEsrb { get; set; }
        public bool StickerPegi18 { get; set; }
        public bool StickerSeal { get; set; }
    }
}
