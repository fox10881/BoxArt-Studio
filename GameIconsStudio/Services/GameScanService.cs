using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GameIconsStudio.Services;

public sealed record ShortcutInfo(
    string Path,
    string Name,
    string Target,
    string Arguments,
    string WorkingDirectory,
    string IconLocation,
    string NormalizedName,
    long SteamAppId,
    int Year);

/// <summary>
/// Scans folders and files for game shortcuts. Read-only: nothing on disk is
/// modified by scanning. .lnk parsing uses IShellLinkW (full Unicode paths).
/// </summary>
public sealed partial class GameScanService
{
    /// <summary>Enumerate and parse every .lnk under a folder.</summary>
    public Task<IReadOnlyList<ShortcutInfo>> ScanFolderAsync(
        string folder, bool recursive = true, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<ShortcutInfo>>(() =>
        {
            var folders = ScanRoots(folder);
            var search = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var results = new List<ShortcutInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in folders)
            {
                List<string> files;
                try
                {
                    files = Directory.EnumerateFiles(dir, "*.lnk", search).ToList();
                }
                catch (Exception ex)
                {
                    LogService.Error($"SCAN enumerate failed '{dir}': {ex.Message}");
                    continue;
                }

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    if (seen.Add(Path.GetFileName(file)))
                        results.Add(ReadShortcut(file));
                }
            }

            LogService.Info(folders.Count > 1
                ? $"SCAN lnk '{folder}' + Public Desktop -> {results.Count} shortcuts"
                : $"SCAN lnk '{folder}' -> {results.Count} shortcuts");
            return results;
        }, ct);

    /// <summary>
    /// Folders to scan for a picked root: normally just the folder itself,
    /// but when Desktop is picked, the Public Desktop is merged in too.
    /// </summary>
    private static List<string> ScanRoots(string folder)
    {
        var folders = new List<string> { folder };

        // The FolderPicker resolves "Desktop" to the user's logical
        // desktop only (which may be OneDrive- or D:\-backed). Launchers
        // often place .lnk files on the Public Desktop instead, so when
        // Desktop is picked, scan and merge both locations.
        if (TryGetPublicDesktop(folder, out var publicDesktop))
            folders.Add(publicDesktop!);

        return folders;
    }

    /// <summary>
    /// True when <paramref name="folder"/> is the user's logical desktop; also
    /// returns the Public Desktop path to merge in — false when the Public
    /// Desktop is unavailable, missing, or identical to the picked folder.
    /// </summary>
    private static bool TryGetPublicDesktop(string folder, out string? publicDesktop)
    {
        publicDesktop = null;
        var userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(userDesktop) || !PathsEqual(folder, userDesktop))
            return false;

        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        if (string.IsNullOrWhiteSpace(common) || PathsEqual(common, folder) || !Directory.Exists(common))
            return false;

        publicDesktop = common;
        return true;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// "All Sub-Folders" mode: each direct child directory of the picked
    /// parent becomes one game entry, named and normalized from the folder.
    /// </summary>
    public Task<IReadOnlyList<ShortcutInfo>> ScanSubFoldersAsync(
        string parent, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<ShortcutInfo>>(() =>
        {
            var results = new List<ShortcutInfo>();
            IEnumerable<string> dirs;
            try
            {
                dirs = Directory.EnumerateDirectories(parent);
            }
            catch (Exception ex)
            {
                LogService.Error($"SCAN enumerate failed '{parent}': {ex.Message}");
                return results;
            }

            foreach (var dir in dirs)
            {
                ct.ThrowIfCancellationRequested();
                results.Add(FolderEntry(dir));
            }
            LogService.Info($"SCAN folders '{parent}' -> {results.Count} folders");
            return results;
        }, ct);

    /// <summary>"Specific Folder" mode: the picked directory itself is the game.</summary>
    public ShortcutInfo FolderEntry(string folder)
    {
        var name = new DirectoryInfo(folder).Name;
        return new ShortcutInfo(
            folder, name, string.Empty, string.Empty, folder, string.Empty,
            NormalizeGameName(name), 0, ExtractYear(name));
    }

    /// <summary>Parse an explicit list of .lnk paths (the file-picker mode).</summary>
    public Task<IReadOnlyList<ShortcutInfo>> FromPathsAsync(
        IEnumerable<string> paths, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<ShortcutInfo>>(() =>
        {
            var list = (IReadOnlyList<ShortcutInfo>)paths
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                .Select(ReadShortcut)
                .ToList();
            LogService.Info($"SCAN picked files -> {list.Count} shortcuts");
            return list;
        }, ct);

    /// <summary>Health report: counts of .lnk / broken-target / .url files.</summary>
    public Task<(int Lnk, int Broken, int Url)> GetFolderHealthAsync(
        string folder, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var lnk = 0;
            var broken = 0;
            var url = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in ScanRoots(folder))
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
                    {
                        if (!seen.Add(Path.GetFileName(f))) continue;
                        lnk++;
                        try
                        {
                            var info = ReadShortcut(f);
                            if (!string.IsNullOrWhiteSpace(info.Target) &&
                                DriveTargetRegex().IsMatch(info.Target) &&
                                !File.Exists(info.Target))
                                broken++;
                        }
                        catch { broken++; }
                    }
                    foreach (var f in Directory.EnumerateFiles(dir, "*.url", SearchOption.AllDirectories))
                        if (seen.Add(Path.GetFileName(f))) url++;
                }
                catch { }
            }
            return (lnk, broken, url);
        }, ct);

    // ---------------- shortcut parsing ----------------

    private static ShortcutInfo ReadShortcut(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        try
        {
            using var lnk = ShellShortcut.Open(path);
            var target = lnk.TargetPath;
            var args = lnk.Arguments;

            return new ShortcutInfo(
                path, name, target, args, lnk.WorkingDirectory, lnk.IconLocation,
                NormalizeGameName(name),
                ExtractSteamAppId(target, args),
                ExtractYear(name));
        }
        catch
        {
            return new ShortcutInfo(
                path, name, string.Empty, string.Empty, string.Empty, string.Empty,
                NormalizeGameName(name), 0, 0);
        }
    }

    // ---------------- normalization (ported rules) ----------------

    private static readonly string[] EditionPatterns =
    [
        @"Game of the Year Edition", @"Game of the Year", @"GOTY Edition", @"GOTY",
        @"Director'?s Cut", @"Deluxe Edition", @"Digital Deluxe Edition", @"Gold Edition",
        @"Complete Edition", @"Definitive Edition", @"Ultimate Edition", @"Enhanced Edition",
        @"Anniversary Edition", @"HD Remaster(?:ed)?", @"Remastered", @"Remaster",
    ];

    /// <summary>
    /// Clean a shortcut filename into a searchable game title: strips
    /// trademark symbols, bracketed tags, and edition suffixes.
    /// </summary>
    public static string NormalizeGameName(string name)
    {
        var n = name;
        n = Regex.Replace(n, "[™®©]", "");
        n = Regex.Replace(n, @"\s*[\(\[\{][^\)\]\}]*[\)\]\}]", "");
        foreach (var pattern in EditionPatterns)
            n = Regex.Replace(n, $@"[\s:\-]*\b{pattern}\b", "", RegexOptions.IgnoreCase);
        n = Regex.Replace(n, "_+", " ");
        n = Regex.Replace(n, @"\s{2,}", " ");
        n = n.Trim(' ', '-', ':', '.');
        return string.IsNullOrWhiteSpace(n) ? name.Trim() : n;
    }

    /// <summary>
    /// Steam AppID from target/args: steam://rungameid/N or -applaunch N.
    /// Values >= 3,000,000 are non-Steam and rejected.
    /// </summary>
    public static long ExtractSteamAppId(string target, string arguments)
    {
        var combined = $"{target} {arguments}";
        var m = SteamRunGameIdRegex().Match(combined);
        if (!m.Success) m = AppLaunchRegex().Match(combined);
        if (!m.Success) return 0;
        var value = long.Parse(m.Groups[1].Value);
        return value is > 0 and < 3_000_000 ? value : 0;
    }

    /// <summary>Release year only when parenthesized — "(2018)" — so "Cyberpunk 2077" is safe.</summary>
    public static int ExtractYear(string name)
    {
        var m = YearRegex().Match(name);
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    /// <summary>File part of an IconLocation string ("C:\path\x.ico,0"), .ico only.</summary>
    public static string? CurrentIconFile(string? iconLocation)
    {
        if (string.IsNullOrWhiteSpace(iconLocation)) return null;
        var part = iconLocation.Split(',')[0].Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(part)) return null;
        return part.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) && File.Exists(part) ? part : null;
    }

    [GeneratedRegex("^[A-Za-z]:\\\\")]
    private static partial Regex DriveTargetRegex();

    [GeneratedRegex(@"steam://rungameid/(\d+)")]
    private static partial Regex SteamRunGameIdRegex();

    [GeneratedRegex(@"-applaunch\s+(\d+)")]
    private static partial Regex AppLaunchRegex();

    [GeneratedRegex(@"\(((19|20)\d{2})\)")]
    private static partial Regex YearRegex();
}
