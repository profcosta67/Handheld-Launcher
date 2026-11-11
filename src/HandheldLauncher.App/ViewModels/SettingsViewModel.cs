using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using HandheldLauncher.App.Platform;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.ViewModels;

public sealed class GitHubRelease
{
    public string TagName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

public sealed class SettingsViewModel : ViewModelBase
{
    private string _steamGridDbApiKey = string.Empty;
    public string SteamGridDbApiKey
    {
        get => _steamGridDbApiKey;
        set
        {
            if (SetField(ref _steamGridDbApiKey, value))
            {
                OnPropertyChanged(nameof(HasSteamGridDbKey));
                OnPropertyChanged(nameof(SteamGridDbStatusText));
            }
        }
    }

    public bool HasSteamGridDbKey => !string.IsNullOrWhiteSpace(_steamGridDbApiKey);
    public string SteamGridDbStatusText => HasSteamGridDbKey ? "configurado ✓" : "não configurado";

    private string _steamWebApiKey = string.Empty;
    public string SteamWebApiKey
    {
        get => _steamWebApiKey;
        set
        {
            if (SetField(ref _steamWebApiKey, value))
            {
                OnPropertyChanged(nameof(HasSteamWebApiKey));
                OnPropertyChanged(nameof(SteamWebApiStatusText));
            }
        }
    }

    public bool HasSteamWebApiKey => !string.IsNullOrWhiteSpace(_steamWebApiKey);
    public string SteamWebApiStatusText => HasSteamWebApiKey ? "configurado ✓" : "não configurado";

    // 🟢 IGDB (Twitch) — Client ID + Client Secret
    private string _igdbClientId = "";
    public string IgdbClientId
    {
        get => _igdbClientId;
        set
        {
            if (SetField(ref _igdbClientId, value))
                OnPropertyChanged(nameof(IgdbApiStatusText));
        }
    }

    private string _igdbClientSecret = "";
    public string IgdbClientSecret
    {
        get => _igdbClientSecret;
        set
        {
            if (SetField(ref _igdbClientSecret, value))
                OnPropertyChanged(nameof(IgdbApiStatusText));
        }
    }

    public string IgdbApiStatusText =>
        (!string.IsNullOrWhiteSpace(_igdbClientId) && !string.IsNullOrWhiteSpace(_igdbClientSecret))
            ? "configurado ✓"
            : "não configurado";

    private string _rawgApiKey = string.Empty;
    public string RawgApiKey
    {
        get => _rawgApiKey;
        set
        {
            if (SetField(ref _rawgApiKey, value))
            {
                OnPropertyChanged(nameof(HasRawgApiKey));
                OnPropertyChanged(nameof(RawgApiStatusText));
            }
        }
    }

    public bool HasRawgApiKey => !string.IsNullOrWhiteSpace(_rawgApiKey);
    public string RawgApiStatusText => HasRawgApiKey ? "configurado ✓" : "não configurado";

    private string _steamId64 = string.Empty;
    public string SteamId64
    {
        get => _steamId64;
        set
        {
            if (SetField(ref _steamId64, value))
            {
                OnPropertyChanged(nameof(HasSteamId64));
                OnPropertyChanged(nameof(SteamIdStatusText));
            }
        }
    }

    public bool HasSteamId64 => !string.IsNullOrWhiteSpace(_steamId64);

    private string? _steamPersonaName;
    public string SteamIdStatusText => !HasSteamId64
        ? "não configurado"
        : string.IsNullOrWhiteSpace(_steamPersonaName)
            ? "configurado ✓"
            : $"{_steamPersonaName} (configurado ✓)";

    private string _xboxScanFoldersText = string.Empty;
    public string XboxScanFoldersText
    {
        get => _xboxScanFoldersText;
        set => SetField(ref _xboxScanFoldersText, value);
    }

    private string _edenExecutablePath = string.Empty;
    public string EdenExecutablePath
    {
        get => _edenExecutablePath;
        set
        {
            if (SetField(ref _edenExecutablePath, value))
            {
                OnPropertyChanged(nameof(EdenStatusText));
            }
        }
    }

