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
    void SetFavorite(string gameId, bool isFavorite);
    void RecordPlaySession(string gameId, TimeSpan duration);

    /// <summary>
    /// Marks a game as the one just opened — distinct from RecordPlaySession
    /// (which only fires once the game exits): this is the "último jogo
    /// aberto" the UI puts in the wide landscape tile, and it has to update
    /// the instant the player launches something, not after they quit it.
    /// Mutates the matching in-memory GameEntry directly instead of doing a
    /// full Refresh(), so callers don't lose whatever UI-only state (current
    /// selection, running-game reference) is riding on the existing instances.
    /// </summary>
    void MarkLaunched(string gameId);
}
