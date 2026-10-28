using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

public interface ISteamLibraryScanner
{
    /// <summary>
    /// True if a Steam install was found on this machine.
    /// </summary>
    bool IsSteamInstalled { get; }

    /// <summary>
    /// Scans all Steam library folders (default + any added drives) and
    /// returns every currently-installed app as a GameEntry.
    /// Non-game apps (runtimes, SteamVR, tools) are filtered out heuristically.
    /// </summary>
    IReadOnlyList<GameEntry> ScanInstalledGames();
}
