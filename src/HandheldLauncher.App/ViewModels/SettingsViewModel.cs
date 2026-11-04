using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
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
                return File.Exists(_edenExecutablePath) ? "Encontrado ✓" : "Esse arquivo não existe — confira o caminho.";
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
                OnPropertyChanged(nameof(IsCategoryFoldersActive));
                OnPropertyChanged(nameof(IsCategoryEmulatorsActive));
                OnPropertyChanged(nameof(IsCategoryModeActive));
                OnPropertyChanged(nameof(IsCategoryAccountsActive));
                OnPropertyChanged(nameof(IsCategoryShortcutsActive));
                OnPropertyChanged(nameof(IsCategoryLosslessActive));
                OnPropertyChanged(nameof(IsCategoryConnectivityActive));
                OnPropertyChanged(nameof(IsCategoryNewsActive));
                OnPropertyChanged(nameof(SaveButtonText));
            }
        }
    }

    public bool IsCategoryAppearanceActive => _categoryIndex == 0;
    public bool IsCategoryFoldersActive => _categoryIndex == 1;
    public bool IsCategoryEmulatorsActive => _categoryIndex == 2;
    public bool IsCategoryModeActive => _categoryIndex == 3;
    public bool IsCategoryAccountsActive => _categoryIndex == 4;
    public bool IsCategoryShortcutsActive => _categoryIndex == 5;
    public bool IsCategoryLosslessActive => _categoryIndex == 6;
    public bool IsCategoryConnectivityActive => _categoryIndex == 7;
    public bool IsCategoryNewsActive => _categoryIndex == 8;

    // v0.16.2: antes, a aba Novidades mostrava "✕ Fechar" (e o clique não
    // salvava) porque era só leitura. Desde a v0.16 ela tem o toggle
    // "Receber atualizações beta", então o rótulo enganava o usuário.
    public string SaveButtonText => "✕ Salvar e fechar";

    private int _fontIndex;
    public int FontIndex { get => _fontIndex; set => SetField(ref _fontIndex, value); }

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
    public int FontSizeIndex { get => _fontSizeIndex; set => SetField(ref _fontSizeIndex, value); }

    private int _themeIndex;
    public int ThemeIndex { get => _themeIndex; set => SetField(ref _themeIndex, value); }

    private int _layoutThemeIndex;
    public int LayoutThemeIndex
    {
        get => _layoutThemeIndex;
        set
        {
            if (SetField(ref _layoutThemeIndex, value))
            {
                OnPropertyChanged(nameof(LayoutThemeButtonText));
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
    public int LayoutIndex { get => _layoutIndex; set => SetField(ref _layoutIndex, value); }

    private bool _animations = true;
    public bool Animations { get => _animations; set => SetField(ref _animations, value); }

    private bool _soundEffects = true;
    public bool SoundEffects { get => _soundEffects; set => SetField(ref _soundEffects, value); }

    // v0.16: opt-in pro canal beta do Velopack — ver AppSettings.BetaChannelOptIn
    // e UpdateService. Só tem efeito depois de reiniciar o launcher (o
    // UpdateService é montado uma vez, na abertura do app).
    private bool _betaChannelOptIn;
    public bool BetaChannelOptIn { get => _betaChannelOptIn; set => SetField(ref _betaChannelOptIn, value); }

    // v0.16: opt-in pra congelar o jogo enquanto o Quick Settings está aberto
    // — ver AppSettings.FreezeGameOnOverlay e InputBlockService. Lido direto
    // pelo OverlayWindow a cada vez que abre, não precisa reiniciar.
    private bool _freezeGameOnOverlay;
    public bool FreezeGameOnOverlay { get => _freezeGameOnOverlay; set => SetField(ref _freezeGameOnOverlay, value); }

    private int _soundThemeIndex;
    public int SoundThemeIndex { get => _soundThemeIndex; set => SetField(ref _soundThemeIndex, value); }

    private double _soundVolume = 70;
    public double SoundVolume { get => _soundVolume; set => SetField(ref _soundVolume, value); }

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

    // 🦆 Caminho do Lossless Scaling
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
            // User-Agent obrigatório para a API do GitHub não bloquear
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
                        // Limpa o Markdown básico para ficar legível na tela
                        var cleanBody = r.Body?.Replace("### ", "").Replace("## ", "").Replace("**", "").Replace("- ", "• ") ?? "";
                        r.Body = cleanBody;
                        ReleaseHistory.Add(r);
                    }
                }
            }
        }
        catch
        {
            // Se falhar (sem internet), não faz nada, deixa a tela vazia.
        }
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
        SteamGridDbApiKey = settings.SteamGridDbApiKey ?? string.Empty;
        SteamWebApiKey = settings.SteamWebApiKey ?? string.Empty;
        RawgApiKey = settings.RawgApiKey ?? string.Empty;

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
        LayoutIndex = settings.VerticalLayout ? 1 : 0;
        Animations = settings.Animations;
        SoundEffects = settings.SoundEffects;
        BetaChannelOptIn = settings.BetaChannelOptIn;
        FreezeGameOnOverlay = settings.FreezeGameOnOverlay;
        SoundThemeIndex = Math.Clamp(settings.SoundTheme, 0, HandheldLauncher.App.Platform.UiSounds.PackNames.Length - 1);
        SoundVolume = Math.Clamp(settings.SoundVolume, 0, 100);
        ColorModeIndex = Math.Clamp(settings.ColorMode, 0, HandheldLauncher.App.Platform.DisplayColorManager.ModeNames.Length - 1);

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

    public void SaveTo(AppSettings settings)
    {
        settings.SteamGridDbApiKey = string.IsNullOrWhiteSpace(SteamGridDbApiKey) ? null : SteamGridDbApiKey.Trim();
        settings.SteamWebApiKey = string.IsNullOrWhiteSpace(SteamWebApiKey) ? null : SteamWebApiKey.Trim();
        settings.RawgApiKey = string.IsNullOrWhiteSpace(RawgApiKey) ? string.Empty : RawgApiKey.Trim();
        settings.SteamId64 = string.IsNullOrWhiteSpace(SteamId64) ? null : SteamId64.Trim();
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
        settings.Theme = Math.Clamp(ThemeIndex, 0, HandheldLauncher.App.Platform.ThemeManager.ThemeNames.Length - 1);
        settings.LayoutTheme = Math.Clamp(LayoutThemeIndex, 0, HandheldLauncher.App.Platform.ThemeManager.LayoutThemeNames.Length - 1);
        settings.UiFont = Math.Clamp(FontIndex, 0, HandheldLauncher.App.Platform.ThemeManager.Fonts.Length - 1);
        settings.UiFontSize = Math.Clamp(FontSizeIndex, 0, 2);
        settings.TileSize = Math.Clamp(TileSizeIndex, 0, 2);
        settings.VerticalLayout = LayoutIndex == 1;
        settings.Animations = Animations;
        settings.SoundEffects = SoundEffects;
        settings.BetaChannelOptIn = BetaChannelOptIn;
        settings.FreezeGameOnOverlay = FreezeGameOnOverlay;
        settings.SoundTheme = Math.Clamp(SoundThemeIndex, 0, HandheldLauncher.App.Platform.UiSounds.PackNames.Length - 1);
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
            + (taskOk ? string.Empty : " Aviso: não consegui ajustar a abertura automática do Windows (veja debug.log).");
    }

    public void DismissSavedMessage() => SavedMessage = null;
}