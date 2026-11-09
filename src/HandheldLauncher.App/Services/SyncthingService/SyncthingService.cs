using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.Services.Syncthing;

public sealed record SyncthingDashboard(string Status, string Devices, string Folders);

public sealed class SyncthingService : IDisposable
{
    private Process? _syncthingProcess;
    private string? _syncthingPath;
    private static readonly HttpClient ApiHttp = new() { Timeout = TimeSpan.FromSeconds(5) };
    private const string ApiBaseAddress = "http://127.0.0.1:8384";

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
                RedirectStandardOutput = false,
                RedirectStandardError = false
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

    public async Task<SyncthingDashboard> GetDashboardAsync()
    {
        var apiKey = ReadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Chave da API não encontrada no config.xml do Syncthing. Inicie o serviço e tente novamente.");

        using var statusDoc = await GetJsonAsync("/rest/system/status", apiKey);
        using var devicesDoc = await GetJsonAsync("/rest/config/devices", apiKey);
        using var foldersDoc = await GetJsonAsync("/rest/config/folders", apiKey);
        using var connectionsDoc = await GetJsonAsync("/rest/system/connections", apiKey);

        var statusRoot = statusDoc.RootElement;
        var version = statusRoot.TryGetProperty("version", out var v) ? v.GetString() : "desconhecida";
        var uptime = statusRoot.TryGetProperty("uptime", out var up) && up.TryGetInt64(out var seconds)
            ? TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"d.hh:mm:ss")
            : "—";

        var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (connectionsDoc.RootElement.TryGetProperty("connections", out var connections))
        {
            foreach (var connection in connections.EnumerateObject())
                if (connection.Value.TryGetProperty("connected", out var isConnected) && isConnected.ValueKind == JsonValueKind.True)
                    connected.Add(connection.Name);
        }

        var deviceLines = new List<string>();
        foreach (var device in devicesDoc.RootElement.EnumerateArray())
        {
            var id = device.TryGetProperty("deviceID", out var idValue) ? idValue.GetString() ?? "" : "";
            var name = device.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
            var paused = device.TryGetProperty("paused", out var pausedValue) && pausedValue.ValueKind == JsonValueKind.True;
            var label = string.IsNullOrWhiteSpace(name) ? (id.Length > 12 ? id[..12] + "…" : id) : name;
            var state = paused ? "pausado" : connected.Contains(id) ? "conectado" : "desconectado";
            deviceLines.Add($"• {label} — {state}");
        }

        var folderLines = new List<string>();
        foreach (var folder in foldersDoc.RootElement.EnumerateArray())
        {
            var id = folder.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? "" : "";
            var label = folder.TryGetProperty("label", out var labelValue) ? labelValue.GetString() : null;
            var path = folder.TryGetProperty("path", out var pathValue) ? pathValue.GetString() : null;
            var paused = folder.TryGetProperty("paused", out var pausedValue) && pausedValue.ValueKind == JsonValueKind.True;
            folderLines.Add($"• {(string.IsNullOrWhiteSpace(label) ? id : label)} — {(paused ? "pausada" : "ativa")}\n  {path}");
        }

        return new SyncthingDashboard(
            $"Em execução · Syncthing {version} · ativo há {uptime}",
            deviceLines.Count == 0 ? "Nenhum dispositivo remoto configurado." : string.Join(Environment.NewLine, deviceLines),
            folderLines.Count == 0 ? "Nenhuma pasta compartilhada configurada." : string.Join(Environment.NewLine, folderLines));
    }

    public async Task ScanAllFoldersAsync()
    {
        var apiKey = ReadApiKey();
        using var folders = await GetJsonAsync("/rest/config/folders", apiKey);
        foreach (var folder in folders.RootElement.EnumerateArray())
        {
            if (!folder.TryGetProperty("id", out var idValue)) continue;
            var id = idValue.GetString();
            if (string.IsNullOrWhiteSpace(id)) continue;
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseAddress}/rest/db/scan?folder={Uri.EscapeDataString(id)}");
            request.Headers.Add("X-API-Key", apiKey);
            using var response = await ApiHttp.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task RestartSyncthingAsync()
    {
        var apiKey = ReadApiKey();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseAddress}/rest/system/restart");
        request.Headers.Add("X-API-Key", apiKey);
        using var response = await ApiHttp.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<JsonDocument> GetJsonAsync(string endpoint, string apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiBaseAddress + endpoint);
        request.Headers.Add("X-API-Key", apiKey);
        using var response = await ApiHttp.SendAsync(request);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static string ReadApiKey()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var candidates = new[]
        {
            Path.Combine(local, "Syncthing", "config.xml"),
            Path.Combine(roaming, "Syncthing", "config.xml"),
            Path.Combine(AppContext.BaseDirectory, "config.xml")
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var document = XDocument.Load(path);
                var key = document.Root?.Element("gui")?.Element("apikey")?.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(key)) return key;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[Syncthing] Não foi possível ler a chave da API em {path}: {ex.Message}");
            }
        }

        throw new InvalidOperationException("Não encontrei a chave da API no config.xml do Syncthing.");
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