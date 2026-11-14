using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.ViewModels;

public sealed class GameTileViewModel : ViewModelBase
{
    private readonly SteamStoreService _steamStore;
    private readonly IgdbService _igdb;
    private readonly ISteamOwnedGamesClient? _steamOwnedGames; // 🟢 NOVO

    public GameTileViewModel(GameEntry game, SteamStoreService steamStore, IgdbService igdb, ISteamOwnedGamesClient? steamOwnedGames = null) 
    { 
        Game = game; 
        _steamStore = steamStore;
        _igdb = igdb;
        _steamOwnedGames = steamOwnedGames; // 🟢 NOVO
    }

    public GameEntry Game { get; }
    public string Title => Game.Title;
    public string? GridImagePath => Game.GridImagePath;
    public string? HeroImagePath => Game.HeroImagePath;
    public string? LogoImagePath => Game.LogoImagePath;
    public string? IconImagePath => Game.IconImagePath;

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
    
    // 🟢 NOVO: Lógica de Tempo Jogado Total (Steam + Launcher)
    private long _steamPlaytimeMinutes;
    public string PlaytimeLabel => GetTotalPlaytimeText();
    
    private string GetTotalPlaytimeText()
    {
        var totalMinutes = Math.Max(_steamPlaytimeMinutes, Game.PlaytimeMinutes);
        if (totalMinutes <= 0) return "Nunca jogado";
        return $"{totalMinutes / 60.0:0.#}h jogadas";
    }

    public string FavoriteLabel => Game.IsFavorite ? "★ Favorito" : "";

    // 🟢 Ajustado para mostrar se tem tempo (Steam ou Local) OU se nunca foi jogado
    public bool HasLastPlayed => Game.LastPlayedUtc.HasValue;
    public bool ShowPlaytime => _steamPlaytimeMinutes > 0 || Game.PlaytimeMinutes > 0 || !HasLastPlayed;

    public string LastPlayedLabel
    {
        get
        {
            if (Game.LastPlayedUtc is not { } lastPlayed) return "";
            var diff = DateTimeOffset.UtcNow - lastPlayed;

            if (diff.TotalMinutes < 60) return "Jogado agora há pouco";
            if (diff.TotalHours < 24) return "Jogado hoje";
            if (diff.TotalHours < 48) return "Jogado ontem";
            if (diff.TotalDays < 30) return $"Jogado há {(int)diff.TotalDays} dias";
            
            return $"Jogado em {lastPlayed.LocalDateTime:dd/MM/yyyy}";
        }
    }

    private string _description = string.Empty;
    public string Description 
    {
        get => System.Net.WebUtility.HtmlDecode(_description);
        set
        {
            if (SetField(ref _description, value))
                OnPropertyChanged(nameof(HasDescription));
        }
    }
    
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    private string _genre = "";
    public string Genre => _genre;
    public bool HasGenre => !string.IsNullOrWhiteSpace(_genre);

    private SteamReviewSummary? _review;
    private bool _reviewLoaded;

    public bool HasReviewData => _review is not null && _review.HasEnoughReviews;
    public int  ReviewStars    => _review?.StarRating ?? 0;
    public string StarsString => _review is null
        ? ""
        : new string('★', _review.StarRating) + new string('☆', 5 - _review.StarRating);
    public string ReviewLabel  => _review is null
        ? "Sem análise ainda"
        : $"{_review.Percentage}% — {_review.ScoreDescription}";
    public string ReviewCountLabel => _review is null
        ? ""
        : $"{_review.TotalReviews:N0} análises";

    // 🏆 Lógica de Conquistas
    private bool _achievementsLoaded;
    private int _achievementsEarned;
    private int _achievementsTotal;

    public bool HasAchievements => _achievementsTotal > 0;
    public string AchievementBadgeLabel => $" {_achievementsEarned}/{_achievementsTotal}";

    private bool _isLoadingDescription;

