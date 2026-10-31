using System;
using System.Diagnostics;
using System.IO;
using HandheldLauncher.Core.Services;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

public class WindowsHardwareService : IHardwareService
{
    private readonly string _ryzenAdjPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ryzenadj.exe");

    public void SetTdpMode(string mode)
    {
        if (!OperatingSystem.IsWindows()) return;

        // 🟢 Novos limites de TDP em miliwatts (mW) com margem de segurança
        int tdpLimit = mode switch
        {
            "Eco" => 8000,         // 8W
            "Normal" => 15000,     // 15W
            "Desempenho" => 25000, // 25W
            "Boost" => 30000,      // 30W (Teto seguro para AMD)
            _ => 15000
        };

        // 🟢 ADICIONADO: A trava de segurança para 90 graus!
        string arguments = $"--stapm-limit={tdpLimit} --fast-limit={tdpLimit} --slow-limit={tdpLimit} --tctl-temp=90";

        try
        {
            DebugLog.Write($"[WINDOWS TDP] Executando RyzenAdj: {arguments}");
            
            var psi = new ProcessStartInfo
            {
                FileName = _ryzenAdjPath,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                // Lê as saídas antes do WaitForExit para evitar deadlock
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit(2000); 
                
                DebugLog.Write($"[WINDOWS TDP] Saída: {output.Trim()} | Erro: {error.Trim()}");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[WINDOWS TDP] Erro ao chamar RyzenAdj: {ex.Message}");
        }
    }

    /// <summary>
    /// Cria a Tarefa Agendada no Windows para iniciar o Launcher como Admin sem UAC.
    /// </summary>
    public void RegisterAutoStartTask()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

            string taskName = "HandheldLauncher_Startup";
            
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Create /TN \"{taskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var proc = Process.Start(psi);
            proc?.WaitForExit();
            
            DebugLog.Write($"[AUTOSTART] Tarefa criada com código {proc?.ExitCode}");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[AUTOSTART] Erro: {ex.Message}");
        }
    }

    public void SetVolume(double level)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device));
            var iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, CLSCTX.ALL, IntPtr.Zero, out var endpointObject));
            var endpoint = (IAudioEndpointVolume)endpointObject;
            Marshal.ThrowExceptionForHR(endpoint.SetMasterVolumeLevelScalar((float)Math.Clamp(level / 100.0, 0, 1), Guid.Empty));
            DebugLog.Write($"[WINDOWS] Volume ajustado para {level:0}%.");
            Marshal.ReleaseComObject(endpoint);
            Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
        catch (Exception ex) { DebugLog.Write($"[WINDOWS] Falha ao ajustar volume: {ex.Message}"); }
    }

    public void SetBrightness(double level)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var brightness = (byte)Math.Clamp(Math.Round(level), 0, 100);
            using var searcher = new System.Management.ManagementObjectSearcher(
                "root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (System.Management.ManagementObject monitor in searcher.Get())
            {
                monitor.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, brightness });
                monitor.Dispose();
            }
            DebugLog.Write($"[WINDOWS] Solicitação de brilho: {brightness}%.");
        }
        catch (Exception ex) { DebugLog.Write($"[WINDOWS] Falha ao ajustar brilho (monitor pode não aceitar WMI): {ex.Message}"); }
    }

    public void SetColorMode(string mode)
    {
        if (!OperatingSystem.IsWindows()) return;
        var index = mode switch
        {
            "Vívido" => 1,
            "OLED (Alto Contraste)" or "OLED (contraste alto)" => 2,
            "Noturno (Luz Quente)" => 3,
            "Confortável" => 4,
            _ => 0
        };
        DisplayColorManager.Apply(index);
        DebugLog.Write($"[WINDOWS] Modo de cor aplicado: {mode}.");
    }

    public void SetPerformanceHudEnabled(bool enabled)
    {
        // O HUD é controlado pelo MainViewModel, que possui a instância real
        // de PerformanceHud. Mantido na interface por compatibilidade.
        DebugLog.Write($"[WINDOWS] HUD deve ser controlado pelo MainViewModel: {enabled}.");
    }

    private enum EDataFlow { Render = 0, Capture = 1, All = 2 }
    private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }
    private static class CLSCTX { public const int ALL = 23; }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int classContext, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out uint channelCount);
        int SetMasterVolumeLevel(float levelDb, Guid eventContext);
        int SetMasterVolumeLevelScalar(float level, Guid eventContext);
    }
}