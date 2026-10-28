using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services.Artwork;

public interface IArtworkService
{
    bool HasApiKey { get; }

    /// <summary>
    /// Fetches and caches a cover for this game, persisting it as an
    /// override so it survives the next library re-scan. Mutates
    /// game.GridImagePath directly on success so the caller can repaint
    /// immediately without a full Refresh(). Returns false on any failure
    /// (no API key, no match found, download failed) — never throws.
    /// Always grabs SteamGridDB's top-scored match; for picking a specific
    /// one among several options, see GetGridCandidatesAsync +
    /// ApplyGridCandidateAsync below.
    /// </summary>
    Task<bool> FetchGridAsync(GameEntry game, CancellationToken ct = default);

    /// <summary>
    /// Same idea as FetchGridAsync but for the wide "hero" banner
    /// (game.HeroImagePath) used behind the focused game. Independent of
    /// the grid fetch — a game can have one without the other, since
    /// SteamGridDB's hero coverage is thinner than its grid coverage.
    /// </summary>
    Task<bool> FetchHeroAsync(GameEntry game, CancellationToken ct = default);

    /// <summary>
    /// Every static-image grid candidate SteamGridDB has for this game,
    /// already downloaded to a local cache file so the UI can show them in
    /// a picker without any async-network-image plumbing. Re-opening the
    /// picker for the same game skips re-downloading anything already
    /// cached from a previous open.
    /// </summary>
    Task<IReadOnlyList<ArtworkCandidate>> GetGridCandidatesAsync(GameEntry game, CancellationToken ct = default);

    /// <summary>Same idea as GetGridCandidatesAsync but for hero banners.</summary>
    Task<IReadOnlyList<ArtworkCandidate>> GetHeroCandidatesAsync(GameEntry game, CancellationToken ct = default);

    /// <summary>
    /// Makes one specific candidate (from GetGridCandidatesAsync) this
    /// game's actual cover — copies the already-downloaded candidate file
    /// into the canonical path, persists the override, and mutates
    /// game.GridImagePath directly, same contract as FetchGridAsync.
    /// </summary>
    Task<bool> ApplyGridCandidateAsync(GameEntry game, ArtworkCandidate candidate, CancellationToken ct = default);

    /// <summary>Same idea as ApplyGridCandidateAsync but for hero banners.</summary>
    Task<bool> ApplyHeroCandidateAsync(GameEntry game, ArtworkCandidate candidate, CancellationToken ct = default);
}
