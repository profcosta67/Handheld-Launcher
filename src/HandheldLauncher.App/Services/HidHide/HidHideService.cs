using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using Nefarius.Drivers.HidHide;

namespace HandheldLauncher.Services.HidHide;

public sealed class HidHideService : IHidHideService, IDisposable
{
    private const string ControllerVendorId = "VID_17EF";
    private readonly IHidHideControlService? _hidHideControl;
    private bool _isInitialized;

    public string? ControllerInstanceId { get; private set; }

    public HidHideService(IHidHideControlService? hidHideControl = null)
    {
        _hidHideControl = hidHideControl;
    }

    public bool IsAvailable
    {
        get
        {
            if (_hidHideControl is null) return false;
            try { return _hidHideControl.IsInstalled && _hidHideControl.IsOperational; }
            catch { return false; }
        }
    }

    public void Initialize()
    {
        if (!IsAvailable || _hidHideControl is null) return;

        try
        {
            ControllerInstanceId = FindControllerInstanceId();
            if (ControllerInstanceId is null)
            {
                HandheldLauncher.App.Diagnostics.DebugLog.Write("[HidHide] Controle Lenovo (VID_17EF) não encontrado; isolamento não inicializado.");
                return;
            }

            // XInput pode abrir o nó USB/base do mesmo controle, não apenas o
            // filho HID. Registrar todos os nós com o mesmo VID/PID evita deixar
            // o caminho USB visível ao jogo quando só o HID foi mascarado.
            foreach (var instanceId in FindRelatedControllerInstanceIds(ControllerInstanceId))
            {
                if (_hidHideControl.BlockedInstanceIds.Contains(instanceId)) continue;
                _hidHideControl.AddBlockedInstanceId(instanceId);
            }

            // Modo inverso = lista de processos bloqueados. O jogo é incluído
            // somente enquanto o QS está aberto; o launcher permanece fora dela.
            _hidHideControl.IsAppListInverted = true;
            _hidHideControl.IsActive = true;
            _isInitialized = true;

            HandheldLauncher.App.Diagnostics.DebugLog.Write(
                $"[HidHide] Inicializado. Nós do controle mascarados: {string.Join(" | ", FindRelatedControllerInstanceIds(ControllerInstanceId))}");
        }
        catch (Exception ex)
        {
            HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] Falha ao inicializar isolamento: {ex.GetType().Name}: {ex.Message}");
            _isInitialized = false;
        }
    }

    public void HideControllerForProcess(int processId)
    {
        if (!_isInitialized || _hidHideControl is null || processId <= 0) return;

        try
        {
            var processPath = GetProcessPath(processId);
            if (string.IsNullOrWhiteSpace(processPath))
            {
                HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] Não foi possível obter o executável do jogo (PID {processId}); controle não foi isolado.");
                return;
            }

            // Operação síncrona intencional: o overlay só deve receber o foco
            // depois que o processo do jogo entrar na lista de bloqueio.
            _hidHideControl.AddApplicationPath(processPath, throwIfInvalid: false);
            HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] Processo do jogo bloqueado para o controle: {processPath}");
        }
        catch (Exception ex)
        {
            HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] Erro ao bloquear jogo: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void RestoreControllerForProcess(int processId)
    {
        if (!_isInitialized || _hidHideControl is null || processId <= 0) return;

        try
        {
            var processPath = GetProcessPath(processId);
            if (string.IsNullOrWhiteSpace(processPath)) return;

            _hidHideControl.RemoveApplicationPath(processPath);
            HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] Acesso do jogo restaurado: {processPath}");
        }
        catch (Exception ex)
        {
            HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] Erro ao restaurar jogo: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_hidHideControl is null || !IsAvailable || !_isInitialized) return;
        try
        {
            _hidHideControl.IsActive = false;
            HandheldLauncher.App.Diagnostics.DebugLog.Write("[HidHide] Mascaramento desativado no Dispose (segurança).");
        }
        catch (Exception ex)
        {
            HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] Falha ao desativar mascaramento no Dispose: {ex.Message}");
        }
    }

    private static string? GetProcessPath(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.MainModule?.FileName;
        }
        catch (Exception ex)
        {
            HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] Não foi possível ler o caminho do PID {processId}: {ex.Message}");
            return null;
        }
    }

    private static string? FindControllerInstanceId()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID FROM Win32_PnPEntity WHERE PNPClass = 'HIDClass'");
            foreach (var device in searcher.Get())
            {
                var deviceId = device["DeviceID"]?.ToString();
                if (string.IsNullOrWhiteSpace(deviceId) ||
                    !deviceId.Contains(ControllerVendorId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (deviceId.Contains("IG_00", StringComparison.OrdinalIgnoreCase) ||
                    deviceId.Contains("IG_01", StringComparison.OrdinalIgnoreCase) ||
                    deviceId.Contains("MI_00&Col01", StringComparison.OrdinalIgnoreCase))
                    return deviceId;
            }
        }
        catch (Exception ex)
        {
            HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] Erro ao localizar controle HID: {ex.Message}");
        }

        return null;
    }

    private static IReadOnlyList<string> FindRelatedControllerInstanceIds(string controllerInstanceId)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { controllerInstanceId };
        var markerStart = controllerInstanceId.IndexOf(ControllerVendorId, StringComparison.OrdinalIgnoreCase);
        if (markerStart < 0) return ids.ToArray();

        var markerEnd = controllerInstanceId.IndexOf('\\', markerStart);
        if (markerEnd < 0) markerEnd = controllerInstanceId.Length;
        var hardwareKey = controllerInstanceId[markerStart..markerEnd];

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID FROM Win32_PnPEntity WHERE DeviceID IS NOT NULL");
            foreach (var device in searcher.Get())
            {
                var deviceId = device["DeviceID"]?.ToString();
                if (!string.IsNullOrWhiteSpace(deviceId) &&
                    deviceId.Contains(hardwareKey, StringComparison.OrdinalIgnoreCase))
                    ids.Add(deviceId);
            }
        }
        catch (Exception ex)
        {
            HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] Não foi possível enumerar os nós USB/HID relacionados: {ex.Message}");
        }

        return ids.ToArray();
    }

    public void ForceRestartController()
    {
        // Mantido por compatibilidade; não é chamado no App.axaml.cs.
    }
}