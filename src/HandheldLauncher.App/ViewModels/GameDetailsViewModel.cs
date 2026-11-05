using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Platform;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.ViewModels;

/// <summary>
/// Página de detalhes do jogo. v0.9.11 nasceu como janela de consulta;
/// v0.18 "Detalhes 2.0" (Fase 2 do roadmap) vira a central do jogo:
/// cabeçalho com ações, faixa de informações e 5 seções navegáveis por
/// LB/RB (Visão geral, Conquistas, Mídia, Desempenho, Arquivos), com cache
/// em disco pra funcionar sem internet depois da primeira visita.
///
/// Tudo o que a versão anterior mostrava continua aqui (capa/banner/logo,
/// horas jogadas, desenvolvedora/gêneros/lançamento, descrição, conquistas
/// com progresso, fallback RAWG pra jogos fora da Steam) — só reorganizado.
/// </summary>
public sealed class GameDetailsViewModel : ViewModelBase
{
    private static readonly HttpClient IconHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly HttpClient StoreHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    static GameDetailsViewModel()
    {
        try
        {
            StoreHttp.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppUserAgent.Value} (Windows; C#)");
            StoreHttp.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }
        catch { /* melhor esforço */ }
    }

    private static readonly string IconCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HandheldLauncher", "artwork", "achievements");

    public static readonly string[] SectionNames = ["Visão geral", "Conquistas", "Mídia", "Desempenho", "Arquivos"];

    private const int MaxScreenshots = 6;

    private readonly ISteamOwnedGamesClient? _ownedGames;
    private readonly ISteamAchievementsClient? _achievements;

    public GameDetailsViewModel(GameEntry game, ISteamOwnedGamesClient? ownedGames, ISteamAchievementsClient? achievements)
    {
        Game = game;
        _ownedGames = ownedGames;
        _achievements = achievements;
        Achievements = new ObservableCollection<AchievementItemViewModel>();
        NextAchievements = new ObservableCollection<AchievementItemViewModel>();
        Tags = new ObservableCollection<DetailsTagItem>();
        Screenshots = new ObservableCollection<DetailsImageItem>();

        _isFavorite = game.IsFavorite;
        BuildStaticInfo();

        DebugLog.Write($"[GameDetails] CTOR: Title='{game.Title}', SteamAppId={game.SteamAppId?.ToString() ?? "null"}, Source={game.Source}");
        _ = LoadAsync();
        _ = LoadFolderSizeAsync();
    }

    public GameEntry Game { get; }
    public string Title => Game.Title;
    public string? CoverImagePath => Game.GridImagePath;
    public string? BannerImagePath => Game.HeroImagePath ?? Game.WideGridImagePath;
    public string? LogoImagePath => Game.LogoImagePath;

    // ---- Estado geral -------------------------------------------------------------

    private bool _isLoading = true;
    public bool IsLoading { get => _isLoading; private set => SetField(ref _isLoading, value); }

