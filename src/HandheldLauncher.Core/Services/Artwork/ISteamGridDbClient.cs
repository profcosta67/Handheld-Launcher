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
}
