namespace HandheldLauncher.Core.Models;

public class AppSettings
{
    public int Theme { get; set; } = 0;
    public int LayoutTheme { get; set; } = 0;
    public int UiFont { get; set; } = 0;
    public int UiFontSize { get; set; } = 14;
    public int TileSize { get; set; } = 1;
    public bool VerticalLayout { get; set; } = false;
    public bool Animations { get; set; } = true;
    public bool SoundEffects { get; set; } = true;
    public int SoundTheme { get; set; } = 0;
    public int SoundVolume { get; set; } = 50;
    public string SteamGridDbApiKey { get; set; } = string.Empty;
    public string SteamWebApiKey { get; set; } = string.Empty;
    public string SteamId64 { get; set; } = string.Empty;
    public List<string> XboxScanFolders { get; set; } = new();
    public string EdenExecutablePath { get; set; } = string.Empty;
    public List<string> EdenRomFolders { get; set; } = new();
    public bool KioskMode { get; set; } = false;
    public bool AutoRestart { get; set; } = false;
    public bool ModeChosen { get; set; } = false;
    public int ColorMode { get; set; } = 0;
    public int OverlayPreset { get; set; } = 0;

    // 🟢 AS 3 PROPRIEDADES NOVAS (com os tipos corretos):
    public string TdpMode { get; set; } = "Normal";
    public int Volume { get; set; } = 70;
    public int Brightness { get; set; } = 80;
}