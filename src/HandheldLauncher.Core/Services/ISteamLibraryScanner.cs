using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

public interface ISteamLibraryScanner
{
    /// <summary>
    /// True if a Steam install was found on this machine.
    /// </summary>
    bool IsSteamInstalled { get; }

    /// <summary>
    /// Scans all Steam library folders (default + any added drives) and
    /// returns every currently-installed app as a GameEntry.
    /// Non-game apps (runtimes, SteamVR, tools) are filtered out heuristically.
    /// </summary>
    IReadOnlyList<GameEntry> ScanInstalledGames();

    /// <summary>
    /// Pedido do usuário ("gerenciador de download... olhar os jogos que
    /// estão sendo baixados"): devolve todo app Steam que está no meio de
    /// um download/atualização AGORA, com o percentual já calculado — lido
    /// dos mesmos `appmanifest_*.acf` que ScanInstalledGames usa, sem
    /// precisar do cliente Steam rodando nem de nenhuma API. Lista vazia
    /// se nada estiver baixando (o caso comum).
    /// </summary>
    IReadOnlyList<DownloadProgressInfo> ScanActiveDownloads();
}
