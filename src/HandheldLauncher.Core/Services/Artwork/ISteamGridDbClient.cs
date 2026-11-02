using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HandheldLauncher.Core.Services.Artwork;

public interface ISteamGridDbClient
{
    bool HasApiKey { get; }

    // 🟢 NOVO MÉTODO AQUI
    void UpdateApiKey(string? apiKey);

    Task<SteamGridDbImage?> GetBestGridForSteamAppAsync(uint steamAppId, CancellationToken ct = default);
    Task<SteamGridDbImage?> GetBestGridForTitleAsync(string title, CancellationToken ct = default);
    Task<SteamGridDbImage?> GetBestHeroForSteamAppAsync(uint steamAppId, CancellationToken ct = default);
    Task<SteamGridDbImage?> GetBestHeroForTitleAsync(string title, CancellationToken ct = default);
    Task<SteamGridDbImage?> GetBestLogoForSteamAppAsync(uint steamAppId, CancellationToken ct = default);
    Task<SteamGridDbImage?> GetBestLogoForTitleAsync(string title, CancellationToken ct = default);
    Task<SteamGridDbImage?> GetBestIconForSteamAppAsync(uint steamAppId, CancellationToken ct = default);
    Task<SteamGridDbImage?> GetBestIconForTitleAsync(string title, CancellationToken ct = default);
    Task<SteamGridDbImage?> GetBestWideGridForSteamAppAsync(uint steamAppId, CancellationToken ct = default);
    Task<SteamGridDbImage?> GetBestWideGridForTitleAsync(string title, CancellationToken ct = default);

    Task<IReadOnlyList<SteamGridDbImage>> GetGridCandidatesForSteamAppAsync(uint steamAppId, bool animatedOnly = false, CancellationToken ct = default);
    Task<IReadOnlyList<SteamGridDbImage>> GetGridCandidatesForTitleAsync(string title, bool animatedOnly = false, CancellationToken ct = default);
    Task<IReadOnlyList<SteamGridDbImage>> GetHeroCandidatesForSteamAppAsync(uint steamAppId, bool animatedOnly = false, CancellationToken ct = default);
    Task<IReadOnlyList<SteamGridDbImage>> GetHeroCandidatesForTitleAsync(string title, bool animatedOnly = false, CancellationToken ct = default);
    Task<IReadOnlyList<SteamGridDbImage>> GetLogoCandidatesForSteamAppAsync(uint steamAppId, bool animatedOnly = false, CancellationToken ct = default);
    Task<IReadOnlyList<SteamGridDbImage>> GetLogoCandidatesForTitleAsync(string title, bool animatedOnly = false, CancellationToken ct = default);
    Task<IReadOnlyList<SteamGridDbImage>> GetIconCandidatesForSteamAppAsync(uint steamAppId, bool animatedOnly = false, CancellationToken ct = default);
    Task<IReadOnlyList<SteamGridDbImage>> GetIconCandidatesForTitleAsync(string title, bool animatedOnly = false, CancellationToken ct = default);
    Task<IReadOnlyList<SteamGridDbImage>> GetWideGridCandidatesForSteamAppAsync(uint steamAppId, bool animatedOnly = false, CancellationToken ct = default);
    Task<IReadOnlyList<SteamGridDbImage>> GetWideGridCandidatesForTitleAsync(string title, bool animatedOnly = false, CancellationToken ct = default);
}