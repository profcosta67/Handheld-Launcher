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
    /// "CURRENT LOGO" — SteamGridDB's transparent wordmark/title overlay,
    /// a third asset type distinct from GridImagePath/HeroImagePath (see
    /// ArtworkService.FetchLogoAsync). Only ever actually shown when
    /// ShowCurrentLogo is true (see below) — this raw path is exposed
    /// separately in case a future picker needs it directly.
    /// </summary>
    public string? LogoImagePath => Game.LogoImagePath;

    /// <summary>True once SteamGridDB has actually given us a logo for this game (see "Atualizar Capas Automático").</summary>
    public bool HasLogo => !string.IsNullOrEmpty(LogoImagePath);

    /// <summary>
    /// Drives the "CURRENT LOGO" overlay in MainWindow.axaml — deliberately
    /// scoped to ONLY the wide "último jogo aberto" tile (per the user's
    /// request: "em cima *Apenas* da capa do ultimo jogo aberto"), never
    /// the smaller tiles, and only once there's actually a logo to show.
    /// </summary>
    public bool ShowCurrentLogo => IsLastPlayed && HasLogo;

    /// <summary>
    /// The centered title-only placeholder INSIDE the capsule border
    /// (shown pre-SteamGridDB, before any art at all has been fetched for
    /// that specific tile) — every tile now also always shows its name as
    /// a caption ABOVE the capsule (see MainWindow.axaml's "tileCaption"
    /// StackPanel, and the user's correction: "o nome dos jogos deve
    /// aparecer em cima das capas, não dentro dela"), so this placeholder
    /// only covers the case where the image area itself has nothing to
    /// show.
    /// </summary>
    public bool ShowEmptyPlaceholder => string.IsNullOrEmpty(TileImagePath);

    /// <summary>
    /// Must match Theme.axaml's Border.gameTile (170) / .lastPlayed (460)
    /// width exactly — same kind of duplication already accepted
    /// elsewhere in this project (see MainWindow.
    /// ScrollGameRowSelectionIntoView's hardcoded widths). Needed because
    /// the per-tile StackPanel (name caption above, capsule below) has no
    /// width of its own otherwise — it would size itself to whichever is
    /// widest (often the caption text for a long title), breaking the
    /// row's fixed-width layout and the scroll math that assumes it.
    /// </summary>
    public double TileWidth => IsLastPlayed ? 460 : 170;

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
                OnPropertyChanged(nameof(ShowCurrentLogo));
                OnPropertyChanged(nameof(ShowEmptyPlaceholder));
                OnPropertyChanged(nameof(TileWidth));
            }
        }
    }
}
