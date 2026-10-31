using System;
using System.Diagnostics;
using HandheldLauncher.Core.Services;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

public class LinuxHardwareService : IHardwareService
{
    public void SetTdpMode(string mode) => DebugLog.Write($"[LINUX] TDP: {mode}");
    public void SetColorMode(string mode) => DebugLog.Write($"[LINUX] Cor: {mode}");
    public void SetVolume(double level) => ExecuteCommand($"amixer -D pulse sset Master {level}%");
    public void SetBrightness(double level) => ExecuteCommand($"brightnessctl set {level}%");
    public void SetPerformanceHudEnabled(bool enabled) => DebugLog.Write($"[LINUX] HUD: {enabled}");

    private void ExecuteCommand(string command)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "/bin/bash", Arguments = $"-c \"{command}\"", RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception ex) { DebugLog.Write($"[LINUX] Erro: {ex.Message}"); }
    }
}