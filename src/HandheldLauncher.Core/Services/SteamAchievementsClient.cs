using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// Thin client pra duas chamadas da Steam Web API — mesma filosofia de
/// SteamOwnedGamesClient (HttpClient + System.Text.Json puro, DTOs com
/// [JsonPropertyName] explícito porque a Steam responde em snake_case/
/// lowercase, não em camelCase):
///
/// <list type="bullet">
/// <item>ISteamUserStats/GetSchemaForGame/v2 — metadados de TODAS as
/// conquistas do jogo (nome, descrição, ícone desbloqueado/bloqueado),
/// iguais pra qualquer conta. https://partner.steamgames.com/doc/webapi/ISteamUserStats#GetSchemaForGame</item>
/// <item>ISteamUserStats/GetPlayerAchievements/v0001 — quais dessas o
/// SteamId64 configurado já desbloqueou. https://partner.steamgames.com/doc/webapi/ISteamUserStats#GetPlayerAchievements</item>
/// </list>
///
/// `l=brazilian` nas duas chamadas pede nome/descrição em português — a
/// Steam cai pro inglês sozinha se o jogo não tiver essa tradução, então
/// não precisa de nenhum fallback manual aqui.
///
/// As duas falhas "normais" (jogo sem conquista nenhuma; perfil Steam
/// privado) nunca lançam exceção — ver comentário em
/// ISteamAchievementsClient.GetAchievementsAsync. GetPlayerAchievements em
/// particular devolve HTTP 400 pra jogo sem estatística E 200 com
/// "success": false pra perfil privado — os dois casos tratados aqui como
/// "não consegui checar o progresso", não como erro.
/// </summary>
public sealed class SteamAchievementsClient : ISteamAchievementsClient, IDisposable
{
    private const string SchemaUrl = "https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/";
    private const string PlayerAchievementsUrl = "https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v0001/";

    private readonly HttpClient _http = new();
    private readonly string? _apiKey;
    private readonly string? _steamId64;

    public SteamAchievementsClient(string? apiKey, string? steamId64)
    {
        _apiKey = apiKey;
        _steamId64 = steamId64;
    }

