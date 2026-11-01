using HandheldLauncher.Core.Services;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

public class MockHardwareService : IHardwareService
{
    public bool SetTdpMode(string mode) { DebugLog.Write($"[HARDWARE MOCK] TDP: {mode}"); return true; }
    public bool SetColorMode(string mode) { DebugLog.Write($"[HARDWARE MOCK] Cor: {mode}"); return true; }
    public void SetVolume(double level) => DebugLog.Write($"[HARDWARE MOCK] Volume: {level}%");
    public void SetBrightness(double level) => DebugLog.Write($"[HARDWARE MOCK] Brilho: {level}%");
    public void SetPerformanceHudEnabled(bool enabled) => DebugLog.Write($"[HARDWARE MOCK] HUD: {enabled}");
    public string GetDisplayInfo() => "1920x1080 60Hz";
    public void SetDisplayMode(int width, int height, int refreshRate)
        => DebugLog.Write($"[HARDWARE MOCK] Tela: {width}x{height}@{refreshRate}Hz");
}