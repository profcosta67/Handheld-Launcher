using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HandheldLauncher.Core.Services.Artwork;

/// <summary>
/// Thin client for the SteamGridDB v2 REST API (docs: steamgriddb.com/api/v2).
/// Uses plain HttpClient + System.Text.Json — both are base-class-library,
/// so this needed zero NuGet packages, same reasoning as the XInput P/Invoke
/// in the App project: everything written where we couldn't reach nuget.org
/// to verify a package-based approach even builds.
///
/// Covers grids (portrait covers) and heroes (wide banners) — both come back
/// in the same response shape, so FetchGridCandidates is reused for both and
/// just gets pointed at a different endpoint. Logos/icons aren't wired up
/// yet; same pattern applies if that's ever needed.
///
/// Every request adds "types=static" — SteamGridDB can also serve animated
/// GIF/webm covers, but Avalonia's Image control doesn't animate those, so
/// asking the API to filter them out up front means we never even download
/// something we can't display. Animated support is tracked as a separate,
/// not-yet-started phase in README's "Seletor de capas" section.
/// </summary>
public sealed class SteamGridDbClient : ISteamGridDbClient, IDisposable
{
    private const string BaseUrl = "https://www.steamgriddb.com/api/v2";

    // A picker UI needs *some* cap — popular games can have hundreds of
    // candidate grids/heroes, and downloading all of them just to show a
    // scrollable grid would be slow and wasteful on a handheld's connection.
    // 24 is generous for "pick the one you like" without pagination.
    private const int MaxCandidates = 24;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly string? _apiKey;

    public SteamGridDbClient(string? apiKey)
    {
        _apiKey = apiKey;
        _http = new HttpClient();

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<SteamGridDbImage?> GetBestGridForSteamAppAsync(uint steamAppId, CancellationToken ct = default)
    {
        var candidates = await GetGridCandidatesForSteamAppAsync(steamAppId, ct);
        return candidates.FirstOrDefault();
    }

    public async Task<SteamGridDbImage?> GetBestGridForTitleAsync(string title, CancellationToken ct = default)
    {
        var candidates = await GetGridCandidatesForTitleAsync(title, ct);
        return candidates.FirstOrDefault();
    }

    public async Task<SteamGridDbImage?> GetBestHeroForSteamAppAsync(uint steamAppId, CancellationToken ct = default)
    {
        var candidates = await GetHeroCandidatesForSteamAppAsync(steamAppId, ct);
        return candidates.FirstOrDefault();
    }

    public async Task<SteamGridDbImage?> GetBestHeroForTitleAsync(string title, CancellationToken ct = default)
    {
        var candidates = await GetHeroCandidatesForTitleAsync(title, ct);
        return candidates.FirstOrDefault();
    }

    public async Task<IReadOnlyList<SteamGridDbImage>> GetGridCandidatesForSteamAppAsync(uint steamAppId, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();
        return await FetchGridCandidates($"{BaseUrl}/grids/steam/{steamAppId}?dimensions=600x900&types=static", ct);
    }

    public async Task<IReadOnlyList<SteamGridDbImage>> GetGridCandidatesForTitleAsync(string title, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();

        var gameId = await SearchGameId(title, ct);
        if (gameId is null) return Array.Empty<SteamGridDbImage>();

        return await FetchGridCandidates($"{BaseUrl}/grids/game/{gameId}?dimensions=600x900&types=static", ct);
    }

    public async Task<IReadOnlyList<SteamGridDbImage>> GetHeroCandidatesForSteamAppAsync(uint steamAppId, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();
        // No dimensions filter here on purpose — heroes come in a few wide
        // aspect ratios (1920x620, 3840x1240, 1600x650...) and SteamGridDB's
        // hero library is thinner than its grid library, so narrowing by
        // dimensions would throw away matches we'd rather keep.
        return await FetchGridCandidates($"{BaseUrl}/heroes/steam/{steamAppId}?types=static", ct);
    }

    public async Task<IReadOnlyList<SteamGridDbImage>> GetHeroCandidatesForTitleAsync(string title, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();

        var gameId = await SearchGameId(title, ct);
        if (gameId is null) return Array.Empty<SteamGridDbImage>();

        return await FetchGridCandidates($"{BaseUrl}/heroes/game/{gameId}?types=static", ct);
    }

    private async Task<int?> SearchGameId(string title, CancellationToken ct)
    {
        var url = $"{BaseUrl}/search/autocomplete/{Uri.EscapeDataString(title)}";

        using var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return null;

        var payload = await response.Content.ReadFromJsonSafe<SearchResponse>(ct);
        return payload?.Data?.FirstOrDefault()?.Id;
    }

    private async Task<IReadOnlyList<SteamGridDbImage>> FetchGridCandidates(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return Array.Empty<SteamGridDbImage>();

        var payload = await response.Content.ReadFromJsonSafe<GridResponse>(ct);
        if (payload?.Data is null) return Array.Empty<SteamGridDbImage>();

        // Prefer safe-for-work, highest-scored results first. SteamGridDB
        // sorts by score already, but we don't trust that ordering blindly —
        // a bare pass-through on an unexpected order would surface a bad
        // cover as the top (and, pre-picker, only) pick.
        return payload.Data
            .Where(g => !g.Nsfw && !g.Humor)
            .OrderByDescending(g => g.Score)
            .Take(MaxCandidates)
            .Select(g => new SteamGridDbImage { Id = g.Id, Url = g.Url, Width = g.Width, Height = g.Height })
            .ToList();
    }

    public void Dispose() => _http.Dispose();

    private sealed class SearchResponse
    {
        public List<SearchResult>? Data { get; set; }
    }

    private sealed class SearchResult
    {
        public int Id { get; set; }
    }

    private sealed class GridResponse
    {
        public List<GridResult>? Data { get; set; }
    }

    private sealed class GridResult
    {
        public int Id { get; set; }
        public string Url { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Nsfw { get; set; }
        public bool Humor { get; set; }
        public int Score { get; set; }
    }
}

internal static class HttpContentJsonExtensions
{
    /// <summary>
    /// Deserializes without throwing on a malformed/unexpected body — the
    /// API being briefly down or rate-limiting shouldn't take the whole
    /// fetch-artwork pass down with it.
    /// </summary>
    public static async Task<T?> ReadFromJsonSafe<T>(this HttpContent content, CancellationToken ct) where T : class
    {
        try
        {
            var stream = await content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<T>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
