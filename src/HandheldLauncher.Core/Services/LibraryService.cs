using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

public sealed class LibraryService : ILibraryService
{
    private readonly ISteamLibraryScanner _steamScanner;
    private readonly IGameLibraryStore _store;
    private List<GameEntry> _games = new();

    public LibraryService(ISteamLibraryScanner steamScanner, IGameLibraryStore store)
    {
        _steamScanner = steamScanner;
        _store = store;
        Refresh();
    }

    public IReadOnlyList<GameEntry> Games => _games;

    public void Refresh()
    {
        var overrides = _store.LoadOverrides();
        var merged = new List<GameEntry>();

        foreach (var game in _steamScanner.ScanInstalledGames())
        {
            ApplyOverride(game, overrides);
            merged.Add(game);
        }

        foreach (var game in _store.LoadManualGames())
        {
            ApplyOverride(game, overrides);
            merged.Add(game);
        }

        // Future scanners (Xbox app, Epic, emulator folders) plug in here
        // the same way: scan -> ApplyOverride -> merged.Add.

        merged.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
        _games = merged;
    }

    public void AddManualGame(GameEntry game)
    {
        game.Source = GameSource.Manual;
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
        var existing = _games.FirstOrDefault(g => g.Id == gameId);
        var overrideData = ToOverride(existing) ?? new GameMetadataOverride();
        overrideData.Collection = collection;
        _store.SaveOverride(gameId, overrideData);
        Refresh();
    }

    public void RecordPlaySession(string gameId, TimeSpan duration)
    {
        var existing = _games.FirstOrDefault(g => g.Id == gameId);
        var overrideData = ToOverride(existing) ?? new GameMetadataOverride();
        overrideData.PlaytimeMinutes += (long)duration.TotalMinutes;
        overrideData.LastPlayedUtc = DateTimeOffset.UtcNow;
        _store.SaveOverride(gameId, overrideData);
        Refresh();
    }

    public void MarkLaunched(string gameId)
    {
        var existing = _games.FirstOrDefault(g => g.Id == gameId);
        var overrideData = ToOverride(existing) ?? new GameMetadataOverride();
        overrideData.LastPlayedUtc = DateTimeOffset.UtcNow;
        _store.SaveOverride(gameId, overrideData);

        // No Refresh() here on purpose — a full re-scan would replace every
        // GameEntry instance, orphaning whatever the UI currently holds
        // (SelectedTile, RunningGame...) for no benefit. Mutate the live
        // instance directly so the "último jogo aberto" change is visible
        // immediately.
        if (existing is not null) existing.LastPlayedUtc = overrideData.LastPlayedUtc;
    }

    private static void ApplyOverride(GameEntry game, IReadOnlyDictionary<string, GameMetadataOverride> overrides)
    {
        if (!overrides.TryGetValue(game.Id, out var o)) return;

        game.Collection = o.Collection;
        game.GridImagePath = o.GridImagePath;
        game.HeroImagePath = o.HeroImagePath;
        game.LogoImagePath = o.LogoImagePath;
        game.IconImagePath = o.IconImagePath;
        game.PlaytimeMinutes = o.PlaytimeMinutes;
        game.LastPlayedUtc = o.LastPlayedUtc;
    }

    private static GameMetadataOverride? ToOverride(GameEntry? game)
    {
        if (game is null) return null;
        return new GameMetadataOverride
        {
            Collection = game.Collection,
            GridImagePath = game.GridImagePath,
            HeroImagePath = game.HeroImagePath,
            LogoImagePath = game.LogoImagePath,
            IconImagePath = game.IconImagePath,
            PlaytimeMinutes = game.PlaytimeMinutes,
            LastPlayedUtc = game.LastPlayedUtc,
        };
    }
}
