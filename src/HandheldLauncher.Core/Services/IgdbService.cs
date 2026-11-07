using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HandheldLauncher.Core.Services;

public class IgdbService
{
    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly string _clientSecret;
    
    private string? _accessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    private readonly Dictionary<string, GameMetadata?> _cache = new();

    public IgdbService(string clientId, string clientSecret)
    {
        _http = new HttpClient();
        _clientId = clientId;
        _clientSecret = clientSecret;
    }

    private async Task<string?> GetAccessTokenAsync()
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
            return _accessToken;

        try
        {
            var url = $"https://id.twitch.tv/oauth2/token?client_id={_clientId}&client_secret={_clientSecret}&grant_type=client_credentials";
            var response = await _http.PostAsync(url, null);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            _accessToken = doc.RootElement.GetProperty("access_token").GetString();
            var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);

            return _accessToken;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IGDB] Falha ao obter token: {ex.Message}");
            return null;
        }
    }

    public async Task<GameMetadata?> SearchGameAsync(string title)
    {
        if (_cache.TryGetValue(title, out var cached))
            return cached;

        if (string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret))
            return null;

        var token = await GetAccessTokenAsync();
        if (string.IsNullOrEmpty(token)) return null;

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games");
            request.Headers.Add("Client-ID", _clientId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var body = $"search \"{title}\"; fields name,summary,genres.name,rating; limit 1;";
            request.Content = new StringContent(body, Encoding.UTF8, "text/plain");

            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(json) || json == "[]")
            {
                _cache[title] = null;
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            var game = doc.RootElement[0];

            var meta = new GameMetadata();

            if (game.TryGetProperty("summary", out var summary))
                meta.Description = summary.GetString();

            if (game.TryGetProperty("genres", out var genres))
            {
                var genreList = new List<string>();
                foreach (var g in genres.EnumerateArray())
                {
                    if (g.TryGetProperty("name", out var name))
                        genreList.Add(name.GetString() ?? "");
                }
                meta.Genres = string.Join(", ", genreList);
            }
            
            if (game.TryGetProperty("rating", out var rating))
                meta.IgdbRating = (int)Math.Round(rating.GetDouble());

            _cache[title] = meta;
            return meta;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IGDB] Erro na busca '{title}': {ex.Message}");
            return null; // Não cacheia o erro — permite nova tentativa
        }
    }
}