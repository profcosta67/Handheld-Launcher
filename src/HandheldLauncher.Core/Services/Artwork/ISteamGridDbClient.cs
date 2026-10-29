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

    /// <summary>
    /// Ícone quadrado do jogo (seção "Icons" do SteamGridDB) — usado na
    /// tela "Iniciando &lt;jogo&gt;...". Só PNG estático (os .ico do site
    /// vêm em vários tamanhos num arquivo só e nem sempre abrem bem).
    /// </summary>
    Task<SteamGridDbImage?> GetBestIconForSteamAppAsync(uint steamAppId, CancellationToken ct = default);

    /// <summary>Mesmo que GetBestIconForSteamAppAsync, buscando pelo nome (jogos não-Steam).</summary>
    Task<SteamGridDbImage?> GetBestIconForTitleAsync(string title, CancellationToken ct = default);

    /// <summary>Lista de ícones candidatos pro seletor de artes ("Escolher ícone").</summary>
    Task<IReadOnlyList<SteamGridDbImage>> GetIconCandidatesForSteamAppAsync(uint steamAppId, CancellationToken ct = default);

    /// <summary>Mesmo que GetIconCandidatesForSteamAppAsync, buscando pelo nome.</summary>
    Task<IReadOnlyList<SteamGridDbImage>> GetIconCandidatesForTitleAsync(string title, CancellationToken ct = default);

    /// <summary>
    /// "Wide grid" — a CAPA (não um banner/hero), só que em formato
    /// landscape (dimensions=460x215/920x430, o "capsule horizontal" que o
    /// próprio Steam usa) em vez do portrait 600x900 normal. Independente
    /// de GetBestGridFor... (grid normal) E de GetBestHeroFor... (hero) —
    /// pedido explícito do usuário depois de reportar que a WIDE CAPSULE
    /// estava esticando a capa vertical normal dentro de uma caixa larga:
    /// "tem que vir de capas mas com formato estilo banner, mas não pode
    /// ser a mesma do banner, elas têm que ser independentes".
    /// </summary>
    Task<SteamGridDbImage?> GetBestWideGridForSteamAppAsync(uint steamAppId, CancellationToken ct = default);

    /// <summary>Fallback for non-Steam entries: searches by title, takes the top match's wide grid.</summary>
    Task<SteamGridDbImage?> GetBestWideGridForTitleAsync(string title, CancellationToken ct = default);

    /// <summary>Same idea as GetGridCandidatesForSteamAppAsync but for wide grids — powers the "Escolher capa larga" picker.</summary>
    Task<IReadOnlyList<SteamGridDbImage>> GetWideGridCandidatesForSteamAppAsync(uint steamAppId, CancellationToken ct = default);

    /// <summary>Fallback for non-Steam entries: searches by title, same candidate list as the AppId version.</summary>
    Task<IReadOnlyList<SteamGridDbImage>> GetWideGridCandidatesForTitleAsync(string title, CancellationToken ct = default);

    /// <summary>Same idea as GetGridCandidatesForSteamAppAsync but for hero banners.</summary>
    Task<IReadOnlyList<SteamGridDbImage>> GetHeroCandidatesForSteamAppAsync(uint steamAppId, CancellationToken ct = default);

    /// <summary>Fallback for non-Steam entries: searches by title, same candidate list as the AppId version.</summary>
    Task<IReadOnlyList<SteamGridDbImage>> GetHeroCandidatesForTitleAsync(string title, CancellationToken ct = default);

    /// <summary>
    /// "Logo" art — a transparent-background wordmark/title treatment,
    /// distinct from the grid (cover) and hero (wide banner). This is the
    /// "CURRENT LOGO" overlay shown over the row's wide "último jogo
    /// aberto" tile. No candidate picker for this one yet (just the
    /// auto-fetch best match, same shape as GetBestHeroForSteamAppAsync) —
    /// a manual picker can be added later the same way the grid/hero ones
    /// were, if that turns out to be needed.
    /// </summary>
    Task<SteamGridDbImage?> GetBestLogoForSteamAppAsync(uint steamAppId, CancellationToken ct = default);

    /// <summary>Fallback for non-Steam entries: searches by title, takes the top match's logo.</summary>
    Task<SteamGridDbImage?> GetBestLogoForTitleAsync(string title, CancellationToken ct = default);

    /// <summary>Same idea as GetGridCandidatesForSteamAppAsync but for logos — powers the "Escolher logo" picker.</summary>
    Task<IReadOnlyList<SteamGridDbImage>> GetLogoCandidatesForSteamAppAsync(uint steamAppId, CancellationToken ct = default);

    /// <summary>Fallback for non-Steam entries: searches by title, same candidate list as the AppId version.</summary>
    Task<IReadOnlyList<SteamGridDbImage>> GetLogoCandidatesForTitleAsync(string title, CancellationToken ct = default);
}
