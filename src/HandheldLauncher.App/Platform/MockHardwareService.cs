using HandheldLauncher.Core.Services;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

public class MockHardwareService : IHardwareService
{
    public void SetTdpMode(string mode) => DebugLog.Write($"[HARDWARE MOCK] TDP: {mode}");
    public void SetColorMode(string mode) => DebugLog.Write($"[HARDWARE MOCK] Cor: {mode}");
    public void SetVolume(double level) => DebugLog.Write($"[HARDWARE MOCK] Volume: {level}%");
    public void SetBrightness(double level) => DebugLog.Write($"[HARDWARE MOCK] Brilho: {level}%");
    public void SetPerformanceHudEnabled(bool enabled) => DebugLog.Write($"[HARDWARE MOCK] HUD: {enabled}");
}