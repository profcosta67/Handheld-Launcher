using HandheldLauncher.Core.Services;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

public class LinuxHardwareService : IHardwareService
{
    public bool SetTdpMode(string mode) { DebugLog.Write($"[LINUX] TDP indisponível neste provider: {mode}"); return false; }
    public void SetVolume(double level) => DebugLog.Write($"[LINUX] Volume: {level}");
    public void SetBrightness(double level) => DebugLog.Write($"[LINUX] Brilho: {level}");
    public bool SetColorMode(string mode) { DebugLog.Write($"[LINUX] Cor indisponível neste provider: {mode}"); return false; }
    public void SetPerformanceHudEnabled(bool enabled) => DebugLog.Write($"[LINUX] HUD: {enabled}");
    public string GetDisplayInfo() => "Linux";
    public void SetDisplayMode(int width, int height, int refreshRate)
        => DebugLog.Write($"[LINUX] Tela: {width}x{height}@{refreshRate}Hz");
}