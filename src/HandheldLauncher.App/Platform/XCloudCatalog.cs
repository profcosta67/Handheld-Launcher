using System.Net.Http;
using System.Text.Json;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Platform;

public sealed class XCloudGame
{
    public string ProductId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? PosterPath { get; set; }
    public string? PosterUrl { get; set; }
}

/// <summary>
/// v0.18 — aba "Nuvem": catálogo do Xbox Cloud Gaming.
///
/// Fontes (públicas, sem login — confirmadas em projetos independentes que
/// usam as mesmas, ex.: XStreaming, GameVault, levantamento Qiita out/2026):
/// <list type="bullet">
/// <item>Lista de IDs: catalog.gamepass.com/sigls/v2?id=29a81209-… ("Xbox
/// Cloud Gaming: All games", ~560 títulos). Primeiro item é metadado da
/// lista, depois {"id": "9N…"}.</item>
/// <item>Nome e imagens: displaycatalog.mp.microsoft.com/v7.0/products
/// (LocalizedProperties[0].ProductTitle e .Images[] com ImagePurpose/Uri).</item>
/// </list>
/// NÃO são APIs oficiais documentadas: podem mudar. Por isso tudo aqui é
/// melhor esforço — cache em disco (24h), leitura tolerante (campo que não
/// vier = jogo só com nome) e falha silenciosa (a aba continua com o bloco
/// fixo "Xbox Cloud Gaming" e a última lista salva).
/// </summary>
public static class XCloudCatalog
{
    private const string AllCloudGamesSigl = "29a81209-df6f-41fd-a528-2ae6b91f719c";
    private const string Market = "BR";
    private const string Language = "pt-BR";
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    static XCloudCatalog()
    {
        try { Http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppUserAgent.Value} (Windows; C#)"); }
        catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); }
    }

    private static readonly string BaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HandheldLauncher", "xcloud");
    private static string CachePath => Path.Combine(BaseDir, "catalog.json");
    private static string ImageDir => Path.Combine(BaseDir, "images");

    private sealed class CacheFile
    {
        public DateTimeOffset FetchedUtc { get; set; }
        public List<XCloudGame> Games { get; set; } = new();
    }

    public static IReadOnlyList<XCloudGame> LoadCached(out DateTimeOffset fetchedUtc)
    {
        fetchedUtc = default;
        try
        {
            if (!File.Exists(CachePath)) return Array.Empty<XCloudGame>();
            var file = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(CachePath));
            if (file is null) return Array.Empty<XCloudGame>();
            fetchedUtc = file.FetchedUtc;
            foreach (var g in file.Games)
            {
                if (g.PosterPath is { } p && !File.Exists(p)) g.PosterPath = null;
            }
            return file.Games;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[XCLOUD] Cache do catálogo ilegível: {ex.Message}");
            return Array.Empty<XCloudGame>();
        }
    }

    public static bool IsStale(DateTimeOffset fetchedUtc) => DateTimeOffset.UtcNow - fetchedUtc > MaxAge;

    /// <summary>
    /// Fase 1: baixa a lista + nomes (rápido, ~30 pedidos). Devolve null se
    /// não deu (sem internet, API mudou...). As capas vêm na fase 2
    /// (<see cref="DownloadMissingPostersAsync"/>), pra aba já mostrar os nomes.
    /// </summary>
    public static async Task<IReadOnlyList<XCloudGame>?> RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var ids = await FetchIdsAsync(ct);
            if (ids.Count == 0) { DebugLog.Write("[XCLOUD] Lista de jogos de nuvem veio vazia."); return null; }

            var games = new List<XCloudGame>();
            for (var i = 0; i < ids.Count; i += 20)
            {
                ct.ThrowIfCancellationRequested();
                var batch = ids.Skip(i).Take(20).ToList();
                games.AddRange(await FetchDetailsAsync(batch, ct));
            }

            // Mantém a ordem da lista oficial (a mesma do xbox.com/play).
            var order = ids.Select((id, idx) => (id, idx)).ToDictionary(x => x.id, x => x.idx, StringComparer.OrdinalIgnoreCase);
            games = games.Where(g => g.Title.Length > 0)
                         .OrderBy(g => order.TryGetValue(g.ProductId, out var o) ? o : int.MaxValue)
                         .ToList();

            // Reaproveita capas já baixadas em outra atualização.
            foreach (var g in games)
            {
                var existing = PosterFile(g.ProductId);
                if (File.Exists(existing)) g.PosterPath = existing;
            }

            SaveCache(games);
            DebugLog.Write($"[XCLOUD] Catálogo atualizado: {games.Count} jogos.");
            return games;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[XCLOUD] Não consegui atualizar o catálogo: {ex.Message}");
            return null;
        }
    }

    /// <summary>Fase 2: capas que ainda faltam, 6 por vez, em tamanho reduzido. Atualiza o cache no fim.</summary>
    public static async Task<int> DownloadMissingPostersAsync(IReadOnlyList<XCloudGame> games, CancellationToken ct = default)
    {
        var missing = games.Where(g => g.PosterPath is null && !string.IsNullOrWhiteSpace(g.PosterUrl)).ToList();
        if (missing.Count == 0) return 0;
        var downloaded = 0;
        using var gate = new SemaphoreSlim(6);
        var tasks = missing.Select(async g =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var path = await DownloadImageAsync(g.ProductId, g.PosterUrl!, 300, 450, ct);
                if (path is not null) { g.PosterPath = path; Interlocked.Increment(ref downloaded); }
            }
            finally { gate.Release(); }
        });
        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { }
        SaveCache(games);
        DebugLog.Write($"[XCLOUD] Capas baixadas: {downloaded} de {missing.Count}.");
        return downloaded;
    }

    private static void SaveCache(IReadOnlyList<XCloudGame> games)
    {
        try
        {
            Directory.CreateDirectory(BaseDir);
            var previous = File.Exists(CachePath) ? JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(CachePath)) : null;
            var tmp = CachePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new CacheFile
            {
                FetchedUtc = games.Count > 0 ? DateTimeOffset.UtcNow : previous?.FetchedUtc ?? default,
                Games = games.ToList(),
            }));
            File.Move(tmp, CachePath, overwrite: true);
        }
        catch (Exception ex) { DebugLog.Write($"[XCLOUD] Falha ao salvar cache: {ex.Message}"); }
    }

    private static string PosterFile(string productId)
        => Path.Combine(ImageDir, $"{new string(productId.Where(char.IsLetterOrDigit).ToArray())}_poster.jpg");

    private static async Task<List<string>> FetchIdsAsync(CancellationToken ct)
    {
        var url = $"https://catalog.gamepass.com/sigls/v2?id={AllCloudGamesSigl}&language={Language}&market={Market}";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
        var ids = new List<string>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return ids;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } s) ids.Add(s);
        }
        return ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task<List<XCloudGame>> FetchDetailsAsync(List<string> ids, CancellationToken ct)
    {
        var result = new List<XCloudGame>();
        var url = $"https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={string.Join(",", ids)}&market={Market}&languages={Language.ToLowerInvariant()}&MS-CV=DGU1mcuYo0WMMp+F.1";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
        if (!doc.RootElement.TryGetProperty("Products", out var products) || products.ValueKind != JsonValueKind.Array) return result;

        foreach (var p in products.EnumerateArray())
        {
            if (!p.TryGetProperty("ProductId", out var pid) || pid.GetString() is not { Length: > 0 } productId) continue;
            if (!p.TryGetProperty("LocalizedProperties", out var locs) || locs.ValueKind != JsonValueKind.Array || locs.GetArrayLength() == 0) continue;
            var loc = locs[0];
            var title = loc.TryGetProperty("ProductTitle", out var t) ? t.GetString() ?? "" : "";
            if (title.Length == 0) continue;

            string? poster = null;
            if (loc.TryGetProperty("Images", out var images) && images.ValueKind == JsonValueKind.Array)
                poster = PickImage(images, "Poster") ?? PickImage(images, "BoxArt");

            result.Add(new XCloudGame { ProductId = productId, Title = title.Trim(), PosterUrl = poster });
        }
        return result;
    }

    private static string? PickImage(JsonElement images, string purpose)
    {
        foreach (var img in images.EnumerateArray())
        {
            if (img.TryGetProperty("ImagePurpose", out var ip) && string.Equals(ip.GetString(), purpose, StringComparison.OrdinalIgnoreCase)
                && img.TryGetProperty("Uri", out var uri) && uri.GetString() is { Length: > 0 } u)
            {
                return u.StartsWith("//", StringComparison.Ordinal) ? "https:" + u : u;
            }
        }
        return null;
    }

    private static async Task<string?> DownloadImageAsync(string productId, string url, int w, int h, CancellationToken ct)
    {
        var path = PosterFile(productId);
        if (File.Exists(path)) return path;
        try
        {
            Directory.CreateDirectory(ImageDir);
            // O CDN de imagens da loja aceita redimensionar por parâmetro — evita
            // baixar ~560 pôsteres em tamanho cheio. Se não aceitar, tenta sem.
            var sep = url.Contains('?') ? "&" : "?";
            byte[] bytes;
            try { bytes = await Http.GetByteArrayAsync($"{url}{sep}w={w}&h={h}&q=85", ct); }
            catch (HttpRequestException) { bytes = await Http.GetByteArrayAsync(url, ct); }
            var tmp = path + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, ct);
            File.Move(tmp, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DebugLog.Swallowed(ex);
            return null;
        }
    }

    /// <summary>Página do jogo no xbox.com/play (tem o botão Jogar). O xbox.com acha o jogo pelo ID; o trecho do nome é só cosmético.</summary>
    public static string GamePageUrl(XCloudGame game)
    {
        var slug = new string(game.Title.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        if (slug.Length == 0) slug = "jogo";
        return $"https://www.xbox.com/{Language}/play/games/{slug}/{game.ProductId}";
    }

    public const string HomeUrl = "https://www.xbox.com/pt-BR/play";
}
