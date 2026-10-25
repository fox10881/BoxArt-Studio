using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace GameIconsStudio.Services;

public enum ArtworkKind { VerticalGrid, Grid, Square, Icon, Logo }

// Settable properties (not init-only positional params) — the WinUI XAML
// compiler generates assignment code for bound types and rejects `init`.
public sealed record GameCandidate : System.ComponentModel.INotifyPropertyChanged
{
    public int GameId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Year { get; set; }
    public double Score { get; set; }

    private string? _thumbUrl;
    /// <summary>Small cover thumb loaded lazily for the search dialog.</summary>
    public string? ThumbUrl
    {
        get => _thumbUrl;
        set { _thumbUrl = value; PropertyChanged?.Invoke(this, new(nameof(ThumbUrl))); }
    }

    public string Title => Year > 0 ? $"{Name} ({Year})" : Name;
    public string YearLabel => Year > 0 ? Year.ToString() : "—";
    public string MatchLabel => $"{(int)Math.Round(Score * 100)}% match";
    public bool IsStrongMatch => Score >= 0.85;
    public string SubLabel => $"#{GameId} · {YearLabel} · {MatchLabel}";

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public sealed record ArtworkCandidate
{
    public string Url { get; set; } = string.Empty;
    public string Thumb { get; set; } = string.Empty;
    public int Score { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Label => $"{Width}×{Height} · score {Score}";
}

public sealed record GameMatchResult(
    int GameId, string? GameName, double Score, bool Ambiguous, IReadOnlyList<GameCandidate> Candidates);

/// <summary>
/// Async SteamGridDB v2 client. Ported (rethought, not translated) from the
/// legacy SteamGridDB.ps1 matching engine:
///  - Bearer auth, single retry on HTTP 429, 404 -> null, 401 -> UnauthorizedAccessException
///  - name matching = Levenshtein over normalized compare-keys with a 0.85
///    containment floor; release-year agreement nudges the score (+0.08/-0.05)
///  - ambiguity heuristics decide when the UI should ask the user to confirm
/// </summary>
public sealed class SteamGridService : IDisposable
{
    private const string BaseUrl = "https://www.steamgriddb.com/api/v2";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private string? _apiKey;

    public void Configure(string apiKey) => _apiKey = apiKey;

    private void EnsureKey()
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("SteamGridDB API key is not configured.");
    }

    private async Task<JsonNode?> GetAsync(string endpoint, CancellationToken ct)
    {
        EnsureKey();
        var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        LogService.Info($"SGDB GET {endpoint}");

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request.Clone(), ct);
            }
            catch (Exception ex)
            {
                LogService.Error($"SGDB {endpoint} — network error: {ex.Message}");
                throw;
            }

