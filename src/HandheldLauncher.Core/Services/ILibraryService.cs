using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// The single source of truth the UI binds to: merges whatever the scanners
/// find (Steam today; Xbox/Epic/emulators later) with manually-added games
/// and locally-stored overrides (art, collection, playtime).
/// </summary>
public interface ILibraryService
{
    IReadOnlyList<GameEntry> Games { get; }

    /// <summary>Re-runs every scanner and re-merges with stored manual games + overrides.</summary>
    void Refresh();

    void AddManualGame(GameEntry game);
    void RemoveManualGame(string id);

    void SetCollection(string gameId, string? collection);
    void SetFavorite(string gameId, bool isFavorite);
    void RecordPlaySession(string gameId, TimeSpan duration);

    /// <summary>
    /// Marks a game as the one just opened — distinct from RecordPlaySession
    /// (which only fires once the game exits): this is the "último jogo
    /// aberto" the UI puts in the wide landscape tile, and it has to update
    /// the instant the player launches something, not after they quit it.
    /// Mutates the matching in-memory GameEntry directly instead of doing a
    /// full Refresh(), so callers don't lose whatever UI-only state (current
    /// selection, running-game reference) is riding on the existing instances.
    /// </summary>
    void MarkLaunched(string gameId);

    /// <summary>
    /// Painel de Downloads, seção "baixando agora" — jogos Steam no meio
    /// de um download/atualização neste exato momento (ver
    /// ISteamLibraryScanner.ScanActiveDownloads). Lista vazia (comum) se
    /// nada estiver baixando.
    /// </summary>
    IReadOnlyList<DownloadProgressInfo> GetActiveDownloads();

    /// <summary>
    /// True só quando AppSettings.SteamWebApiKey e SteamId64 estão os
    /// dois preenchidos — controla se a UI mostra a seção "na sua conta,
    /// não instalado" do painel de Downloads ou a mensagem explicando o
    /// que falta configurar.
    /// </summary>
    bool HasSteamWebApiCredentials { get; }

    /// <summary>
    /// Painel de Downloads, seção "na sua conta, não instalado" — busca
    /// TODA a biblioteca Steam da conta (via ISteamOwnedGamesClient) e
    /// filtra fora quem já está instalado (comparando contra Games,
    /// Source==Steam). Lista vazia sem lançar se HasSteamWebApiCredentials
    /// for false; lança se a chamada de rede falhar (quem chama decide
    /// como mostrar o erro).
    /// </summary>
    Task<IReadOnlyList<OwnedSteamGame>> GetOwnedButNotInstalledSteamGamesAsync();
}
