namespace HandheldLauncher.Core.Models;

/// <summary>
/// Everything about a game that the launcher itself owns and must survive a
/// re-scan — a fresh Steam scan rebuilds GameEntry from the manifest every
/// time, so anything we set locally (custom art, collection, playtime) has
/// to live here and get re-applied on top after each scan.
/// </summary>
public class GameMetadataOverride
{
    public string? Collection { get; set; }
    public string? GridImagePath { get; set; }
    public string? HeroImagePath { get; set; }
    public string? LogoImagePath { get; set; }
    public string? IconImagePath { get; set; }
    public long PlaytimeMinutes { get; set; }
    public DateTimeOffset? LastPlayedUtc { get; set; }
}
