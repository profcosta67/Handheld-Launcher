using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// Scans the folders listed in AppSettings.XboxScanFolders for Xbox app /
/// Game Pass games — see XboxFolderScanner for how ("melhor esforço" por
/// pasta, não um scanner real de UWP). Pedido do usuário depois que a
/// detecção automática de jogos Xbox não achou nada ("Os jogos Xbox não
/// encontrou automático, será que tem como por um editor de onde
/// procurar?").
/// </summary>
public interface IXboxFolderScanner
{
    /// <summary>
    /// Escaneia toda pasta configurada e devolve um GameEntry (Source =
    /// GameSource.XboxApp) por SUBPASTA IMEDIATA encontrada dentro de
    /// cada uma. Lista vazia se nenhuma pasta estiver configurada ou
    /// nenhuma subpasta tiver um .exe dentro.
    /// </summary>
    IReadOnlyList<GameEntry> ScanInstalledGames();
}
