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
    /// Drives the "CURRENT LOGO" shown ABOVE the tile (in the caption area,
    /// see MainWindow.axaml) — deliberately scoped to ONLY the WIDE CAPSULE
    /// ("último jogo aberto" tile), never the smaller CAPSULE tiles, and
    /// only once there's actually a logo to show. Per the user's explicit
    /// split: "logo em cima do wide capsule sem nome escrito, e os nomes
    /// escrito sem logo em cima da capsule menores" — the WIDE CAPSULE
    /// shows ONLY the logo above it (never the name too), and if there's no
    /// logo yet, that caption area is just blank (no name fallback there).
    /// </summary>
    public bool ShowCurrentLogo => IsLastPlayed && HasLogo;

    /// <summary>
    /// Drives the plain name caption above a CAPSULE tile — the exact
    /// complement of ShowCurrentLogo (see above): every tile except the
    /// WIDE CAPSULE gets its name written above it, and the WIDE CAPSULE
    /// never does (logo instead, or nothing while no logo has been fetched
    /// yet — the two are never shown together on the same tile).
    /// </summary>
    public bool ShowNameCaption => !IsLastPlayed;

    /// <summary>
    /// The centered title-only placeholder INSIDE the capsule border
    /// (shown pre-SteamGridDB, before any art at all has been fetched for
    /// that specific tile) — every CAPSULE tile also shows its name as a
    /// caption ABOVE it (see ShowNameCaption/MainWindow.axaml's
    /// "tileCaption"), so this placeholder only covers the case where the
    /// image area itself has nothing to show.
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
    /// What the full-window background (the blurred/stretched backdrop
    /// behind the whole app, see MainWindow.axaml's Panel of Panel bound to
    /// SelectedTile.BannerImagePath) shows for whichever game the player is
    /// currently navigated over. This is now the ONLY place HERO art is
    /// used — per the user's explicit split ("hero (banner) vai ser a tela
    /// do plano de fundo da tela"), there is no separate top banner box
    /// anymore, and the WIDE CAPSULE tile in the row uses the cover/grid
    /// art (TileImagePath below), never this. Falls back to the portrait
    /// cover when there's no hero yet (still better than a blank
    /// background), then null (background Panel just hides itself).
    /// </summary>
    public string? BannerImagePath => Game.HeroImagePath ?? Game.GridImagePath;

    /// <summary>
    /// What this tile shows IN THE ROW — always the cover/grid art
    /// ("CAPSULE"/"WIDE CAPSULE" per the user's SteamGridDB terminology),
    /// for every tile including the WIDE CAPSULE one. Deliberately never
    /// hero art here anymore (that's BannerImagePath's job now, background
    /// only) — the WIDE CAPSULE is just the same cover art rendered bigger,
    /// not a different asset.
    /// </summary>
    public string? TileImagePath => GridImagePath;

    private bool _isFocused;
    public bool IsFocused
    {
        get => _isFocused;
        set => SetField(ref _isFocused, value);
    }

    private bool _isLastPlayed;

    /// <summary>
    /// True for exactly one tile (or none, on a fresh library with nothing
    /// played yet): the "último jogo aberto" / WIDE CAPSULE, rendered wider
    /// than the normal CAPSULE tiles (same cover art, just bigger — see
    /// TileWidth/TileImagePath). Set by MainViewModel whenever a game is
    /// launched or the library reorders/refreshes — never by navigation
    /// alone.
    /// </summary>
    public bool IsLastPlayed
    {
        get => _isLastPlayed;
        set
        {
            if (SetField(ref _isLastPlayed, value))
            {
                OnPropertyChanged(nameof(ShowCurrentLogo));
                OnPropertyChanged(nameof(ShowNameCaption));
                OnPropertyChanged(nameof(TileWidth));
            }
        }
    }
}