            using (response)
            {
                LogService.Info($"SGDB {(int)response.StatusCode} {endpoint}");
                switch (response.StatusCode)
                {
                    case HttpStatusCode.Unauthorized:
                        throw new UnauthorizedAccessException("SteamGridDB rejected the API key (HTTP 401).");
                    case HttpStatusCode.NotFound:
                        return null;
                    case HttpStatusCode.TooManyRequests when attempt == 0:
                        LogService.Warn($"SGDB 429 {endpoint} — retrying in 5s");
                        await Task.Delay(TimeSpan.FromSeconds(5), ct);
                        continue;
                }
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(ct);
                return JsonNode.Parse(json);
            }
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try { return await GetAsync("/search/autocomplete/portal", ct) is not null; }
        catch (UnauthorizedAccessException) { throw; }
        catch { return false; }
    }

    // ---------------- matching ----------------

    public async Task<IReadOnlyList<GameCandidate>> SearchGamesAsync(
        string query, int top = 8, CancellationToken ct = default)
    {
        var node = await GetAsync($"/search/autocomplete/{Uri.EscapeDataString(query)}", ct);
        var data = node?["data"] as JsonArray;
        if (data is null) return Array.Empty<GameCandidate>();

        var list = data
            .Where(i => i is not null)
            .Select(i => new GameCandidate
            {
                GameId = (int)(i!["id"] ?? 0),
                Name = (string?)i!["name"] ?? string.Empty,
                Year = YearFromUnix(i["release_date"]),
                Score = NameSimilarity(query, (string?)i["name"] ?? string.Empty),
            })
            .OrderByDescending(c => c.Score)
            .Take(top)
            .ToList();
        return list;
    }

    public async Task<GameMatchResult?> MatchBySteamAppIdAsync(long appId, CancellationToken ct = default)
    {
        var node = await GetAsync($"/games/steam/{appId}", ct);
        var data = node?["data"];
        if (data?["id"] is null) return null;
        return new GameMatchResult(
            (int)data["id"]!, (string?)data["name"], 1.0, false, Array.Empty<GameCandidate>());
    }

    /// <summary>
    /// Resolve the best game for a shortcut name. A positive Steam AppID is
    /// definitive. Otherwise candidates are similarity-scored, year-adjusted,
    /// then flagged ambiguous when the winner is not clearly safe.
    /// </summary>
    public async Task<GameMatchResult> ResolveGameMatchAsync(
        string name, int year = 0, long steamAppId = 0,
        double minScore = 0.55, int top = 8, CancellationToken ct = default)
    {
        if (steamAppId > 0)
        {
            var bySteam = await MatchBySteamAppIdAsync(steamAppId, ct);
            if (bySteam is not null) return bySteam;
        }

        var cands = (await SearchGamesAsync(name, top, ct)).ToList();

        if (year > 0)
        {
            foreach (var (c, i) in cands.Select((c, i) => (c, i)))
            {
                if (c.Year <= 0) continue;
                var adjusted = c.Year == year
                    ? Math.Min(1.0, Math.Round(c.Score + 0.08, 3))
                    : Math.Max(0.0, Math.Round(c.Score - 0.05, 3));
                cands[i] = c with { Score = adjusted };
            }
            cands = cands.OrderByDescending(c => c.Score).ToList();
        }

        if (cands.Count == 0 || cands[0].Score < minScore)
            return new GameMatchResult(0, null, 0, true, cands);

        var winner = cands[0];
        var ambiguous =
            winner.Score < 0.80 ||
            (cands.Count >= 2 && winner.Score - cands[1].Score < 0.03 && cands[1].Score >= 0.80) ||
            (year > 0 && winner.Year > 0 && winner.Year != year) ||
            cands.Count(c => c.Score >= 0.85) >= 6;

        return new GameMatchResult(winner.GameId, winner.Name, winner.Score, ambiguous, cands);
    }

    // ---------------- artwork ----------------

    /// <summary>
    /// Artwork candidates for a game, best community score first with
    /// resolution breaking ties. VerticalGrid targets SGDB 600x900 (2:3)
    /// covers to match the app's cover design.
    /// </summary>
    public async Task<IReadOnlyList<ArtworkCandidate>> GetArtworkCandidatesAsync(
        int gameId, ArtworkKind kind, int top = 10, CancellationToken ct = default)
    {
        const string safe = "mimes=image/png,image/jpeg&types=static";
        var endpoint = kind switch
        {
            ArtworkKind.VerticalGrid => $"/grids/game/{gameId}?dimensions=600x900&{safe}",
            ArtworkKind.Square => $"/grids/game/{gameId}?dimensions=512x512,1024x1024&{safe}",
            ArtworkKind.Grid => $"/grids/game/{gameId}?{safe}",
            ArtworkKind.Icon => $"/icons/game/{gameId}",
            ArtworkKind.Logo => $"/logos/game/{gameId}?types=static&mimes=image/png",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var data = (await GetAsync(endpoint, ct))?["data"] as JsonArray;
        if (data is null || data.Count == 0) return Array.Empty<ArtworkCandidate>();

        return data
            .Where(i => i?["url"] is not null)
            .Select(i => new ArtworkCandidate
            {
                Url = (string)i!["url"]!,
                Thumb = (string?)i["thumb"] ?? (string)i["url"]!,
                Score = (int?)(i["score"]) ?? 0,
                Width = (int?)(i["width"]) ?? 0,
                Height = (int?)(i["height"]) ?? 0,
            })
            .OrderByDescending(a => a.Score)
            .ThenByDescending(a => a.Width * (long)a.Height)
            .Take(top)
            .ToList();
    }

    /// <summary>Best vertical cover, falling back VerticalGrid -> Grid -> Square.</summary>
    public async Task<ArtworkCandidate?> GetBestCoverAsync(int gameId, CancellationToken ct = default)
    {
        foreach (var kind in new[] { ArtworkKind.VerticalGrid, ArtworkKind.Grid, ArtworkKind.Square })
        {
            var cands = await GetArtworkCandidatesAsync(gameId, kind, 1, ct);
            if (cands.Count > 0) return cands[0];
        }
        return null;
    }

    public async Task<byte[]> DownloadImageAsync(string url, CancellationToken ct = default) =>
        await _http.GetByteArrayAsync(url, ct);

    // ---------------- similarity helpers ----------------

    private static int YearFromUnix(JsonNode? node)
    {
        try
        {
            if (node is null) return 0;
            var secs = (long?)node ?? 0;
            return secs > 0 ? DateTimeOffset.FromUnixTimeSeconds(secs).Year : 0;
        }
        catch { return 0; }
    }

    private static string CompareKey(string name)
    {
        var k = name.ToLowerInvariant().Replace("&", "and");
        return new string(k.Where(char.IsLetterOrDigit).ToArray());
    }

    public static double NameSimilarity(string a, string b)
    {
        var ka = CompareKey(a);
        var kb = CompareKey(b);
        if (ka.Length == 0 || kb.Length == 0) return 0.0;
        if (ka == kb) return 1.0;

        var dist = Levenshtein(ka, kb);
        var score = 1.0 - (double)dist / Math.Max(ka.Length, kb.Length);
        if (ka.Contains(kb) || kb.Contains(ka)) score = Math.Max(score, 0.85);
        return Math.Round(score, 3);
    }

    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    public void Dispose() => _http.Dispose();
}

internal static class HttpRequestMessageExtensions
{
    public static HttpRequestMessage Clone(this HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        if (request.Headers.Authorization is not null)
            clone.Headers.Authorization = request.Headers.Authorization;
        return clone;
    }
}
