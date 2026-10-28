namespace HandheldLauncher.Core.Models;

/// <summary>
/// A single game/app the launcher knows about, regardless of where it came from.
/// This is the one shape the UI binds to — Steam scanner, manual entries and
/// future scanners (Xbox app, Epic, emulators) all normalize into this.
/// </summary>
public class GameEntry
{
    /// <summary>
    /// Stable identity for this entry. For Steam games this is "steam_{appid}".
    /// For manual entries it's a generated GUID. Used as the SteamGridDB lookup
    /// key cache and to detect duplicates across re-scans.
    /// </summary>
    public required string Id { get; set; }

    public required string Title { get; set; }

    public GameSource Source { get; set; } = GameSource.Manual;

    /// <summary>Steam AppId when Source == Steam, otherwise null.</summary>
    public uint? SteamAppId { get; set; }

    /// <summary>Full path to the executable to launch (manual/emulator entries).</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>Command-line arguments passed to ExecutablePath, if any.</summary>
    public string? LaunchArguments { get; set; }

    /// <summary>Working directory to launch from. Defaults to the exe's folder.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Install directory, when known (used to help locate save data, icons, etc).</summary>
    public string? InstallDirectory { get; set; }

    // --- Artwork, filled in by the SteamGridDB integration later ---
    public string? GridImagePath { get; set; }   // vertical/box cover (the tile art)
    public string? HeroImagePath { get; set; }   // wide banner for the "now selected" hero area
    public string? LogoImagePath { get; set; }   // transparent logo overlay
    public string? IconImagePath { get; set; }   // small icon (taskbar/list fallback)

    /// <summary>Local playtime tracked by the launcher itself, in minutes.</summary>
    public long PlaytimeMinutes { get; set; }

    public DateTimeOffset? LastPlayedUtc { get; set; }

    /// <summary>User-defined grouping, e.g. "Mario Games", "Indies" (matches the folder concept in the reference UI).</summary>
    public string? Collection { get; set; }

    /// <summary>True while this game's process is currently running.</summary>
    public bool IsRunning { get; set; }
}
