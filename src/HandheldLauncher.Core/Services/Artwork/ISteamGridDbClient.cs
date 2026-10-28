namespace HandheldLauncher.Core.Services.Artwork;

public interface ISteamGridDbClient
{
    bool HasApiKey { get; }

    /// <summary>Looks up a cover ("grid") directly by Steam AppId — no search needed, most accurate.</summary>
    Task<SteamGridDbImage?> GetBestGridForSteamAppAsync(uint steamAppId, CancellationToken ct = default);

    /// <summary>Fallback for non-Steam entries: searches by title, takes the top match's cover.</summary>
    Task<SteamGridDbImage?> GetBestGridForTitleAsync(string title, CancellationToken ct = default);

    /// <summary>
    /// "Hero" art — the wide landscape banner SteamGridDB serves for the
    /// background/banner area (same idea as Steam Deck's recent-games
    /// banner). Same response shape as grids, different endpoint.
    /// </summary>
    Task<SteamGridDbImage?> GetBestHeroForSteamAppAsync(uint steamAppId, CancellationToken ct = default);

    /// <summary>Fallback for non-Steam entries: searches by title, takes the top match's hero banner.</summary>
    Task<SteamGridDbImage?> GetBestHeroForTitleAsync(string title, CancellationToken ct = default);

    /// <summary>
    /// Every safe-for-work grid candidate SteamGridDB has for this Steam
    /// AppId, static images only (types=static — no animated GIF/webm, see
    /// README), highest score first, capped to a sane count for a picker UI.
    /// GetBestGridForSteamAppAsync is just this list's first entry.
    /// </summary>
    Task<IReadOnlyList<SteamGridDbImage>> GetGridCandidatesForSteamAppAsync(uint steamAppId, CancellationToken ct = default);

    /// <summary>Fallback for non-Steam entries: searches by title, same candidate list as the AppId version.</summary>
    Task<IReadOnlyList<SteamGridDbImage>> GetGridCandidatesForTitleAsync(string title, CancellationToken ct = default);

    /// <summary>Same idea as GetGridCandidatesForSteamAppAsync but for hero banners.</summary>
    Task<IReadOnlyList<SteamGridDbImage>> GetHeroCandidatesForSteamAppAsync(uint steamAppId, CancellationToken ct = default);

    /// <summary>Fallback for non-Steam entries: searches by title, same candidate list as the AppId version.</summary>
    Task<IReadOnlyList<SteamGridDbImage>> GetHeroCandidatesForTitleAsync(string title, CancellationToken ct = default);
}
