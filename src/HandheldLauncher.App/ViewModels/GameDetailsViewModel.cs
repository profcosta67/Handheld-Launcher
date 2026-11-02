using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.ViewModels;

public sealed class GameDetailsViewModel : ViewModelBase
{
    private static readonly HttpClient IconHttp = new();

    private static readonly HttpClient StoreHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    static GameDetailsViewModel()
    {
        try
        {
            StoreHttp.DefaultRequestHeaders.UserAgent.ParseAdd("HandheldLauncher/0.14 (Windows; C#)");
            StoreHttp.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }
        catch { /* melhor esforço */ }
    }

    private static readonly string IconCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HandheldLauncher", "artwork", "achievements");

    private readonly ISteamOwnedGamesClient? _ownedGames;
    private readonly ISteamAchievementsClient? _achievements;

    public GameDetailsViewModel(GameEntry game, ISteamOwnedGamesClient? ownedGames, ISteamAchievementsClient? achievements)
    {
        Game = game;
        _ownedGames = ownedGames;
        _achievements = achievements;
        Achievements = new ObservableCollection<AchievementItemViewModel>();

        DebugLog.Write($"[GameDetails] CTOR: Title='{game.Title}', SteamAppId={game.SteamAppId?.ToString() ?? "null"}, Source={game.Source}");
        _ = LoadAsync();
    }

    public GameEntry Game { get; }
    public string Title => Game.Title;
    public string? CoverImagePath => Game.GridImagePath;
    public string? BannerImagePath => Game.HeroImagePath ?? Game.WideGridImagePath;
    public string? LogoImagePath => Game.LogoImagePath;

    private bool _isLoading = true;
    public bool IsLoading { get => _isLoading; private set => SetField(ref _isLoading, value); }

    private string _playtimeText = "Carregando horas jogadas...";
    public string PlaytimeText { get => _playtimeText; private set => SetField(ref _playtimeText, value); }

    private string _description = "";
    public string Description { get => _description; private set => SetField(ref _description, value); }

    private string _metadataText = "";
    public string MetadataText { get => _metadataText; private set => SetField(ref _metadataText, value); }

    private string _achievementsStatusText = "Carregando conquistas...";
    public string AchievementsStatusText { get => _achievementsStatusText; private set => SetField(ref _achievementsStatusText, value); }

    private double _achievementProgress = 0;
    public double AchievementProgress { get => _achievementProgress; private set => SetField(ref _achievementProgress, value); }

    public ObservableCollection<AchievementItemViewModel> Achievements { get; }

