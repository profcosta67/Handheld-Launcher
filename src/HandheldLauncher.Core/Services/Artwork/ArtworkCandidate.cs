namespace HandheldLauncher.Core.Services.Artwork;

/// <summary>
/// One option in the cover/banner picker — a single SteamGridDB image that's
/// already been downloaded to a local cache file, so the UI can show it in
/// an Image control without any async-network-image plumbing (Avalonia has
/// none built in, and adding one would mean a new dependency, same tension
/// as the animated-GIF question in README's "Seletor de capas" section).
///
/// RemoteUrl is kept alongside the local path on purpose: ApplyGridCandidateAsync/
/// ApplyHeroCandidateAsync don't re-download from it today (they just copy
/// the cached file), but having it around means a future "cache got wiped
/// mid-session" edge case has somewhere to recover from without re-querying
/// the API.
/// </summary>
public sealed class ArtworkCandidate
{
    public required int Id { get; init; }
    public required string RemoteUrl { get; init; }
    public required string LocalThumbnailPath { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}