    public async Task LoadDescriptionAsync()
    {
        if (HasDescription || _isLoadingDescription) return;

        _isLoadingDescription = true;

        if (MainViewModel.IsCloudEntry(Game))
        {
            Description = "☁ Joga na nuvem pelo Xbox Cloud Gaming (precisa de Game Pass Ultimate). A abre no Edge em tela cheia; o login na conta Microsoft é pedido só na primeira vez.";
            _isLoadingDescription = false;
            return;
        }

        if (Game.Source == GameSource.Steam && Game.SteamAppId is { } appId)
        {
            try
            {
                var meta = await _steamStore.FetchMetadataAsync(appId);
                if (meta is not null)
                {
                    Description = !string.IsNullOrWhiteSpace(meta.Description)
                        ? meta.Description
                        : meta.ShortDescription ?? "";

                    _genre = meta.Genres ?? "";
                    OnPropertyChanged(nameof(Genre));
                    OnPropertyChanged(nameof(HasGenre));
                }
            }
            catch { }
        }
        else
        {
            try
            {
                var meta = await _igdb.SearchGameAsync(Title);
                if (meta is not null)
                {
                    Description = !string.IsNullOrWhiteSpace(meta.Description)
                        ? meta.Description
                        : "Informações indisponíveis.";

                    _genre = meta.Genres ?? "";
                    OnPropertyChanged(nameof(Genre));
                    OnPropertyChanged(nameof(HasGenre));

                    if (meta.IgdbRating.HasValue)
                    {
                        int pct = meta.IgdbRating.Value;
                        _review = new SteamReviewSummary
                        {
                            Percentage = pct,
                            TotalReviews = 999,
                            PositiveReviews = 0,
                            ScoreDescription = "IGDB Rating",
                            StarRating = System.Math.Clamp((int)System.Math.Round(pct / 20.0), 1, 5)
                        };
                        
                        OnPropertyChanged(nameof(HasReviewData));
                        OnPropertyChanged(nameof(ReviewStars));
                        OnPropertyChanged(nameof(StarsString));
                        OnPropertyChanged(nameof(ReviewLabel));
                        OnPropertyChanged(nameof(ReviewCountLabel));
                    }
                }
            }
            catch { }
        }

        _isLoadingDescription = false;
    }

    public async Task LoadReviewAsync()
    {
        if (_reviewLoaded) return;
        _reviewLoaded = true;

        if (Game.Source != GameSource.Steam || Game.SteamAppId is not { } appId) return;

        try
        {
            _review = await _steamStore.FetchReviewSummaryAsync(appId);
            OnPropertyChanged(nameof(HasReviewData));
            OnPropertyChanged(nameof(ReviewStars));
            OnPropertyChanged(nameof(StarsString));
            OnPropertyChanged(nameof(ReviewLabel));
            OnPropertyChanged(nameof(ReviewCountLabel));
        }
        catch { }

        // 🟢 Carrega as conquistas e o tempo da Steam junto com a review
        await LoadAchievementsAsync();
        await LoadSteamPlaytimeAsync();
    }

    public async Task LoadAchievementsAsync()
    {
        if (_achievementsLoaded) return;
        _achievementsLoaded = true;

        if (Game.Source != GameSource.Steam || Game.SteamAppId is not { } appId) return;

        try
        {
            var result = await _steamStore.GetAchievementsAsync(appId);
            if (result is not null && result.Achievements is not null && result.Achievements.Count > 0)
            {
                _achievementsTotal = result.Achievements.Count;
                _achievementsEarned = result.Achievements.Count(a => a.Achieved);
                
                OnPropertyChanged(nameof(HasAchievements));
                OnPropertyChanged(nameof(AchievementBadgeLabel));
            }
        }
        catch { }
    }

    // 🟢 NOVO: Busca o tempo jogado na Steam
    public async Task LoadSteamPlaytimeAsync()
    {
        if (_steamPlaytimeMinutes > 0 || _steamOwnedGames is null) return;
        if (Game.Source != GameSource.Steam || Game.SteamAppId is not { } appId) return;

        try
        {
            var owned = await _steamOwnedGames.GetOwnedGamesAsync();
            var match = owned.FirstOrDefault(g => g.AppId == appId);
            if (match != null)
            {
                _steamPlaytimeMinutes = match.PlaytimeMinutes;
                OnPropertyChanged(nameof(PlaytimeLabel));
                OnPropertyChanged(nameof(ShowPlaytime));
            }
        }
        catch { }
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

    public double CarouselTileWidth => 200 * SizeScale;
    public double CarouselTileHeight => 300 * SizeScale;
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

    public double CarouselScale => 1.0 - ClampedDistance * 0.12; 
    // v0.26: era d²×10 (160 px no 4º vizinho) — as capas das pontas desciam para fora da
    // área do carrossel e apareciam cortadas no tema Clean. Agora o degrau é suave (máx. 64 px).
    public double CarouselOffsetY => ClampedDistance * ClampedDistance * 4.0;
    public double CarouselOpacity => 1.0 - ClampedDistance * 0.15;
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