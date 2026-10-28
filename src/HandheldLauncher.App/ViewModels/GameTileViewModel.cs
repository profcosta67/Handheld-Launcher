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
    /// Wide banner art (SteamGridDB "hero"). Independent fetch from
    /// GridImagePath — a game can have one without the other.
    /// </summary>
    public string? HeroImagePath => Game.HeroImagePath;

    /// <summary>
    /// What the big top banner shows for whichever game the player is
    /// currently navigated over (the top banner follows SelectedTile, so
    /// this re-evaluates every time MainWindow rebinds it): the real hero
    /// art when we have it, otherwise the portrait cover stretched to fill
    /// (still better than a blank panel), otherwise null for the
    /// title-only placeholder.
    /// </summary>
    public string? BannerImagePath => Game.HeroImagePath ?? Game.GridImagePath;

    /// <summary>
    /// What this tile shows IN THE ROW. Independent of navigation/focus —
    /// only IsLastPlayed decides it, so moving the cursor over other games
    /// doesn't change which tile is big: only actually launching a game
    /// does (see MainViewModel.LaunchSelected).
    /// </summary>
    public string? TileImagePath => IsLastPlayed ? BannerImagePath : GridImagePath;

    private bool _isFocused;
    public bool IsFocused
    {
        get => _isFocused;
        set => SetField(ref _isFocused, value);
    }

    private bool _isLastPlayed;

    /// <summary>
    /// True for exactly one tile (or none, on a fresh library with nothing
    /// played yet): the "último jogo aberto", rendered as a wide landscape
    /// tile using hero art instead of the normal small portrait cover. Set
    /// by MainViewModel whenever a game is launched or the library
    /// reorders/refreshes — never by navigation alone.
    /// </summary>
    public bool IsLastPlayed
    {
        get => _isLastPlayed;
        set
        {
            if (SetField(ref _isLastPlayed, value))
            {
                OnPropertyChanged(nameof(TileImagePath));
            }
        }
    }
}
