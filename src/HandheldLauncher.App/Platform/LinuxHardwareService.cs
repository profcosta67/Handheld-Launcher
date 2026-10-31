using HandheldLauncher.Core.Services;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

public class LinuxHardwareService : IHardwareService
{
    public void SetTdpMode(string mode) => DebugLog.Write($"[LINUX] TDP: {mode}");
    public void SetVolume(double level) => DebugLog.Write($"[LINUX] Volume: {level}");
    public void SetBrightness(double level) => DebugLog.Write($"[LINUX] Brilho: {level}");
    public void SetColorMode(string mode) => DebugLog.Write($"[LINUX] Cor: {mode}");
    public void SetPerformanceHudEnabled(bool enabled) => DebugLog.Write($"[LINUX] HUD: {enabled}");
    public string GetDisplayInfo() => "Linux";
    
    // 🟢 FALTava esse método para a interface IHardwareService
    public void SetDisplayMode(int width, int height, int refreshRate) 
        => DebugLog.Write($"[LINUX] Tela: {width}x{height}@{refreshRate}Hz");
}