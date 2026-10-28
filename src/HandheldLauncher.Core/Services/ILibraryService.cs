using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// The single source of truth the UI binds to: merges whatever the scanners
/// find (Steam today; Xbox/Epic/emulators later) with manually-added games
/// and locally-stored overrides (art, collection, playtime).
/// </summary>
public interface ILibraryService
{
    IReadOnlyList<GameEntry> Games { get; }

    /// <summary>Re-runs every scanner and re-merges with stored manual games + overrides.</summary>
    void Refresh();

    void AddManualGame(GameEntry game);
    void RemoveManualGame(string id);

    void SetCollection(string gameId, string? collection);
    void RecordPlaySession(string gameId, TimeSpan duration);
}
