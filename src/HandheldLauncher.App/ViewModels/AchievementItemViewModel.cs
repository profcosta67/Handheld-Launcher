namespace HandheldLauncher.App.ViewModels;

/// <summary>
/// Uma linha da lista de conquistas em GameDetailsWindow — já pronta pra
/// bind direto (ícone já baixado/em cache local, texto já formatado). Ver
/// GameDetailsViewModel.LoadAchievementsAsync, que monta esta lista a
/// partir de SteamAchievementsResult (HandheldLauncher.Core).
/// </summary>
public sealed class AchievementItemViewModel
{
    /// <summary>v0.18: identificador da Steam, usado pra casar a % global e o cache em disco.</summary>
    public string ApiName { get; init; } = "";

    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public bool Achieved { get; init; }

    /// <summary>Caminho local (já em cache em disco) do ícone — desbloqueado se Achieved, cinza/bloqueado senão. Null se não deu pra baixar (ver GameDetailsViewModel.DownloadIconAsync) — a tela cai pro título/descrição sem ícone, nunca escondendo a linha inteira.</summary>
    public string? IconLocalPath { get; init; }

    public DateTimeOffset? UnlockedAtUtc { get; init; }

    /// <summary>v0.18: % dos jogadores da Steam que têm essa conquista (null = não deu pra consultar).</summary>
    public double? GlobalPercent { get; init; }

    /// <summary>"Desbloqueada em 29/10/2026" / "Desbloqueada" (sem data) / "Bloqueada".</summary>
    public string StatusText => Achieved
        ? (UnlockedAtUtc is { } t ? $"Desbloqueada em {t.ToLocalTime():dd/MM/yyyy}" : "Desbloqueada")
        : GlobalPercent is { } p ? $"Bloqueada · {p:0.#}% dos jogadores têm" : "Bloqueada";

    public string DescriptionText => string.IsNullOrWhiteSpace(Description) ? "Conquista secreta ou sem descrição." : Description!;

    public double RowOpacity => Achieved ? 1.0 : 0.72;
}
