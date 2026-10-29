using System.Collections.ObjectModel;
using System.Net.Http;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.ViewModels;

/// <summary>
/// ViewModel de GameDetailsWindow — v0.9.11, etapa 2 ("tela de informações
/// do jogo", pedido do usuário: "ideias para tema original de informações
/// de jogos igual tem no decky loader, classificação, resumo etc... puxar
/// horas jogadas da Steam, medalhas e conquistas"). Escopo desta leva:
/// horas jogadas (reais, da conta Steam) + conquistas. Classificação/
/// resumo ficaram pra uma fase 2 (ver comentário em
/// SteamAchievementsClient/README — a API não-oficial de detalhes de loja
/// não é documentada/estável o suficiente ainda).
///
/// Um jogo por instância — criada do zero a cada "Detalhes do jogo" (ver
/// MainViewModel.CreateGameDetailsViewModel), nunca reaproveitada.
/// </summary>
public sealed class GameDetailsViewModel : ViewModelBase
{
    private static readonly HttpClient IconHttp = new();

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

        _ = LoadAsync();
    }

    public GameEntry Game { get; }

    public string Title => Game.Title;

    /// <summary>Capa vertical (mesma arte da tile) — bind com FileToBitmapConverter igual MainWindow já faz.</summary>
    public string? CoverImagePath => Game.GridImagePath;

    private bool _isLoading = true;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    private string _playtimeText = "Carregando horas jogadas...";

    public string PlaytimeText
    {
        get => _playtimeText;
        private set => SetField(ref _playtimeText, value);
    }

    private string _achievementsStatusText = "Carregando conquistas...";

    public string AchievementsStatusText
    {
        get => _achievementsStatusText;
        private set => SetField(ref _achievementsStatusText, value);
    }

    public ObservableCollection<AchievementItemViewModel> Achievements { get; }

    private async Task LoadAsync()
    {
        try
        {
            // Garantido pelo gate em MainViewModel.HasGameDetails (só chega
            // aqui jogo Source==Steam com SteamAppId preenchido).
            var appId = Game.SteamAppId!.Value;

            await Task.WhenAll(
                LoadPlaytimeAsync(appId),
                LoadAchievementsAsync(appId));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadPlaytimeAsync(uint appId)
    {
        if (_ownedGames is null)
        {
            PlaytimeText = "Configure sua Steam Web API key e SteamID64 em Configurações pra ver horas jogadas (ver README).";
            return;
        }

        try
        {
            var owned = await _ownedGames.GetOwnedGamesAsync();
            var match = owned.FirstOrDefault(g => g.AppId == appId);
            PlaytimeText = match is null
                ? "Não encontrado na sua biblioteca Steam (conta configurada é outra?)."
                : FormatPlaytime(match.PlaytimeMinutes);
        }
        catch (Exception)
        {
            // Melhor esforço — perfil privado, rede fora do ar, etc.
            PlaytimeText = "Não foi possível consultar as horas jogadas na Steam agora.";
        }
    }

    private static string FormatPlaytime(long minutes)
    {
        if (minutes <= 0) return "Ainda não jogou pela Steam.";
        var hours = minutes / 60;
        var mins = minutes % 60;
        return hours > 0 ? $"{hours}h{mins:00}min jogadas (Steam)" : $"{mins} min jogados (Steam)";
    }

    private async Task LoadAchievementsAsync(uint appId)
    {
        if (_achievements is null)
        {
            AchievementsStatusText = "Configure sua Steam Web API key e SteamID64 em Configurações pra ver conquistas (ver README).";
            return;
        }

        try
        {
            var result = await _achievements.GetAchievementsAsync(appId);

            if (!result.HasAchievements)
            {
                AchievementsStatusText = "Este jogo não tem conquistas na Steam.";
                return;
            }

            AchievementsStatusText = result.PlayerDataUnavailable
                ? "Não foi possível ver quais você já desbloqueou (perfil Steam privado?) — mostrando a lista completa."
                : $"{result.Achievements.Count(a => a.Achieved)}/{result.Achievements.Count} desbloqueadas";

            // Desbloqueadas primeiro, depois por nome — mesmo critério
            // visual do Decky Loader que inspirou o pedido.
            var ordered = result.Achievements
                .OrderByDescending(a => a.Achieved)
                .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase);

            foreach (var achievement in ordered)
            {
                var iconUrl = achievement.Achieved
                    ? (achievement.IconUrl ?? achievement.IconGrayUrl)
                    : (achievement.IconGrayUrl ?? achievement.IconUrl);

                var localIconPath = iconUrl is null
                    ? null
                    : await DownloadIconAsync(appId, achievement.ApiName, achievement.Achieved, iconUrl);

                Achievements.Add(new AchievementItemViewModel
                {
                    DisplayName = achievement.DisplayName,
                    Description = achievement.Description,
                    Achieved = achievement.Achieved,
                    IconLocalPath = localIconPath,
                    UnlockedAtUtc = achievement.UnlockedAtUtc,
                });
            }
        }
        catch (Exception)
        {
            AchievementsStatusText = "Não foi possível consultar as conquistas na Steam agora.";
        }
    }

    /// <summary>
    /// Cache em disco por appid+apiname+estado (desbloqueado/bloqueado usam
    /// ícones DIFERENTES na Steam) — mesma ideia de ArtworkService, só que
    /// standalone aqui (não vale a pena compartilhar: tamanho/formato bem
    /// diferentes de capa/banner/logo, e só esta tela usa). Melhor
    /// esforço: qualquer falha de rede devolve null (a linha aparece sem
    /// ícone, nunca quebra a tela inteira).
    /// </summary>
    private static async Task<string?> DownloadIconAsync(uint appId, string apiName, bool achieved, string url)
    {
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
        catch (Exception)
        {
            return null;
        }
    }
}