    private async Task LoadAsync()
    {
        try
        {
            DebugLog.Write($"[GameDetails] LoadAsync início: Title='{Game.Title}', SteamAppId={(Game.SteamAppId?.ToString() ?? "null")}");

            if (Game.SteamAppId.HasValue)
            {
                var appId = Game.SteamAppId.Value;

                // Playtime, Store Data e Conquistas rodam em paralelo.
                await Task.WhenAll(
                    LoadPlaytimeAsync(appId),
                    LoadSteamStoreDataAsync(appId),
                    LoadAchievementsAsync(appId)
                );
            }
            else
            {
                DebugLog.Write($"[GameDetails] Jogo NÃO-Steam — chamando LoadRawgDataAsync('{Game.Title}')");
                await LoadRawgDataAsync(Game.Title);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[GameDetails] EXCEPTION em LoadAsync: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            IsLoading = false;
            DebugLog.Write($"[GameDetails] LoadAsync fim: Description.Len={Description.Length}, Metadata.Len={MetadataText.Length}, Playtime='{PlaytimeText}'");
        }
    }

    private async Task LoadSteamStoreDataAsync(uint appId)
    {
        try
        {
            var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&l=portuguese";
            var response = await StoreHttp.GetStringAsync(url);

            using var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty(appId.ToString(), out var entry)) return;
            if (!entry.TryGetProperty("success", out var success) || !success.GetBoolean()) return;
            if (!entry.TryGetProperty("data", out var data)) return;

            string? desc = null;
            if (data.TryGetProperty("short_description", out var shortDesc))
                desc = shortDesc.GetString();
            if (string.IsNullOrWhiteSpace(desc) && data.TryGetProperty("detailed_description", out var detailed))
                desc = CleanHtml(detailed.GetString() ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(desc)) Description = desc.Trim();

            var metaParts = new List<string>();

            if (data.TryGetProperty("developers", out var devs) && devs.GetArrayLength() > 0)
            {
                var list = devs.EnumerateArray().Select(d => d.GetString() ?? "").Where(s => s.Length > 0);
                if (list.Any()) metaParts.Add($"Desenvolvedora: {string.Join(", ", list)}");
            }

            if (data.TryGetProperty("genres", out var genres) && genres.GetArrayLength() > 0)
            {
                var list = genres.EnumerateArray()
                    .Select(g => g.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "")
                    .Where(s => s.Length > 0);
                if (list.Any()) metaParts.Add($"Gêneros: {string.Join(", ", list)}");
            }

            if (data.TryGetProperty("release_date", out var rel) && rel.TryGetProperty("date", out var date))
            {
                var dateStr = date.GetString();
                if (!string.IsNullOrWhiteSpace(dateStr)) metaParts.Add($"Lançamento: {dateStr}");
            }

            if (metaParts.Count > 0) MetadataText = string.Join("  ·  ", metaParts);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[GameDetails] EXCEPTION em LoadSteamStoreDataAsync: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task LoadAchievementsAsync(uint appId)
    {
        if (_achievements is null || !_achievements.HasCredentials)
        {
            AchievementsStatusText = "Configure a Steam API Key e o SteamID64 em Configurações.";
            return;
        }

        try
        {
            var result = await _achievements.GetAchievementsAsync(appId);

            if (result is null || result.Achievements is null || result.Achievements.Count == 0)
            {
                AchievementsStatusText = "Este jogo não tem conquistas.";
                AchievementProgress = 0;
                return;
            }

            var totalCount = result.Achievements.Count;
            var unlockedCount = result.Achievements.Count(a => a.Achieved);

            AchievementsStatusText = $"{unlockedCount} de {totalCount} conquistadas";
            AchievementProgress = totalCount > 0 ? (double)unlockedCount / totalCount * 100 : 0;

            foreach (var ach in result.Achievements)
            {
                var iconUrl = ach.Achieved ? ach.IconUrl : ach.IconGrayUrl;
                var localPath = await DownloadIconAsync(appId, ach.ApiName, ach.Achieved, iconUrl ?? "");

                Achievements.Add(new AchievementItemViewModel
                {
                    DisplayName = ach.DisplayName ?? ach.ApiName, 
                    Description = ach.Description,
                    Achieved = ach.Achieved,
                    IconLocalPath = localPath,
                    UnlockedAtUtc = ach.UnlockedAtUtc
                });
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[GameDetails] EXCEPTION em LoadAchievementsAsync: {ex.GetType().Name}: {ex.Message}");
            AchievementsStatusText = "Erro ao carregar conquistas.";
        }
    }

    private static string CleanHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        var noTags = System.Text.RegularExpressions.Regex.Replace(html, @"<[^>]+>", " ");
        return System.Net.WebUtility.HtmlDecode(noTags).Replace("  ", " ").Trim();
    }

    private async Task LoadRawgDataAsync(string title)
    {
        try
        {
            var rawg = new HandheldLauncher.App.Platform.RawgMetadataService();
            var data = await rawg.GetGameDetailsAsync(title);

            if (data is null)
            {
                Description = "Informações não encontradas na RAWG. Configure a API Key nas Configurações.";
                PlaytimeText = "Este não é um jogo da Steam.";
                AchievementsStatusText = "Conquistas disponíveis apenas para jogos da Steam.";
                return;
            }

            PlaytimeText = "Jogo de outra plataforma (Xbox, Emulador, etc.)";

            var metaParts = new List<string>();
            if (data.Developers.Count > 0)
                metaParts.Add($"Desenvolvedora: {string.Join(", ", data.Developers)}");
            if (data.Genres.Count > 0)
                metaParts.Add($"Gêneros: {string.Join(", ", data.Genres)}");
            if (!string.IsNullOrEmpty(data.ReleaseDate))
                metaParts.Add($"Lançamento: {data.ReleaseDate}");

            MetadataText = string.Join("  ·  ", metaParts);
            Description = string.IsNullOrWhiteSpace(data.Description)
                ? "Descrição não disponível na RAWG para este jogo."
                : data.Description;

            AchievementsStatusText = "Conquistas disponíveis apenas para jogos da Steam.";
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[GameDetails] EXCEPTION em LoadRawgDataAsync: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            Description = $"Erro ao consultar RAWG: {ex.Message}";
        }
    }

    private async Task LoadPlaytimeAsync(uint appId)
    {
        if (_ownedGames is null) { PlaytimeText = "Configure sua Steam API Key em Configurações."; return; }
        try
        {
            var owned = await _ownedGames.GetOwnedGamesAsync();
            var match = owned.FirstOrDefault(g => g.AppId == appId);
            PlaytimeText = match is null ? "Não encontrado na sua biblioteca." : FormatPlaytime(match.PlaytimeMinutes);
        }
        catch { PlaytimeText = "Não foi possível consultar as horas agora."; }
    }

    private static string FormatPlaytime(long minutes)
    {
        if (minutes <= 0) return "Ainda não jogou pela Steam.";
        var hours = minutes / 60;
        var mins = minutes % 60;
        return hours > 0 ? $"{hours}h{mins:00}min jogadas" : $"{mins} min jogados";
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
}