namespace HandheldLauncher.App.ViewModels;

/// <summary>
/// Uma linha da lista de conquistas em GameDetailsWindow — já pronta pra
/// bind direto (ícone já baixado/em cache local, texto já formatado). Ver
/// GameDetailsViewModel.LoadAchievementsAsync, que monta esta lista a
/// partir de SteamAchievementsResult (HandheldLauncher.Core).
/// </summary>
public sealed class AchievementItemViewModel
{
    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public bool Achieved { get; init; }

    /// <summary>Caminho local (já em cache em disco) do ícone — desbloqueado se Achieved, cinza/bloqueado senão. Null se não deu pra baixar (ver GameDetailsViewModel.DownloadIconAsync) — a tela cai pro título/descrição sem ícone, nunca escondendo a linha inteira.</summary>
    public string? IconLocalPath { get; init; }

    public DateTimeOffset? UnlockedAtUtc { get; init; }

    /// <summary>"Desbloqueada em 29/10/2026" / "Desbloqueada" (sem data) / "Bloqueada".</summary>
    public string StatusText => Achieved
        ? (UnlockedAtUtc is { } t ? $"Desbloqueada em {t.ToLocalTime():dd/MM/yyyy}" : "Desbloqueada")
        : "Bloqueada";
}
