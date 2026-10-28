namespace HandheldLauncher.Core.Services.Artwork;

public sealed class SteamGridDbImage
{
    public required int Id { get; init; }
    public required string Url { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}
