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
    /// </summary>
    Task<bool> FetchGridAsync(GameEntry game, CancellationToken ct = default);
}
