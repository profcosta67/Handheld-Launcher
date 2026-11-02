using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Platform;

public class RawgMetadataService
{
    private readonly HttpClient _http = new();

    public async Task<RawgGameData?> GetGameDetailsAsync(string title)
    {
        try
        {
            DebugLog.Write($"[RAWG] === Início (título='{title}') ===");

            var settings = AppSettingsStore.Load();
            var apiKey = settings.RawgApiKey;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                DebugLog.Write("[RAWG] API Key não configurada — abortando.");
                return null;
            }

            var cleanTitle = Uri.EscapeDataString(title.Trim());
            
            // --- PASSO 1: BUSCA ---
            // page_size=5 pra dar margem de escolha (a RAWG costuma trazer o jogo mais famoso primeiro)
            var searchUrl = $"https://api.rawg.io/api/games?search={cleanTitle}&key={apiKey}&page_size=5";
            DebugLog.Write($"[RAWG] GET search='{cleanTitle}' (top 5)");

            var searchResponse = await _http.GetStringAsync(searchUrl);
            using var searchDoc = JsonDocument.Parse(searchResponse);

            if (!searchDoc.RootElement.TryGetProperty("results", out var results))
            {
                DebugLog.Write("[RAWG] Sem 'results' na resposta da busca.");
                return null;
            }

            if (results.GetArrayLength() == 0)
            {
                DebugLog.Write("[RAWG] Nenhum resultado para a busca.");
                return null;
            }

            // --- PASSO 2: ESCOLHER O JOGO CERTO ---
            JsonElement game = results[0];
            bool foundMatch = false;

            foreach (var item in results.EnumerateArray())
            {
                var name = item.GetProperty("name").GetString() ?? "";
                
                // Tenta match exato ou muito próximo (ignora maiúsculas/minúsculas)
                if (string.Equals(name, title.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    name.Contains(title, StringComparison.OrdinalIgnoreCase) ||
                    title.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    game = item;
                    foundMatch = true;
                    DebugLog.Write($"[RAWG] Match exato encontrado: '{name}'");
                    break;
                }
            }

            if (!foundMatch)
            {
                DebugLog.Write($"[RAWG] Nenhum match exato. Usando o primeiro resultado: '{game.GetProperty("name").GetString()}'");
            }

            var gameId = game.GetProperty("id").GetInt32();

            // --- PASSO 3: BUSCAR DETALHES (pra pegar a descrição) ---
            var detailsUrl = $"https://api.rawg.io/api/games/{gameId}?key={apiKey}";
            DebugLog.Write($"[RAWG] GET details/{gameId}");

            var detailsResponse = await _http.GetStringAsync(detailsUrl);
            using var detailsDoc = JsonDocument.Parse(detailsResponse);
            var details = detailsDoc.RootElement;

            string desc = "";
            if (details.TryGetProperty("description_raw", out var descProp))
                desc = descProp.GetString() ?? "";

            // Fallback limpando HTML
            if (string.IsNullOrWhiteSpace(desc) && details.TryGetProperty("description", out var descHtml))
                desc = System.Text.RegularExpressions.Regex.Replace(descHtml.GetString() ?? "", @"<[^>]+>", " ");

            var developers = new List<string>();
            if (details.TryGetProperty("developers", out var devs))
                developers.AddRange(devs.EnumerateArray().Select(d => d.GetProperty("name").GetString() ?? ""));

            var genres = new List<string>();
            if (details.TryGetProperty("genres", out var gens))
                genres.AddRange(gens.EnumerateArray().Select(g => g.GetProperty("name").GetString() ?? ""));

            string releaseDate = "";
            if (details.TryGetProperty("released", out var rel))
                releaseDate = rel.GetString() ?? "";

            DebugLog.Write($"[RAWG] Sucesso: desc={desc.Length}ch, devs={developers.Count}, genres={genres.Count}, release={releaseDate}");

            return new RawgGameData
            {
                Title = details.GetProperty("name").GetString() ?? title,
                Description = desc.Trim(),
                Developers = developers,
                Genres = genres,
                ReleaseDate = releaseDate
            };
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[RAWG] EXCEPTION: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }

        return null;
    }
}

public class RawgGameData
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Developers { get; set; } = new();
    public List<string> Genres { get; set; } = new();
    public string ReleaseDate { get; set; } = "";
}