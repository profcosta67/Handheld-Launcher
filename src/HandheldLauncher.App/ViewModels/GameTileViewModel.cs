using System.IO;
using Avalonia.Media.Imaging;
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

    public string SourceLabel => Game.Source switch
    {
        GameSource.Steam => "STEAM",
        GameSource.EpicGames => "EPIC GAMES",
        GameSource.Gog => "GOG",
        GameSource.Ubisoft => "UBISOFT CONNECT",
        GameSource.EaApp => "EA APP",
        GameSource.XboxApp => "XBOX",
        GameSource.Emulator => "EMULADOR",
        GameSource.Manual => "ADICIONADO MANUALMENTE",
        _ => "BIBLIOTECA",
    };

    public string PlaytimeLabel => Game.PlaytimeMinutes switch
    {
        <= 0 => "AINDA NÃO JOGADO",
        < 60 => $"{Game.PlaytimeMinutes} MIN JOGADOS",
        _ => $"{Game.PlaytimeMinutes / 60}H {Game.PlaytimeMinutes % 60:00}MIN JOGADOS",
    };

    public string FavoriteLabel => Game.IsFavorite ? "★ FAVORITO" : string.Empty;

    public string InstallStatusLabel => Game.IsOwnedNotInstalled
        ? "DISPONÍVEL PARA INSTALAR"
        : Game.IsRunning
            ? "EM EXECUÇÃO"
            : "INSTALADO";

    public string? GridImagePath => Game.GridImagePath;
    public string? HeroImagePath => Game.HeroImagePath;
    public string? LogoImagePath => Game.LogoImagePath;
    public string? IconImagePath => Game.IconImagePath;

    /// <summary>
    /// ✅ v15.6-R3: HasLogo agora valida que o arquivo REALMENTE carrega
    /// com Avalonia (new Bitmap), não só que o path existe. Bug do round
    /// anterior: logo baixado + trim corrompido = HasLogo=true escondia
    /// o nome, mas o Image não renderizava nada → tile vazia.
    ///
    /// Agora: se arquivo não existe, ou new Bitmap falha, HasLogo=false e
    /// o nome do jogo aparece em vez do logo quebrado.
    /// </summary>
    private string? _validatedLogoPath;
    private DateTime _validatedLogoLastWriteUtc;
    private bool _hasValidatedLogo;
    private bool _cachedHasLogo;

    public bool HasLogo
    {
        get
        {
            var path = LogoImagePath;
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                if (!File.Exists(path)) return false;

                // Validate each logo only once per file version. Both
                // ShowCurrentLogo and ShowNameCaption bind to HasLogo;
                // decoding the same PNG on every binding read needlessly
                // performs synchronous disk I/O and bitmap decoding on UI.
                var lastWriteUtc = File.GetLastWriteTimeUtc(path);
                if (_hasValidatedLogo &&
                    string.Equals(_validatedLogoPath, path, StringComparison.OrdinalIgnoreCase) &&
                    _validatedLogoLastWriteUtc == lastWriteUtc)
                {
                    return _cachedHasLogo;
                }

                using var bmp = new Bitmap(path);
                _cachedHasLogo = bmp.PixelSize.Width > 0 && bmp.PixelSize.Height > 0;
                _validatedLogoPath = path;
                _validatedLogoLastWriteUtc = lastWriteUtc;
                _hasValidatedLogo = true;
                return _cachedHasLogo;
            }
            catch
            {
                // Do not cache failures: a missing or temporarily locked
                // artwork file may become readable later.
                _hasValidatedLogo = false;
                return false;
            }
        }
    }

    public bool IsFavorite => Game.IsFavorite;
    public bool ShowCurrentLogo => HasLogo;
    public bool ShowNameCaption => !HasLogo;

    public bool ShowEmptyPlaceholder => string.IsNullOrEmpty(TileImagePath);
    public bool IsOwnedNotInstalled => Game.IsOwnedNotInstalled;

    public string? PlatformBadgeText => Game.Source switch
    {
        GameSource.Steam => "S",
        GameSource.EpicGames => "E",
        GameSource.Gog => "G",
        GameSource.Ubisoft => "U",
        GameSource.EaApp => "EA",
        GameSource.XboxApp => "X",
        GameSource.Emulator => "SW",
        GameSource.Manual => "A",
        _ => null,
    };

    public bool HasPlatformBadge => PlatformBadgeText is not null;

    public string PlatformBadgeColorHex => Game.Source switch
    {
        GameSource.Steam => "#1B2838",
        GameSource.EpicGames => "#2A2A2A",
        GameSource.Gog => "#5C3A99",
        GameSource.Ubisoft => "#0070CC",
        GameSource.EaApp => "#C40C28",
        GameSource.XboxApp => "#107C10",
        GameSource.Emulator => "#C2185B",
        GameSource.Manual => "#5A5A5A",
        _ => "#000000",
    };

    public static double SizeScale { get; set; } = 1.0;
    public static double ScaleForSetting(int tileSize) => tileSize switch { 0 => 0.8, 2 => 1.2, _ => 1.0 };

    public double CaptionLogoHeight => (IsLastPlayed ? 32 : 22) * SizeScale;
    public double TileHeight => 290 * SizeScale;
    public double TileWidth => (IsLastPlayed ? 580 : 220) * SizeScale;

    public string? BannerImagePath => Game.HeroImagePath ?? Game.GridImagePath;
    // Não deixa o tile ficar vazio quando a arte principal ainda não existe.
    // Jogos recém-adicionados ou sem wide-grid usam a melhor arte disponível.
    public string? TileImagePath => IsLastPlayed
        ? Game.WideGridImagePath ?? GridImagePath ?? IconImagePath
        : GridImagePath ?? Game.WideGridImagePath ?? IconImagePath;

    public double CarouselTileWidth => 220 * SizeScale;
    public double CarouselTileHeight => 290 * SizeScale;
    public string? CarouselTileImagePath => GridImagePath ?? Game.WideGridImagePath ?? IconImagePath;
    public bool ShowEmptyCarouselPlaceholder => string.IsNullOrEmpty(CarouselTileImagePath);

    private int _carouselDistance;
    public int CarouselDistance
    {
        get => _carouselDistance;
        set
        {
            if (SetField(ref _carouselDistance, value))
            {
                OnPropertyChanged(nameof(CarouselScale));
                OnPropertyChanged(nameof(CarouselOffsetY));
                OnPropertyChanged(nameof(CarouselOpacity));
                OnPropertyChanged(nameof(CarouselZIndex));
            }
        }
    }

    private int ClampedDistance => Math.Min(Math.Abs(_carouselDistance), 4);
    public double CarouselScale => 1.42 - ClampedDistance * 0.16;
    public double CarouselOffsetY => ClampedDistance * ClampedDistance * 14.0;
    public double CarouselOpacity => 1.0 - ClampedDistance * 0.14;
    public int CarouselZIndex => 100 - ClampedDistance;

    private bool _isFocused;
    public bool IsFocused { get => _isFocused; set => SetField(ref _isFocused, value); }

    private bool _isLastPlayed;
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
                OnPropertyChanged(nameof(TileImagePath));
                OnPropertyChanged(nameof(ShowEmptyPlaceholder));
            }
        }
    }
}