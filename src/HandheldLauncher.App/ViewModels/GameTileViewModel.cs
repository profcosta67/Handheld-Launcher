using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services; // 🟢 ADICIONADO

namespace HandheldLauncher.App.ViewModels;

public sealed class GameTileViewModel : ViewModelBase
{
    private readonly SteamStoreService _steamStore; // 🟢 INJEÇÃO

    // 🟢 Construtor agora exige o serviço
    public GameTileViewModel(GameEntry game, SteamStoreService steamStore) 
    { 
        Game = game; 
        _steamStore = steamStore;
    }

    public GameEntry Game { get; }
    public string Title => Game.Title;
    public string? GridImagePath => Game.GridImagePath;
    public string? HeroImagePath => Game.HeroImagePath;
    public string? LogoImagePath => Game.LogoImagePath;
    public string? IconImagePath => Game.IconImagePath;

    // v0.18.3 (pente fino de desempenho): antes, cada leitura de HasLogo
    // DECODIFICAVA a imagem inteira do logo (new Bitmap) só pra saber se era
    // válida — e ShowCurrentLogo/ShowNameCaption leem isso, então eram 2
    // decodificações por bloco a cada troca de aba/favoritar (a biblioteca
    // inteira é recriada nessas horas), tudo na thread da tela. Agora o
    // resultado fica guardado por arquivo + data de modificação (troca de
    // logo pelo SteamGridDB muda a data, então continua atualizando sozinho).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime LastWriteUtc, bool Valid)> LogoValidityCache = new();

    public bool HasLogo
    {
        get
        {
            var path = LogoImagePath;
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                if (!File.Exists(path)) return false;
                var lastWrite = File.GetLastWriteTimeUtc(path);
                if (LogoValidityCache.TryGetValue(path, out var cached) && cached.LastWriteUtc == lastWrite) return cached.Valid;
                bool valid;
                try
                {
                    using var bmp = new Bitmap(path);
                    valid = bmp.PixelSize.Width > 0 && bmp.PixelSize.Height > 0;
                }
                catch { valid = false; }
                LogoValidityCache[path] = (lastWrite, valid);
                return valid;
            }
            catch { return false; }
        }
    }

    public bool IsFavorite => Game.IsFavorite;
    public bool ShowCurrentLogo => HasLogo;
    public bool ShowNameCaption => !HasLogo;
    public bool IsOwnedNotInstalled => Game.IsOwnedNotInstalled;
    public bool ShowEmptyPlaceholder => string.IsNullOrEmpty(TileImagePath);

    public string SourceLabel => Game.Source switch
    {
        GameSource.Steam => "STEAM", GameSource.EpicGames => "EPIC", GameSource.Gog => "GOG",
        GameSource.Ubisoft => "UBISOFT", GameSource.EaApp => "EA", GameSource.XboxApp => "XBOX",
        GameSource.Emulator => "EMULADOR", GameSource.Manual => "ATALHO", _ => ""
    };
    public string InstallStatusLabel => Game.IsOwnedNotInstalled ? "NÃO INSTALADO" : "INSTALADO";
    public string PlaytimeLabel => Game.PlaytimeMinutes > 0 ? $"{Game.PlaytimeMinutes / 60.0:0.#}h jogadas" : "Nunca jogado";
    public string FavoriteLabel => Game.IsFavorite ? "★ Favorito" : "";

    private string _description = string.Empty;
    public string Description 
{
    get => System.Net.WebUtility.HtmlDecode(_description);
    set => _description = value;
}
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    
    private bool _isLoadingDescription;
    
    public async Task LoadDescriptionAsync() 
    { 
        // 🟢 Se já carregou ou está carregando, não faz nada
        if (HasDescription || _isLoadingDescription) return;
        
        _isLoadingDescription = true;

        // v0.18 aba "Nuvem": entrada sintética do Xbox Cloud Gaming.
        if (MainViewModel.IsCloudEntry(Game))
        {
            Description = "☁ Joga na nuvem pelo Xbox Cloud Gaming (precisa de Game Pass Ultimate). A abre no Edge em tela cheia; o login na conta Microsoft é pedido só na primeira vez.";
            _isLoadingDescription = false;
            return;
        }

        // 🟢 Se não for da Steam, coloca fallback
        if (Game.Source != GameSource.Steam || Game.SteamAppId is not { } appId)
        {
            Description = "Informações detalhadas indisponíveis para este jogo.";
            _isLoadingDescription = false;
            return;
        }

        try
        {
            var meta = await _steamStore.FetchMetadataAsync(appId);
            if (meta is not null)
            {
                Description = !string.IsNullOrWhiteSpace(meta.Description) 
                    ? meta.Description 
                    : meta.ShortDescription ?? "";
            }
            else
            {
                Description = "Não foi possível carregar a descrição no momento.";
            }
        }
        catch 
        {
            Description = "Erro ao carregar descrição.";
        }
        finally
        {
            _isLoadingDescription = false;
        }
    }

    public string? PlatformBadgeText => Game.Source switch
    {
        GameSource.Steam => "S", GameSource.EpicGames => "E", GameSource.Gog => "G",
        GameSource.Ubisoft => "U", GameSource.EaApp => "EA", GameSource.XboxApp => "X",
        GameSource.Emulator => "SW", GameSource.Manual => "A", _ => null,
    };
    public bool HasPlatformBadge => PlatformBadgeText is not null;

    public string PlatformBadgeColorHex => Game.Source switch
    {
        GameSource.Steam => "#1B2838", GameSource.EpicGames => "#2A2A2A", GameSource.Gog => "#5C3A99",
        GameSource.Ubisoft => "#0070CC", GameSource.EaApp => "#C40C28", GameSource.XboxApp => "#107C10",
        GameSource.Emulator => "#C2185B", GameSource.Manual => "#5A5A5A", _ => "#000000",
    };

    public static double SizeScale { get; set; } = 1.0;
    public static double ScaleForSetting(int tileSize) => tileSize switch { 0 => 0.8, 2 => 1.2, _ => 1.0 };

    public double CaptionLogoHeight => (IsLastPlayed ? 32 : 22) * SizeScale;
    public double TileHeight => 290 * SizeScale;
    public double TileWidth => (IsLastPlayed ? 580 : 220) * SizeScale;

    public string? BannerImagePath => Game.HeroImagePath ?? Game.GridImagePath;

    private static string? ExistsOrNull(string? path)
        => !string.IsNullOrEmpty(path) && File.Exists(path) ? path : null;

    public string? TileImagePath => IsLastPlayed
        ? (ExistsOrNull(Game.WideGridImagePath) ?? ExistsOrNull(Game.HeroImagePath) ?? ExistsOrNull(Game.GridImagePath))
        : ExistsOrNull(GridImagePath);

    public string? WideCapsuleImagePath
        => ExistsOrNull(Game.WideGridImagePath) ?? ExistsOrNull(Game.HeroImagePath) ?? ExistsOrNull(Game.GridImagePath);
    public bool ShowEmptyWideCapsulePlaceholder => string.IsNullOrEmpty(WideCapsuleImagePath);

    public double CarouselTileWidth => 220 * SizeScale;
    public double CarouselTileHeight => 290 * SizeScale;
    public string? CarouselTileImagePath => ExistsOrNull(GridImagePath) ?? ExistsOrNull(Game.WideGridImagePath) ?? ExistsOrNull(Game.HeroImagePath);
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

    private int ClampedDistance => System.Math.Min(System.Math.Abs(_carouselDistance), 4);
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