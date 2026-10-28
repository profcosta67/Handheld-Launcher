using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// Persists everything the launcher adds on top of what the scanners find:
/// manually-added (non-Steam) games, and per-game overrides like custom
/// artwork, collection assignment and locally-tracked playtime.
/// </summary>
public interface IGameLibraryStore
{
    IReadOnlyList<GameEntry> LoadManualGames();
    void AddManualGame(GameEntry game);
    void RemoveManualGame(string id);

    IReadOnlyDictionary<string, GameMetadataOverride> LoadOverrides();
    void SaveOverride(string gameId, GameMetadataOverride data);
}
