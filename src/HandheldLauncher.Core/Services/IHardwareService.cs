namespace HandheldLauncher.Core.Services;

public interface IHardwareService
{
    bool SetTdpMode(string mode);
    bool SetColorMode(string mode);
    void SetVolume(double level);
    void SetBrightness(double level);
    void SetPerformanceHudEnabled(bool enabled);
    string GetDisplayInfo();
    void SetDisplayMode(int width, int height, int refreshRate);
}