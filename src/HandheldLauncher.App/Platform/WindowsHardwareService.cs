using System;
using System.Diagnostics;
using HandheldLauncher.Core.Services;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

public class WindowsHardwareService : IHardwareService
{
    public void SetTdpMode(string mode) => DebugLog.Write($"[WINDOWS] TDP: {mode}");
    public void SetColorMode(string mode) => DebugLog.Write($"[WINDOWS] Cor: {mode}");

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