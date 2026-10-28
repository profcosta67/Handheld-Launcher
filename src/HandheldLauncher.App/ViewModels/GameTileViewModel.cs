using HandheldLauncher.Core.Models;

namespace HandheldLauncher.App.ViewModels;

/// <summary>
/// Thin display wrapper around a GameEntry for one tile in the horizontal
/// row. Kept separate from GameEntry so the UI can carry view-only state
/// (IsFocused) without polluting the Core model.
/// </summary>
public sealed class GameTileViewModel : ViewModelBase
{
    public GameTileViewModel(GameEntry game)
    {
        Game = game;
    }

    public GameEntry Game { get; }

    public string Title => Game.Title;

    /// <summary>
    /// Cover art to show on the tile. Falls back to null (the view shows a
    /// title-only placeholder tile) until SteamGridDB art is wired in.
    /// </summary>
    public string? GridImagePath => Game.GridImagePath;

    /// <summary>
    /// Wide banner art (SteamGridDB "hero") used when this tile is the
    /// focused/hero game. Independent fetch from GridImagePath — a game can
    /// have one without the other.
    /// </summary>
    public string? HeroImagePath => Game.HeroImagePath;

    /// <summary>
    /// What the big landscape banner should show for this game: the real
    /// hero art when we have it, otherwise the portrait cover stretched to
    /// fill (still better than a blank panel), otherwise null for the
    /// title-only placeholder.
    /// </summary>
    public string? BannerImagePath => Game.HeroImagePath ?? Game.GridImagePath;

    private bool _isFocused;
    public bool IsFocused
    {
        get => _isFocused;
        set => SetField(ref _isFocused, value);
    }
}
