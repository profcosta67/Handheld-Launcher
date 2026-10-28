namespace HandheldLauncher.Core.Models;

/// <summary>
/// App-wide settings — not per-game, unlike GameMetadataOverride. Lives at
/// %AppData%\HandheldLauncher\settings.json. There's no settings UI yet
/// (just a disabled gear icon placeholder), so for now this file is meant
/// to be hand-edited; AppSettingsStore creates it with empty fields on
/// first run so there's an obvious place to paste values.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// From https://www.steamgriddb.com/profile/preferences/api — needed
    /// for cover-art fetching. Artwork fetching is silently skipped
    /// (not an error) whenever this is empty.
    /// </summary>
    public string? SteamGridDbApiKey { get; set; }
}
