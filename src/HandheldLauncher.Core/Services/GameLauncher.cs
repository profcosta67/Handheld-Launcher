using System.Diagnostics;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// Starts games and tracks how long they ran.
///
/// Manual / emulator entries: we own the process directly (Process.Start),
/// so we get an exact start/exit time and exit code.
///
/// Steam entries: we launch via "steam://rungameid/{appid}" and hand off to
/// Steam's own client, which means the process we start is just the URI
/// activation — it exits almost immediately, before the real game process
/// even appears. Accurately tracking "when did the actual game close" for
/// Steam titles needs watching for a new process under the game's install
/// directory (or polling Steam's RunningAppID registry value), which is
/// listed as a follow-up — not implemented here so we don't ship an
/// untested polling loop. For now, Steam launches fire-and-forget and don't
/// raise GameExited.
/// </summary>
public sealed class GameLauncher : IGameLauncher
{
    private readonly List<Process> _trackedProcesses = new();

    public event EventHandler<GameExitedEventArgs>? GameExited;

    public bool IsAnyGameRunning => _trackedProcesses.Any(p => !HasExitedSafe(p));

    public void Launch(GameEntry game)
    {
        if (game.Source == Models.GameSource.Steam && game.SteamAppId is { } appId)
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
            _trackedProcesses.Remove(process);
            GameExited?.Invoke(this, new GameExitedEventArgs { Game = game, Duration = duration });
            process.Dispose();
        };

        process.Start();
        _trackedProcesses.Add(process);
    }

    private static bool HasExitedSafe(Process p)
    {
        try { return p.HasExited; }
        catch { return true; }
    }
}
