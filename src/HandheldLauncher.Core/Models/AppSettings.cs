namespace HandheldLauncher.Core.Models;

public class AppSettings
{
    public bool HwInfoDisabled { get; set; }
      // 🟢 NOVO: Substitui o FreezeGameOnOverlay e o GameModeEnabled
    public bool IsolateControllerOnOverlay { get; set; } = true;
   public bool GameModeEnabled { get; set; } = true;
    public string RawgApiKey { get; set; } = string.Empty;
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
    public string? IgdbClientId { get; set; }
    public string? IgdbClientSecret { get; set; }
    public List<string> XboxScanFolders { get; set; } = new();
    public string EdenExecutablePath { get; set; } = string.Empty;
    public List<string> EdenRomFolders { get; set; } = new();
    // v0.16.2: padrão Handheld (tela cheia) — pedido do usuário. Só vale
    // pra quem ainda não tem essa chave salva (instalação nova); quem já
    // escolheu Desktop/Handheld mantém a escolha. AutoRestart continua
    // false: a reabertura automática (tarefa agendada) só é criada quando
    // o usuário salva o modo Handheld nas Configurações, então uma
    // instalação nova abre em tela cheia sem ficar presa num loop de
    // reabertura. O InstallTask.ps1 do instalador já fazia o mesmo.
    public bool KioskMode { get; set; } = true;
    public bool AutoRestart { get; set; } = false;
    public bool ModeChosen { get; set; } = false;
    public int ColorMode { get; set; } = 0;
    public int OverlayPreset { get; set; } = 0;

    // v0.16: opt-in pro canal beta do Velopack. Default false — ninguém sai
    // do canal estável sem pedir. Ver UpdateService.
    public bool BetaChannelOptIn { get; set; } = false;

    // v0.16: opt-in pra suspender (congelar) o processo do jogo enquanto o
    // Quick Settings está aberto. Default false — alguns anti-cheats (EAC,
    // BattlEye, Vanguard) podem marcar NtSuspendProcess como suspeito. Ver
    // InputBlockService.BlockGamepadForGame / OverlayWindow.
    public bool FreezeGameOnOverlay { get; set; } = false;

    // v0.16.4: o Quick Settings pega o foco enquanto está aberto, então o jogo
    // para de receber os botões (a maioria dos jogos ignora o controle sem
    // foco). Desligado por padrão: alguns jogos em tela cheia exclusiva
    // minimizam ao perder o foco — testar jogo por jogo.
    public bool FocusOverlayInGame { get; set; } = false;

    // TDP e outros
    public string TdpMode { get; set; } = "Normal";
    /// <summary>v0.18.5: último valor do TDP personalizado (W), lembrado mesmo depois de voltar pra um perfil.</summary>
    public int TdpCustomWatts { get; set; } = 20;
    public string FanMode { get; set; } = "Automático";
    public int Volume { get; set; } = 70;
    public int Brightness { get; set; } = 80;

    // Atalhos de Teclado
    public string KeyPrevFilter { get; set; } = "Q";
    public string KeyNextFilter { get; set; } = "E";
    public string KeyDownloads { get; set; } = "D";
    public string KeyContextMenu { get; set; } = "Space";
    public string KeyGlobalMenu { get; set; } = "Escape";
    public string KeyCloseGame { get; set; } = "Ctrl+W";
    public string KeyLaunchGame { get; set; } = "Enter";

    // 🦆 Lossless Scaling (Patinho)
    public string LosslessScalingPath { get; set; } = string.Empty;
    public int LosslessMultiplier { get; set; } = 2; // Padrão: 2x
}