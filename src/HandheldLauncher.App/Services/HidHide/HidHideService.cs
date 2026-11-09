using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading.Tasks; // Adicionado para Task.Run
using Nefarius.Drivers.HidHide;

namespace HandheldLauncher.Services.HidHide;

public sealed class HidHideService : IHidHideService, IDisposable
{
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
            try
            {
                return _hidHideControl.IsInstalled && _hidHideControl.IsOperational;
            }
            catch
            {
                return false;
            }
        }
    }

    public void Initialize()
    {
        if (!IsAvailable) return;

        ControllerInstanceId = FindControllerInstanceId();
        
        if (ControllerInstanceId != null)
        {
            try
            {
                if (!_hidHideControl.BlockedInstanceIds.Contains(ControllerInstanceId))
                {
                    _hidHideControl.AddBlockedInstanceId(ControllerInstanceId);
                }

                // MODO DENY-LIST: O controle é escondido apenas de quem está na lista (o jogo)
                _hidHideControl.IsAppListInverted = true; 
                _hidHideControl.IsActive = true;
                _isInitialized = true;
            }
            catch {}
        }
    }

    // ==========================================
    // B5: Bloqueio por Jogo (Deny-List)
    // ==========================================
    
    public void HideControllerForProcess(int processId)
    {
        if (!_isInitialized || _hidHideControl == null || processId <= 0) return;
        
        // Roda em background para NÃO travar a interface do Overlay
        Task.Run(() =>
        {
            try
            {
                var processPath = GetProcessPath(processId);
                if (!string.IsNullOrEmpty(processPath))
                {
                    _hidHideControl.AddApplicationPath(processPath, throwIfInvalid: false);
                    HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] Controle OCULTADO para o jogo: {processPath}");
                }
            }
            catch (Exception ex)
            {
                HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] Erro ao esconder controle: {ex.Message}");
            }
        });
    }

    public void RestoreControllerForProcess(int processId)
    {
        if (!_isInitialized || _hidHideControl == null || processId <= 0) return;
        
        Task.Run(() =>
        {
            try
            {
                var processPath = GetProcessPath(processId);
                if (!string.IsNullOrEmpty(processPath))
                {
                    _hidHideControl.RemoveApplicationPath(processPath);
                    HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] Controle RESTAURADO para o jogo: {processPath}");
                }
            }
            catch (Exception ex)
            {
                HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] Erro ao restaurar controle: {ex.Message}");
            }
        });
    }

    public void Dispose()
    {
        if (_hidHideControl != null && IsAvailable && _isInitialized)
        {
            try 
            {
                _hidHideControl.IsActive = false;
                HandheldLauncher.App.Diagnostics.DebugLog.Write("[HidHide] Mascaramento desativado no Dispose (segurança).");
            } 
            catch {}
        }
    }

    // ==========================================
    // Helpers (Síncronos, rodam no boot)
    // ==========================================

    private string? GetProcessPath(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.MainModule?.FileName;
        }
        catch { return null; }
    }

    private string? FindControllerInstanceId()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DeviceID, Name FROM Win32_PnPEntity WHERE PNPClass = 'HIDClass'");
            
            foreach (var device in searcher.Get())
            {
                var deviceId = device["DeviceID"]?.ToString();
                if (string.IsNullOrEmpty(deviceId)) continue;

                if (deviceId.Contains("VID_17EF"))
                {
                    if (deviceId.Contains("IG_00") || deviceId.Contains("IG_01") || deviceId.Contains("MI_00&Col01"))
                    {
                        return deviceId;
                    }
                }
            }
        }
        catch {}
        return null;
    }

    public void ForceRestartController()
    {
        // Método mantido por compatibilidade, mas não é mais chamado no App.axaml.cs
    }
}