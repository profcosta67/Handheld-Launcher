using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.Services.Syncthing;

public sealed class SyncthingService : IDisposable
{
    private Process? _syncthingProcess;
    private string? _syncthingPath;

    public bool IsRunning => _syncthingProcess != null && !_syncthingProcess.HasExited;

    public void Initialize()
    {
        _syncthingPath = FindSyncthingExecutable();
        if (_syncthingPath != null)
        {
            DebugLog.Write($"[Syncthing] Executável encontrado em: {_syncthingPath}");
        }
        else
        {
            DebugLog.Write("[Syncthing] Executável não encontrado. A sincronização não funcionará até que o Syncthing seja instalado.");
        }
    }

    public bool Start()
    {
        if (IsRunning) return true;
        if (string.IsNullOrEmpty(_syncthingPath)) return false;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _syncthingPath,
                Arguments = "serve --no-browser", // Roda em background sem abrir o navegador
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _syncthingProcess = Process.Start(startInfo);
            if (_syncthingProcess != null)
            {
                DebugLog.Write("[Syncthing] Processo iniciado em segundo plano.");
                return true;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Syncthing] Erro ao iniciar: {ex.Message}");
        }

        return false;
    }

    public void Stop()
    {
        if (_syncthingProcess == null) return;
        try
        {
            if (!_syncthingProcess.HasExited)
            {
                _syncthingProcess.Kill(entireProcessTree: true);
                _syncthingProcess.WaitForExit(3000);
            }
            _syncthingProcess.Dispose();
            _syncthingProcess = null;
            DebugLog.Write("[Syncthing] Processo parado.");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Syncthing] Erro ao parar: {ex.Message}");
        }
    }

    public void Dispose() => Stop();

    private string? FindSyncthingExecutable()
    {
        // 1. Procura na pasta Tools do próprio launcher
        var localPath = Path.Combine(AppContext.BaseDirectory, "Tools", "syncthing.exe");
        if (File.Exists(localPath)) return localPath;

        // 2. Procura no PATH do sistema
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
        foreach (var path in paths)
        {
            var fullPath = Path.Combine(path, "syncthing.exe");
            if (File.Exists(fullPath)) return fullPath;
        }

        // 3. Procura no AppData e Chocolatey
        var commonPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Syncthing", "syncthing.exe"),
            "C:\\ProgramData\\chocolatey\\bin\\syncthing.exe"
        };

        return commonPaths.FirstOrDefault(File.Exists);
    }
}