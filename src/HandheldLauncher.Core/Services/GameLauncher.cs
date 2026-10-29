using System.Diagnostics;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// Starts games and tracks how long they ran.
///
/// Manual / emulator entries: we own the process directly (Process.Start),
/// so we get an exact start/exit time and exit code, and CloseGame can just
/// kill the process tree we started.
///
/// Steam entries: we launch via "steam://rungameid/{appid}" and hand off to
/// Steam's own client, which means the process we start is just the URI
/// activation — it exits almost immediately, before the real game process
/// even appears. We don't own that process, so:
///   - GameExited never fires for Steam titles (no automatic exit
///     detection yet — a real fix needs watching for a new process under
///     the install dir, or polling Steam's RunningAppID, neither
///     implemented here so we don't ship an untested polling loop).
///   - CloseGame falls back to a best-effort sweep: kill anything currently
///     running whose executable lives under the game's InstallDirectory.
///     Good enough for "stuck, get me back to the launcher"; not meant to
///     be surgical.
/// </summary>
public sealed class GameLauncher : IGameLauncher
{
    private readonly Dictionary<string, Process> _processByGameId = new();

    public event EventHandler<GameExitedEventArgs>? GameExited;

    public bool IsAnyGameRunning => _processByGameId.Values.Any(p => !HasExitedSafe(p));

    public void Launch(GameEntry game)
    {
        if (game.Source == GameSource.Steam && game.SteamAppId is { } appId)
        {
            LaunchViaSteamUri(appId);
            return;
        }

        if (!string.IsNullOrWhiteSpace(game.LaunchUri))
        {
            Process.Start(new ProcessStartInfo { FileName = game.LaunchUri, UseShellExecute = true });
            return;
        }

        if (string.IsNullOrWhiteSpace(game.ExecutablePath))
        {
            throw new InvalidOperationException(
                $"Game '{game.Title}' has no ExecutablePath and isn't a Steam entry — nothing to launch.");
        }

        LaunchDirect(game);
    }

    public void CloseGame(GameEntry game)
    {
        if (_processByGameId.TryGetValue(game.Id, out var owned))
        {
            TryKill(owned);
            _processByGameId.Remove(game.Id);
            return;
        }

        // Not a process we started ourselves (Steam) — best-effort sweep by
        // install directory. No-op if we never learned the install path.
        if (string.IsNullOrWhiteSpace(game.InstallDirectory)) return;

        foreach (var candidate in Process.GetProcesses())
        {
            if (TryGetMainModulePath(candidate, out var path) &&
                path.StartsWith(game.InstallDirectory, StringComparison.OrdinalIgnoreCase))
            {
                TryKill(candidate);
            }
        }
    }

