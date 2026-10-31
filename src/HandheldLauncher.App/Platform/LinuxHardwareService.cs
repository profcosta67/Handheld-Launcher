using HandheldLauncher.Core.Services;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

public class LinuxHardwareService : IHardwareService
{
    public void SetTdpMode(string mode) => DebugLog.Write($"[LINUX] TDP não implementado: {mode}");
    public void SetVolume(double level) => DebugLog.Write($"[LINUX] Volume não implementado: {level}");
    public void SetBrightness(double level) => DebugLog.Write($"[LINUX] Brilho não implementado: {level}");
    public void SetColorMode(string mode) => DebugLog.Write($"[LINUX] Cor não implementado: {mode}");
    public void SetPerformanceHudEnabled(bool enabled) => DebugLog.Write($"[LINUX] HUD não implementado: {enabled}");
}