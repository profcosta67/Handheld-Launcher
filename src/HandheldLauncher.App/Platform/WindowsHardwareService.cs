using System;
using System.Diagnostics;
using HandheldLauncher.Core.Services;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

public class WindowsHardwareService : IHardwareService
{
    public void SetTdpMode(string mode)
    {
        DebugLog.Write($"[WINDOWS] TDP solicitado: {mode}. (Aguardando integração RyzenAdj)");
    }

    public void SetColorMode(string mode)
    {
        DebugLog.Write($"[WINDOWS] Aplicando cor: {mode}");
        try 
        {
            // Converte a string do Overlay para o índice que o DisplayColorManager espera
            int colorIndex = mode switch
            {
                "Padrão" => 0,
                "Vívido" => 1,
                "OLED (Alto Contraste)" => 2,
                "Noturno (Luz Quente)" => 3,
                _ => 0
            };

            DisplayColorManager.Apply(colorIndex);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[WINDOWS] Erro ao aplicar cor: {ex.Message}");
        }
    }

    public void SetVolume(double level)
    {
        try
        {
            int steps = (int)(level / 2);
            string script = $"$obj = New-Object -ComObject WScript.Shell; 1..100 | ForEach-Object {{ $obj.SendKeys([char]174) }}; 1..{steps} | ForEach-Object {{ $obj.SendKeys([char]175) }}";
            Process.Start(new ProcessStartInfo { FileName = "powershell", Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"", CreateNoWindow = true, UseShellExecute = false });
        }
        catch (Exception ex) { DebugLog.Write($"[WINDOWS] Erro volume: {ex.Message}"); }
    }

    public void SetBrightness(double level)
    {
        try
        {
            string script = $"(Get-WmiObject -Namespace root/WMI -Class WmiMonitorBrightnessMethods).WmiSetBrightness(1, {(int)level})";
            Process.Start(new ProcessStartInfo { FileName = "powershell", Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"", CreateNoWindow = true, UseShellExecute = false });
        }
        catch (Exception ex) { DebugLog.Write($"[WINDOWS] Erro brilho: {ex.Message}"); }
    }

    public void SetPerformanceHudEnabled(bool enabled) => DebugLog.Write($"[WINDOWS] HUD: {enabled}");
}