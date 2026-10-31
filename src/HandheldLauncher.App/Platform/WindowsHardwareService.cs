using System;
using System.Diagnostics;
using System.IO;
using System.Management; // Necessário para o Brilho (WMI)
using System.Runtime.InteropServices; // Necessário para o Volume (COM)
using HandheldLauncher.Core.Services;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Platform; // Para achar o DisplayColorManager

namespace HandheldLauncher.App.Platform;

public class WindowsHardwareService : IHardwareService
{
    private readonly string _ryzenAdjPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ryzenadj.exe");

    public void SetTdpMode(string mode)
    {
        if (!OperatingSystem.IsWindows()) return;

        int tdpLimit = mode switch
        {
            "Eco" => 8000,
            "Normal" => 15000,
            "Desempenho" => 25000,
            "Boost" => 30000,
            _ => 15000
        };

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
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit(2000);
                DebugLog.Write($"[WINDOWS TDP] Resultado: {output.Trim()} | Erro: {error.Trim()}");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[WINDOWS TDP] Erro: {ex.Message}");
        }
    }
        public void SetVolume(double level)
    {
        if (!OperatingSystem.IsWindows()) return;
        
        try
        {
            // Usa a API nativa do Windows para volume
            var volume = Math.Clamp(level, 0, 100);
            // 🟢 CORREÇÃO: Cast para float
            var result = VolumeMixer.SetMasterVolume((float)(volume / 100.0));
            DebugLog.Write($"[WINDOWS] Volume ajustado para {level}% (Sucesso: {result})");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[WINDOWS] Erro ao ajustar volume: {ex.Message}");
        }
    }
    public void SetBrightness(double level)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var scope = new ManagementScope("root\\WMI");
            scope.Connect();
            var query = new ObjectQuery("SELECT * FROM WmiMonitorBrightnessMethods");
            using var searcher = new ManagementObjectSearcher(scope, query);
            using var collection = searcher.Get();
            
            foreach (ManagementObject mo in collection)
            {
                var args = mo.GetMethodParameters("WmiSetBrightness");
                args["Timeout"] = 1;
                args["Brightness"] = (byte)Math.Clamp(level, 0, 100);
                mo.InvokeMethod("WmiSetBrightness", args, null);
            }
            DebugLog.Write($"[WINDOWS] Brilho ajustado para {level}%");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[WINDOWS] Erro ao ajustar brilho: {ex.Message}");
        }
    }

    public void SetColorMode(string mode)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            int index = mode switch
            {
                "Vívido" => 1,
                "OLED (Alto Contraste)" => 2,
                "Noturno (Luz Quente)" => 3,
                _ => 0
            };
            
            DisplayColorManager.Apply(index);
            DebugLog.Write($"[WINDOWS] Modo de cor alterado para {mode} (índice {index})");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[WINDOWS] Erro ao alterar cor: {ex.Message}");
        }
    }

    public void SetPerformanceHudEnabled(bool enabled)
    {
        DebugLog.Write($"[WINDOWS] HUD: {enabled}");
    }
}

// 🟢 CLASSE AUXILIAR PARA VOLUME (Core Audio API via COM)
internal static class VolumeMixer
{
    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int NotImpl1();
        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int NotImpl1();
        int NotImpl2();
        [PreserveSig]
        int SetMasterVolumeLevelScalar(float fLevel, Guid pguidEventContext);
    }

    public static bool SetMasterVolume(float level)
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.GetDefaultAudioEndpoint(0, 1, out var device); // 0=render, 1=multimedia
            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, 23, IntPtr.Zero, out var obj);
            var volume = (IAudioEndpointVolume)obj;
            volume.SetMasterVolumeLevelScalar(level, Guid.Empty);
            return true;
        }
        catch
        {
            return false;
        }
    }
}