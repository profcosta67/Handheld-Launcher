using System;
using System.Collections.ObjectModel;
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
                OnPropertyChanged(nameof(IsCategorySyncthingActive));
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
    public bool IsCategorySyncthingActive => _categoryIndex == 8;     // Syncthing
    public bool IsCategoryNewsActive => _categoryIndex == 9;          // Atualizações

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
                if (value == 1 && IsCleanThemeActive) 
                {
                    _layoutIndex = 0;
                    OnPropertyChanged(nameof(LayoutIndex));
                }
            }
        }
    }

    public bool IsCleanThemeActive => LayoutThemeIndex == 1;

    private bool _animations = true;
    public bool Animations { get => _animations; set => SetField(ref _animations, value); }

    private bool _soundEffects = true;
    public bool SoundEffects { get => _soundEffects; set => SetField(ref _soundEffects, value); }

    private bool _betaChannelOptIn;
    public bool BetaChannelOptIn { get => _betaChannelOptIn; set => SetField(ref _betaChannelOptIn, value); }

    // 🟢 B7: Substitui o FreezeGameOnOverlay e o GameModeEnabled
    private bool _isolateControllerOnOverlay = true;
    public bool IsolateControllerOnOverlay { get => _isolateControllerOnOverlay; set => SetField(ref _isolateControllerOnOverlay, value); }

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
        LayoutIndex = settings.VerticalLayout ? 1 : 0;
        Animations = settings.Animations;
        SoundEffects = settings.SoundEffects;
        BetaChannelOptIn = settings.BetaChannelOptIn;
        
        // 🟢 NOVO: Carrega o estado do isolamento
        IsolateControllerOnOverlay = settings.IsolateControllerOnOverlay;
        
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

    private readonly HandheldLauncher.Services.Syncthing.SyncthingService? _syncthingService;

    public SettingsViewModel(HandheldLauncher.Services.Syncthing.SyncthingService? syncthingService = null)
    {
        _syncthingService = syncthingService;
    }

    private bool _syncthingEnabled;
    public bool SyncthingEnabled
    {
        get => _syncthingEnabled;
        set
        {
            if (SetField(ref _syncthingEnabled, value))
            {
                if (value)
                {
                    var started = _syncthingService?.Start() == true;
                    SyncthingStatusText = started ? "Serviço iniciado. Atualize o painel para consultar dispositivos e pastas." : "Não foi possível iniciar o Syncthing. Verifique se syncthing.exe está disponível.";
                }
                else
                {
                    _syncthingService?.Stop();
                    SyncthingStatusText = "Serviço parado.";
                }
            }
        }
    }

    private string _syncthingStatusText = "Ative a sincronização para consultar o serviço.";
    public string SyncthingStatusText { get => _syncthingStatusText; set => SetField(ref _syncthingStatusText, value); }

    private string _syncthingDevicesText = "Nenhum dispositivo carregado.";
    public string SyncthingDevicesText { get => _syncthingDevicesText; set => SetField(ref _syncthingDevicesText, value); }

    private string _syncthingFoldersText = "Nenhuma pasta carregada.";
    public string SyncthingFoldersText { get => _syncthingFoldersText; set => SetField(ref _syncthingFoldersText, value); }

    public async Task RefreshSyncthingAsync()
    {
        if (_syncthingService is null)
        {
            SyncthingStatusText = "Serviço Syncthing indisponível nesta sessão.";
            return;
        }
        if (!_syncthingService.IsRunning)
        {
            SyncthingStatusText = "Serviço parado. Ative a sincronização acima.";
            return;
        }

        SyncthingStatusText = "Consultando serviço local…";
        try
        {
            var dashboard = await _syncthingService.GetDashboardAsync();
            SyncthingStatusText = dashboard.Status;
            SyncthingDevicesText = dashboard.Devices;
            SyncthingFoldersText = dashboard.Folders;
        }
        catch (Exception ex)
        {
            SyncthingStatusText = "API local indisponível: " + ex.Message;
        }
    }

    public async Task ScanSyncthingFoldersAsync()
    {
        if (_syncthingService is null || !_syncthingService.IsRunning)
        {
            SyncthingStatusText = "Inicie o Syncthing antes de solicitar a verificação.";
            return;
        }
        try
        {
            SyncthingStatusText = "Solicitando verificação de todas as pastas…";
            await _syncthingService.ScanAllFoldersAsync();
            await RefreshSyncthingAsync();
            SyncthingStatusText += " · verificação solicitada";
        }
        catch (Exception ex)
        {
            SyncthingStatusText = "Falha ao verificar pastas: " + ex.Message;
        }
    }

    private string _syncthingNewDeviceId = string.Empty;
    public string SyncthingNewDeviceId { get => _syncthingNewDeviceId; set => SetField(ref _syncthingNewDeviceId, value); }

    private string _syncthingNewDeviceName = string.Empty;
    public string SyncthingNewDeviceName { get => _syncthingNewDeviceName; set => SetField(ref _syncthingNewDeviceName, value); }

    private string _syncthingNewFolderId = string.Empty;
    public string SyncthingNewFolderId { get => _syncthingNewFolderId; set => SetField(ref _syncthingNewFolderId, value); }

    private string _syncthingNewFolderLabel = string.Empty;
    public string SyncthingNewFolderLabel { get => _syncthingNewFolderLabel; set => SetField(ref _syncthingNewFolderLabel, value); }

    private string _syncthingNewFolderPath = string.Empty;
    public string SyncthingNewFolderPath { get => _syncthingNewFolderPath; set => SetField(ref _syncthingNewFolderPath, value); }

    private string _syncthingRemoveId = string.Empty;
    public string SyncthingRemoveId { get => _syncthingRemoveId; set => SetField(ref _syncthingRemoveId, value); }

    public async Task AddSyncthingDeviceAsync()
    {
        if (_syncthingService is null || !_syncthingService.IsRunning) { SyncthingStatusText = "Inicie o Syncthing antes de adicionar dispositivos."; return; }
        try
        {
            await _syncthingService.AddDeviceAsync(SyncthingNewDeviceId, SyncthingNewDeviceName);
            SyncthingStatusText = "Dispositivo adicionado. Confirme o compartilhamento também no outro aparelho.";
            await RefreshSyncthingAsync();
        }
        catch (Exception ex) { SyncthingStatusText = "Não foi possível adicionar o dispositivo: " + ex.Message; }
    }

    public async Task AddSyncthingFolderAsync()
    {
        if (_syncthingService is null || !_syncthingService.IsRunning) { SyncthingStatusText = "Inicie o Syncthing antes de adicionar pastas."; return; }
        try
        {
            await _syncthingService.AddFolderAsync(SyncthingNewFolderId, SyncthingNewFolderLabel, SyncthingNewFolderPath);
            SyncthingStatusText = "Pasta adicionada. Compartilhe-a com os dispositivos desejados no Syncthing.";
            await RefreshSyncthingAsync();
        }
        catch (Exception ex) { SyncthingStatusText = "Não foi possível adicionar a pasta: " + ex.Message; }
    }

    public async Task RemoveSyncthingDeviceAsync()
    {
        if (_syncthingService is null || !_syncthingService.IsRunning) { SyncthingStatusText = "Inicie o Syncthing antes de remover dispositivos."; return; }
        try
        {
            await _syncthingService.RemoveDeviceAsync(SyncthingRemoveId);
            SyncthingStatusText = "Dispositivo removido da configuração local.";
            await RefreshSyncthingAsync();
        }
        catch (Exception ex) { SyncthingStatusText = "Não foi possível remover o dispositivo: " + ex.Message; }
    }

    public async Task RemoveSyncthingFolderAsync()
    {
        if (_syncthingService is null || !_syncthingService.IsRunning) { SyncthingStatusText = "Inicie o Syncthing antes de remover pastas."; return; }
        try
        {
            await _syncthingService.RemoveFolderAsync(SyncthingRemoveId);
            SyncthingStatusText = "Pasta removida da configuração local. Os arquivos locais não foram apagados.";
            await RefreshSyncthingAsync();
        }
        catch (Exception ex) { SyncthingStatusText = "Não foi possível remover a pasta: " + ex.Message; }
    }

    public async Task SetSyncthingDevicesPausedAsync(bool paused)
    {
        if (_syncthingService is null || !_syncthingService.IsRunning)
        {
            SyncthingStatusText = "O Syncthing não está em execução.";
            return;
        }
        try
        {
            await _syncthingService.SetAllDevicesPausedAsync(paused);
            SyncthingStatusText = paused ? "Sincronização com todos os dispositivos pausada." : "Sincronização com todos os dispositivos retomada.";
            await RefreshSyncthingAsync();
        }
        catch (Exception ex)
        {
            SyncthingStatusText = "Falha ao alterar dispositivos: " + ex.Message;
        }
    }

    public async Task RestartSyncthingAsync()
    {
        if (_syncthingService is null || !_syncthingService.IsRunning)
        {
            SyncthingStatusText = "O Syncthing não está em execução.";
            return;
        }
        try
        {
            SyncthingStatusText = "Solicitando reinicialização…";
            await _syncthingService.RestartSyncthingAsync();
            SyncthingStatusText = "Reinicialização solicitada. Atualize o painel em alguns segundos.";
        }
        catch (Exception ex)
        {
            SyncthingStatusText = "Falha ao reiniciar: " + ex.Message;
        }
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

        settings.Theme = Math.Clamp(ThemeIndex, 0, HandheldLauncher.App.Platform.ThemeManager.ThemeNames.Length - 1);
        settings.LayoutTheme = Math.Clamp(LayoutThemeIndex, 0, HandheldLauncher.App.Platform.ThemeManager.LayoutThemeNames.Length - 1);
        settings.UiFont = Math.Clamp(FontIndex, 0, HandheldLauncher.App.Platform.ThemeManager.Fonts.Length - 1);
        settings.UiFontSize = Math.Clamp(FontSizeIndex, 0, 2);
        settings.TileSize = Math.Clamp(TileSizeIndex, 0, 2);
        settings.VerticalLayout = LayoutIndex == 1;
        settings.Animations = Animations;
        settings.SoundEffects = SoundEffects;
        settings.BetaChannelOptIn = BetaChannelOptIn;
        
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