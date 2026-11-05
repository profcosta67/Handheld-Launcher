using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    public void UpdateApiKey(string? apiKey)
    {
        _apiKey = apiKey;
        _http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(apiKey)
            ? null
            : new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    // v0.17 (05/11, bug reportado "logo não baixa/aplica"): os 4 métodos
    // abaixo compartilhavam GetBestStaticFor...Async, cujo parâmetro
    // wideOnly tinha padrão `false` (não `null`). Para "grids" isso era
    // proposital (capas são retrato, altura > largura). Mas logos/ícones
    // OMITIAM o parâmetro pra "usar o padrão" esperando "sem filtro", e
    // caíam nesse mesmo `false` — que filtra só imagens com altura MAIOR
    // que a largura. Logo é sempre mais largo que alto (wordmark), então
    // praticamente nenhum resultado real de /logos/ passava nesse filtro;
    // ícones (quase sempre quadrados) também perdiam a maioria. Agora cada
    // categoria passa o valor certo explicitamente: grids continua
    // retrato, heroes/wide-grids continuam paisagem, logos/ícones não
    // filtram por proporção (null).
    public Task<SteamGridDbImage?> GetBestGridForSteamAppAsync(uint steamAppId, CancellationToken ct = default)
        => GetBestStaticForSteamAppAsync("grids", steamAppId, ct, wideOnly: false);

    public Task<SteamGridDbImage?> GetBestGridForTitleAsync(string title, CancellationToken ct = default)
        => GetBestStaticForTitleAsync("grids", title, ct, wideOnly: false);

    public Task<SteamGridDbImage?> GetBestHeroForSteamAppAsync(uint steamAppId, CancellationToken ct = default)
        => GetBestStaticForSteamAppAsync("heroes", steamAppId, ct, wideOnly: true);

    public Task<SteamGridDbImage?> GetBestHeroForTitleAsync(string title, CancellationToken ct = default)
        => GetBestStaticForTitleAsync("heroes", title, ct, wideOnly: true);

    public Task<SteamGridDbImage?> GetBestLogoForSteamAppAsync(uint steamAppId, CancellationToken ct = default)
        => GetBestStaticForSteamAppAsync("logos", steamAppId, ct, wideOnly: null);

    public Task<SteamGridDbImage?> GetBestLogoForTitleAsync(string title, CancellationToken ct = default)
        => GetBestStaticForTitleAsync("logos", title, ct, wideOnly: null);

    public Task<SteamGridDbImage?> GetBestIconForSteamAppAsync(uint steamAppId, CancellationToken ct = default)
        => GetBestStaticForSteamAppAsync("icons", steamAppId, ct, wideOnly: null);

    public Task<SteamGridDbImage?> GetBestIconForTitleAsync(string title, CancellationToken ct = default)
        => GetBestStaticForTitleAsync("icons", title, ct, wideOnly: null);

    public Task<SteamGridDbImage?> GetBestWideGridForSteamAppAsync(uint steamAppId, CancellationToken ct = default)
        => GetBestStaticForSteamAppAsync("grids", steamAppId, ct, wideOnly: true);

    public Task<SteamGridDbImage?> GetBestWideGridForTitleAsync(string title, CancellationToken ct = default)
        => GetBestStaticForTitleAsync("grids", title, ct, wideOnly: true);

    private async Task<SteamGridDbImage?> GetBestStaticForSteamAppAsync(
        string category, uint steamAppId, CancellationToken ct, bool? wideOnly = null, string extraQuery = "")
    {
        if (!HasApiKey) return null;
        return (await FetchGridCandidates(
            $"{BaseUrl}/{category}/steam/{steamAppId}?types=static{extraQuery}", ct, wideOnly)).FirstOrDefault();
    }

    private async Task<SteamGridDbImage?> GetBestStaticForTitleAsync(
        string category, string title, CancellationToken ct, bool? wideOnly = null, string extraQuery = "")
    {
        if (!HasApiKey) return null;
        var gameId = await SearchGameId(title, ct);
        if (gameId is null) return null;
        return (await FetchGridCandidates(
            $"{BaseUrl}/{category}/game/{gameId}?types=static{extraQuery}", ct, wideOnly)).FirstOrDefault();
    }

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

        // v0.17 (05/11, bug reportado como "voltou"): comparableTitle existe
        // para COMPARAR o nome local com o resultado da SGDB sem distinguir
        // acento/caixa (ex.: pasta "Pokemon" vs. nome oficial "Pokémon" na
        // API). Até a v0.15.2 esse MESMO texto sem acento também era usado
        // como o TERMO DE BUSCA enviado à API — e a busca da SteamGridDB não
        // acha "Pokémon" a partir de "pokemon" sem o acento. A query precisa
        // ir com o título original (com acento); só a comparação do
        // resultado é que deve ignorar acento.
        var url = $"{BaseUrl}/search/autocomplete/{Uri.EscapeDataString(normalizedTitle)}";
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

    // internal (não private) só pra ficar testável por
    // HandheldLauncher.Core.Tests (InternalsVisibleTo no .csproj) — mesmo
    // comportamento, continua fora da API pública do Core.
    internal static string NormalizeGameTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        value = Regex.Replace(value, @"-\d{3}$", "");

        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        var result = new string(sb.ToString()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
            .ToArray());
            
        return string.Join(' ', result.Split(' ', StringSplitOptions.RemoveEmptyEntries));
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