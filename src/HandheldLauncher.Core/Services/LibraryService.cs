using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

public sealed class LibraryService : ILibraryService
{
    private readonly ISteamLibraryScanner _steamScanner;
    private readonly IXboxFolderScanner? _xboxFolderScanner;
    private readonly IReadOnlyList<IStoreLibraryScanner> _storeScanners;
    private readonly ISteamOwnedGamesClient? _steamOwnedGamesClient;
    private readonly IGameLibraryStore _store;
    private List<GameEntry> _games = new();

    // NOVO: Cache de overrides em memória para evitar leituras repetidas no disco
    private IReadOnlyDictionary<string, GameMetadataOverride>? _cachedOverrides;

    public LibraryService(
        ISteamLibraryScanner steamScanner,
        IGameLibraryStore store,
        IXboxFolderScanner? xboxFolderScanner = null,
        ISteamOwnedGamesClient? steamOwnedGamesClient = null,
        IReadOnlyList<IStoreLibraryScanner>? storeScanners = null)
    {
        _steamScanner = steamScanner;
        _store = store;
        _xboxFolderScanner = xboxFolderScanner;
        _storeScanners = storeScanners ?? Array.Empty<IStoreLibraryScanner>();
        _steamOwnedGamesClient = steamOwnedGamesClient;

        try
        {
            Refresh();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LibraryService] Falha crítica no construtor: {ex.Message}");
        }
    }

    public IReadOnlyList<GameEntry> Games => _games;

    public void Refresh()
    {
        // Carrega os overrides UMA VEZ e guarda no cache
        _cachedOverrides = _store.LoadOverrides();
        var merged = new List<GameEntry>();

        // 1. Scanner Steam
        try
        {
            foreach (var game in _steamScanner.ScanInstalledGames())
            {
                ApplyOverride(game, _cachedOverrides);
                merged.Add(game);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LibraryService] Erro ao escanear Steam: {ex.Message}");
        }

        // 2. Jogos Manuais
        try
        {
            foreach (var game in _store.LoadManualGames())
            {
                ApplyOverride(game, _cachedOverrides);
                merged.Add(game);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LibraryService] Erro ao carregar jogos manuais: {ex.Message}");
        }

        // 3. Scanner Xbox
        if (_xboxFolderScanner is not null)
        {
            try
            {
                foreach (var game in _xboxFolderScanner.ScanInstalledGames())
                {
                    if (merged.Any(existing => ExecutablePathsMatch(existing.ExecutablePath, game.ExecutablePath)))
                    {
                        continue;
                    }
                    ApplyOverride(game, _cachedOverrides);
                    merged.Add(game);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LibraryService] Erro ao escanear Xbox: {ex.Message}");
            }
        }

        // 4. Lojas (Epic, GOG, EA, Ubisoft)
        foreach (var scanner in _storeScanners)
        {
            IReadOnlyList<GameEntry> scanned;
            try
            {
                scanned = scanner.ScanInstalledGames();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LibraryService] Erro no scanner {scanner.GetType().Name}: {ex.Message}");
                continue; 
            }

            foreach (var game in scanned)
            {
                if (merged.Any(existing => SameLaunchTarget(existing, game) || SameInstallFolder(existing, game)))
                {
                    continue;
                }

                ApplyOverride(game, _cachedOverrides);
                merged.Add(game);
            }
        }

        merged.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
        _games = merged;
    }

    private static bool SameLaunchTarget(GameEntry a, GameEntry b)
    {
        if (!ExecutablePathsMatch(a.ExecutablePath, b.ExecutablePath)) return false;

        return string.Equals(
            a.LaunchArguments?.Trim() ?? string.Empty,
            b.LaunchArguments?.Trim() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameInstallFolder(GameEntry a, GameEntry b)
    {
        if (a.Source == GameSource.Emulator || b.Source == GameSource.Emulator) return false;
        return SameFolder(a.InstallDirectory, b.InstallDirectory);
    }

    private static bool SameFolder(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool ExecutablePathsMatch(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;

        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public void AddManualGame(GameEntry game)
    {
        _store.AddManualGame(game);
        Refresh();
    }

    public void RemoveManualGame(string id)
    {
        _store.RemoveManualGame(id);
        Refresh();
    }

    public void SetCollection(string gameId, string? collection)
    {
        var overrideData = OverrideFor(gameId);
        overrideData.Collection = collection;
        _store.SaveOverride(gameId, overrideData);
        _cachedOverrides = null; // Invalida o cache
        Refresh();
    }

    public void SetFavorite(string gameId, bool isFavorite)
    {
        var existing = _games.FirstOrDefault(g => g.Id == gameId);
        var overrideData = OverrideFor(gameId);
        overrideData.IsFavorite = isFavorite;
        _store.SaveOverride(gameId, overrideData);
        _cachedOverrides = null; // Invalida o cache

        if (existing is not null) existing.IsFavorite = isFavorite;
    }

    public void RecordPlaySession(string gameId, TimeSpan duration)
    {
        var overrideData = OverrideFor(gameId);
        overrideData.PlaytimeMinutes += (long)duration.TotalMinutes;
        overrideData.LastPlayedUtc = DateTimeOffset.UtcNow;
        _store.SaveOverride(gameId, overrideData);
        _cachedOverrides = null; // Invalida o cache
        Refresh();
    }

    public void MarkLaunched(string gameId)
    {
        var existing = _games.FirstOrDefault(g => g.Id == gameId);
        var overrideData = OverrideFor(gameId);
        overrideData.LastPlayedUtc = DateTimeOffset.UtcNow;
        _store.SaveOverride(gameId, overrideData);
        _cachedOverrides = null; // Invalida o cache

        if (existing is not null) existing.LastPlayedUtc = overrideData.LastPlayedUtc;
    }

    public IReadOnlyList<DownloadProgressInfo> GetActiveDownloads() 
    {
        try
        {
            return _steamScanner.ScanActiveDownloads();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LibraryService] Erro ao buscar downloads ativos: {ex.Message}");
            return Array.Empty<DownloadProgressInfo>();
        }
    }

    public bool HasSteamWebApiCredentials => _steamOwnedGamesClient?.HasCredentials ?? false;

    public async Task<IReadOnlyList<OwnedSteamGame>> GetOwnedButNotInstalledSteamGamesAsync(CancellationToken ct = default)
    {
        if (_steamOwnedGamesClient is null || !_steamOwnedGamesClient.HasCredentials)
        {
            return Array.Empty<OwnedSteamGame>();
        }

        var owned = await _steamOwnedGamesClient.GetOwnedGamesAsync(ct);

        var installedAppIds = _games
            .Where(g => g.Source == GameSource.Steam && g.SteamAppId is not null)
            .Select(g => g.SteamAppId!.Value)
            .ToHashSet();

        return owned.Where(g => !installedAppIds.Contains(g.AppId)).ToList();
    }

    public GameEntry BuildEntryForOwnedGame(OwnedSteamGame owned)
    {
        var game = new GameEntry
        {
            Id = $"steam_{owned.AppId}",
            Title = owned.Title,
            Source = GameSource.Steam,
            SteamAppId = owned.AppId,
            IsOwnedNotInstalled = true,
        };

        // Em vez de ler do disco, usa o cache na memória
        var overrides = _cachedOverrides ?? _store.LoadOverrides();
        ApplyOverride(game, overrides);

        game.LastPlayedUtc = null;

        return game;
    }

    private static void ApplyOverride(GameEntry game, IReadOnlyDictionary<string, GameMetadataOverride> overrides)
    {
        if (!overrides.TryGetValue(game.Id, out var o)) return;

        if (!string.IsNullOrWhiteSpace(o.TitleOverride)) game.Title = o.TitleOverride;
        game.TitleOverride = o.TitleOverride;
        game.IsApplication = o.IsApplication;

        game.Collection = o.Collection;
        game.GridImagePath = o.GridImagePath;
        game.HeroImagePath = o.HeroImagePath;
        game.LogoImagePath = o.LogoImagePath;
        game.IconImagePath = o.IconImagePath;
        game.WideGridImagePath = o.WideGridImagePath;
        game.PlaytimeMinutes = o.PlaytimeMinutes;
        game.LastPlayedUtc = o.LastPlayedUtc;
        game.IsFavorite = o.IsFavorite;
    }

    public void SaveEntryEdits(string gameId, string? title, bool isApplication)
    {
        var overrideData = OverrideFor(gameId);
        overrideData.TitleOverride = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        overrideData.IsApplication = isApplication;
        _store.SaveOverride(gameId, overrideData);
        _cachedOverrides = null; // Invalida o cache
        Refresh();
    }

    private GameMetadataOverride OverrideFor(string gameId)
    {
        // Se o cache estiver nulo, carrega do disco uma vez
        _cachedOverrides ??= _store.LoadOverrides();
        
        return _cachedOverrides.TryGetValue(gameId, out var existing)
            ? existing.Clone()
            : new GameMetadataOverride();
    }
}