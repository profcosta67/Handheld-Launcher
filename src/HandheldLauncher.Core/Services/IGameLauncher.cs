using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

public interface IGameLauncher
{
    /// <summary>Raised when a launched game's process exits, with how long it ran.</summary>
    event EventHandler<GameExitedEventArgs>? GameExited;

    bool IsAnyGameRunning { get; }

    void Launch(GameEntry game);

    /// <summary>
    /// Forces a running game closed — the explicit "fechar jogo" action from
    /// the UI/controller, for when a game doesn't exit cleanly on its own
    /// (or, for Steam titles, since we don't yet detect their exit
    /// automatically — see GameLauncher's class comment).
    /// Safe to call even if the game isn't actually running.
    /// </summary>
    void CloseGame(GameEntry game);

    /// <summary>
    /// Opens Steam's own uninstall confirmation for a Steam app ("Excluir
    /// jogo" no menu de contexto, só pra entradas Source==Steam) via
    /// "steam://uninstall/{appid}" — mesma ideia de LaunchViaSteamUri, só
    /// que pedindo pra Steam desinstalar em vez de rodar o jogo. A Steam
    /// mesma mostra a confirmação e cuida de apagar os arquivos; o
    /// launcher nunca toca nada no disco diretamente aqui.
    /// </summary>
    void OpenSteamUninstaller(uint steamAppId);

    /// <summary>
    /// "Instalar" (painel de Downloads, jogo da conta ainda não instalado)
    /// via "steam://install/{appid}" — a Steam mesma baixa; o launcher
    /// nunca baixa nada diretamente. Ver ISteamOwnedGamesClient.
    /// </summary>
    void OpenSteamInstaller(uint steamAppId);

    /// <summary>
    /// "Excluir jogo" pra jogos escaneados do Xbox app/Game Pass (v0.9.15,
    /// item antigo do roteiro). Sem URI de desinstalação direta como a
    /// Steam tem — tenta achar o pacote MSIX/UWP instalado em
    /// <paramref name="installDirectory"/> (via PowerShell/Get-AppxPackage)
    /// e abre a página OFICIAL desse app em Configurações do Windows
    /// ("ms-settings:appsfeatures-app?&lt;PackageFamilyName&gt;",
    /// documentado pela Microsoft), onde o USUÁRIO clica em Desinstalar —
    /// mesmo princípio da Steam, o launcher nunca apaga nada sozinho. Se
    /// não achar o pacote (instalação não-MSIX, PowerShell falhar, etc.),
    /// cai pra abrir a lista geral de Apps ("ms-settings:appsfeatures")
    /// em vez de falhar silenciosamente. Ver GameLauncher pros detalhes.
    /// </summary>
    void OpenWindowsAppUninstaller(string? installDirectory);
}

public sealed class GameExitedEventArgs : EventArgs
{
    public required GameEntry Game { get; init; }
    public required TimeSpan Duration { get; init; }
}
