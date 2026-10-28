namespace HandheldLauncher.Core.Models;

/// <summary>
/// Where a game entry came from. Drives how we launch it and whether
/// we're allowed to re-scan/overwrite its metadata automatically.
/// </summary>
public enum GameSource
{
    Steam,
    Manual,
    XboxApp,
    EpicGames,
    Emulator,
}
