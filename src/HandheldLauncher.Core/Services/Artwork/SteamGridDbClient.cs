using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HandheldLauncher.Core.Services.Artwork;

public sealed class SteamGridDbClient : ISteamGridDbClient, IDisposable
{
    private const string BaseUrl = "https://www.steamgriddb.com/api/v2";
    private const int MaxCandidates = 50; // Aumentado para pegar mais animadas

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

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
        => (await GetGridCandidatesForSteamAppAsync(steamAppId, false, ct)).FirstOrDefault();

    public async Task<SteamGridDbImage?> GetBestGridForTitleAsync(string title, CancellationToken ct = default)
        => (await GetGridCandidatesForTitleAsync(title, false, ct)).FirstOrDefault();

    public async Task<SteamGridDbImage?> GetBestHeroForSteamAppAsync(uint steamAppId, CancellationToken ct = default)
        => (await GetHeroCandidatesForSteamAppAsync(steamAppId, false, ct)).FirstOrDefault();

    public async Task<SteamGridDbImage?> GetBestHeroForTitleAsync(string title, CancellationToken ct = default)
        => (await GetHeroCandidatesForTitleAsync(title, false, ct)).FirstOrDefault();

    public async Task<SteamGridDbImage?> GetBestLogoForSteamAppAsync(uint steamAppId, CancellationToken ct = default)
        => (await GetLogoCandidatesForSteamAppAsync(steamAppId, false, ct)).FirstOrDefault();

    public async Task<SteamGridDbImage?> GetBestLogoForTitleAsync(string title, CancellationToken ct = default)
        => (await GetLogoCandidatesForTitleAsync(title, false, ct)).FirstOrDefault();

    public async Task<SteamGridDbImage?> GetBestIconForSteamAppAsync(uint steamAppId, CancellationToken ct = default)
        => (await GetIconCandidatesForSteamAppAsync(steamAppId, false, ct)).FirstOrDefault();

    public async Task<SteamGridDbImage?> GetBestIconForTitleAsync(string title, CancellationToken ct = default)
        => (await GetIconCandidatesForTitleAsync(title, false, ct)).FirstOrDefault();

    public async Task<SteamGridDbImage?> GetBestWideGridForSteamAppAsync(uint steamAppId, CancellationToken ct = default)
        => (await GetWideGridCandidatesForSteamAppAsync(steamAppId, false, ct)).FirstOrDefault();

    public async Task<SteamGridDbImage?> GetBestWideGridForTitleAsync(string title, CancellationToken ct = default)
        => (await GetWideGridCandidatesForTitleAsync(title, false, ct)).FirstOrDefault();

    // --- Candidatos ---
    public async Task<IReadOnlyList<SteamGridDbImage>> GetGridCandidatesForSteamAppAsync(uint steamAppId, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();
        var type = animatedOnly ? "animated" : "static,animated";
        return await FetchGridCandidates($"{BaseUrl}/grids/steam/{steamAppId}?dimensions=600x900&types={type}", ct);
    }

    public async Task<IReadOnlyList<SteamGridDbImage>> GetGridCandidatesForTitleAsync(string title, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();
        var gameId = await SearchGameId(title, ct);
        if (gameId is null) return Array.Empty<SteamGridDbImage>();
        var type = animatedOnly ? "animated" : "static,animated";
        return await FetchGridCandidates($"{BaseUrl}/grids/game/{gameId}?dimensions=600x900&types={type}", ct);
    }

    public async Task<IReadOnlyList<SteamGridDbImage>> GetHeroCandidatesForSteamAppAsync(uint steamAppId, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();
        var type = animatedOnly ? "animated" : "static,animated";
        return await FetchGridCandidates($"{BaseUrl}/heroes/steam/{steamAppId}?types={type}", ct);
    }

    public async Task<IReadOnlyList<SteamGridDbImage>> GetHeroCandidatesForTitleAsync(string title, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();
        var gameId = await SearchGameId(title, ct);
        if (gameId is null) return Array.Empty<SteamGridDbImage>();
        var type = animatedOnly ? "animated" : "static,animated";
        return await FetchGridCandidates($"{BaseUrl}/heroes/game/{gameId}?types={type}", ct);
    }

    public async Task<IReadOnlyList<SteamGridDbImage>> GetLogoCandidatesForSteamAppAsync(uint steamAppId, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();
        var type = animatedOnly ? "animated" : "static,animated";
        return await FetchGridCandidates($"{BaseUrl}/logos/steam/{steamAppId}?types={type}", ct);
    }

    public async Task<IReadOnlyList<SteamGridDbImage>> GetLogoCandidatesForTitleAsync(string title, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();
        var gameId = await SearchGameId(title, ct);
        if (gameId is null) return Array.Empty<SteamGridDbImage>();
        var type = animatedOnly ? "animated" : "static,animated";
        return await FetchGridCandidates($"{BaseUrl}/logos/game/{gameId}?types={type}", ct);
    }

    public async Task<IReadOnlyList<SteamGridDbImage>> GetIconCandidatesForSteamAppAsync(uint steamAppId, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();
        var type = animatedOnly ? "animated" : "static,animated";
        return await FetchGridCandidates($"{BaseUrl}/icons/steam/{steamAppId}?types={type}&mimes=image/png,image/webp", ct);
    }

    public async Task<IReadOnlyList<SteamGridDbImage>> GetIconCandidatesForTitleAsync(string title, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();
        var gameId = await SearchGameId(title, ct);
        if (gameId is null) return Array.Empty<SteamGridDbImage>();
        var type = animatedOnly ? "animated" : "static,animated";
        return await FetchGridCandidates($"{BaseUrl}/icons/game/{gameId}?types={type}&mimes=image/png,image/webp", ct);
    }

    public async Task<IReadOnlyList<SteamGridDbImage>> GetWideGridCandidatesForSteamAppAsync(uint steamAppId, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();
        var type = animatedOnly ? "animated" : "static,animated";
        return await FetchGridCandidates($"{BaseUrl}/grids/steam/{steamAppId}?dimensions=460x215,920x430&types={type}", ct);
    }

    public async Task<IReadOnlyList<SteamGridDbImage>> GetWideGridCandidatesForTitleAsync(string title, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();
        var gameId = await SearchGameId(title, ct);
        if (gameId is null) return Array.Empty<SteamGridDbImage>();
        var type = animatedOnly ? "animated" : "static,animated";
        return await FetchGridCandidates($"{BaseUrl}/grids/game/{gameId}?dimensions=460x215,920x430&types={type}", ct);
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

        return payload.Data
            .Where(g => !g.Nsfw && !g.Humor)
            .OrderByDescending(g => g.Score)
            .Take(MaxCandidates)
            .Select(g => new SteamGridDbImage { Id = g.Id, Url = g.Url, Width = g.Width, Height = g.Height })
            .ToList();
    }

    public void Dispose() => _http.Dispose();

    private sealed class SearchResponse { public List<SearchResult>? Data { get; set; } }
    private sealed class SearchResult { public int Id { get; set; } }
    private sealed class GridResponse { public List<GridResult>? Data { get; set; } }
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
    public static async Task<T?> ReadFromJsonSafe<T>(this HttpContent content, CancellationToken ct) where T : class
    {
        try
        {
            var stream = await content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<T>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        }
        catch (JsonException) { return null; }
    }
}