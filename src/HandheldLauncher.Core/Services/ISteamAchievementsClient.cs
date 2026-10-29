using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// Steam Web API (ISteamUserStats/GetSchemaForGame + GetPlayerAchievements)
/// — conquistas de UM jogo específico, pra tela de "Detalhes do jogo"
/// (pedido do usuário: "medalhas e conquistas... igual tem no decky
/// loader"). Mesma credencial de ISteamOwnedGamesClient (SteamWebApiKey +
/// SteamId64 em AppSettings) — não precisa de nenhuma chave nova.
/// </summary>
public interface ISteamAchievementsClient
{
    /// <summary>True só quando os dois campos (key + SteamId64) estão preenchidos.</summary>
    bool HasCredentials { get; }

    /// <summary>
    /// Lança se HasCredentials for false (quem chama decide como mostrar o
    /// erro, mesmo padrão de ISteamOwnedGamesClient.GetOwnedGamesAsync).
    /// Nunca lança por o JOGO não ter conquistas ou por o PERFIL estar
    /// privado — essas duas situações voltam dentro do resultado (ver
    /// SteamAchievementsResult).
    /// </summary>
    Task<SteamAchievementsResult> GetAchievementsAsync(uint appId, CancellationToken ct = default);
}