    private string _dataSourceNote = "";
    /// <summary>"Atualizado agora" / "Sem conexão — dados salvos em 03/11" etc.</summary>
    public string DataSourceNote { get => _dataSourceNote; private set => SetField(ref _dataSourceNote, value); }

    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set { if (SetField(ref _isFavorite, value)) { OnPropertyChanged(nameof(FavoriteLabel)); OnPropertyChanged(nameof(FavoriteBadge)); } }
    }
    public string FavoriteLabel => IsFavorite ? "Desfavoritar" : "Favoritar";
    public string FavoriteBadge => IsFavorite ? "★ Favorito" : "";

    // ---- Seções (LB/RB) -------------------------------------------------------------

    private int _sectionIndex;
    public int SectionIndex
    {
        get => _sectionIndex;
        set
        {
            var clamped = ((value % SectionNames.Length) + SectionNames.Length) % SectionNames.Length;
            if (!SetField(ref _sectionIndex, clamped)) return;
            OnPropertyChanged(nameof(IsOverviewSection));
            OnPropertyChanged(nameof(IsAchievementsSection));
            OnPropertyChanged(nameof(IsMediaSection));
            OnPropertyChanged(nameof(IsPerformanceSection));
            OnPropertyChanged(nameof(IsFilesSection));
            OnPropertyChanged(nameof(SectionActionLabel));
            OnPropertyChanged(nameof(HasSectionAction));
        }
    }

    public bool IsOverviewSection => SectionIndex == 0;
    public bool IsAchievementsSection => SectionIndex == 1;
    public bool IsMediaSection => SectionIndex == 2;
    public bool IsPerformanceSection => SectionIndex == 3;
    public bool IsFilesSection => SectionIndex == 4;

    /// <summary>
    /// Ação do botão Y na seção atual. O plano previa "Y = Receita do jogo",
    /// mas Receitas só existem na v0.20 — até lá o Y faz a ação útil da seção
    /// (abrir trailer/página da loja em Mídia, abrir a pasta em Arquivos).
    /// </summary>
    public string SectionActionLabel => SectionIndex switch
    {
        2 when HasTrailerOrStorePage => HasTrailer ? "Ver trailer na loja" : "Abrir página na loja",
        4 when CanOpenGameFolder => "Abrir pasta do jogo",
        _ => "",
    };
    public bool HasSectionAction => SectionActionLabel.Length > 0;

    // ---- Faixa de informações ---------------------------------------------------

    public string PlatformText { get; private set; } = "";
    public string LastPlayedText { get; private set; } = "";
    public string LauncherPlaytimeText { get; private set; } = "";

    private string _totalPlaytimeText = "";
    public string TotalPlaytimeText { get => _totalPlaytimeText; private set => SetField(ref _totalPlaytimeText, value); }

    private string _sizeText = "Calculando...";
    public string SizeText { get => _sizeText; private set => SetField(ref _sizeText, value); }

    // ---- Visão geral --------------------------------------------------------------

    private string _playtimeText = "Carregando horas jogadas...";
    /// <summary>Horas da Steam (mantido da versão anterior; usado em Desempenho).</summary>
    public string PlaytimeText { get => _playtimeText; private set => SetField(ref _playtimeText, value); }

    private string _description = "";
    public string Description { get => _description; private set => SetField(ref _description, value); }

    private string _longDescription = "";
    public string LongDescription
    {
        get => _longDescription;
        private set { if (SetField(ref _longDescription, value)) OnPropertyChanged(nameof(HasLongDescription)); }
    }
    public bool HasLongDescription => !string.IsNullOrWhiteSpace(LongDescription);

    private string _metadataText = "";
    public string MetadataText { get => _metadataText; private set => SetField(ref _metadataText, value); }

    public ObservableCollection<DetailsTagItem> Tags { get; }

    // ---- Conquistas ---------------------------------------------------------------

    private string _achievementsStatusText = "Carregando conquistas...";
    public string AchievementsStatusText { get => _achievementsStatusText; private set => SetField(ref _achievementsStatusText, value); }

    private double _achievementProgress = 0;
    public double AchievementProgress { get => _achievementProgress; private set => SetField(ref _achievementProgress, value); }

    public ObservableCollection<AchievementItemViewModel> Achievements { get; }

    /// <summary>As 3 bloqueadas que mais jogadores já têm — "as mais próximas", em destaque no topo.</summary>
    public ObservableCollection<AchievementItemViewModel> NextAchievements { get; }

    private bool _hasNextAchievements;
    public bool HasNextAchievements { get => _hasNextAchievements; private set => SetField(ref _hasNextAchievements, value); }

    // ---- Mídia --------------------------------------------------------------------

    public ObservableCollection<DetailsImageItem> Screenshots { get; }

    private string _mediaStatusText = "Carregando mídia...";
    public string MediaStatusText { get => _mediaStatusText; private set => SetField(ref _mediaStatusText, value); }

    private bool _hasTrailer;
    public bool HasTrailer
    {
        get => _hasTrailer;
        private set { if (SetField(ref _hasTrailer, value)) { OnPropertyChanged(nameof(SectionActionLabel)); OnPropertyChanged(nameof(HasSectionAction)); } }
    }

    public bool HasTrailerOrStorePage => Game.SteamAppId.HasValue;
    public string? StorePageUrl => Game.SteamAppId is { } id ? $"https://store.steampowered.com/app/{id}" : null;

    // ---- Arquivos -----------------------------------------------------------------

    public string GameFolderText { get; private set; } = "";
    public string? GameFolderPath { get; private set; }
    public bool CanOpenGameFolder => GameFolderPath is not null && Directory.Exists(GameFolderPath);
    public string SaveFolderText { get; private set; } = "";
    public string SaveFolderHint { get; private set; } = "";

    // ==================================================================================

    private void BuildStaticInfo()
    {
        PlatformText = Game.Source switch
        {
            GameSource.Steam => "Steam",
            GameSource.XboxApp => "Xbox / Game Pass",
            GameSource.EpicGames => "Epic Games",
            GameSource.Gog => "GOG",
            GameSource.EaApp => "EA app",
            GameSource.Ubisoft => "Ubisoft Connect",
            GameSource.Emulator => "Emulador",
            _ => "Adicionado manualmente",
        };

        LastPlayedText = Game.LastPlayedUtc is { } played ? FormatRelativeDate(played) : "Nunca aberto pelo launcher";
        LauncherPlaytimeText = Game.PlaytimeMinutes > 0 ? FormatDuration(Game.PlaytimeMinutes) : "Nenhuma sessão medida ainda";
        TotalPlaytimeText = Game.PlaytimeMinutes > 0 ? FormatDuration(Game.PlaytimeMinutes) : "—";

        // Pasta do jogo: InstallDirectory é a única que é com certeza "a pasta
        // do jogo" (vem do scanner da loja). Pra jogo adicionado à mão, a pasta
        // do .exe pode ser só uma subpasta (bin\x64) — mostra, mas avisa.
        if (!string.IsNullOrWhiteSpace(Game.InstallDirectory))
        {
            GameFolderPath = Game.InstallDirectory;
            GameFolderText = Game.InstallDirectory!;
        }
        else
        {
            var exeDir = string.IsNullOrWhiteSpace(Game.WorkingDirectory)
                ? (Game.ExecutablePath is { } exe ? SafeDirectoryName(exe) : null)
                : Game.WorkingDirectory;
            GameFolderPath = exeDir;
            GameFolderText = exeDir is null ? "Desconhecida" : $"{exeDir}  (pasta do executável)";
        }

        var savePath = FindSteamCloudSaveFolder();
        if (savePath is not null)
        {
            SaveFolderText = savePath;
            SaveFolderHint = "Pasta de saves sincronizados pela Steam Cloud deste jogo.";
        }
        else
        {
            SaveFolderText = "Não identificada automaticamente";
            SaveFolderHint = "Cada jogo salva num lugar diferente — os mais comuns são Documentos, Documentos\\My Games e %AppData%. A Central de saves (sincronizar entre aparelhos) fica pra uma versão futura.";
        }
    }

    private async Task LoadFolderSizeAsync()
    {
        if (string.IsNullOrWhiteSpace(Game.InstallDirectory) || !Directory.Exists(Game.InstallDirectory))
        {
            SizeText = "Tamanho desconhecido";
            return;
        }

        try
        {
            var bytes = await Task.Run(() => DiskUsage.GetDirectorySizeBytes(Game.InstallDirectory!));
            SizeText = bytes > 0 ? DiskUsage.FormatSize(bytes) : "Tamanho desconhecido";
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[GameDetails] Falha ao medir pasta: {ex.Message}");
            SizeText = "Tamanho desconhecido";
        }
    }

    private string? FindSteamCloudSaveFolder()
    {
        if (Game.SteamAppId is not { } appId) return null;
        try
        {
            var root = SteamLibraryScanner.LocateSteamRoot();
            if (root is null) return null;
            var userdata = Path.Combine(root, "userdata");
            if (!Directory.Exists(userdata)) return null;
            foreach (var user in Directory.EnumerateDirectories(userdata))
            {
                var candidate = Path.Combine(user, appId.ToString(), "remote");
                if (Directory.Exists(candidate)) return candidate;
            }
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
        return null;
    }

    // ---- Carregamento ---------------------------------------------------------------

    private async Task LoadAsync()
    {
        var cache = GameDetailsCache.Load(Game.Id);
        if (cache is not null) ApplyCache(cache);

        var storeStale = cache is null || DateTimeOffset.UtcNow - cache.StoreFetchedUtc > GameDetailsCache.StoreDataMaxAge;
        var anyFresh = false;
        var anyFailed = false;

        try
        {
            DebugLog.Write($"[GameDetails] LoadAsync início: Title='{Game.Title}', cache={(cache is null ? "nenhum" : cache.StoreFetchedUtc.ToString("dd/MM"))}, lojaVencida={storeStale}");

            if (Game.SteamAppId.HasValue)
            {
                var appId = Game.SteamAppId.Value;
                var results = await Task.WhenAll(
                    LoadPlaytimeAsync(appId),
                    storeStale ? LoadSteamStoreDataAsync(appId) : Task.FromResult<bool?>(null),
                    LoadAchievementsAsync(appId));
                anyFresh = results.Any(r => r == true);
                anyFailed = results.Any(r => r == false);
            }
            else
            {
                PlaytimeText = "Jogo de outra plataforma — tempo medido pelo launcher.";
                AchievementsStatusText = "Conquistas disponíveis só para jogos da Steam por enquanto.";
                MediaStatusText = "Capturas e trailer disponíveis só para jogos da Steam por enquanto.";
                if (storeStale)
                {
                    var ok = await LoadRawgDataAsync(Game.Title);
                    anyFresh = ok == true;
                    anyFailed = ok == false;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[GameDetails] EXCEPTION em LoadAsync: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            anyFailed = true;
        }
        finally
        {
            IsLoading = false;
        }

        if (anyFresh) SaveCache();

        DataSourceNote = (anyFailed, cache) switch
        {
            (true, not null) => $"Sem conexão com a loja — mostrando dados salvos em {cache.StoreFetchedUtc.ToLocalTime():dd/MM}.",
            (true, null) => "Não foi possível baixar as informações agora (sem internet?).",
            (false, not null) when !anyFresh => $"Dados salvos em {cache.StoreFetchedUtc.ToLocalTime():dd/MM} (atualiza a cada 7 dias).",
            _ => "",
        };

        if (Screenshots.Count == 0 && Game.SteamAppId.HasValue && MediaStatusText == "Carregando mídia...")
            MediaStatusText = "Sem capturas de tela na loja para este jogo.";

        DebugLog.Write($"[GameDetails] LoadAsync fim: Description.Len={Description.Length}, Tags={Tags.Count}, Shots={Screenshots.Count}, Conquistas={Achievements.Count}, Playtime='{PlaytimeText}'");
    }

    private void ApplyCache(GameDetailsCacheData cache)
    {
        if (!string.IsNullOrWhiteSpace(cache.Description)) Description = cache.Description!;
        if (!string.IsNullOrWhiteSpace(cache.LongDescription)) LongDescription = cache.LongDescription!;
        SetTags(cache.Tags);

        Screenshots.Clear();
        foreach (var p in cache.ScreenshotPaths.Where(File.Exists)) Screenshots.Add(new DetailsImageItem { Path = p });
        if (Screenshots.Count > 0) MediaStatusText = "";
        HasTrailer = cache.HasTrailer;

        if (cache.SteamPlaytimeMinutes is { } steamMinutes) ApplySteamPlaytime(steamMinutes);

        if (cache.Achievements.Count > 0)
        {
            SetAchievements(cache.Achievements.Select(a => new AchievementItemViewModel
            {
                ApiName = a.ApiName,
                DisplayName = a.DisplayName,
                Description = a.Description,
                Achieved = a.Achieved,
                IconLocalPath = a.IconLocalPath is { } ip && File.Exists(ip) ? ip : null,
                UnlockedAtUtc = a.UnlockedAtUtc,
                GlobalPercent = a.GlobalPercent,
            }).ToList());
            AchievementProgress = cache.AchievementProgress;
        }
        if (!string.IsNullOrWhiteSpace(cache.AchievementsStatus)) AchievementsStatusText = cache.AchievementsStatus!;
    }

    private DateTimeOffset _storeFetchedUtc;
    private long? _steamPlaytimeMinutes;
    private DateTimeOffset? _achievementsFetchedUtc;

    private void SaveCache()
    {
        var previous = GameDetailsCache.Load(Game.Id);
        var data = new GameDetailsCacheData
        {
            StoreFetchedUtc = _storeFetchedUtc != default ? _storeFetchedUtc : previous?.StoreFetchedUtc ?? default,
            Description = Description,
            LongDescription = LongDescription,
            Tags = Tags.Select(t => t.Text).ToList(),
            ScreenshotPaths = Screenshots.Select(s => s.Path).ToList(),
            HasTrailer = HasTrailer,
            SteamPlaytimeMinutes = _steamPlaytimeMinutes ?? previous?.SteamPlaytimeMinutes,
            AchievementsFetchedUtc = _achievementsFetchedUtc ?? previous?.AchievementsFetchedUtc,
            AchievementsStatus = AchievementsStatusText,
            AchievementProgress = AchievementProgress,
            Achievements = Achievements.Select(a => new CachedAchievement
            {
                ApiName = a.ApiName,
                DisplayName = a.DisplayName,
                Description = a.Description,
                Achieved = a.Achieved,
                IconLocalPath = a.IconLocalPath,
                UnlockedAtUtc = a.UnlockedAtUtc,
                GlobalPercent = a.GlobalPercent,
            }).ToList(),
        };
        GameDetailsCache.Save(Game.Id, data);
    }

    /// <returns>true = baixou; false = falhou; null = não precisou.</returns>
    private async Task<bool?> LoadSteamStoreDataAsync(uint appId)
    {
        try
        {
            var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&l=portuguese";
            var response = await StoreHttp.GetStringAsync(url);

            using var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty(appId.ToString(), out var entry)) return false;
            if (!entry.TryGetProperty("success", out var success) || !success.GetBoolean())
            {
                // Jogo fora da loja (removido/região): não é falha de rede.
                MediaStatusText = "Este jogo não tem página ativa na loja da Steam.";
                _storeFetchedUtc = DateTimeOffset.UtcNow;
                return true;
            }
            if (!entry.TryGetProperty("data", out var data)) return false;

            string? desc = null;
            if (data.TryGetProperty("short_description", out var shortDesc))
                desc = System.Net.WebUtility.HtmlDecode(shortDesc.GetString() ?? "");
            if (data.TryGetProperty("about_the_game", out var about))
            {
                var longText = CleanHtml(about.GetString() ?? string.Empty);
                if (longText.Length > 2000) longText = longText[..2000].TrimEnd() + "…";
                if (!string.IsNullOrWhiteSpace(longText) && !string.Equals(longText.Trim(), desc?.Trim(), StringComparison.Ordinal))
                    LongDescription = longText;
            }
            if (string.IsNullOrWhiteSpace(desc) && data.TryGetProperty("detailed_description", out var detailed))
                desc = CleanHtml(detailed.GetString() ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(desc)) Description = desc.Trim();

            var metaParts = new List<string>();
            var tags = new List<string>();

            if (data.TryGetProperty("developers", out var devs) && devs.ValueKind == JsonValueKind.Array && devs.GetArrayLength() > 0)
            {
                var list = devs.EnumerateArray().Select(d => d.GetString() ?? "").Where(s => s.Length > 0).ToList();
                if (list.Count > 0) { metaParts.Add($"Desenvolvedora: {string.Join(", ", list)}"); tags.AddRange(list); }
            }

            if (data.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array && genres.GetArrayLength() > 0)
            {
                var list = genres.EnumerateArray()
                    .Select(g => g.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "")
                    .Where(s => s.Length > 0).ToList();
                if (list.Count > 0) { metaParts.Add($"Gêneros: {string.Join(", ", list)}"); tags.AddRange(list); }
            }

            if (data.TryGetProperty("release_date", out var rel) && rel.TryGetProperty("date", out var date))
            {
                var dateStr = date.GetString();
                if (!string.IsNullOrWhiteSpace(dateStr)) { metaParts.Add($"Lançamento: {dateStr}"); tags.Add($"Lançado em {dateStr}"); }
            }

            if (metaParts.Count > 0) MetadataText = string.Join("  ·  ", metaParts);
            SetTags(tags);

            HasTrailer = data.TryGetProperty("movies", out var movies) && movies.ValueKind == JsonValueKind.Array && movies.GetArrayLength() > 0;

            if (data.TryGetProperty("screenshots", out var shots) && shots.ValueKind == JsonValueKind.Array)
            {
                var urls = shots.EnumerateArray()
                    .Select(s => s.TryGetProperty("path_thumbnail", out var t) ? t.GetString() : null)
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Take(MaxScreenshots)
                    .ToList();
                var paths = new List<string>();
                for (var i = 0; i < urls.Count; i++)
                {
                    var local = await DownloadScreenshotAsync(appId, i, urls[i]!);
                    if (local is not null) paths.Add(local);
                }
                Screenshots.Clear();
                foreach (var p in paths) Screenshots.Add(new DetailsImageItem { Path = p });
                MediaStatusText = paths.Count > 0 ? "" : "Sem capturas de tela na loja para este jogo.";
            }

            _storeFetchedUtc = DateTimeOffset.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[GameDetails] EXCEPTION em LoadSteamStoreDataAsync: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private async Task<bool?> LoadAchievementsAsync(uint appId)
    {
        if (_achievements is null || !_achievements.HasCredentials)
        {
            if (Achievements.Count == 0) AchievementsStatusText = "Configure a Steam API Key e o SteamID64 em Configurações → Contas para ver as conquistas.";
            return null;
        }

        try
        {
            var result = await _achievements.GetAchievementsAsync(appId);

            if (result is null || result.Achievements is null || result.Achievements.Count == 0)
            {
                AchievementsStatusText = "Este jogo não tem conquistas.";
                AchievementProgress = 0;
                SetAchievements(new List<AchievementItemViewModel>());
                _achievementsFetchedUtc = DateTimeOffset.UtcNow;
                return true;
            }

            var percents = await LoadGlobalPercentagesAsync(appId);

            var totalCount = result.Achievements.Count;
            var unlockedCount = result.Achievements.Count(a => a.Achieved);

            AchievementsStatusText = result.PlayerDataUnavailable
                ? $"{totalCount} conquistas — seu progresso não está visível (perfil ou \"Detalhes do jogo\" privados na Steam)."
                : $"{unlockedCount} de {totalCount} conquistadas";
            AchievementProgress = totalCount > 0 ? (double)unlockedCount / totalCount * 100 : 0;

            var items = new List<AchievementItemViewModel>();
            foreach (var ach in result.Achievements)
            {
                var iconUrl = ach.Achieved ? ach.IconUrl : ach.IconGrayUrl;
                var localPath = await DownloadIconAsync(appId, ach.ApiName, ach.Achieved, iconUrl ?? "");
                items.Add(new AchievementItemViewModel
                {
                    ApiName = ach.ApiName,
                    DisplayName = ach.DisplayName ?? ach.ApiName,
                    Description = ach.Description,
                    Achieved = ach.Achieved,
                    IconLocalPath = localPath,
                    UnlockedAtUtc = ach.UnlockedAtUtc,
                    GlobalPercent = percents.TryGetValue(ach.ApiName, out var p) ? p : null,
                });
            }
            SetAchievements(items);
            _achievementsFetchedUtc = DateTimeOffset.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[GameDetails] EXCEPTION em LoadAchievementsAsync: {ex.GetType().Name}: {ex.Message}");
            if (Achievements.Count == 0) AchievementsStatusText = "Erro ao carregar conquistas.";
            return false;
        }
    }

    /// <summary>
    /// % global de cada conquista (endpoint público da Steam, sem chave).
    /// Usado só pra ordenar/destacar "as mais próximas" — falhar aqui não
    /// afeta mais nada.
    /// </summary>
    private static async Task<Dictionary<string, double>> LoadGlobalPercentagesAsync(uint appId)
    {
        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        try
        {
            var url = $"https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v0002/?gameid={appId}&format=json";
            var response = await StoreHttp.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            if (!doc.RootElement.TryGetProperty("achievementpercentages", out var root)) return map;
            if (!root.TryGetProperty("achievements", out var list) || list.ValueKind != JsonValueKind.Array) return map;
            foreach (var a in list.EnumerateArray())
            {
                if (!a.TryGetProperty("name", out var n) || n.GetString() is not { } name) continue;
                if (!a.TryGetProperty("percent", out var pct)) continue;
                double value;
                if (pct.ValueKind == JsonValueKind.Number) value = pct.GetDouble();
                else if (pct.ValueKind == JsonValueKind.String && double.TryParse(pct.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) value = parsed;
                else continue;
                map[name] = value;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[GameDetails] % global de conquistas indisponível: {ex.Message}");
        }
        return map;
    }

    private void SetAchievements(List<AchievementItemViewModel> items)
    {
        // Desbloqueadas primeiro (mais recentes no topo); depois as bloqueadas,
        // das que mais gente tem pras mais raras.
        var ordered = items
            .Where(a => a.Achieved).OrderByDescending(a => a.UnlockedAtUtc ?? DateTimeOffset.MinValue)
            .Concat(items.Where(a => !a.Achieved).OrderByDescending(a => a.GlobalPercent ?? -1))
            .ToList();

        Achievements.Clear();
        foreach (var a in ordered) Achievements.Add(a);

        NextAchievements.Clear();
        foreach (var a in items.Where(a => !a.Achieved && a.GlobalPercent is not null).OrderByDescending(a => a.GlobalPercent).Take(3))
            NextAchievements.Add(a);
        HasNextAchievements = NextAchievements.Count > 0;
    }

    private void SetTags(IEnumerable<string> tags)
    {
        var distinct = tags.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
        if (distinct.Count == 0) return;
        Tags.Clear();
        foreach (var t in distinct) Tags.Add(new DetailsTagItem { Text = t });
    }

    private static string CleanHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        var withBreaks = System.Text.RegularExpressions.Regex.Replace(html, @"<\s*(br|/p|/li|/h\d)\s*/?\s*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var noTags = System.Text.RegularExpressions.Regex.Replace(withBreaks, @"<[^>]+>", " ");
        var decoded = System.Net.WebUtility.HtmlDecode(noTags);
        var lines = decoded.Split('\n').Select(l => System.Text.RegularExpressions.Regex.Replace(l, @"\s+", " ").Trim()).Where(l => l.Length > 0);
        return string.Join("\n", lines);
    }

    private async Task<bool?> LoadRawgDataAsync(string title)
    {
        try
        {
            var rawg = new HandheldLauncher.App.Platform.RawgMetadataService();
            var data = await rawg.GetGameDetailsAsync(title);

            if (data is null)
            {
                if (string.IsNullOrWhiteSpace(Description))
                    Description = "Informações não encontradas. Para jogos fora da Steam, a descrição vem da RAWG — configure a chave em Configurações → Contas.";
                return null;
            }

            var metaParts = new List<string>();
            var tags = new List<string>();
            if (data.Developers.Count > 0) { metaParts.Add($"Desenvolvedora: {string.Join(", ", data.Developers)}"); tags.AddRange(data.Developers); }
            if (data.Genres.Count > 0) { metaParts.Add($"Gêneros: {string.Join(", ", data.Genres)}"); tags.AddRange(data.Genres); }
            if (!string.IsNullOrEmpty(data.ReleaseDate)) { metaParts.Add($"Lançamento: {data.ReleaseDate}"); tags.Add($"Lançado em {data.ReleaseDate}"); }

            MetadataText = string.Join("  ·  ", metaParts);
            SetTags(tags);
            Description = string.IsNullOrWhiteSpace(data.Description)
                ? "Descrição não disponível na RAWG para este jogo."
                : data.Description;
            _storeFetchedUtc = DateTimeOffset.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[GameDetails] EXCEPTION em LoadRawgDataAsync: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            if (string.IsNullOrWhiteSpace(Description)) Description = "Não foi possível consultar a RAWG agora.";
            return false;
        }
    }

    private async Task<bool?> LoadPlaytimeAsync(uint appId)
    {
        if (_ownedGames is null) { PlaytimeText = "Configure sua Steam API Key em Configurações para ver as horas da Steam."; return null; }
        try
        {
            var owned = await _ownedGames.GetOwnedGamesAsync();
            var match = owned.FirstOrDefault(g => g.AppId == appId);
            if (match is null) { PlaytimeText = "Não encontrado na sua biblioteca da Steam."; return true; }
            _steamPlaytimeMinutes = match.PlaytimeMinutes;
            ApplySteamPlaytime(match.PlaytimeMinutes);
            return true;
        }
        catch
        {
            if (_steamPlaytimeMinutes is null && PlaytimeText.StartsWith("Carregando", StringComparison.Ordinal))
                PlaytimeText = "Não foi possível consultar as horas da Steam agora.";
            return false;
        }
    }

    private void ApplySteamPlaytime(long steamMinutes)
    {
        PlaytimeText = FormatPlaytime(steamMinutes);
        // A Steam já conta as sessões abertas pelo launcher; o maior dos dois
        // é o "total" mais honesto.
        var total = Math.Max(steamMinutes, Game.PlaytimeMinutes);
        TotalPlaytimeText = total > 0 ? FormatDuration(total) : "—";
    }

    private static string FormatPlaytime(long minutes)
    {
        if (minutes <= 0) return "Ainda não jogou pela Steam.";
        return $"{FormatDuration(minutes)} na Steam";
    }

    private static string FormatDuration(long minutes)
    {
        var hours = minutes / 60;
        var mins = minutes % 60;
        return hours > 0 ? $"{hours}h{mins:00}min" : $"{mins} min";
    }

    private static string FormatRelativeDate(DateTimeOffset utc)
    {
        var local = utc.ToLocalTime();
        var days = (int)(DateTime.Today - local.Date).TotalDays;
        return days switch
        {
            <= 0 => "Hoje",
            1 => "Ontem",
            < 30 => $"Há {days} dias",
            _ => local.ToString("dd/MM/yyyy"),
        };
    }

    private static string? SafeDirectoryName(string path)
    {
        try { return Path.GetDirectoryName(path); } catch { return null; }
    }

    private static async Task<string?> DownloadIconAsync(uint appId, string apiName, bool achieved, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            Directory.CreateDirectory(IconCacheDir);
            var safeName = new string(apiName.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').ToArray());
            var suffix = achieved ? "on" : "off";
            var localPath = Path.Combine(IconCacheDir, $"{appId}_{safeName}_{suffix}.img");

            if (File.Exists(localPath)) return localPath;

            var bytes = await IconHttp.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(localPath, bytes);
            return localPath;
        }
        catch { return null; }
    }

    private static async Task<string?> DownloadScreenshotAsync(uint appId, int index, string url)
    {
        try
        {
            Directory.CreateDirectory(GameDetailsCache.MediaDir);
            var localPath = Path.Combine(GameDetailsCache.MediaDir, $"{appId}_{index}.jpg");
            var bytes = await IconHttp.GetByteArrayAsync(url);
            var tmp = localPath + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes);
            File.Move(tmp, localPath, overwrite: true);
            return localPath;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[GameDetails] Captura {index} não baixou: {ex.Message}");
            var existing = Path.Combine(GameDetailsCache.MediaDir, $"{appId}_{index}.jpg");
            return File.Exists(existing) ? existing : null;
        }
    }
}
