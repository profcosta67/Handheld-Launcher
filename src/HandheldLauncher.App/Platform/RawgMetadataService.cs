using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Platform;

public class RawgMetadataService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions CacheJsonOpts = new() { WriteIndented = true };

    private static string CacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HandheldLauncher", "rawg_cache");

    // RAWG may intermittently fail to load or respond slowly. Keep metadata
    // lookups bounded so opening game details never stalls the UI for a long time.
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // ── API pública de cache ──
    public void ClearCache()
    {
        try
        {
            if (!Directory.Exists(CacheDir)) return;
            int removed = 0;
            foreach (var file in Directory.GetFiles(CacheDir, "*.json"))
            {
                try { File.Delete(file); removed++; } catch { /* ignora arquivo travado */ }
            }
            DebugLog.Write($"[RAWG] Cache limpo: {removed} arquivo(s).");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[RAWG] Falha ao limpar cache: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string CachePathFor(string title)
    {
        var normalized = (title ?? "").Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        // 8 bytes (16 hex chars) — colisão desprezível nesse escopo
        var hex = Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
        return Path.Combine(CacheDir, $"{hex}.json");
    }

    private static CachedEntry? TryLoadCache(string title, out bool isExpired)
    {
        isExpired = false;
        try
        {
            var path = CachePathFor(title);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            var entry = JsonSerializer.Deserialize<CachedEntry>(json, CacheJsonOpts);
            if (entry?.Data is null) return null;
            isExpired = (DateTime.UtcNow - entry.SavedAtUtc) > CacheTtl;
            return entry;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[RAWG] Falha ao ler cache para '{title}': {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static void SaveCache(string title, RawgGameData data)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var entry = new CachedEntry { SavedAtUtc = DateTime.UtcNow, Data = data };
            var json = JsonSerializer.Serialize(entry, CacheJsonOpts);
            File.WriteAllText(CachePathFor(title), json);
            DebugLog.Write($"[RAWG] Cache salvo para '{title}'.");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[RAWG] Falha ao salvar cache para '{title}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ── Fluxo principal ──
    public async Task<RawgGameData?> GetGameDetailsAsync(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        // 1) Tenta cache válido
        var cached = TryLoadCache(title, out var cachedExpired);
        if (cached?.Data is not null && !cachedExpired)
        {
            DebugLog.Write($"[RAWG] Cache HIT (válido) para '{title}'.");
            return cached.Data;
        }

        // 2) Tenta API
        try
        {
            DebugLog.Write($"[RAWG] === Início (título='{title}') ===");

            var settings = AppSettingsStore.Load();
            var apiKey = settings.RawgApiKey;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                DebugLog.Write("[RAWG] API Key não configurada — tentando cache expirado como fallback.");
                if (cached?.Data is not null) return cached.Data;
                return null;
            }

            var cleanTitle = Uri.EscapeDataString(title.Trim());
            var searchUrl = $"https://api.rawg.io/api/games?search={cleanTitle}&key={apiKey}&page_size=5";
            DebugLog.Write($"[RAWG] GET search='{cleanTitle}' (top 5)");

            var searchResponse = await _http.GetStringAsync(searchUrl);
            using var searchDoc = JsonDocument.Parse(searchResponse);

            if (!searchDoc.RootElement.TryGetProperty("results", out var results))
            {
                DebugLog.Write("[RAWG] Sem 'results' na resposta da busca.");
                if (cached?.Data is not null) return cached.Data;
                return null;
            }

            if (results.GetArrayLength() == 0)
            {
                DebugLog.Write("[RAWG] Nenhum resultado para a busca.");
                if (cached?.Data is not null) return cached.Data;
                return null;
            }

            JsonElement game = results[0];
            bool foundMatch = false;

            foreach (var item in results.EnumerateArray())
            {
                var name = item.GetProperty("name").GetString() ?? "";
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
            var detailsUrl = $"https://api.rawg.io/api/games/{gameId}?key={apiKey}";
            DebugLog.Write($"[RAWG] GET details/{gameId}");

            var detailsResponse = await _http.GetStringAsync(detailsUrl);
            using var detailsDoc = JsonDocument.Parse(detailsResponse);
            var details = detailsDoc.RootElement;

            string desc = "";
            if (details.TryGetProperty("description_raw", out var descProp))
                desc = descProp.GetString() ?? "";

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

            var result = new RawgGameData
            {
                Title = details.GetProperty("name").GetString() ?? title,
                Description = desc.Trim(),
                Developers = developers,
                Genres = genres,
                ReleaseDate = releaseDate
            };

            SaveCache(title, result);
            DebugLog.Write($"[RAWG] Sucesso e cache atualizado: desc={desc.Length}ch, devs={developers.Count}, genres={genres.Count}, release={releaseDate}");
            return result;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[RAWG] EXCEPTION: {ex.GetType().Name}: {ex.Message}");

            // 3) Fallback: cache expirado ainda é melhor que nada
            if (cached?.Data is not null)
            {
                DebugLog.Write($"[RAWG] API falhou — usando cache EXPIRADO como fallback para '{title}'.");
                return cached.Data;
            }
            DebugLog.Write($"[RAWG] API falhou e não há cache — retornando null.");
            return null;
        }
    }

    private sealed class CachedEntry
    {
        public DateTime SavedAtUtc { get; set; }
        public RawgGameData? Data { get; set; }
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