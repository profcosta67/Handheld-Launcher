using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// Thin client for Steam's IPlayerService/GetOwnedGames Web API endpoint —
/// mesma filosofia do SteamGridDbClient (HttpClient + System.Text.Json
/// puro, zero pacotes NuGet novos). Endpoint público, documentado em
/// https://partner.steamgames.com/doc/webapi/IPlayerService#GetOwnedGames.
///
/// Diferente da SteamGridDB (que responde em camelCase e já tem um helper
/// de parse reusável — ver SteamGridDbClient/HttpContentJsonExtensions na
/// pasta Artwork), a Steam Web API responde em snake_case
/// ("playtime_forever", "game_count"), então os DTOs aqui usam
/// [JsonPropertyName] explícito em vez de confiar em
/// PropertyNameCaseInsensitive (que só ignora MAIÚSCULA/minúscula, não
/// resolve a diferença entre "PlaytimeForever" e "playtime_forever").
/// </summary>
public sealed class SteamOwnedGamesClient : ISteamOwnedGamesClient, IDisposable
{
    private const string Url = "https://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/";

    private readonly HttpClient _http = new();
    private readonly string? _apiKey;
    private readonly string? _steamId64;

    public SteamOwnedGamesClient(string? apiKey, string? steamId64)
    {
        _apiKey = apiKey;
        _steamId64 = steamId64;
    }

    public bool HasCredentials => !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_steamId64);

    public async Task<IReadOnlyList<OwnedSteamGame>> GetOwnedGamesAsync(CancellationToken ct = default)
    {
        if (!HasCredentials)
        {
            throw new InvalidOperationException(
                "SteamWebApiKey e/ou SteamId64 não configurados em settings.json — ver README.");
        }

        // include_appinfo=true pra vir o "name" junto (sem isso a API só
        // devolve appid/playtime, e teríamos que resolver o nome de outro
        // jeito). include_played_free_games=true pra não esconder jogos
        // grátis já jogados (comportamento padrão da API é excluir eles
        // de "owned games" se playtime_forever==0 — igual acontece com
        // jogos pagos nunca jogados, mas pra free games isso parecia
        // esconder jogo de verdade da conta).
        var url = $"{Url}?key={Uri.EscapeDataString(_apiKey!)}&steamid={Uri.EscapeDataString(_steamId64!)}" +
                  "&format=json&include_appinfo=true&include_played_free_games=true";

        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<OwnedGamesEnvelope>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

        var games = envelope?.Response?.Games;
        if (games is null) return Array.Empty<OwnedSteamGame>();

        return games
            .Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .Select(g => new OwnedSteamGame
            {
                AppId = g.AppId,
                Title = g.Name,
                PlaytimeMinutes = g.PlaytimeForever,
            })
            .ToList();
    }

    public void Dispose() => _http.Dispose();

    private sealed class OwnedGamesEnvelope
    {
        [JsonPropertyName("response")]
        public OwnedGamesResponse? Response { get; set; }
    }

    private sealed class OwnedGamesResponse
    {
        [JsonPropertyName("game_count")]
        public int GameCount { get; set; }

        [JsonPropertyName("games")]
        public List<OwnedGameDto>? Games { get; set; }
    }

    private sealed class OwnedGameDto
    {
        [JsonPropertyName("appid")]
        public uint AppId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("playtime_forever")]
        public long PlaytimeForever { get; set; }
    }
}