    public bool HasCredentials => !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_steamId64);

    public async Task<SteamAchievementsResult> GetAchievementsAsync(uint appId, CancellationToken ct = default)
    {
        if (!HasCredentials)
        {
            throw new InvalidOperationException(
                "SteamWebApiKey e/ou SteamId64 não configurados em settings.json — ver README.");
        }

        var schemaAchievements = await GetSchemaAchievementsAsync(appId, ct);
        if (schemaAchievements.Count == 0)
        {
            // Jogo sem conquista nenhuma — comum, não é erro.
            return new SteamAchievementsResult { Achievements = Array.Empty<SteamAchievementInfo>() };
        }

        var (progress, playerDataUnavailable) = await GetPlayerProgressAsync(appId, ct);

        var achievements = schemaAchievements
            .Select(schema =>
            {
                progress.TryGetValue(schema.ApiName, out var p);

                // 🟢 CORREÇÃO AQUI: p pode ser null se o jogador não desbloqueou a conquista.
                // Sem isso, o p.Achieved lançava NullReferenceException e quebrava a tela.
                var achieved = p?.Achieved ?? false;
                var unlockedAt = (p is not null && p.Achieved && p.UnlockTimeUnix > 0)
                    ? DateTimeOffset.FromUnixTimeSeconds(p.UnlockTimeUnix)
                    : (DateTimeOffset?)null;

                return new SteamAchievementInfo
                {
                    ApiName = schema.ApiName,
                    DisplayName = schema.DisplayName,
                    Description = schema.Description,
                    IconUrl = schema.IconUrl,
                    IconGrayUrl = schema.IconGrayUrl,
                    Achieved = achieved,
                    UnlockedAtUtc = unlockedAt,
                };
            })
            .ToList();

        return new SteamAchievementsResult
        {
            Achievements = achievements,
            PlayerDataUnavailable = playerDataUnavailable,
        };
    }

    private async Task<List<SchemaAchievementDto>> GetSchemaAchievementsAsync(uint appId, CancellationToken ct)
    {
        var url = $"{SchemaUrl}?key={Uri.EscapeDataString(_apiKey!)}&appid={appId}&l=brazilian";

        using var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            // AppId inválido ou sem schema — trata igual a "sem conquistas".
            return new List<SchemaAchievementDto>();
        }

        var envelope = await response.Content.ReadFromJsonAsync<SchemaEnvelope>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

        return envelope?.Game?.AvailableGameStats?.Achievements ?? new List<SchemaAchievementDto>();
    }

    /// <summary>
    /// Devolve (apiname → desbloqueada/quando) + um aviso de "não consegui
    /// checar" (perfil privado, sem estatística pra esse jogo, ou qualquer
    /// outro problema de rede) — nunca lança.
    /// </summary>
    private async Task<(Dictionary<string, PlayerAchievementDto> Progress, bool Unavailable)> GetPlayerProgressAsync(
        uint appId, CancellationToken ct)
    {
        try
        {
            var url = $"{PlayerAchievementsUrl}?key={Uri.EscapeDataString(_apiKey!)}" +
                      $"&steamid={Uri.EscapeDataString(_steamId64!)}&appid={appId}&l=brazilian";

            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                // Comum pra jogo sem estatística (HTTP 400) — não é bug.
                return (new Dictionary<string, PlayerAchievementDto>(), true);
            }

            var envelope = await response.Content.ReadFromJsonAsync<PlayerAchievementsEnvelope>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

            var stats = envelope?.PlayerStats;
            if (stats is null || !stats.Success || stats.Achievements is null)
            {
                // "success": false — normalmente perfil/"Detalhes do jogo" privado.
                return (new Dictionary<string, PlayerAchievementDto>(), true);
            }

            var byApiName = stats.Achievements
                .Where(a => !string.IsNullOrWhiteSpace(a.ApiName))
                .ToDictionary(a => a.ApiName, StringComparer.OrdinalIgnoreCase);

            return (byApiName, false);
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            // Melhor esforço — nunca deixa a tela de detalhes quebrar por
            // causa só do progresso (os nomes/descrições já vieram do schema).
            return (new Dictionary<string, PlayerAchievementDto>(), true);
        }
    }

    public void Dispose() => _http.Dispose();

    private sealed class SchemaEnvelope
    {
        [JsonPropertyName("game")]
        public SchemaGameDto? Game { get; set; }
    }

    private sealed class SchemaGameDto
    {
        [JsonPropertyName("availableGameStats")]
        public SchemaStatsDto? AvailableGameStats { get; set; }
    }

    private sealed class SchemaStatsDto
    {
        [JsonPropertyName("achievements")]
        public List<SchemaAchievementDto>? Achievements { get; set; }
    }

    private sealed class SchemaAchievementDto
    {
        [JsonPropertyName("name")]
        public string ApiName { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("icon")]
        public string? IconUrl { get; set; }

        [JsonPropertyName("icongray")]
        public string? IconGrayUrl { get; set; }
    }

    private sealed class PlayerAchievementsEnvelope
    {
        [JsonPropertyName("playerstats")]
        public PlayerStatsDto? PlayerStats { get; set; }
    }

    private sealed class PlayerStatsDto
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("achievements")]
        public List<PlayerAchievementDto>? Achievements { get; set; }
    }

    private sealed class PlayerAchievementDto
    {
        [JsonPropertyName("apiname")]
        public string ApiName { get; set; } = string.Empty;

        [JsonPropertyName("achieved")]
        public int AchievedRaw { get; set; }

        [JsonPropertyName("unlocktime")]
        public long UnlockTimeUnix { get; set; }

        public bool Achieved => AchievedRaw != 0;
    }
}