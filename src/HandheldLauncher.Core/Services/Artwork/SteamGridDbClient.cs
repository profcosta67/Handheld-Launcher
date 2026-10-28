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
/// in the same response shape, so FetchBestGrid is reused for both and just
/// gets pointed at a different endpoint. Logos/icons aren't wired up yet;
/// same pattern applies if that's ever needed.
/// </summary>
public sealed class SteamGridDbClient : ISteamGridDbClient, IDisposable
{
    private const string BaseUrl = "https://www.steamgriddb.com/api/v2";

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
        if (!HasApiKey) return null;
        return await FetchBestGrid($"{BaseUrl}/grids/steam/{steamAppId}?dimensions=600x900", ct);
    }

    public async Task<SteamGridDbImage?> GetBestGridForTitleAsync(string title, CancellationToken ct = default)
    {
        if (!HasApiKey) return null;

        var gameId = await SearchGameId(title, ct);
        if (gameId is null) return null;

        return await FetchBestGrid($"{BaseUrl}/grids/game/{gameId}?dimensions=600x900", ct);
    }

    public async Task<SteamGridDbImage?> GetBestHeroForSteamAppAsync(uint steamAppId, CancellationToken ct = default)
    {
        if (!HasApiKey) return null;
        // No dimensions filter here on purpose — heroes come in a few wide
        // aspect ratios (1920x620, 3840x1240, 1600x650...) and SteamGridDB's
        // hero library is thinner than its grid library, so narrowing by
        // dimensions would throw away matches we'd rather keep.
        return await FetchBestGrid($"{BaseUrl}/heroes/steam/{steamAppId}", ct);
    }

    public async Task<SteamGridDbImage?> GetBestHeroForTitleAsync(string title, CancellationToken ct = default)
    {
        if (!HasApiKey) return null;

        var gameId = await SearchGameId(title, ct);
        if (gameId is null) return null;

        return await FetchBestGrid($"{BaseUrl}/heroes/game/{gameId}", ct);
    }

    private async Task<int?> SearchGameId(string title, CancellationToken ct)
    {
        var url = $"{BaseUrl}/search/autocomplete/{Uri.EscapeDataString(title)}";

        using var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return null;

        var payload = await response.Content.ReadFromJsonSafe<SearchResponse>(ct);
        return payload?.Data?.FirstOrDefault()?.Id;
    }

    private async Task<SteamGridDbImage?> FetchBestGrid(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return null;

        var payload = await response.Content.ReadFromJsonSafe<GridResponse>(ct);

        // Prefer safe-for-work, highest-scored result. SteamGridDB sorts by
        // score already, but we don't trust that ordering blindly — a
        // bare .FirstOrDefault() on an unexpected order would pick a bad cover.
        var best = payload?.Data?
            .Where(g => !g.Nsfw && !g.Humor)
            .OrderByDescending(g => g.Score)
            .FirstOrDefault();

        return best is null
            ? null
            : new SteamGridDbImage { Id = best.Id, Url = best.Url, Width = best.Width, Height = best.Height };
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
