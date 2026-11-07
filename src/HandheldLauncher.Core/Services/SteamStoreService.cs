using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace HandheldLauncher.Core.Services;

public class SteamStoreService
{
    private readonly HttpClient _http;
    private readonly Dictionary<uint, GameMetadata> _cache = new();
    private readonly Dictionary<uint, SteamReviewSummary?> _reviewCache = new();

    public SteamStoreService()
    {
        _http = new HttpClient();
        var version = Assembly.GetEntryAssembly()?
            .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
            .OfType<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "dev";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"HandheldLauncher/{version}");
    }

    public async Task<GameMetadata?> FetchMetadataAsync(uint appId)
    {
        if (_cache.TryGetValue(appId, out var cached))
            return cached;

        try
        {
            // 🟢 Tenta PORTUGUÊS primeiro
            var meta = await FetchMetadataInternalAsync(appId, "portuguese");
            
            // 🟢 Se não veio descrição em PT, tenta INGLÊS
            if (string.IsNullOrWhiteSpace(meta?.Description) && string.IsNullOrWhiteSpace(meta?.ShortDescription))
            {
                meta = await FetchMetadataInternalAsync(appId, "english");
            }

            if (meta != null) _cache[appId] = meta;
            return meta;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[STEAM] Falha ao buscar metadata: {ex.Message}");
        }
        return null;
    }

    private async Task<GameMetadata?> FetchMetadataInternalAsync(uint appId, string lang)
    {
        var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&l={lang}";
        var response = await _http.GetStringAsync(url);

        using var doc = JsonDocument.Parse(response);
        if (doc.RootElement.TryGetProperty(appId.ToString(), out var gameObj) &&
            gameObj.TryGetProperty("success", out var success) && success.GetBoolean())
        {
            var data = gameObj.GetProperty("data");

            var meta = new GameMetadata
            {
                Description      = data.TryGetProperty("detailed_description", out var desc)      ? desc.GetString()      ?? "" : "",
                ShortDescription = data.TryGetProperty("short_description",    out var shortDesc) ? shortDesc.GetString() ?? "" : "",
                Developer        = data.TryGetProperty("developers", out var devs) && devs.GetArrayLength() > 0
                                    ? devs[0].GetString() ?? "" : "",
                Genres           = data.TryGetProperty("genres", out var genres)
                                    ? string.Join(", ", GetGenres(genres)) : ""
            };

            meta.Description = System.Text.RegularExpressions.Regex.Replace(meta.Description ?? "", "<.*?>", string.Empty);
            return meta;
        }
        return null;
    }

    public async Task<SteamReviewSummary?> FetchReviewSummaryAsync(uint appId)
    {
        if (_reviewCache.TryGetValue(appId, out var cached))
            return cached;

        try
        {
            var url = $"https://api.steampowered.com/IUserReviewsService/GetAppReviews/v1/?input_json=" +
                      Uri.EscapeDataString($"{{\"appid\":{appId},\"filter\":1,\"languages\":[\"all\"],\"purchase_type\":1,\"num_per_page\":0}}");
            var response = await _http.GetStringAsync(url);

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (!root.TryGetProperty("response", out var responseObj) || !responseObj.TryGetProperty("query_summary", out var qs))
                return null;

            int total    = qs.TryGetProperty("total_reviews",  out var tr) ? tr.GetInt32() : 0;
            int positive = qs.TryGetProperty("total_positive", out var tp) ? tp.GetInt32() : 0;

            if (total == 0) { _reviewCache[appId] = null; return null; }

            int pct = (int)Math.Round(positive * 100.0 / total);
            var summary = new SteamReviewSummary
            {
                Percentage       = pct,
                TotalReviews     = total,
                PositiveReviews  = positive,
                ScoreDescription = qs.TryGetProperty("review_score_desc", out var d) ? d.GetString() ?? "" : "",
                StarRating       = Math.Clamp((int)Math.Round(pct / 20.0), 1, 5),
            };
            _reviewCache[appId] = summary;
            return summary;
        }
        catch { _reviewCache[appId] = null; return null; }
    }

    private static string[] GetGenres(JsonElement genres)
    {
        var result = new string[genres.GetArrayLength()];
        for (int i = 0; i < result.Length; i++)
            result[i] = genres[i].TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
        return result;
    }
}

public class GameMetadata
{
    public string? Description { get; set; }
    public string? ShortDescription { get; set; }
    public string? Developer { get; set; }
    public string? Genres { get; set; }
    public int? IgdbRating { get; set; }
}

public sealed class SteamReviewSummary
{
    public int Percentage { get; init; }
    public int TotalReviews { get; init; }
    public int PositiveReviews { get; init; }
    public string ScoreDescription { get; init; } = "";
    public int StarRating { get; init; }
    public bool HasEnoughReviews => TotalReviews >= 10;
}