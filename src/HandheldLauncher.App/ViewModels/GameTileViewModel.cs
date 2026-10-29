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

    /// <summary>Ícone quadrado (SteamGridDB, seção Icons) — cabeçalho do menu do jogo.</summary>
    public string? IconImagePath => Game.IconImagePath;

    /// <summary>True once SteamGridDB has actually given us a logo for this game (see "Atualizar Capas Automático").</summary>
    public bool HasLogo => !string.IsNullOrEmpty(LogoImagePath);

    /// <summary>
    /// Toggled pelo item "Favoritar"/"Desfavoritar" do menu de contexto
    /// (ver MainViewModel.ConfirmContextMenuSelection) — drives a estrelinha
    /// discreta no canto da tile, pra dar algum jeito de ver quem tá
    /// favoritado sem precisar entrar na aba "Favoritos" pra checar.
    /// </summary>
    public bool IsFavorite => Game.IsFavorite;

    /// <summary>
    /// True pra uma tile da aba "Não instalados" (jogo da conta Steam
    /// ainda não instalado nesta máquina — ver GameEntry.
    /// IsOwnedNotInstalled). Drives o badge "Instalar" no canto da tile
    /// (MainWindow.axaml) — confirmar/clicar nessas tiles instala em vez
    /// de abrir (ver MainViewModel.LaunchSelected/BuildContextMenu).
    /// </summary>
    public bool IsOwnedNotInstalled => Game.IsOwnedNotInstalled;

    /// <summary>
    /// Drives showing the LOGO instead of the game's name — in the hero
    /// banner's bottom-left corner (MainWindow.axaml, follows SelectedTile)
    /// AND above every tile's capsule, CAPSULE or WIDE CAPSULE alike (see
    /// ShowNameCaption below for the exact opposite). Per the user's
    /// explicit, final rule: "a LOGO não vai dentro das capas... ele vai
    /// apenas dentro da hero (banner) lá em cima e ACIMA DAS CAPAS NO LUGAR
    /// DO NOME" — logo replaces the name wherever a logo exists, on ANY
    /// tile (earlier in this session this was scoped to only the WIDE
    /// CAPSULE — that was wrong, this corrects it). Only true once
    /// SteamGridDB has actually given us a logo for this specific game.
    /// </summary>
    public bool ShowCurrentLogo => HasLogo;

    /// <summary>
    /// Drives showing the plain name instead — the exact complement of
    /// ShowCurrentLogo (see above): whichever tile doesn't have a logo yet
    /// falls back to its name, CAPSULE or WIDE CAPSULE alike. The two are
    /// never shown together on the same tile/banner.
    /// </summary>
    public bool ShowNameCaption => !HasLogo;

    /// <summary>
    /// How tall the logo image renders above the tile's capsule (see
    /// MainWindow.axaml's caption Grid) — a bit bigger on the WIDE CAPSULE
    /// than on a normal CAPSULE, per the user's request ("deixa ela um
    /// pouco maior o tamanho na wide capsule"). The caption Grid itself
    /// stays a FIXED height for every tile regardless (see MainWindow.axaml
    /// comment) so this size difference never shifts the capsule images
    /// below out of alignment across the row.
    /// </summary>
    public double CaptionLogoHeight => (IsLastPlayed ? 32 : 22) * SizeScale;

    /// <summary>
    /// Escala do "Tamanho das capas" (Configurações, v0.6.0): 0.8 pequeno,
    /// 1.0 médio, 1.2 grande. Estático porque vale pra biblioteca inteira;
    /// MainViewModel recria as tiles depois de mudar.
    /// </summary>
    public static double SizeScale { get; set; } = 1.0;

    public static double ScaleForSetting(int tileSize) => tileSize switch { 0 => 0.8, 2 => 1.2, _ => 1.0 };

    /// <summary>Altura da capa (bindada direto no Border.gameTile desde a v0.6.0).</summary>
    public double TileHeight => 290 * SizeScale;

    /// <summary>
    /// The centered title-only placeholder INSIDE the capsule border
    /// (shown pre-SteamGridDB, before any art at all has been fetched for
    /// that specific tile) — every CAPSULE tile also shows a caption
    /// (name or logo, see ShowNameCaption/ShowCurrentLogo) ABOVE it, so this
    /// placeholder only covers the case where the image area itself has
    /// nothing to show.
    /// </summary>
    public bool ShowEmptyPlaceholder => string.IsNullOrEmpty(TileImagePath);

    /// <summary>
    /// Must match Theme.axaml's Border.gameTile (220) / .lastPlayed (620)
    /// width exactly — same kind of duplication already accepted
    /// elsewhere in this project (see MainWindow.
    /// ScrollGameRowSelectionIntoView's hardcoded widths). Needed because
    /// the per-tile StackPanel (caption above, capsule below) has no
    /// width of its own otherwise — it would size itself to whichever is
    /// widest (often the caption text for a long title), breaking the
    /// row's fixed-width layout and the scroll math that assumes it.
    /// 170/460 → 190/510 → 220/620 → 220/580 nesta leva — a WIDE CAPSULE
    /// encolheu um pouco (620 → 580) porque o usuário reportou que ela
    /// estava "pegando o ícone do lado" (sobrepondo a próxima tile).
    /// </summary>
    public double TileWidth => (IsLastPlayed ? 580 : 220) * SizeScale;

    /// <summary>
    /// What the hero banner (the crisp, non-blurred box back at the top of
    /// MainWindow, restored this leva — see its comment there) AND the
    /// full-window blurred background both show for whichever game the
    /// player is currently navigated over (both follow SelectedTile, so
    /// this re-evaluates every time MainWindow rebinds it): the real hero
    /// art when we have it, otherwise the portrait cover stretched to fill
    /// (still better than a blank panel), otherwise null. The WIDE CAPSULE
    /// tile in the row never uses this — see TileImagePath below, always
    /// cover/grid art regardless of size.
    /// </summary>
    public string? BannerImagePath => Game.HeroImagePath ?? Game.GridImagePath;

    /// <summary>
    /// What this tile shows IN THE ROW. CAPSULE normal: sempre o
    /// GridImagePath (capa portrait), igual sempre foi. WIDE CAPSULE
    /// ("último jogo aberto"): agora usa Game.WideGridImagePath — um
    /// asset INDEPENDENTE (capa landscape, buscada separado — ver
    /// GameEntry.WideGridImagePath), NUNCA mais GridImagePath. Correção
    /// desta leva: antes a WIDE CAPSULE reusava a mesma capa portrait da
    /// CAPSULE normal só renderizada mais larga, e isso esticava/puxava a
    /// imagem vertical dentro da caixa horizontal — bug reportado pelo
    /// usuário como urgente. Sem WideGridImagePath baixado ainda, cai pro
    /// placeholder vazio (ShowEmptyPlaceholder) — de propósito NÃO cai de
    /// volta pro GridImagePath esticado, que é exatamente o visual que
    /// estava sendo corrigido.
    /// </summary>
    public string? TileImagePath => IsLastPlayed ? Game.WideGridImagePath : GridImagePath;

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
                OnPropertyChanged(nameof(TileWidth));
                OnPropertyChanged(nameof(TileHeight));
                OnPropertyChanged(nameof(CaptionLogoHeight));

                // TileImagePath agora depende de IsLastPlayed (WideGridImagePath
                // vs GridImagePath, ver TileImagePath acima) — sem isso, ao
                // lançar um jogo diferente (LaunchSelected chama
                // ReorderLastPlayedFirst SEM reconstruir as tiles do zero),
                // a imagem continuaria mostrando a capa errada até o
                // próximo Refresh() completo.
                OnPropertyChanged(nameof(TileImagePath));
                OnPropertyChanged(nameof(ShowEmptyPlaceholder));
            }
        }
    }
}
