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

    /// <summary>
    /// When true, MainWindow opens fullscreen/borderless instead of the
    /// normal resizable dev window — the "modo kiosk" requested for the
    /// handheld (Legion Go 2 / ROG Ally). Deliberately does NOT replace
    /// the Windows shell: Alt+Tab, Alt+F4 and the taskbar underneath all
    /// keep working exactly as normal, so there's no way to get stuck —
    /// closing the app (or Alt+Tab'ing away) always gets back to a normal
    /// desktop. installer/Install-Launcher.ps1 sets this to true; plain
    /// `dotnet run` during development leaves it false (default) so the
    /// window stays resizable for iterating.
    /// </summary>
    public bool KioskMode { get; set; }
}
