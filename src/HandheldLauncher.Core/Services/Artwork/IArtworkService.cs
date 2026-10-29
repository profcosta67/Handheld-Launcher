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
    /// Same idea as FetchHeroAsync but for the transparent "logo" wordmark
    /// (game.LogoImagePath) — the "CURRENT LOGO" shown over the row's wide
    /// "último jogo aberto" tile. Independent of grid/hero — a game can be
    /// missing any combination of the three.
    /// </summary>
    Task<bool> FetchLogoAsync(GameEntry game, CancellationToken ct = default);

    /// <summary>
    /// Ícone quadrado do jogo (game.IconImagePath) — aparece na tela
    /// "Iniciando &lt;jogo&gt;...". Mesmo esquema de cache/override das
    /// outras artes.
    /// </summary>
    Task<bool> FetchIconAsync(GameEntry game, CancellationToken ct = default);

    /// <summary>
    /// Same idea as FetchGridAsync but for a LANDSCAPE capa
    /// (game.WideGridImagePath) — a capa, não um banner/hero, só em
    /// formato horizontal. Independente de FetchGridAsync (capa normal,
    /// portrait) e de FetchHeroAsync (hero): pedido do usuário depois de
    /// reportar que a WIDE CAPSULE ("último jogo aberto") estava
    /// esticando a capa portrait normal dentro de uma caixa larga.
    /// </summary>
    Task<bool> FetchWideGridAsync(GameEntry game, CancellationToken ct = default);

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

    /// <summary>Same idea as GetGridCandidatesAsync but for logos — powers the "Escolher logo" picker.</summary>
    Task<IReadOnlyList<ArtworkCandidate>> GetLogoCandidatesAsync(GameEntry game, CancellationToken ct = default);

    /// <summary>Candidatos de ícone (seção Icons do SteamGridDB) pro seletor de artes.</summary>
    Task<IReadOnlyList<ArtworkCandidate>> GetIconCandidatesAsync(GameEntry game, CancellationToken ct = default);

    /// <summary>Same idea as GetGridCandidatesAsync but for a capa landscape (WideGridImagePath) — powers o "Escolher capa larga" picker.</summary>
    Task<IReadOnlyList<ArtworkCandidate>> GetWideGridCandidatesAsync(GameEntry game, CancellationToken ct = default);

    /// <summary>
    /// Makes one specific candidate (from GetGridCandidatesAsync) this
    /// game's actual cover — copies the already-downloaded candidate file
    /// into the canonical path, persists the override, and mutates
    /// game.GridImagePath directly, same contract as FetchGridAsync.
    /// </summary>
    Task<bool> ApplyGridCandidateAsync(GameEntry game, ArtworkCandidate candidate, CancellationToken ct = default);

    /// <summary>Same idea as ApplyGridCandidateAsync but for hero banners.</summary>
    Task<bool> ApplyHeroCandidateAsync(GameEntry game, ArtworkCandidate candidate, CancellationToken ct = default);

    /// <summary>Same idea as ApplyGridCandidateAsync but for logos.</summary>
    Task<bool> ApplyLogoCandidateAsync(GameEntry game, ArtworkCandidate candidate, CancellationToken ct = default);

    /// <summary>Aplica o ícone escolhido no seletor (game.IconImagePath).</summary>
    Task<bool> ApplyIconCandidateAsync(GameEntry game, ArtworkCandidate candidate, CancellationToken ct = default);

    /// <summary>Same idea as ApplyGridCandidateAsync but for a capa landscape (WideGridImagePath).</summary>
    Task<bool> ApplyWideGridCandidateAsync(GameEntry game, ArtworkCandidate candidate, CancellationToken ct = default);
}