    private static void LaunchViaSteamUri(uint appId)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = $"steam://rungameid/{appId}",
            UseShellExecute = true,
        });
    }

    /// <summary>
    /// "Excluir jogo" (menu de contexto, só Steam) — abre o fluxo de
    /// desinstalação da própria Steam em vez do launcher apagar qualquer
    /// coisa diretamente. Mesmo padrão de UseShellExecute=true da URI de
    /// lançamento acima; só muda o verbo da URI.
    /// </summary>
    public void OpenSteamUninstaller(uint steamAppId)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = $"steam://uninstall/{steamAppId}",
            UseShellExecute = true,
        });
    }

    /// <summary>
    /// "Instalar" no painel de Downloads, pra um jogo que está na conta
    /// Steam mas ainda não foi instalado (ver ISteamOwnedGamesClient) —
    /// mesmo padrão de OpenSteamUninstaller acima, só muda o verbo da
    /// URI. A Steam mesma baixa/instala; o launcher nunca baixa nada.
    /// </summary>
    public void OpenSteamInstaller(uint steamAppId)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = $"steam://install/{steamAppId}",
            UseShellExecute = true,
        });
    }

    /// <summary>
    /// v0.9.15, item antigo do roteiro ("desinstalar jogo do Xbox app/Game
    /// Pass de verdade"). Diferente da Steam, o Xbox app/Game Pass não tem
    /// um protocolo "xbox://uninstall/..." documentado — e
    /// XboxFolderScanner só lê PASTAS (nunca captura a identidade real do
    /// pacote MSIX/UWP), então primeiro precisa DESCOBRIR o pacote antes
    /// de poder abrir a página certa.
    ///
    /// Caminho escolhido (avaliado contra apagar a pasta nós mesmos, que
    /// quebraria o princípio seguido em todo o resto do app — "nunca apaga
    /// arquivo de jogo diretamente"): PowerShell (Get-AppxPackage) pra
    /// achar o PackageFamilyName cujo InstallLocation bate com a pasta do
    /// jogo, depois abre esse app específico em Configurações via
    /// "ms-settings:appsfeatures-app?&lt;PFN&gt;" — URI documentada pela
    /// própria Microsoft (learn.microsoft.com/windows/apps/develop/launch/
    /// launch-settings-app). O usuário termina clicando em "Desinstalar"
    /// lá; o launcher só abre a tela certa, nunca toca no disco.
    ///
    /// NUNCA TESTADO contra uma instalação real do Xbox app/Game Pass
    /// (sem Windows/Xbox app neste sandbox) — se o PFN não vier por
    /// qualquer motivo (nome não bater, PowerShell indisponível/bloqueado
    /// por política, instalação não ser MSIX de verdade), cai pra abrir a
    /// lista GERAL de Apps em vez de falhar silenciosamente: o usuário
    /// sempre chega em algum lugar útil pra terminar manualmente.
    /// </summary>
    public void OpenWindowsAppUninstaller(string? installDirectory)
    {
        var pfn = string.IsNullOrWhiteSpace(installDirectory) ? null : TryFindPackageFamilyName(installDirectory);

        Process.Start(new ProcessStartInfo
        {
            FileName = pfn is null ? "ms-settings:appsfeatures" : $"ms-settings:appsfeatures-app?{pfn}",
            UseShellExecute = true,
        });
    }

    /// <summary>
    /// -ieq com os dois lados sem barra final: InstallLocation normalmente
    /// não tem barra no final, mas tolera os dois formatos. Nunca lança —
    /// qualquer falha (powershell.exe ausente, timeout, saída vazia) só
    /// resulta em null, tratado pelo fallback em OpenWindowsAppUninstaller.
    /// </summary>
    private static string? TryFindPackageFamilyName(string installDirectory)
    {
        try
        {
            var escaped = installDirectory.Replace("'", "''").TrimEnd('\\', '/');
            var script = "(Get-AppxPackage -AllUsers | Where-Object { $_.InstallLocation -and " +
                $"$_.InstallLocation.TrimEnd('\\') -ieq '{escaped}' }} | Select-Object -First 1).PackageFamilyName";

            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"{script}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(psi);
            if (process is null) return null;

            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit(8000);

            var pfn = stdout.Trim();
            return string.IsNullOrWhiteSpace(pfn) ? null : pfn;
        }
        catch
        {
            return null;
        }
    }

    private void LaunchDirect(GameEntry game)
    {
        var workingDirectory = game.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            workingDirectory = Path.GetDirectoryName(game.ExecutablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = game.ExecutablePath,
            Arguments = game.LaunchArguments ?? string.Empty,
            WorkingDirectory = workingDirectory ?? string.Empty,
            UseShellExecute = false,
        };

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var startedAt = DateTimeOffset.UtcNow;

        process.Exited += (_, _) =>
        {
            var duration = DateTimeOffset.UtcNow - startedAt;
            _processByGameId.Remove(game.Id);
            GameExited?.Invoke(this, new GameExitedEventArgs { Game = game, Duration = duration });
            process.Dispose();
        };

        process.Start();
        _processByGameId[game.Id] = process;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already exited, or we don't have permission (protected/system
            // process) — either way, nothing more we can do here.
        }
    }

    private static bool TryGetMainModulePath(Process process, out string path)
    {
        path = string.Empty;
        try
        {
            path = process.MainModule?.FileName ?? string.Empty;
            return !string.IsNullOrEmpty(path);
        }
        catch
        {
            // Most system/other-user processes throw (access denied) here —
            // skip them rather than let one bad process break the sweep.
            return false;
        }
    }

    private static bool HasExitedSafe(Process p)
    {
        try { return p.HasExited; }
        catch { return true; }
    }
}
