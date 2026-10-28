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
