using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// Steam Web API (IPlayerService/GetOwnedGames) — lista TODA a biblioteca
/// Steam da conta, instalada ou não. Diferente de ISteamLibraryScanner
/// (que só lê manifestos locais de jogos JÁ instalados): isto é uma
/// chamada de rede autenticada, precisa de AppSettings.SteamWebApiKey +
/// SteamId64 (ver essa classe). Pedido do usuário: "se der pra ver os
/// jogos que tem na conta seria legal também".
/// </summary>
public interface ISteamOwnedGamesClient
{
    /// <summary>True só quando os dois campos (key + SteamId64) estão preenchidos.</summary>
    bool HasCredentials { get; }

    /// <summary>
    /// Lança se HasCredentials for false, ou se a chamada HTTP falhar —
    /// de propósito (quem chama decide como mostrar o erro, mesmo padrão
    /// de IArtworkService.GetGridCandidatesAsync e similares).
    /// </summary>
    Task<IReadOnlyList<OwnedSteamGame>> GetOwnedGamesAsync(CancellationToken ct = default);
}
