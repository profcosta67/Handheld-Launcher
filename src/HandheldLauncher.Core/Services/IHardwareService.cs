namespace HandheldLauncher.Core.Services;

public interface IHardwareService
{
    // TDP: "Eco", "Equilibrado", "Turbo"
    void SetTdpMode(string mode);
    
    // Modo de Cor: "Padrão", "Vívido", etc.
    void SetColorMode(string mode);
    
    // Volume: valor de 0.0 a 1.0 (ou 0 a 100)
    void SetVolume(double level);
    
    // Brilho: valor de 0.0 a 1.0 (ou 0 a 100)
    void SetBrightness(double level);
    
    // HUD
    void SetPerformanceHudEnabled(bool enabled);
}