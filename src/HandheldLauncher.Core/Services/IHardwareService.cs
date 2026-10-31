namespace HandheldLauncher.Core.Services;

public interface IHardwareService
{
    void SetTdpMode(string mode);
    void SetColorMode(string mode);
    void SetVolume(double level);
    void SetBrightness(double level);
    void SetPerformanceHudEnabled(bool enabled);
    string GetDisplayInfo();
    
    // 🟢 NOVO: Mudar resolução e Hz
    void SetDisplayMode(int width, int height, int refreshRate);
}