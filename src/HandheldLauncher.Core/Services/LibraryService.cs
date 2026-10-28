using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

public sealed class LibraryService : ILibraryService
{
    private readonly ISteamLibraryScanner _steamScanner;
    private readonly IXboxFolderScanner? _xboxFolderScanner;
    private readonly IGameLibraryStore _store;
    private List<GameEntry> _games = new();

    /// <summary>
    /// xboxFolderScanner é opcional (null = nenhuma pasta configurada em
    /// AppSettings.XboxScanFolders, comportamento idêntico a antes desta
    /// leva) — ver XboxFolderScanner pra o que ele faz.
    /// </summary>
    public LibraryService(ISteamLibraryScanner steamScanner, IGameLibraryStore store, IXboxFolderScanner? xboxFolderScanner = null)
    {
        _steamScanner = steamScanner;
        _store = store;
        _xboxFolderScanner = xboxFolderScanner;
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

        // XboxFolderScanner — pastas extras configuradas em
        // AppSettings.XboxScanFolders (nova nesta leva, ver essa classe).
        // Null quando nenhuma pasta está configurada, então nada muda pra
        // quem não configurou isso.
        if (_xboxFolderScanner is not null)
        {
            foreach (var game in _xboxFolderScanner.ScanInstalledGames())
            {
                ApplyOverride(game, overrides);
                merged.Add(game);
            }
        }

        // Future scanners (Epic, emulator folders) plug in here the same
        // way: scan -> ApplyOverride -> merged.Add.

        merged.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
        _games = merged;
    }

    public void AddManualGame(GameEntry game)
    {
        // Não força mais Source = Manual aqui: a tela "Adicionar jogo" agora
        // deixa marcar "É um jogo do Xbox/Game Pass", e sobrescrever sempre
        // pra Manual jogaria essa escolha fora. O default do próprio
        // GameEntry (Manual) já cobre o caso comum.
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

    public void SetFavorite(string gameId, bool isFavorite)
    {
        var existing = _games.FirstOrDefault(g => g.Id == gameId);
        var overrideData = ToOverride(existing) ?? new GameMetadataOverride();
        overrideData.IsFavorite = isFavorite;
        _store.SaveOverride(gameId, overrideData);

        // Mesma ideia do MarkLaunched: muta a instância em memória direto em
        // vez de dar Refresh() completo, pra não perder SelectedTile/etc e
        // pro toggle refletir na hora (a aba "Favoritos" depende disso).
        if (existing is not null) existing.IsFavorite = isFavorite;
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
        game.WideGridImagePath = o.WideGridImagePath;
        game.PlaytimeMinutes = o.PlaytimeMinutes;
        game.LastPlayedUtc = o.LastPlayedUtc;
        game.IsFavorite = o.IsFavorite;
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
            WideGridImagePath = game.WideGridImagePath,
            PlaytimeMinutes = game.PlaytimeMinutes,
            LastPlayedUtc = game.LastPlayedUtc,
            IsFavorite = game.IsFavorite,
        };
    }
}
