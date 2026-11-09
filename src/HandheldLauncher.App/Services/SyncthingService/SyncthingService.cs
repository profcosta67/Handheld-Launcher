using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.Services.Syncthing;

/// <summary>
/// Sobe e mantém o Syncthing em segundo plano e garante que a GUI local já
/// responde antes de a tela embutida (WebView) apontar para ela.
/// </summary>
public sealed class SyncthingService : IDisposable
{
    /// <summary>Endereço da interface web local — é a URL carregada no WebView.</summary>
    public const string BaseUrl = "http://127.0.0.1:8384";

    private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(2) };

    private Process? _syncthingProcess;
    private string? _syncthingPath;

    /// <summary>True quando o syncthing.exe foi encontrado (Tools do app, PATH ou instalação padrão).</summary>
    public bool IsAvailable => !string.IsNullOrEmpty(_syncthingPath);

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

    /// <summary>
    /// Garante o Syncthing no ar: se a GUI já responde (inclusive uma instância
    /// aberta fora do launcher, ou de uma sessão anterior) não inicia nada;
    /// senão inicia o executável e espera a GUI responder até
    /// <paramref name="timeout"/>. Devolve false se não der para ficar pronto.
    /// </summary>
    public async Task<bool> EnsureRunningAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (await IsGuiUpAsync(cancellationToken).ConfigureAwait(false)) return true;

        if (!IsAvailable)
        {
            DebugLog.Write("[Syncthing] Executável não encontrado: não há como iniciar.");
            return false;
        }

        if (!Start()) return false;

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await IsGuiUpAsync(cancellationToken).ConfigureAwait(false)) return true;
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        DebugLog.Write($"[Syncthing] GUI não respondeu em {timeout.TotalSeconds:0}s.");
        return false;
    }

    // /rest/noauth/health responde 200 assim que a GUI está pronta para uso —
    // mais confiável do que esperar um tempo fixo depois do Process.Start.
    private static async Task<bool> IsGuiUpAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await HealthClient
                .GetAsync(BaseUrl + "/rest/noauth/health", cancellationToken)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
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
        // Fase 1 Linux: o binário é `syncthing` no Linux/macOS e `syncthing.exe`
        // no Windows.
        var exeName = OperatingSystem.IsWindows() ? "syncthing.exe" : "syncthing";

        // 1. Procura na pasta Tools do próprio launcher
        var localPath = Path.Combine(AppContext.BaseDirectory, "Tools", exeName);
        if (File.Exists(localPath)) return localPath;

        // 2. Procura no PATH do sistema
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
        foreach (var path in paths)
        {
            var fullPath = Path.Combine(path, exeName);
            if (File.Exists(fullPath)) return fullPath;
        }

        // 3. Locais comuns por plataforma
        if (OperatingSystem.IsWindows())
        {
            var commonPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Syncthing", "syncthing.exe"),
                "C:\\ProgramData\\chocolatey\\bin\\syncthing.exe"
            };
            return commonPaths.FirstOrDefault(File.Exists);
        }

        // Linux: instalações manuais costumam ficar fora do PATH.
        var linuxCandidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "syncthing"),
            "/usr/local/bin/syncthing"
        };
        return linuxCandidates.FirstOrDefault(File.Exists);
    }
}