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
    // 🟢 Cache em memória para não bater na API da Steam a cada troca de tile
    private readonly Dictionary<uint, GameMetadata> _cache = new();

    public SteamStoreService()
    {
        _http = new HttpClient();
        // Versão lida do assembly (que por sua vez vem do arquivo VERSION na
        // raiz, via HandheldLauncher.App.csproj) em vez de fixa no código —
        // evita repetir o número e ele ficar desatualizado como "15.6" estava.
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
            // Pede em inglês por padrão (a API não tem português pra maioria dos jogos novos)
            var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&l=english";
            var response = await _http.GetStringAsync(url);
            
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty(appId.ToString(), out var gameObj) &&
                gameObj.TryGetProperty("success", out var success) && success.GetBoolean())
            {
                var data = gameObj.GetProperty("data");
                
                var meta = new GameMetadata
                {
                    Description = data.TryGetProperty("detailed_description", out var desc) ? desc.GetString() ?? "" : "",
                    ShortDescription = data.TryGetProperty("short_description", out var shortDesc) ? shortDesc.GetString() ?? "" : "",
                    Developer = data.TryGetProperty("developers", out var devs) && devs.GetArrayLength() > 0 ? devs[0].GetString() ?? "" : "",
                    Genres = data.TryGetProperty("genres", out var genres) ? string.Join(", ", GetGenres(genres)) : ""
                };

                // Remove tags HTML (como <b> e <br>) da descrição detalhada
                meta.Description = System.Text.RegularExpressions.Regex.Replace(meta.Description ?? "", "<.*?>", string.Empty);

                _cache[appId] = meta;
                return meta;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[STEAM] Falha ao buscar metadata: {ex.Message}");
        }
        return null;
    }

    private static string[] GetGenres(JsonElement genres)
    {
        var result = new string[genres.GetArrayLength()];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = genres[i].TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
        }
        return result;
    }
}

public class GameMetadata
{
    public string? Description { get; set; }
    public string? ShortDescription { get; set; }
    public string? Developer { get; set; }
    public string? Genres { get; set; }
}