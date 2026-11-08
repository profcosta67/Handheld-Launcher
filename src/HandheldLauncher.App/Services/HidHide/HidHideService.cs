using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;
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
            catch (Exception ex)
            {
                HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] Erro ao verificar o driver: {ex.Message}");
                return false;
            }
        }
    }

    public void Initialize()
    {
        if (!IsAvailable)
        {
            HandheldLauncher.App.Diagnostics.DebugLog.Write("[HidHide] Initialize abortado: driver indisponível.");
            return;
        }

        ControllerInstanceId = FindControllerInstanceId();
        
        if (ControllerInstanceId != null)
        {
            HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] Controle físico detectado: {ControllerInstanceId}");
            
            try
            {
                HandheldLauncher.App.Diagnostics.DebugLog.Write("[HidHide] (B4) Configurando listas do HidHide...");
                
                if (!_hidHideControl.BlockedInstanceIds.Contains(ControllerInstanceId))
                {
                    _hidHideControl.AddBlockedInstanceId(ControllerInstanceId);
                }

                // Começamos no modo Deny-list (lista de bloqueio)
                _hidHideControl.IsAppListInverted = true; 
                _hidHideControl.IsActive = true;
                
                _isInitialized = true;
                HandheldLauncher.App.Diagnostics.DebugLog.Write("[HidHide] (B4) Sucesso! O controle está invisível globalmente.");
            }
            catch (Exception ex)
            {
                HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] (B4) ERRO ao configurar HidHide: {ex.Message}");
            }
        }
        else
        {
            HandheldLauncher.App.Diagnostics.DebugLog.Write("[HidHide] Nenhum controle físico compatível encontrado via WMI.");
        }
    }

    // ==========================================
    // B5: Bloqueio Global (Allow-list)
    // ==========================================
    
    public void HideControllerForProcess(int processId)
    {
        if (!_isInitialized || _hidHideControl == null) return;
        
        try
        {
            // Muda para Allow-list (Apenas quem estiver na lista vê o controle)
            _hidHideControl.IsAppListInverted = false;

            // Limpa qualquer resíduo de jogos anteriores na lista
            foreach (var app in _hidHideControl.ApplicationPaths.ToList())
            {
                _hidHideControl.RemoveApplicationPath(app);
            }

            // Pega o caminho do nosso próprio executável (o Launcher/Overlay)
            var currentProcessPath = Process.GetCurrentProcess().MainModule?.FileName;
            
            if (!string.IsNullOrEmpty(currentProcessPath))
            {
                _hidHideControl.AddApplicationPath(currentProcessPath, throwIfInvalid: false);
                HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] (B5) Controle OCULTADO de todo o sistema. Liberado apenas para: {currentProcessPath}");
            }
        }
        catch (Exception ex)
        {
            HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] (B5) Erro ao ocultar controle: {ex.Message}");
        }
    }

    public void RestoreControllerForProcess(int processId)
    {
        if (!_isInitialized || _hidHideControl == null) return;
        
        try
        {
            // Volta para Deny-list
            _hidHideControl.IsAppListInverted = true;

            // Limpa a lista. Como está no modo Deny-list e a lista está vazia,
            // TODOS os programas voltam a ver o controle.
            foreach (var app in _hidHideControl.ApplicationPaths.ToList())
            {
                _hidHideControl.RemoveApplicationPath(app);
            }
            
            HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] (B5) Controle RESTAURADO para todo o sistema (jogos e launchers).");
        }
        catch (Exception ex)
        {
            HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] (B5) Erro ao restaurar controle: {ex.Message}");
        }
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
    // Helpers
    // ==========================================

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
        catch (Exception ex)
        {
            HandheldLauncher.App.Diagnostics.DebugLog.Write($"[HidHide] Erro ao buscar controle no WMI: {ex.Message}");
        }

        return null;
    }

    public void ForceRestartController()
    {
        if (string.IsNullOrEmpty(ControllerInstanceId)) return;

        try
        {
            HandheldLauncher.App.Diagnostics.DebugLog.Write($"[CONTROLE-FIX] Forçando reinicialização do PnP para: {ControllerInstanceId}");
            
            if (_hidHideControl != null && IsAvailable)
            {
                _hidHideControl.IsActive = false; 
                _hidHideControl.RemoveBlockedInstanceId(ControllerInstanceId);
            }

            string escapedId = ControllerInstanceId.Replace("\\", "\\\\");
            
            using (var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_PnPEntity WHERE DeviceID='{escapedId}'"))
            {
                using var collection = searcher.Get();
                if (collection.Count == 0)
                {
                    HandheldLauncher.App.Diagnostics.DebugLog.Write("[CONTROLE-FIX] Dispositivo não encontrado na query WMI.");
                    return;
                }

                foreach (ManagementObject device in collection)
                {
                    device.InvokeMethod("Disable", null, null);
                }
            }

            Thread.Sleep(1000);

            using (var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_PnPEntity WHERE DeviceID='{escapedId}'"))
            {
                using var collection = searcher.Get();
                
                foreach (ManagementObject device in collection)
                {
                    device.InvokeMethod("Enable", null, null);
                }
            }

            Thread.Sleep(1500);

            HandheldLauncher.App.Diagnostics.DebugLog.Write("[CONTROLE-FIX] Reinicialização concluída.");
            
            if (_hidHideControl != null && IsAvailable)
            {
                _hidHideControl.AddBlockedInstanceId(ControllerInstanceId);
                _hidHideControl.IsActive = true;
            }
        }
        catch (Exception ex)
        {
            HandheldLauncher.App.Diagnostics.DebugLog.Write($"[CONTROLE-FIX] ERRO ao reiniciar PnP: {ex.Message}");
        }
    }
}