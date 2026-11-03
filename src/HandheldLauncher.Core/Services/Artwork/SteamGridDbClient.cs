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
    private const int MaxCandidates = 50;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private string? _apiKey;

    public SteamGridDbClient(string? apiKey)
    {
        _apiKey = apiKey;
        _http = new HttpClient();

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    // 🟢 NOVO MÉTODO: Permite atualizar a chave sem reiniciar o app
    public void UpdateApiKey(string? apiKey)
    {
        _apiKey = apiKey;
        _http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(apiKey)
            ? null
            : new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    // Automatic artwork downloads must always use static images.
    // The manual picker uses the candidate methods below and intentionally
    // keeps both static and animated options available.
    public Task<SteamGridDbImage?> GetBestGridForSteamAppAsync(uint steamAppId, CancellationToken ct = default)
        => GetBestStaticForSteamAppAsync("grids", steamAppId, ct);

    public Task<SteamGridDbImage?> GetBestGridForTitleAsync(string title, CancellationToken ct = default)
        => GetBestStaticForTitleAsync("grids", title, ct);

    public Task<SteamGridDbImage?> GetBestHeroForSteamAppAsync(uint steamAppId, CancellationToken ct = default)
        => GetBestStaticForSteamAppAsync("heroes", steamAppId, ct, wideOnly: true);

    public Task<SteamGridDbImage?> GetBestHeroForTitleAsync(string title, CancellationToken ct = default)
        => GetBestStaticForTitleAsync("heroes", title, ct, wideOnly: true);

    public Task<SteamGridDbImage?> GetBestLogoForSteamAppAsync(uint steamAppId, CancellationToken ct = default)
        => GetBestStaticForSteamAppAsync("logos", steamAppId, ct);

    public Task<SteamGridDbImage?> GetBestLogoForTitleAsync(string title, CancellationToken ct = default)
        => GetBestStaticForTitleAsync("logos", title, ct);

    public Task<SteamGridDbImage?> GetBestIconForSteamAppAsync(uint steamAppId, CancellationToken ct = default)
        => GetBestStaticForSteamAppAsync("icons", steamAppId, ct);

    public Task<SteamGridDbImage?> GetBestIconForTitleAsync(string title, CancellationToken ct = default)
        => GetBestStaticForTitleAsync("icons", title, ct);

    public Task<SteamGridDbImage?> GetBestWideGridForSteamAppAsync(uint steamAppId, CancellationToken ct = default)
        => GetBestStaticForSteamAppAsync("grids", steamAppId, ct, wideOnly: true);

    public Task<SteamGridDbImage?> GetBestWideGridForTitleAsync(string title, CancellationToken ct = default)
        => GetBestStaticForTitleAsync("grids", title, ct, wideOnly: true);

    private async Task<SteamGridDbImage?> GetBestStaticForSteamAppAsync(
        string category, uint steamAppId, CancellationToken ct, bool wideOnly = false, string extraQuery = "")
    {
        if (!HasApiKey) return null;
        return (await FetchGridCandidates(
            $"{BaseUrl}/{category}/steam/{steamAppId}?types=static{extraQuery}", ct, wideOnly)).FirstOrDefault();
    }

    private async Task<SteamGridDbImage?> GetBestStaticForTitleAsync(
        string category, string title, CancellationToken ct, bool wideOnly = false, string extraQuery = "")
    {
        if (!HasApiKey) return null;
        var gameId = await SearchGameId(title, ct);
        if (gameId is null) return null;
        return (await FetchGridCandidates(
            $"{BaseUrl}/{category}/game/{gameId}?types=static{extraQuery}", ct, wideOnly)).FirstOrDefault();
    }

    // --- Candidatos ---
    public async Task<IReadOnlyList<SteamGridDbImage>> GetGridCandidatesForSteamAppAsync(uint steamAppId, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();
        var type = animatedOnly ? "animated" : "static,animated";
        return await FetchGridCandidates($"{BaseUrl}/grids/steam/{steamAppId}?types={type}", ct, wideOnly: false);
    }

    public async Task<IReadOnlyList<SteamGridDbImage>> GetGridCandidatesForTitleAsync(string title, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();
        var gameId = await SearchGameId(title, ct);
        if (gameId is null) return Array.Empty<SteamGridDbImage>();
        var type = animatedOnly ? "animated" : "static,animated";
        return await FetchGridCandidates($"{BaseUrl}/grids/game/{gameId}?types={type}", ct, wideOnly: false);
    }

    public async Task<IReadOnlyList<SteamGridDbImage>> GetHeroCandidatesForSteamAppAsync(uint steamAppId, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();
        var type = animatedOnly ? "animated" : "static,animated";
        return await FetchGridCandidates($"{BaseUrl}/heroes/steam/{steamAppId}?types={type}", ct, wideOnly: true);
    }

    public async Task<IReadOnlyList<SteamGridDbImage>> GetHeroCandidatesForTitleAsync(string title, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();
        var gameId = await SearchGameId(title, ct);
        if (gameId is null) return Array.Empty<SteamGridDbImage>();
        var type = animatedOnly ? "animated" : "static,animated";
        return await FetchGridCandidates($"{BaseUrl}/heroes/game/{gameId}?types={type}", ct, wideOnly: true);
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
        return await FetchGridCandidates($"{BaseUrl}/icons/steam/{steamAppId}?types={type}", ct);
    }

    public async Task<IReadOnlyList<SteamGridDbImage>> GetIconCandidatesForTitleAsync(string title, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();
        var gameId = await SearchGameId(title, ct);
        if (gameId is null) return Array.Empty<SteamGridDbImage>();
        var type = animatedOnly ? "animated" : "static,animated";
        return await FetchGridCandidates($"{BaseUrl}/icons/game/{gameId}?types={type}", ct);
    }

    public async Task<IReadOnlyList<SteamGridDbImage>> GetWideGridCandidatesForSteamAppAsync(uint steamAppId, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();
        var type = animatedOnly ? "animated" : "static,animated";
        return await FetchGridCandidates($"{BaseUrl}/grids/steam/{steamAppId}?types={type}", ct, wideOnly: true);
    }

    public async Task<IReadOnlyList<SteamGridDbImage>> GetWideGridCandidatesForTitleAsync(string title, bool animatedOnly = false, CancellationToken ct = default)
    {
        if (!HasApiKey) return Array.Empty<SteamGridDbImage>();
        var gameId = await SearchGameId(title, ct);
        if (gameId is null) return Array.Empty<SteamGridDbImage>();
        var type = animatedOnly ? "animated" : "static,animated";
        return await FetchGridCandidates($"{BaseUrl}/grids/game/{gameId}?types={type}", ct, wideOnly: true);
    }

    private async Task<int?> SearchGameId(string title, CancellationToken ct)
    {
        var normalizedTitle = title.Trim();
        var comparableTitle = NormalizeGameTitle(normalizedTitle);
        var searchTitle = comparableTitle.Length > 0 ? comparableTitle : normalizedTitle;
        var url = $"{BaseUrl}/search/autocomplete/{Uri.EscapeDataString(searchTitle)}";
        using var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            System.Diagnostics.Debug.WriteLine($"[SGDB] Search '{normalizedTitle}' failed: HTTP {(int)response.StatusCode}.");
            return null;
        }

        var payload = await response.Content.ReadFromJsonSafe<SearchResponse>(ct);
        var results = payload?.Data;
        if (results is null || results.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine($"[SGDB] No game match for '{normalizedTitle}'.");
            return null;
        }

        // Autocomplete order alone is not a reliable match for editions,
        // remasters and similarly named games. Prefer exact title, then a
        // contained title; do not silently apply artwork from an unrelated game.
        var exact = results.FirstOrDefault(x =>
            string.Equals(NormalizeGameTitle(x.Name), comparableTitle, StringComparison.Ordinal));
        var partial = exact ?? results.FirstOrDefault(x =>
        {
            var candidate = NormalizeGameTitle(x.Name);
            return candidate.Length >= 4 &&
                   (comparableTitle.Contains(candidate, StringComparison.Ordinal) ||
                    candidate.Contains(comparableTitle, StringComparison.Ordinal));
        });

        if (partial is null)
        {
            System.Diagnostics.Debug.WriteLine($"[SGDB] Ambiguous autocomplete for '{normalizedTitle}'; refusing unrelated first result.");
            return null;
        }

        System.Diagnostics.Debug.WriteLine($"[SGDB] Matched '{normalizedTitle}' to '{partial.Name}' (id={partial.Id}).");
        return partial.Id;
    }

    private static string NormalizeGameTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        // Remove trademarks and punctuation differences (e.g. eFootball™),
        // while preserving letters and digits for safe title matching.
        var normalized = new string(value
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
            .ToArray());
        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private async Task<IReadOnlyList<SteamGridDbImage>> FetchGridCandidates(string url, CancellationToken ct, bool? wideOnly = null)
    {
        using var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return Array.Empty<SteamGridDbImage>();

        var payload = await response.Content.ReadFromJsonSafe<GridResponse>(ct);
        if (payload?.Data is null) return Array.Empty<SteamGridDbImage>();

        var query = payload.Data.Where(g => !g.Nsfw && !g.Humor);

        if (wideOnly == true) query = query.Where(g => g.Width > g.Height);
        else if (wideOnly == false) query = query.Where(g => g.Height > g.Width);

        return query
            .OrderByDescending(g => g.Score)
            .Take(MaxCandidates)
            .Select(g => new SteamGridDbImage { Id = g.Id, Url = g.Url, Width = g.Width, Height = g.Height })
            .ToList();
    }

    public void Dispose() => _http.Dispose();

    private sealed class SearchResponse { public List<SearchResult>? Data { get; set; } }
    private sealed class SearchResult
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
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