    public string EdenStatusText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_edenExecutablePath)) return "Não configurado — nenhum jogo do Eden vai aparecer na biblioteca.";
            try
            {
                return System.IO.File.Exists(_edenExecutablePath) ? "Encontrado ✓" : "Esse arquivo não existe — confira o caminho.";
            }
            catch { return "Caminho inválido — confira."; }
        }
    }

    private string _edenRomFoldersText = string.Empty;
    public string EdenRomFoldersText
    {
        get => _edenRomFoldersText;
        set => SetField(ref _edenRomFoldersText, value);
    }

    private string _edenScanResultText = string.Empty;
    public string EdenScanResultText
    {
        get => _edenScanResultText;
        set
        {
            if (SetField(ref _edenScanResultText, value))
            {
                OnPropertyChanged(nameof(HasEdenScanResult));
            }
        }
    }

    public bool HasEdenScanResult => !string.IsNullOrWhiteSpace(_edenScanResultText);

    private bool _isHandheldMode;
    public bool IsHandheldMode
    {
        get => _isHandheldMode;
        set
        {
            if (SetField(ref _isHandheldMode, value))
            {
                OnPropertyChanged(nameof(IsDesktopMode));
            }
        }
    }

    public bool IsDesktopMode
    {
        get => !_isHandheldMode;
        set => IsHandheldMode = !value;
    }

    private string _versionText = string.Empty;
    public string VersionText { get => _versionText; set => SetField(ref _versionText, value); }

    private bool _updatesAvailable;
    public bool UpdatesAvailable { get => _updatesAvailable; set => SetField(ref _updatesAvailable, value); }

    private string _updateResultText = string.Empty;
    public string UpdateResultText
    {
        get => _updateResultText;
        set
        {
            if (SetField(ref _updateResultText, value))
            {
                OnPropertyChanged(nameof(HasUpdateResult));
            }
        }
    }

    public bool HasUpdateResult => !string.IsNullOrEmpty(_updateResultText);

    private int _categoryIndex;
    public int CategoryIndex
    {
        get => _categoryIndex;
        set
        {
            if (SetField(ref _categoryIndex, value))
            {
                OnPropertyChanged(nameof(IsCategoryAppearanceActive));
                OnPropertyChanged(nameof(IsCategorySoundsActive));
                OnPropertyChanged(nameof(IsCategoryLibraryActive));
                OnPropertyChanged(nameof(IsCategoryModeActive));
                OnPropertyChanged(nameof(IsCategoryAccountsActive));
                OnPropertyChanged(nameof(IsCategoryShortcutsActive));
                OnPropertyChanged(nameof(IsCategoryLosslessActive));
                OnPropertyChanged(nameof(IsCategoryConnectivityActive));
                OnPropertyChanged(nameof(IsCategorySaveCenterActive));
                OnPropertyChanged(nameof(IsCategoryNewsActive));
                OnPropertyChanged(nameof(SaveButtonText));
            }
        }
    }

    public bool IsCategoryModeActive => _categoryIndex == 0;          // Geral
    public bool IsCategoryAppearanceActive => _categoryIndex == 1;    // Aparência
    public bool IsCategorySoundsActive => _categoryIndex == 2;        // Sons
    public bool IsCategoryLibraryActive => _categoryIndex == 3;       // Biblioteca
    public bool IsCategoryShortcutsActive => _categoryIndex == 4;     // Controles
    public bool IsCategoryLosslessActive => _categoryIndex == 5;      // Lossless
    public bool IsCategoryAccountsActive => _categoryIndex == 6;      // Contas
    public bool IsCategoryConnectivityActive => _categoryIndex == 7;  // Rede
    public bool IsCategorySaveCenterActive => _categoryIndex == SaveCenterCategoryIndex; // Central de Saves
    public bool IsCategoryNewsActive => _categoryIndex == 9;          // Atualizações

    /// <summary>Índice da aba "Central de Saves" (logo abaixo de Rede).</summary>
    public const int SaveCenterCategoryIndex = 8;

    public string SaveButtonText => "✕ Salvar e fechar";

    private int _fontIndex;
    public int FontIndex 
    { 
        get => _fontIndex; 
        set 
        {
            if (SetField(ref _fontIndex, value))
            {
                var font = HandheldLauncher.App.Platform.ThemeManager.Fonts[value];
                var family = !string.IsNullOrWhiteSpace(font.Family) ? font.Family : font.Name;
                HandheldLauncher.App.Platform.ThemeManager.ApplyFont(family!);
            }
        } 
    }

    private string _steamAccountText = string.Empty;
    public string SteamAccountText { get => _steamAccountText; set => SetField(ref _steamAccountText, value); }

    public void UseDetectedSteamAccount()
    {
        var account = SteamAccountDetector.FindMostRecent();
        if (account is null)
        {
            SteamAccountText = "Nenhuma conta Steam encontrada neste PC (abra e entre na Steam uma vez).";
            return;
        }

        _steamPersonaName = account.PersonaName;
        SteamId64 = account.SteamId64;
        SteamAccountText = $"Conta Steam deste PC: {account.PersonaName} ({account.SteamId64})";
        OnPropertyChanged(nameof(SteamIdStatusText));
    }

    private int _fontSizeIndex = 1;
    public int FontSizeIndex 
    { 
        get => _fontSizeIndex; 
        set 
        {
            if (SetField(ref _fontSizeIndex, value))
            {
                HandheldLauncher.App.Platform.ThemeManager.ApplyFontSize(value);
            }
        } 
    }

    private int _themeIndex;
    public int ThemeIndex 
    { 
        get => _themeIndex; 
        set 
        {
            if (SetField(ref _themeIndex, value))
            {
                HandheldLauncher.App.Platform.ThemeManager.Apply(value);
                OnPropertyChanged(nameof(IsCleanThemeActive)); 
            }
        } 
    }
    
    private int _layoutThemeIndex;
    public int LayoutThemeIndex
    {
        get => _layoutThemeIndex;
        set
        {
            if (SetField(ref _layoutThemeIndex, value))
            {
                OnPropertyChanged(nameof(LayoutThemeButtonText));
                OnPropertyChanged(nameof(IsCleanThemeActive));
                OnPropertyChanged(nameof(IsCartoonThemeActive));
                OnPropertyChanged(nameof(IsSoundStyleEnabled));
            }
        }
    }

    public string LayoutThemeButtonText
    {
        get
        {
            var names = HandheldLauncher.App.Platform.ThemeManager.LayoutThemeNames;
            var index = Math.Clamp(_layoutThemeIndex, 0, names.Length - 1);
            return $"{names[index]}  —  toque para trocar";
        }
    }

    private int _tileSizeIndex = 1;
    public int TileSizeIndex { get => _tileSizeIndex; set => SetField(ref _tileSizeIndex, value); }

    private int _layoutIndex;
    public int LayoutIndex
    {
        get => _layoutIndex;
        set
        {
            if (SetField(ref _layoutIndex, value))
            {
                // Clean é sempre fileira; Cartoon é sempre grade vertical (blocos).
                if ((IsCleanThemeActive && value == 1) || (IsCartoonThemeActive && value == 0))
                {
                    _layoutIndex = IsCartoonThemeActive ? 1 : 0;
                    OnPropertyChanged(nameof(LayoutIndex));
                }
            }
        }
    }

    public bool IsCleanThemeActive => LayoutThemeIndex == 1;

    /// <summary>
    /// v0.25.2: o Cartoon é uma identidade fechada (paleta 16/17, fonte Fredoka e
    /// som Bolha). Nas Configurações os combos de tema/fonte/som ficam
    /// desligados enquanto ele está ativo, e o SaveTo força os valores.
    /// </summary>
    public bool IsCartoonThemeActive => LayoutThemeIndex == 3;

    private int _cartoonVariantIndex;

    /// <summary>
    /// v0.25.2: a única personalização do Cartoon — versão escura (0) ou clara (1).
    /// Aplicada ao vivo, para se ver o resultado sem fechar as Configurações.
    /// </summary>
    public int CartoonVariantIndex
    {
        get => _cartoonVariantIndex;
        set
        {
            if (!SetField(ref _cartoonVariantIndex, Math.Clamp(value, 0, 1))) return;
            if (_isLoading || !IsCartoonThemeActive) return;

            var palette = _cartoonVariantIndex == 1
                ? HandheldLauncher.App.Platform.ThemeManager.ThemeCartoonLight
                : HandheldLauncher.App.Platform.ThemeManager.ThemeCartoon;
            HandheldLauncher.App.Platform.ThemeManager.Apply(palette);
            ThemeIndex = palette; // o combo de tema (desligado) mostra a versão ativa
        }
    }

    private bool _animations = true;
    public bool Animations { get => _animations; set => SetField(ref _animations, value); }

    private bool _soundEffects = true;
    public bool SoundEffects
    {
        get => _soundEffects;
        set
        {
            if (SetField(ref _soundEffects, value))
                OnPropertyChanged(nameof(IsSoundStyleEnabled));
        }
    }

    /// <summary>Estilo do som: só é escolhível sem o layout Cartoon e com efeitos ligados.</summary>
    public bool IsSoundStyleEnabled => SoundEffects && !IsCartoonThemeActive;

    private bool _betaChannelOptIn;
    public bool BetaChannelOptIn { get => _betaChannelOptIn; set => SetField(ref _betaChannelOptIn, value); }

    // 🟢 B7: Substitui o FreezeGameOnOverlay e o GameModeEnabled
    private bool _isolateControllerOnOverlay = true;
    public bool IsolateControllerOnOverlay { get => _isolateControllerOnOverlay; set => SetField(ref _isolateControllerOnOverlay, value); }


    /// <summary>
    /// v0.24.1: quando o driver recusa a configuração (sem administrador), o
    /// mascaramento é delegado ao processo elevado — o UAC aparece uma vez por
    /// abertura/fecho do Quick Settings. Desligado por padrão.
    /// </summary>
    private bool _hidHideElevateOnDemand;
    public bool HidHideElevateOnDemand { get => _hidHideElevateOnDemand; set => SetField(ref _hidHideElevateOnDemand, value); }

    /// <summary>"Reiniciar como administrador" só faz sentido no modo padrão.</summary>
    public bool CanRestartAsAdmin
        => OperatingSystem.IsWindows() && !HandheldLauncher.App.Platform.ElevationService.IsElevated;

    /// <summary>Estado do isolamento do controle, em texto, no cartão do HidHide.</summary>
    public string HidHideModeText => HandheldLauncher.App.Platform.ElevationService.IsElevated
        ? "Modo administrador: o controle sai da vista do jogo enquanto o Quick Settings está aberto."
        : "Modo padrão (sem administrador): o mascaramento NÃO tem efeito. Usa \"Reiniciar como administrador\" — ou liga a opção de pedir permissão.";

    /// <summary>
    /// v0.24.1: congelar o processo do jogo enquanto o Quick Settings está aberto.
    ///
    /// <para>É o único mecanismo que tira o input a um jogo **já aberto** — o
    /// HidHide decide quem vê o dispositivo no momento em que a aplicação abre
    /// o dispositivo, por isso não consegue revogar um handle já aberto. Em
    /// troca, suspender o processo pode ser interpretado por anti-cheats de
    /// jogos competitivos como manipulação: por isso é opt-in, com aviso.</para>
    /// </summary>
    private bool _freezeGameOnOverlay;
    public bool FreezeGameOnOverlay
    {
        get => _freezeGameOnOverlay;
        set
        {
            if (!SetField(ref _freezeGameOnOverlay, value)) return;
            OnPropertyChanged(nameof(FreezeButtonText));
            OnPropertyChanged(nameof(FreezeStatusText));
        }
    }

    /// <summary>Aviso de risco aberto (só ativa depois do "Aceito").</summary>
    private bool _isFreezeConfirmOpen;
    public bool IsFreezeConfirmOpen { get => _isFreezeConfirmOpen; set => SetField(ref _isFreezeConfirmOpen, value); }

    public string FreezeButtonText => FreezeGameOnOverlay ? "Desligar congelamento" : "Ativar congelamento…";

    public string FreezeStatusText => FreezeGameOnOverlay
        ? "Congelamento LIGADO: o jogo fica parado enquanto o Quick Settings está aberto."
        : "Congelamento DESLIGADO.";

    private bool _isLoading;

    private int _soundThemeIndex;
    public int SoundThemeIndex
    {
        get => _soundThemeIndex;
        set
        {
            if (SetField(ref _soundThemeIndex, value) && !_isLoading)
                UiSounds.Preview(_soundThemeIndex, (int)Math.Round(_soundVolume), _soundEffects);
        }
    }

    private double _soundVolume = 70;
    public double SoundVolume
    {
        get => _soundVolume;
        set
        {
            if (SetField(ref _soundVolume, value) && !_isLoading)
                UiSounds.Preview(_soundThemeIndex, (int)Math.Round(_soundVolume), _soundEffects);
        }
    }

    private int _colorModeIndex;
    public int ColorModeIndex { get => _colorModeIndex; set => SetField(ref _colorModeIndex, value); }

    // ===== Atalhos de Teclado =====
    private string _keyPrevFilter = "Q";
    public string KeyPrevFilter { get => _keyPrevFilter; set => SetField(ref _keyPrevFilter, value); }

    private string _keyNextFilter = "E";
    public string KeyNextFilter { get => _keyNextFilter; set => SetField(ref _keyNextFilter, value); }

    private string _keyDownloads = "D";
    public string KeyDownloads { get => _keyDownloads; set => SetField(ref _keyDownloads, value); }

    private string _keyContextMenu = "Space";
    public string KeyContextMenu { get => _keyContextMenu; set => SetField(ref _keyContextMenu, value); }

    private string _keyGlobalMenu = "Escape";
    public string KeyGlobalMenu { get => _keyGlobalMenu; set => SetField(ref _keyGlobalMenu, value); }

    private string _keyCloseGame = "Ctrl+W";
    public string KeyCloseGame { get => _keyCloseGame; set => SetField(ref _keyCloseGame, value); }

    private string _keyLaunchGame = "Enter";
    public string KeyLaunchGame { get => _keyLaunchGame; set => SetField(ref _keyLaunchGame, value); }

    private string _losslessScalingPath = string.Empty;
    public string LosslessScalingPath
    {
        get => _losslessScalingPath;
        set => SetField(ref _losslessScalingPath, value);
    }

    // ===== Notas de Versão (Release Notes) =====
    public ObservableCollection<GitHubRelease> ReleaseHistory { get; } = new();
    private bool _isLoadingReleases;
    public bool IsLoadingReleases { get => _isLoadingReleases; set => SetField(ref _isLoadingReleases, value); }

    public async Task LoadReleaseNotesAsync()
    {
        if (ReleaseHistory.Count > 0 || IsLoadingReleases) return;
        
        IsLoadingReleases = true;
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Handheld-Launcher");
            
            var response = await client.GetAsync("https://api.github.com/repos/profcosta67/Handheld-Launcher/releases?per_page=10");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                if (releases != null)
                {
                    foreach (var r in releases)
                    {
                        var cleanBody = r.Body?.Replace("### ", "").Replace("## ", "").Replace("**", "").Replace("- ", "• ") ?? "";
                        r.Body = cleanBody;
                        ReleaseHistory.Add(r);
                    }
                }
            }
        }
        catch { }
        finally
        {
            IsLoadingReleases = false;
        }
    }

    private string? _savedMessage;
    public string? SavedMessage
    {
        get => _savedMessage;
        private set
        {
            if (SetField(ref _savedMessage, value))
            {
                OnPropertyChanged(nameof(HasSavedMessage));
                OnPropertyChanged(nameof(FormEnabled));
            }
        }
    }

    public bool HasSavedMessage => !string.IsNullOrEmpty(SavedMessage);
    public bool FormEnabled => !HasSavedMessage;

    public void LoadFrom(AppSettings settings)
    {
        _isLoading = true;
        try { LoadFromCore(settings); }
        finally { _isLoading = false; }
    }

    private void LoadFromCore(AppSettings settings)
    {
        SteamGridDbApiKey = settings.SteamGridDbApiKey ?? string.Empty;
        SteamWebApiKey = settings.SteamWebApiKey ?? string.Empty;
        RawgApiKey = settings.RawgApiKey ?? string.Empty;
        IgdbClientId = settings.IgdbClientId ?? "";
        IgdbClientSecret = settings.IgdbClientSecret ?? "";
        SyncthingEnabled = settings.SyncthingEnabled;

        var detected = SteamAccountDetector.FindMostRecent();
        _steamPersonaName = detected?.PersonaName;
        SteamId64 = settings.SteamId64 ?? string.Empty;
        XboxScanFoldersText = string.Join(Environment.NewLine, settings.XboxScanFolders);
        EdenExecutablePath = settings.EdenExecutablePath ?? string.Empty;
        EdenRomFoldersText = string.Join(Environment.NewLine, settings.EdenRomFolders);
        IsHandheldMode = settings.KioskMode;

        SteamAccountText = detected is null
            ? "Nenhuma conta Steam encontrada neste PC."
            : $"Conta Steam deste PC: {detected.PersonaName} ({detected.SteamId64})";
        CategoryIndex = 0;
        ThemeIndex = Math.Clamp(settings.Theme, 0, HandheldLauncher.App.Platform.ThemeManager.ThemeNames.Length - 1);
        LayoutThemeIndex = Math.Clamp(settings.LayoutTheme, 0, HandheldLauncher.App.Platform.ThemeManager.LayoutThemeNames.Length - 1);
        FontIndex = Math.Clamp(settings.UiFont, 0, HandheldLauncher.App.Platform.ThemeManager.Fonts.Length - 1);
        FontSizeIndex = Math.Clamp(settings.UiFontSize, 0, 2);
        TileSizeIndex = Math.Clamp(settings.TileSize, 0, 2);
        LayoutIndex = LayoutThemeIndex == 3 ? 1 : (settings.VerticalLayout ? 1 : 0);
        Animations = settings.Animations;
        SoundEffects = settings.SoundEffects;
        BetaChannelOptIn = settings.BetaChannelOptIn;
        
        // 🟢 NOVO: Carrega o estado do isolamento
        IsolateControllerOnOverlay = settings.IsolateControllerOnOverlay;
        HidHideElevateOnDemand = settings.HidHideElevateOnDemand;
        FreezeGameOnOverlay = settings.FreezeGameOnOverlay;
        
        SoundThemeIndex = Math.Clamp(settings.SoundTheme, 0, HandheldLauncher.App.Platform.UiSounds.PackNames.Length - 1);
        SoundVolume = Math.Clamp(settings.SoundVolume, 0, 100);
        ColorModeIndex = Math.Clamp(settings.ColorMode, 0, HandheldLauncher.App.Platform.DisplayColorManager.ModeNames.Length - 1);

        // v0.25.2: com o layout Cartoon ativo, os combos desligados mostram os
        // valores que o tema força — e não o que ficou salvo de outro layout.
        if (LayoutThemeIndex == 3)
        {
            CartoonVariantIndex = settings.CartoonLightMode ? 1 : 0;
            ThemeIndex = settings.CartoonLightMode
                ? HandheldLauncher.App.Platform.ThemeManager.ThemeCartoonLight
                : HandheldLauncher.App.Platform.ThemeManager.ThemeCartoon;
            FontIndex = HandheldLauncher.App.Platform.ThemeManager.FontFredoka;
            SoundThemeIndex = HandheldLauncher.App.Platform.ThemeManager.SoundBolha;
            LayoutIndex = 1;
        }

        KeyPrevFilter = settings.KeyPrevFilter;
        KeyNextFilter = settings.KeyNextFilter;
        KeyDownloads = settings.KeyDownloads;
        KeyContextMenu = settings.KeyContextMenu;
        KeyGlobalMenu = settings.KeyGlobalMenu;
        KeyCloseGame = settings.KeyCloseGame;
        KeyLaunchGame = settings.KeyLaunchGame;

        LosslessScalingPath = settings.LosslessScalingPath ?? string.Empty;

        SavedMessage = null;
    }

    private readonly HandheldLauncher.Services.Syncthing.SyncthingService? _syncthingService;

    public SettingsViewModel(HandheldLauncher.Services.Syncthing.SyncthingService? syncthingService = null)
    {
        _syncthingService = syncthingService;
        OpenSyncthingCommand = new RelayCommand(() => _ = OpenSyncthingAsync());
        CloseSyncthingCommand = new RelayCommand(() => IsSyncthingOpen = false);
    }

    private bool _syncthingEnabled;
    public bool SyncthingEnabled
    {
        get => _syncthingEnabled;
        set
        {
            if (SetField(ref _syncthingEnabled, value))
            {
                if (value) _syncthingService?.Start();
                else _syncthingService?.Stop();
            }
        }
    }

    private bool _isSyncthingOpen;
    /// <summary>True enquanto a tela cheia com o WebView do Syncthing está aberta.</summary>
    public bool IsSyncthingOpen
    {
        get => _isSyncthingOpen;
        private set => SetField(ref _isSyncthingOpen, value);
    }

    private string _syncthingStatusText = string.Empty;
    /// <summary>Mensagem curta do card (iniciando/erro). Fica vazia quando está tudo certo.</summary>
    public string SyncthingStatusText
    {
        get => _syncthingStatusText;
        private set
        {
            if (SetField(ref _syncthingStatusText, value))
                OnPropertyChanged(nameof(HasSyncthingStatus));
        }
    }

    public bool HasSyncthingStatus => !string.IsNullOrEmpty(_syncthingStatusText);

    /// <summary>Botão "Abrir Syncthing": sobe o serviço (se preciso) e abre a aba Rede com o WebView.</summary>
    public RelayCommand OpenSyncthingCommand { get; }

    /// <summary>Botão "Voltar" da tela do WebView.</summary>
    public RelayCommand CloseSyncthingCommand { get; }

    /// <summary>
    /// Leva para a aba Rede, garante o Syncthing no ar e só então mostra o
    /// WebView (que aponta para a GUI local). Se já estiver rodando, apenas
    /// mostra a tela — nada é reiniciado.
    /// </summary>
    public async Task OpenSyncthingAsync()
    {
        CategoryIndex = SaveCenterCategoryIndex; // aba Central de Saves

        // No Linux o WebView nativo depende do runtime WPE WebKit instalado no
        // sistema; sem isso a janela quebraria, então mantém o navegador externo.
        if (!OperatingSystem.IsWindows())
        {
            OpenSyncthingInBrowser();
            return;
        }

        // O controle nativo usa o runtime do WebView2 (vem junto com o Edge).
        // Sem ele o controle falharia ao ser criado — aí cai no navegador.
        if (!IsWebView2RuntimeInstalled())
        {
            SyncthingStatusText = "Runtime do WebView2 não encontrado: abrindo no navegador.";
            OpenSyncthingInBrowser();
            return;
        }

        SyncthingStatusText = "Iniciando o Syncthing…";
        if (!await EnsureSyncthingReadyAsync()) return;

        SyncthingEnabled = true;   // mantém o toggle coerente com o processo em execução
        IsSyncthingOpen = true;
    }

    /// <summary>
    /// Garante o Syncthing no ar e cuida da mensagem de estado. Usado pelo botão
    /// da aba e pelo toggle do topo da tela de Saves.
    /// </summary>
    public async Task<bool> EnsureSyncthingReadyAsync()
    {
        if (_syncthingService is null)
        {
            SyncthingStatusText = "Serviço do Syncthing indisponível nesta instância.";
            return false;
        }

        if (!_syncthingService.IsAvailable)
        {
            SyncthingStatusText = "syncthing.exe não encontrado na pasta Tools do launcher.";
            return false;
        }

        var ready = await _syncthingService.EnsureRunningAsync(TimeSpan.FromSeconds(20));
        SyncthingStatusText = ready
            ? string.Empty
            : "O Syncthing não respondeu em 127.0.0.1:8384 (veja debug.log).";
        return ready;
    }

    /// <summary>Liga/desliga a sincronização pelo toggle do topo da tela de Saves.</summary>
    public async Task SetSyncthingEnabledAsync(bool enabled)
    {
        SyncthingEnabled = enabled; // o setter já dispara Start()/Stop()
        if (enabled) await EnsureSyncthingReadyAsync();
    }

    /// <summary>Fallback: abre a interface web no navegador padrão.</summary>
    public void OpenSyncthingInBrowser()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = HandheldLauncher.Services.Syncthing.SyncthingService.BaseUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SyncthingStatusText = $"Não foi possível abrir o navegador: {ex.Message}";
        }
    }

    // O runtime do WebView2 (Evergreen) é instalado junto com o Edge: checamos as
    // duas pastas padrão antes de deixar o controle nativo ser criado.
    private static bool IsWebView2RuntimeInstalled()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft", "EdgeWebView", "Application"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "EdgeWebView", "Application"),
        };

        return candidates.Any(Directory.Exists);
    }

    public void SaveTo(AppSettings settings)
    {
        settings.SteamGridDbApiKey = string.IsNullOrWhiteSpace(SteamGridDbApiKey) ? null : SteamGridDbApiKey.Trim();
        settings.SteamWebApiKey = string.IsNullOrWhiteSpace(SteamWebApiKey) ? null : SteamWebApiKey.Trim();
        settings.RawgApiKey = string.IsNullOrWhiteSpace(RawgApiKey) ? string.Empty : RawgApiKey.Trim();
        settings.IgdbClientId = IgdbClientId;
        settings.IgdbClientSecret = IgdbClientSecret;
        settings.SteamId64 = string.IsNullOrWhiteSpace(SteamId64) ? null : SteamId64.Trim();
        settings.SyncthingEnabled = SyncthingEnabled;
        settings.XboxScanFolders = XboxScanFoldersText
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        settings.EdenExecutablePath = string.IsNullOrWhiteSpace(EdenExecutablePath) ? null : EdenExecutablePath.Trim();
        settings.EdenRomFolders = EdenRomFoldersText
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        settings.KioskMode = IsHandheldMode;
        settings.AutoRestart = IsHandheldMode;
        settings.ModeChosen = true;
        
        // 🟢 NOVO: Salva o estado do isolamento
        settings.IsolateControllerOnOverlay = IsolateControllerOnOverlay;
        settings.HidHideElevateOnDemand = HidHideElevateOnDemand;
        settings.FreezeGameOnOverlay = FreezeGameOnOverlay;

        // v0.25.2: o layout Cartoon é uma identidade fechada — paleta 16, fonte
        // Fredoka e som Bolha. Os combos ficam desligados na tela; gravar aqui
        // garante o bloqueio mesmo que algo os altere por outra via.
        var cartoon = LayoutThemeIndex == 3;
        settings.CartoonLightMode = cartoon && CartoonVariantIndex == 1;
        settings.LayoutTheme = Math.Clamp(LayoutThemeIndex, 0, HandheldLauncher.App.Platform.ThemeManager.LayoutThemeNames.Length - 1);
        settings.Theme = cartoon
            ? (settings.CartoonLightMode
                ? HandheldLauncher.App.Platform.ThemeManager.ThemeCartoonLight
                : HandheldLauncher.App.Platform.ThemeManager.ThemeCartoon)
            : Math.Clamp(ThemeIndex, 0, HandheldLauncher.App.Platform.ThemeManager.ThemeNames.Length - 1);
        settings.UiFont = cartoon
            ? HandheldLauncher.App.Platform.ThemeManager.FontFredoka
            : Math.Clamp(FontIndex, 0, HandheldLauncher.App.Platform.ThemeManager.Fonts.Length - 1);
        settings.UiFontSize = Math.Clamp(FontSizeIndex, 0, 2);
        settings.TileSize = Math.Clamp(TileSizeIndex, 0, 2);
        // Com o Cartoon ativo a grade é fixa: não mexer na preferência guardada,
        // para os outros layouts voltarem à disposição que o usuário tinha.
        if (!cartoon) settings.VerticalLayout = LayoutIndex == 1;
        settings.Animations = Animations;
        settings.SoundEffects = SoundEffects;
        settings.BetaChannelOptIn = BetaChannelOptIn;
        
        settings.SoundTheme = cartoon
            ? HandheldLauncher.App.Platform.ThemeManager.SoundBolha
            : Math.Clamp(SoundThemeIndex, 0, HandheldLauncher.App.Platform.UiSounds.PackNames.Length - 1);
        settings.SoundVolume = Math.Clamp((int)Math.Round(SoundVolume), 0, 100);
        settings.ColorMode = Math.Clamp(ColorModeIndex, 0, HandheldLauncher.App.Platform.DisplayColorManager.ModeNames.Length - 1);

        settings.KeyPrevFilter = string.IsNullOrWhiteSpace(KeyPrevFilter) ? "Q" : KeyPrevFilter.Trim();
        settings.KeyNextFilter = string.IsNullOrWhiteSpace(KeyNextFilter) ? "E" : KeyNextFilter.Trim();
        settings.KeyDownloads = string.IsNullOrWhiteSpace(KeyDownloads) ? "D" : KeyDownloads.Trim();
        settings.KeyContextMenu = string.IsNullOrWhiteSpace(KeyContextMenu) ? "Space" : KeyContextMenu.Trim();
        settings.KeyGlobalMenu = string.IsNullOrWhiteSpace(KeyGlobalMenu) ? "Escape" : KeyGlobalMenu.Trim();
        settings.KeyCloseGame = string.IsNullOrWhiteSpace(KeyCloseGame) ? "Ctrl+W" : KeyCloseGame.Trim();
        settings.KeyLaunchGame = string.IsNullOrWhiteSpace(KeyLaunchGame) ? "Enter" : KeyLaunchGame.Trim();

        settings.LosslessScalingPath = string.IsNullOrWhiteSpace(LosslessScalingPath) ? null : LosslessScalingPath.Trim();

        AppSettingsStore.Save(settings);

        var taskOk = WatchdogTask.Apply(IsHandheldMode);

        SavedMessage = "Salvo! Aparência já vale ao fechar esta tela; chaves e modo de uso pedem reiniciar o launcher (fechar de vez e abrir de novo)."
            + (taskOk
                ? " A abertura automática foi ajustada (o Windows pede confirmação de administrador para essa alteração)."
                : " Aviso: não consegui ajustar a abertura automática do Windows (veja debug.log).");
    }

    public void DismissSavedMessage() => SavedMessage = null;
}