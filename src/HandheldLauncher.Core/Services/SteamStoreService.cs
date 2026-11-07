using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

public class SteamStoreService : ISteamAchievementsClient
{
    private readonly HttpClient _http;
    private readonly Dictionary<uint, GameMetadata> _cache = new();
    private readonly Dictionary<uint, SteamReviewSummary?> _reviewCache = new();
    private readonly ConcurrentDictionary<uint, (DateTime FetchTime, SteamAchievementsResult Result)> _achievementsCache = new();

    public string? ApiKey { get; set; }
    public ulong SteamId64 { get; set; }
    public bool HasCredentials => !string.IsNullOrEmpty(ApiKey) && SteamId64 > 0;

    public SteamStoreService(string? apiKey = null, string? steamId64 = null)
    {
        ApiKey = apiKey;
        if (ulong.TryParse(steamId64, out var id)) SteamId64 = id;

        _http = new HttpClient();
        var version = Assembly.GetEntryAssembly()?
            .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
            .OfType<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "dev";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"HandheldLauncher/{version}");
    }

    // ... (FetchMetadataAsync e FetchReviewSummaryAsync permanecem iguais) ...
    public async Task<GameMetadata?> FetchMetadataAsync(uint appId)
    {
        if (_cache.TryGetValue(appId, out var cached)) return cached;
        try
        {
            var meta = await FetchMetadataInternalAsync(appId, "portuguese");
            if (string.IsNullOrWhiteSpace(meta?.Description) && string.IsNullOrWhiteSpace(meta?.ShortDescription))
                meta = await FetchMetadataInternalAsync(appId, "english");
            if (meta != null) _cache[appId] = meta;
            return meta;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[STEAM] Falha metadata: {ex.Message}"); }
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
                Description      = data.TryGetProperty("detailed_description", out var desc)      ? desc.GetString() ?? "" : "",
                ShortDescription = data.TryGetProperty("short_description",    out var shortDesc) ? shortDesc.GetString() ?? "" : "",
                Developer        = data.TryGetProperty("developers", out var devs) && devs.GetArrayLength() > 0 ? devs[0].GetString() ?? "" : "",
                Genres           = data.TryGetProperty("genres", out var genres) ? string.Join(", ", GetGenres(genres)) : ""
            };
            meta.Description = System.Text.RegularExpressions.Regex.Replace(meta.Description ?? "", "<.*?>", string.Empty);
            return meta;
        }
        return null;
    }

    public async Task<SteamReviewSummary?> FetchReviewSummaryAsync(uint appId)
    {
        if (_reviewCache.TryGetValue(appId, out var cached)) return cached;
        try
        {
            var url = $"https://api.steampowered.com/IUserReviewsService/GetAppReviews/v1/?input_json=" +
                      Uri.EscapeDataString($"{{\"appid\":{appId},\"filter\":1,\"languages\":[\"all\"],\"purchase_type\":1,\"num_per_page\":0}}");
            var response = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            if (!root.TryGetProperty("response", out var responseObj) || !responseObj.TryGetProperty("query_summary", out var qs)) return null;
            int total = qs.TryGetProperty("total_reviews", out var tr) ? tr.GetInt32() : 0;
            int positive = qs.TryGetProperty("total_positive", out var tp) ? tp.GetInt32() : 0;
            if (total == 0) { _reviewCache[appId] = null; return null; }
            int pct = (int)Math.Round(positive * 100.0 / total);
            var summary = new SteamReviewSummary { Percentage = pct, TotalReviews = total, PositiveReviews = positive, ScoreDescription = qs.TryGetProperty("review_score_desc", out var d) ? d.GetString() ?? "" : "", StarRating = Math.Clamp((int)Math.Round(pct / 20.0), 1, 5) };
            _reviewCache[appId] = summary;
            return summary;
        }
        catch { _reviewCache[appId] = null; return null; }
    }

    private static string[] GetGenres(JsonElement genres)
    {
        var result = new string[genres.GetArrayLength()];
        for (int i = 0; i < result.Length; i++) result[i] = genres[i].TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
        return result;
    }

    // 🟢 DEBUG: Buscando a causa real do erro
    public async Task<SteamAchievementsResult> GetAchievementsAsync(uint appId, CancellationToken cancellationToken = default)
    {
        if (!HasCredentials)
            return new SteamAchievementsResult { Achievements = new List<SteamAchievementInfo>(), PlayerDataUnavailableReason = "Sem credenciais." };

        if (_achievementsCache.TryGetValue(appId, out var cached))
            if (DateTime.UtcNow - cached.FetchTime < TimeSpan.FromMinutes(30)) return cached.Result;

        try
        {
            var schemaTask = FetchSchemaAsync(appId, ApiKey!, cancellationToken);
            var playerTask = FetchPlayerAchievementsAsync(appId, ApiKey!, SteamId64, cancellationToken);
            await Task.WhenAll(schemaTask, playerTask);

            var schema = schemaTask.Result;
            var player = playerTask.Result;

            if (schema == null || schema.Count == 0)
                return new SteamAchievementsResult { Achievements = new List<SteamAchievementInfo>() };

            var result = new SteamAchievementsResult { Achievements = new List<SteamAchievementInfo>() };

            if (player == null)
            {
                result.PlayerDataUnavailable = true;
                result.PlayerDataUnavailableReason = "A Steam recusou o acesso ao seu progresso. (Verifique o log de debug para detalhes)";
                // Se quiser ver o motivo no console, o FetchPlayerAchievementsAsync fará o log.
            }

            foreach (var kvp in schema)
            {
                var apiName = kvp.Key;
                var sData = kvp.Value;
                bool achieved = false;
                DateTimeOffset? unlockTime = null;

                if (player != null && player.TryGetValue(apiName, out var pData))
                {
                    achieved = pData.Achieved;
                    if (achieved && pData.UnlockTime > 0) unlockTime = DateTimeOffset.FromUnixTimeSeconds(pData.UnlockTime);
                }

                result.Achievements.Add(new SteamAchievementInfo
                {
                    ApiName = apiName, DisplayName = sData.DisplayName, Description = sData.Description,
                    Achieved = achieved, IconUrl = sData.IconUrl, IconGrayUrl = sData.IconGrayUrl, UnlockedAtUtc = unlockTime
                });
            }

            _achievementsCache[appId] = (DateTime.UtcNow, result);
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[STEAM] Erro fatal em GetAchievements: {ex.Message}");
            return new SteamAchievementsResult { Achievements = new List<SteamAchievementInfo>(), PlayerDataUnavailable = true, PlayerDataUnavailableReason = ex.Message };
        }
    }

    private async Task<Dictionary<string, SteamSchemaItem>?> FetchSchemaAsync(uint appId, string key, CancellationToken ct)
    {
        try
        {
            var url = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={key}&appid={appId}&l=portuguese";
            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("game", out var game)) return null;
            if (!game.TryGetProperty("availableGameStats", out var stats)) return null;
            if (!stats.TryGetProperty("achievements", out var achievements)) return null;

            var dict = new Dictionary<string, SteamSchemaItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in achievements.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name == null) continue;
                dict[name] = new SteamSchemaItem
                {
                    DisplayName = a.TryGetProperty("displayName", out var dn) ? dn.GetString() : name,
                    Description = a.TryGetProperty("description", out var d) ? d.GetString() : null,
                    IconUrl = a.TryGetProperty("icon", out var i) ? i.GetString() : null,
                    IconGrayUrl = a.TryGetProperty("icongray", out var ig) ? ig.GetString() : null
                };
            }
            return dict;
        }
        catch { return null; }
    }

    private async Task<Dictionary<string, SteamPlayerAchievement>?> FetchPlayerAchievementsAsync(uint appId, string key, ulong steamId, CancellationToken ct)
    {
        try
        {
            var url = $"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/?appid={appId}&key={key}&steamid={steamId}&l=portuguese";
            var json = await _http.GetStringAsync(url, ct);
            System.Diagnostics.Debug.WriteLine($"[STEAM DEBUG] Resposta API Conquistas: {json}"); // 🟢 PRINT NO CONSOLE

            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("playerstats", out var playerStats))
            {
                System.Diagnostics.Debug.WriteLine("[STEAM DEBUG] JSON não tem 'playerstats'.");
                return null;
            }

            if (playerStats.TryGetProperty("error", out var errorProp))
            {
                var errorMsg = errorProp.GetString();
                System.Diagnostics.Debug.WriteLine($"[STEAM DEBUG] ERRO DA STEAM: {errorMsg}"); // 🟢 AQUI ESTÁ O SEGREDO
                return null;
            }

            if (!playerStats.TryGetProperty("achievements", out var achievements))
            {
                System.Diagnostics.Debug.WriteLine("[STEAM DEBUG] JSON não tem 'achievements'.");
                return null;
            }

            var dict = new Dictionary<string, SteamPlayerAchievement>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in achievements.EnumerateArray())
            {
                var apiName = a.TryGetProperty("apiname", out var n) ? n.GetString() : null;
                if (apiName == null) continue;
                var achieved = a.TryGetProperty("achieved", out var ac) && ac.GetInt32() == 1;
                var unlockTime = a.TryGetProperty("unlocktime", out var ut) ? ut.GetInt64() : 0;
                dict[apiName] = new SteamPlayerAchievement { Achieved = achieved, UnlockTime = unlockTime };
            }
            return dict;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[STEAM DEBUG] EXCEPTION API Conquistas: {ex.Message}");
            return null;
        }
    }
}

// --- MODELOS ---
public class GameMetadata { public string? Description { get; set; } public string? ShortDescription { get; set; } public string? Developer { get; set; } public string? Genres { get; set; } public int? IgdbRating { get; set; } }
public sealed class SteamReviewSummary { public int Percentage { get; init; } public int TotalReviews { get; init; } public int PositiveReviews { get; init; } public string ScoreDescription { get; init; } = ""; public int StarRating { get; init; } public bool HasEnoughReviews => TotalReviews >= 10; }
public sealed class SteamSchemaItem { public string? DisplayName { get; set; } public string? Description { get; set; } public string? IconUrl { get; set; } public string? IconGrayUrl { get; set; } }
public sealed class SteamPlayerAchievement { public bool Achieved { get; set; } public long UnlockTime { get; set; } }

// 🟢 Adicionando a propriedade de motivo no Resultado
public sealed class SteamAchievementsResult
{
    public IList<SteamAchievementInfo> Achievements { get; set; } = new List<SteamAchievementInfo>();
    public bool PlayerDataUnavailable { get; set; }
    public string? PlayerDataUnavailableReason { get; set; }
}