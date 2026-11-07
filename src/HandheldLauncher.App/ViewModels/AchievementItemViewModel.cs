using System;

namespace HandheldLauncher.App.ViewModels;

public sealed class AchievementItemViewModel
{
    public string ApiName { get; init; } = "";
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public bool Achieved { get; init; }
    public string? IconLocalPath { get; init; }
    public DateTimeOffset? UnlockedAtUtc { get; init; }
    public double? GlobalPercent { get; init; }

    public string StatusText => Achieved
        ? (UnlockedAtUtc is { } t ? $"Desbloqueada em {t.ToLocalTime():dd/MM/yyyy}" : "Desbloqueada")
        : GlobalPercent is { } p ? $"Bloqueada · {p:0.#}% dos jogadores têm" : "Bloqueada";

    public string DescriptionText => string.IsNullOrWhiteSpace(Description) ? "Conquista secreta ou sem descrição." : Description!;
    public double RowOpacity => Achieved ? 1.0 : 0.72;
}