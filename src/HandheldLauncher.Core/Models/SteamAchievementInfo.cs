namespace HandheldLauncher.Core.Models;

/// <summary>
/// Uma conquista (achievement) de um jogo Steam, já cruzando duas chamadas
/// da Steam Web API — ver ISteamAchievementsClient. Nome/descrição/ícones
/// vêm de ISteamUserStats/GetSchemaForGame (metadados do JOGO, iguais pra
/// todo mundo); Achieved/UnlockedAtUtc vêm de
/// ISteamUserStats/GetPlayerAchievements (progresso desta CONTA).
/// </summary>
public sealed class SteamAchievementInfo
{
    /// <summary>Identificador interno da Steam (ex: "ACH_WIN_ONE_GAME") — não é exibido, só usado pra casar as duas chamadas.</summary>
    public required string ApiName { get; set; }

    public required string DisplayName { get; set; }

    public string? Description { get; set; }

    /// <summary>URL do ícone "desbloqueado" (colorido).</summary>
    public string? IconUrl { get; set; }

    /// <summary>URL do ícone "bloqueado" (cinza) — mostrado enquanto <see cref="Achieved"/> for false.</summary>
    public string? IconGrayUrl { get; set; }

    public bool Achieved { get; set; }

    /// <summary>Null quando ainda não desbloqueada, ou quando a conta/perfil não permitiu checar (ver SteamAchievementsResult.PlayerDataUnavailable).</summary>
    public DateTimeOffset? UnlockedAtUtc { get; set; }
}

/// <summary>
/// Resultado completo de ISteamAchievementsClient.GetAchievementsAsync pra
/// UM jogo: a lista de conquistas (pode vir vazia — ver HasAchievements) e
/// um aviso de "não consegui checar o que você já desbloqueou" (perfil
/// Steam privado, por exemplo) sem precisar jogar fora a lista inteira —
/// dá pra mostrar os nomes/descrições mesmo sem saber o progresso.
/// </summary>
public sealed class SteamAchievementsResult
{
    public required IReadOnlyList<SteamAchievementInfo> Achievements { get; set; }

    /// <summary>
    /// True quando GetSchemaForGame não listou nenhuma conquista pra esse
    /// jogo (muitos jogos simplesmente não têm — não é erro).
    /// </summary>
    public bool HasAchievements => Achievements.Count > 0;

    /// <summary>
    /// True quando o jogo TEM conquistas, mas GetPlayerAchievements falhou
    /// pra essa conta (perfil Steam privado, "Detalhes do jogo" privado,
    /// ou erro de rede) — Achieved fica false em tudo só por falta de dado,
    /// não porque a conta não desbloqueou nada de verdade.
    /// </summary>
    public bool PlayerDataUnavailable { get; set; }
}
