using System.Text.Json;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// v0.18 Detalhes 2.0 — cache em disco por jogo (plano: "dados aparecem sem
/// internet depois da primeira visita", validade de ~7 dias). Um .json por
/// jogo em %AppData%\HandheldLauncher\details-cache. Ícones de conquista e
/// capturas de tela já ficam em arquivo próprio (só o caminho vem pra cá).
/// Tudo melhor esforço: arquivo corrompido ou sem permissão = sem cache,
/// nunca exceção pra quem chama.
/// </summary>
internal static class GameDetailsCache
{
    public static readonly TimeSpan StoreDataMaxAge = TimeSpan.FromDays(7);

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HandheldLauncher", "details-cache");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static GameDetailsCacheData? Load(string gameId)
    {
        try
        {
            var path = PathFor(gameId);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<GameDetailsCacheData>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[GameDetails] Cache ilegível para '{gameId}': {ex.Message}");
            return null;
        }
    }

    public static void Save(string gameId, GameDetailsCacheData data)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var path = PathFor(gameId);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[GameDetails] Falha ao salvar cache de '{gameId}': {ex.Message}");
        }
    }

    public static string MediaDir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HandheldLauncher", "artwork", "screenshots");
            return dir;
        }
    }

    private static string PathFor(string gameId)
    {
        var safe = new string(gameId.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').ToArray());
        if (safe.Length == 0) safe = "jogo";
        return Path.Combine(CacheDir, safe + ".json");
    }
}

internal sealed class GameDetailsCacheData
{
    /// <summary>Quando os dados da LOJA (descrição/etiquetas/mídia) foram baixados.</summary>
    public DateTimeOffset StoreFetchedUtc { get; set; }
    public string? Description { get; set; }
    public string? LongDescription { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> ScreenshotPaths { get; set; } = new();
    public bool HasTrailer { get; set; }

    public long? SteamPlaytimeMinutes { get; set; }

    public DateTimeOffset? AchievementsFetchedUtc { get; set; }
    public string? AchievementsStatus { get; set; }
    public double AchievementProgress { get; set; }
    public List<CachedAchievement> Achievements { get; set; } = new();
}

internal sealed class CachedAchievement
{
    public string ApiName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public bool Achieved { get; set; }
    public string? IconLocalPath { get; set; }
    public DateTimeOffset? UnlockedAtUtc { get; set; }
    public double? GlobalPercent { get; set; }
}
