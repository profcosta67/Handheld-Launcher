using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.App.ViewModels;

public sealed class GameTileViewModel : ViewModelBase
{
    public GameTileViewModel(GameEntry game)
    {
        Game = game;
    }

    public GameEntry Game { get; }

    public string Title => Game.Title;

    public string? GridImagePath => Game.GridImagePath;
    public string? HeroImagePath => Game.HeroImagePath;
    public string? LogoImagePath => Game.LogoImagePath;
    public string? IconImagePath => Game.IconImagePath;

    public bool HasLogo
    {
        get
        {
            var path = LogoImagePath;
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                if (!File.Exists(path)) return false;
                using var bmp = new Bitmap(path);
                return bmp.PixelSize.Width > 0 && bmp.PixelSize.Height > 0;
            }
            catch { return false; }
        }
    }

    public bool IsFavorite => Game.IsFavorite;
    public bool ShowCurrentLogo => HasLogo;
    public bool ShowNameCaption => !HasLogo;
    public bool IsOwnedNotInstalled => Game.IsOwnedNotInstalled;
    public bool ShowEmptyPlaceholder => string.IsNullOrEmpty(TileImagePath);

    // 🟢 Label properties para o BigScreenLayout
    public string SourceLabel => Game.Source switch
    {
        GameSource.Steam => "STEAM",
        GameSource.EpicGames => "EPIC",
        GameSource.Gog => "GOG",
        GameSource.Ubisoft => "UBISOFT",
        GameSource.EaApp => "EA",
        GameSource.XboxApp => "XBOX",
        GameSource.Emulator => "EMULADOR",
        GameSource.Manual => "ATALHO",
        _ => ""
    };

    public string InstallStatusLabel => Game.IsOwnedNotInstalled ? "NÃO INSTALADO" : "INSTALADO";
    
    public string PlaytimeLabel => Game.PlaytimeMinutes > 0 
        ? $"{Game.PlaytimeMinutes / 60.0:0.#}h jogadas" 
        : "Nunca jogado";

    public string FavoriteLabel => Game.IsFavorite ? "★ Favorito" : "";

    // 🟢 Descrição do jogo (carregada via RAWG)
    private string _description = string.Empty;
    public string Description
    {
        get => _description;
        private set
        {
            if (SetField(ref _description, value))
                OnPropertyChanged(nameof(HasDescription));
        }
    }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public async Task LoadDescriptionAsync()
    {
        try { await Task.CompletedTask; } catch { }
    }

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

    public string? TileImagePath => IsLastPlayed
        ? (Game.WideGridImagePath ?? Game.HeroImagePath ?? Game.GridImagePath)
        : GridImagePath;

    public string? WideCapsuleImagePath => Game.WideGridImagePath ?? Game.HeroImagePath ?? Game.GridImagePath;
    public bool ShowEmptyWideCapsulePlaceholder => string.IsNullOrEmpty(WideCapsuleImagePath);

    public double CarouselTileWidth => 220 * SizeScale;
    public double CarouselTileHeight => 290 * SizeScale;
    public string? CarouselTileImagePath => GridImagePath ?? Game.WideGridImagePath ?? Game.HeroImagePath;
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