using System.Diagnostics;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// Starts games and tracks how long they ran.
/// 
/// v1.0.0 (Linux prep): Adaptado para funcionar no Windows e no Linux.
/// - No Linux, URIs (steam://) são abertas via "xdg-open".
/// - No Linux, jogos diretos ganham permissão de execução (chmod +x).
/// - Funções exclusivas do Windows (AppUninstaller) são bloqueadas em outros SOs.
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
            OpenUri(game.LaunchUri);
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

        if (string.IsNullOrWhiteSpace(game.InstallDirectory)) return;

        // Comparação de caminho ciente do sistema operacional
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        foreach (var candidate in Process.GetProcesses())
        {
            if (TryGetMainModulePath(candidate, out var path) &&
                path.StartsWith(game.InstallDirectory, comparison))
            {
                TryKill(candidate);
            }
        }
    }

    private static void LaunchViaSteamUri(uint appId)
    {
        OpenUri($"steam://rungameid/{appId}");
    }

    public void OpenSteamUninstaller(uint steamAppId)
    {
        OpenUri($"steam://uninstall/{steamAppId}");
    }

    public void OpenSteamInstaller(uint steamAppId)
    {
        OpenUri($"steam://install/{steamAppId}");
    }

    public void OpenWindowsAppUninstaller(string? installDirectory)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Desinstalar apps do Xbox via launcher é uma funcionalidade exclusiva do Windows.");
        }

        var pfn = string.IsNullOrWhiteSpace(installDirectory) ? null : TryFindPackageFamilyName(installDirectory);

        OpenUri(pfn is null ? "ms-settings:appsfeatures" : $"ms-settings:appsfeatures-app?{pfn}");
    }

    private static string? TryFindPackageFamilyName(string installDirectory)
    {
        if (!OperatingSystem.IsWindows()) return null;

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

        // NOVO: No Linux, garante que o arquivo tem permissão de execução (chmod +x)
        if (OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(game.ExecutablePath))
        {
            EnsureExecutablePermission(game.ExecutablePath);
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

    /// <summary>
    /// Abre uma URI (steam://, uplay://, ms-settings:) de forma compatível
    /// com o sistema operacional em uso.
    /// </summary>
    private static void OpenUri(string uri)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true,
            });
        }
        else if (OperatingSystem.IsLinux())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = uri,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        else if (OperatingSystem.IsMacOS())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                Arguments = uri,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
    }

    /// <summary>
    /// No Linux, garante que o binário tenha permissão de execução (equivalente ao chmod +x).
    /// </summary>
    private static void EnsureExecutablePermission(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var mode = File.GetUnixFileMode(path);
            
            // Adiciona permissão de execução para o dono, grupo e outros (se ainda não tiver)
            var executeMask = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            if ((mode & executeMask) != executeMask)
            {
                File.SetUnixFileMode(path, mode | executeMask);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameLauncher] Erro ao tentar dar permissão de execução no Linux: {ex.Message}");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch { }
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
            return false;
        }
    }

    private static bool HasExitedSafe(Process p)
    {
        try { return p.HasExited; } catch { return true; }
    }
}