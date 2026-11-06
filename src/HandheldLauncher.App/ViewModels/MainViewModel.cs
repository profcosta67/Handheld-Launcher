using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Platform;
using HandheldLauncher.App.Services;
using HandheldLauncher.App.Views;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;
using HandheldLauncher.Core.Services.Artwork;
using Avalonia.Threading;

namespace HandheldLauncher.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly ILibraryService _library;
    private readonly IGameLauncher _launcher;
    private readonly IArtworkService _artwork;
    private readonly IPerformanceOverlayService _performanceOverlay;
    private readonly UpdateService? _updates;
    private readonly PerformanceHud? _hud;
    private readonly ISteamOwnedGamesClient? _steamOwnedGames;
    private readonly ISteamAchievementsClient? _steamAchievements;
    
    // 🟢 Instância única do SteamStoreService
    private readonly SteamStoreService _steamStore = new();

    private readonly HardwareMonitorService _monitorService = new();
    private readonly HWiNFOSharedMemoryService _hwInfoService = new();
    private DateTime _lastHwInfoPollTime = DateTime.MinValue;
    private bool _hwInfoWasAvailable;
    private DispatcherTimer? _hardwareTimer;

    private readonly SemaphoreSlim _artworkSemaphore = new(1, 1);
    private CancellationTokenSource? _ownedArtworkCts;
    private readonly ManualResetEventSlim _artworkPauseGate = new(true);
    private readonly HashSet<string> _failedAssets = new();

    private string _cpuTemperature = "--°C";
    public string CpuTemperature
    {
        get => _cpuTemperature;
        private set => SetField(ref _cpuTemperature, value);
    }

    // v0.18 "Saúde do aparelho": maior temperatura vista desde que o
    // launcher abriu (só em memória, zera ao reiniciar). Atualizado junto
    // da leitura normal de CpuTemperature no _hardwareTimer — nenhuma
    // leitura extra de sensor, só guarda o maior valor já visto.
    private double _peakCpuTemperatureC;
    public string PeakCpuTemperatureText => _peakCpuTemperatureC > 0 ? $"{_peakCpuTemperatureC:F0}°C" : "--°C";

    /// <summary>v0.18: alertas da Saúde do aparelho (temperatura alta, bateria desgastada, pouco espaço).</summary>
    public HealthAlertService HealthAlerts { get; } = new();
    private DispatcherTimer? _healthCheckTimer;
    private bool _hardwareReadInFlight;
    private bool _downloadsScanInFlight;

    private int _targetTdpWatts = 20;
    private int _realTimeTdpWatts = 0;

    public string TopBarTdpText => _targetTdpWatts > 0 ? $"{_targetTdpWatts} W" : "-- W";
    public string TopBarTdpToolTip => _realTimeTdpWatts > 0
        ? $"Alvo: {_targetTdpWatts} W | Consumo Real: {_realTimeTdpWatts} W"
        : $"TDP Configurado: {_targetTdpWatts} W";

    private bool _isShortcutsHelpOpen;
    public bool IsShortcutsHelpOpen
    {
        get => _isShortcutsHelpOpen;
        private set => SetField(ref _isShortcutsHelpOpen, value);
    }

    public ObservableCollection<ShortcutItem> GamepadShortcuts { get; } = new();
    public ObservableCollection<ShortcutItem> KeyboardShortcuts { get; } = new();

    public record ShortcutItem(string Label, string Key);

    public void OpenShortcutsHelp() { RefreshShortcuts(); IsShortcutsHelpOpen = true; }
    public void CloseShortcutsHelp() => IsShortcutsHelpOpen = false;
    public void ToggleShortcutsHelp() { if (IsShortcutsHelpOpen) CloseShortcutsHelp(); else OpenShortcutsHelp(); }

    private void RefreshShortcuts()
    {
        GamepadShortcuts.Clear(); KeyboardShortcuts.Clear();
        try
        {
            GamepadShortcuts.Add(new ShortcutItem("Navegar na biblioteca", "D-Pad"));
            GamepadShortcuts.Add(new ShortcutItem("Confirmar / Abrir jogo", "A"));
            GamepadShortcuts.Add(new ShortcutItem("Voltar / Fechar", "B"));
            GamepadShortcuts.Add(new ShortcutItem("Opções do jogo", "Select"));
            GamepadShortcuts.Add(new ShortcutItem("Menu global (sistema)", "Start"));
            GamepadShortcuts.Add(new ShortcutItem("Filtro anterior", "LB"));
            GamepadShortcuts.Add(new ShortcutItem("Próximo filtro", "RB"));
            GamepadShortcuts.Add(new ShortcutItem("Downloads", "Y"));
            GamepadShortcuts.Add(new ShortcutItem("Detalhes do jogo", "X"));
            GamepadShortcuts.Add(new ShortcutItem("Quick Settings (HUD)", "L3 + R3"));

            var s = AppSettingsStore.Load();
            KeyboardShortcuts.Add(new ShortcutItem("Navegar na biblioteca", "← → ↑ ↓"));
            KeyboardShortcuts.Add(new ShortcutItem("Abrir jogo selecionado", s.KeyLaunchGame));
            KeyboardShortcuts.Add(new ShortcutItem("Filtro anterior", s.KeyPrevFilter));
            KeyboardShortcuts.Add(new ShortcutItem("Próximo filtro", s.KeyNextFilter));
            KeyboardShortcuts.Add(new ShortcutItem("Downloads", s.KeyDownloads));
            KeyboardShortcuts.Add(new ShortcutItem("Menu de opções do jogo", s.KeyContextMenu));
            KeyboardShortcuts.Add(new ShortcutItem("Menu global (sistema)", s.KeyGlobalMenu));
            KeyboardShortcuts.Add(new ShortcutItem("Fechar jogo em execução", s.KeyCloseGame));
            KeyboardShortcuts.Add(new ShortcutItem("Este painel de ajuda", "F1"));
            KeyboardShortcuts.Add(new ShortcutItem("Abrir Quick Settings", "Ctrl+Alt+O"));
        }
        catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
    }

    public MainViewModel(
        ILibraryService library,
        IGameLauncher launcher,
        IArtworkService artwork,
        IPerformanceOverlayService performanceOverlay,
        UpdateService? updates = null,
        PerformanceHud? hud = null,
        ISteamOwnedGamesClient? steamOwnedGames = null,
        ISteamAchievementsClient? steamAchievements = null)
    {
        _library = library; _launcher = launcher; _artwork = artwork;
        _performanceOverlay = performanceOverlay; _updates = updates; _hud = hud;
        _steamOwnedGames = steamOwnedGames; _steamAchievements = steamAchievements;
        _launcher.GameExited += OnGameExited;

        Games = new ObservableCollection<GameTileViewModel>();

        MoveNextCommand = new RelayCommand(MoveNext, () => Games.Count > 1);
        MovePreviousCommand = new RelayCommand(MovePrevious, () => Games.Count > 1);
        LaunchSelectedCommand = new RelayCommand(() => _ = LaunchSelected(), () => SelectedTile is not null);
        CloseRunningGameCommand = new RelayCommand(CloseRunningGame, () => RunningGame is not null);
        RefreshCommand = new RelayCommand(Refresh);
        FetchArtworkCommand = new RelayCommand(FetchMissingArtwork, () => CanFetchArtwork);

        SetFilterAllCommand = new RelayCommand(() => SetFilter(LibraryFilter.All));
        SetFilterFavoritesCommand = new RelayCommand(() => SetFilter(LibraryFilter.Favorites));
        SetFilterComputerCommand = new RelayCommand(() => SetFilter(LibraryFilter.Computer));
        SetFilterEmulatorsCommand = new RelayCommand(() => SetFilter(LibraryFilter.Emulators));
        SetFilterApplicationsCommand = new RelayCommand(() => SetFilter(LibraryFilter.Applications));
        SetFilterNotInstalledCommand = new RelayCommand(() => SetFilter(LibraryFilter.NotInstalled));
        SetFilterCloudCommand = new RelayCommand(() => SetFilter(LibraryFilter.Cloud));

        _ = Task.Run(() => EnsureHwInfoRunning());

        _hardwareTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _hardwareTimer.Tick += async (_, _) =>
        {
            // v0.18.3 (pente fino de desempenho): ler os sensores (CPU + GPU via
            // LibreHardwareMonitor) leva de alguns a dezenas de ms e rodava na
            // thread da tela a cada 2 s — um engasgo periódico na navegação. Agora
            // lê em segundo plano; se a leitura anterior ainda não terminou, pula.
            if (_hardwareReadInFlight) return;
            _hardwareReadInFlight = true;
            float? temp;
            try { temp = await Task.Run(() => _monitorService.GetCpuTemperature()); }
            catch (Exception ex) { DebugLog.Swallowed(ex); temp = null; }
            finally { _hardwareReadInFlight = false; }

            if (temp.HasValue && temp.Value > 0)
            {
                CpuTemperature = $"{temp.Value:F0}°C";
                if (_hud != null) _hud.CpuTemperature = CpuTemperature;
                if (temp.Value > _peakCpuTemperatureC)
                {
                    _peakCpuTemperatureC = temp.Value;
                    OnPropertyChanged(nameof(PeakCpuTemperatureText));
                }
                HealthAlerts.OnTemperatureSample(temp.Value);
            }

            if (_hwInfoService.TryRead(out var hwInfo) && hwInfo is not null)
            {
                _hwInfoWasAvailable = true;
                if (hwInfo.PollTime != _lastHwInfoPollTime)
                {
                    _lastHwInfoPollTime = hwInfo.PollTime;
                    foreach (var reading in hwInfo.Readings)
                    {
                        var label = reading.Name;
                        if (label.Contains("Package Power", StringComparison.OrdinalIgnoreCase) ||
                            label.Contains("CPU Package Power", StringComparison.OrdinalIgnoreCase))
                        {
                            _realTimeTdpWatts = (int)reading.Value;
                            OnPropertyChanged(nameof(TopBarTdpToolTip));
                            if (_hud != null) _hud.RealTdpText = $"{_realTimeTdpWatts} W";
                        }
                    }
                }
            }
            else if (_hwInfoWasAvailable)
            {
                _hwInfoWasAvailable = false;
                _realTimeTdpWatts = 0;
                OnPropertyChanged(nameof(TopBarTdpToolTip));
                if (_hud != null) _hud.RealTdpText = "";
                DebugLog.Write("[HWiNFO] Shared Memory deixou de fornecer dados.");
            }
        };
        _hardwareTimer.Start();

        // v0.18: bateria e discos — 1ª verificação 1 min depois de abrir (não
        // atrasa o boot), depois a cada 30 min.
        DispatcherTimer.RunOnce(() => _ = HealthAlerts.RunPeriodicChecksAsync(), TimeSpan.FromMinutes(1));
        _healthCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _healthCheckTimer.Tick += (_, _) => _ = HealthAlerts.RunPeriodicChecksAsync();
        _healthCheckTimer.Start();
        HealthAlerts.AlertRaised += _ => { if (IsGlobalMenuOpen) BuildGlobalMenu(); };

        RebuildGamesFromLibrary();
        InitializeHudFromSettings();

        _ = ScheduleAutoArtworkFetchAsync();
    }

    private async Task ScheduleAutoArtworkFetchAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            if (!_artwork.HasApiKey) { DebugLog.Write("[SGDB] Auto-fetch agendado: sem API key, abortando."); return; }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                DebugLog.Write("[SGDB] Auto-fetch agendado disparando.");
                FetchMissingArtworkBackground();
            });
        }
        catch (Exception ex) { DebugLog.Write($"[SGDB] Auto-fetch agendado EXCEPTION: {ex.Message}"); }
    }

    private static bool NeedsDownload(string? path)
        => string.IsNullOrEmpty(path) || !File.Exists(path);

    private void EnsureHwInfoRunning()
    {
        try
        {
            var settings = AppSettingsStore.Load();
            if (settings.HwInfoDisabled) return;

            string[] possiblePaths =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "HWiNFO64.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "HWiNFO64", "HWiNFO64.exe"),
                @"C:\Program Files\HWiNFO64\HWiNFO64.exe",
                @"C:\Program Files (x86)\HWiNFO64\HWiNFO64.exe"
            };

            var exePath = possiblePaths.FirstOrDefault(File.Exists);

            if (exePath is null)
            {
                DebugLog.Write("[HWiNFO] Executável não encontrado — desabilitando checagem até reativação manual nas Settings.");
                settings.HwInfoDisabled = true;
                AppSettingsStore.Save(settings);
                return;
            }

            DebugLog.Write($"[HWiNFO] Iniciando em segundo plano: {exePath}");
            var psi = new ProcessStartInfo
            {
                FileName = exePath, Arguments = "-s", UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory
            };
            Process.Start(psi);
        }
        catch (Exception ex) { DebugLog.Write($"[HWiNFO] Erro ao tentar iniciar: {ex.Message}"); }
    }

    public void Dispose()
    {
        _hardwareTimer?.Stop();
        _healthCheckTimer?.Stop();
        _monitorService.Dispose();
        _hwInfoService.Dispose();
        try { _ownedArtworkCts?.Cancel(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
        _ownedArtworkCts?.Dispose();
        _artworkSemaphore.Dispose();
        _artworkPauseGate.Dispose();
    }

    public ObservableCollection<GameTileViewModel> Games { get; }

    private int _selectedIndex = -1;
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (Games.Count == 0) { SetField(ref _selectedIndex, -1); SelectedTile = null; return; }
            var clamped = Math.Clamp(value, 0, Games.Count - 1);
            if (SetField(ref _selectedIndex, clamped)) { UpdateFocus(); SelectedTile = Games[clamped]; }
        }
    }

    private GameTileViewModel? _selectedTile;
    public GameTileViewModel? SelectedTile
    {
        get => _selectedTile;
        private set
        {
            if (SetField(ref _selectedTile, value) && value is not null)
                _ = value.LoadDescriptionAsync(); // 🟢 Chama a descrição
        }
    }

    private string _updateStatusText = string.Empty;
    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set { if (SetField(ref _updateStatusText, value)) OnPropertyChanged(nameof(HasUpdateStatus)); }
    }

    public bool HasUpdateStatus => !string.IsNullOrEmpty(_updateStatusText);
    public bool IsUpdateReady => _updates?.IsUpdateReady ?? false;
    public string? PendingUpdateVersion => _updates?.PendingVersionText;

    public string AppVersionText
    {
        get
        {
            string version;
            if (_updates is { IsInstalled: true }) version = _updates.CurrentVersionText;
            else
            {
                var info = System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "dev";
                var plus = info.IndexOf('+');
                version = plus >= 0 ? info[..plus] : info;
            }
            return $"Handheld Launcher v{version} · por profcosta67";
        }
    }

    public async Task RunUpdateLoopAsync()
    {
        if (_updates is not { IsInstalled: true }) return;
        await Task.Delay(TimeSpan.FromSeconds(20));
        while (true) { await CheckForUpdatesAsync(userInitiated: false); await Task.Delay(TimeSpan.FromHours(6)); }
    }

    public async Task CheckForUpdatesAsync(bool userInitiated)
    {
        if (_updates is not { IsInstalled: true } || IsUpdateReady || (!userInitiated && RunningGame is not null)) return;
        if (userInitiated) UpdateStatusText = "Verificando atualizações…";
        var version = await _updates.CheckAndDownloadAsync();
        if (version is not null)
        {
            UpdateStatusText = $"⬆ v{version} pronta — toque pra reiniciar";
            OnPropertyChanged(nameof(IsUpdateReady)); OnPropertyChanged(nameof(PendingUpdateVersion));
        }
        else if (userInitiated)
        {
            UpdateStatusText = "Já está na versão mais recente (ou sem internet).";
            await Task.Delay(TimeSpan.FromSeconds(5));
            if (!IsUpdateReady) UpdateStatusText = string.Empty;
        }
    }

    public async Task<string> CheckForUpdatesWithResultAsync()
    {
        if (_updates is not { IsInstalled: true }) return "Atualização automática só funciona no launcher instalado pelo Setup.exe.";
        await CheckForUpdatesAsync(userInitiated: true);
        return IsUpdateReady ? $"v{PendingUpdateVersion} baixada — feche as Configurações e toque em ⬆ na barra de cima (ou no menu) pra reiniciar." : "Você já está na versão mais recente (ou sem internet).";
    }

    public bool UpdatesInstalled => _updates is { IsInstalled: true };
    public string UpdateWindowVersionText => $"Versão v{PendingUpdateVersion} pronta pra instalar";

    public string PendingUpdateNotesText
    {
        get
        {
            var markdown = _updates?.PendingReleaseNotesMarkdown;
            if (string.IsNullOrWhiteSpace(markdown)) return "Essa versão não trouxe notas detalhadas.";
            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            var kept = new List<string>();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                line = line.Replace("**", "");
                if (line.StartsWith("- ")) line = "•  " + line[2..];
                kept.Add(line);
            }
            return kept.Count > 0 ? string.Join("\n", kept) : markdown;
        }
    }

    public bool UpdateBlockedByRunningGame => RunningGame is not null;
    public bool CanApplyUpdateNow => RunningGame is null;

    public void ApplyUpdate()
    {
        if (!IsUpdateReady) return;
        if (RunningGame is not null) { UpdateStatusText = $"⬆ v{PendingUpdateVersion} pronta — feche o jogo antes de reiniciar"; return; }
        if (OperatingSystem.IsWindows()) { _hud?.ClearNow(); HandheldLauncher.App.Input.GameInputHelper.StopHelperProcess(); }
        _updates?.ApplyAndRestart();
    }

    private long _lastOverlayCycleTicks;

    public void CycleOverlayPreset()
    {
        if (_hud is null || !OperatingSystem.IsWindows()) return;
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastOverlayCycleTicks);
        if (now - last < 500 || Interlocked.CompareExchange(ref _lastOverlayCycleTicks, now, last) != last) return;
        _hud.Preset = (_hud.Preset + 1) % PerformanceHud.PresetNames.Length;
        try { var settings = AppSettingsStore.Load(); settings.OverlayPreset = _hud.Preset; AppSettingsStore.Save(settings); }
        catch (Exception ex) { DebugLog.Write($"[HUD] Falha ao salvar preset do overlay: {ex.Message}"); }
    }

    public void SetOverlayPreset(int preset)
    {
        if (_hud is null || !OperatingSystem.IsWindows()) return;
        _hud.Preset = preset;
        try { var settings = AppSettingsStore.Load(); settings.OverlayPreset = _hud.Preset; AppSettingsStore.Save(settings); }
        catch (Exception ex) { DebugLog.Write($"[HUD] Falha ao salvar preset do overlay: {ex.Message}"); }
    }

    public void ClearOverlayNow() { if (OperatingSystem.IsWindows()) _hud?.ClearNow(); }
    public bool IsPerformanceHudEnabled => _hud is not null && _hud.Preset > 0;
    public int CurrentHudPreset => _hud?.Preset ?? 0;

    public void SetPerformanceHudEnabled(bool enabled)
    {
        if (_hud is null || !OperatingSystem.IsWindows()) return;
        _hud.Preset = enabled ? Math.Max(1, _hud.Preset) : 0;
        try { var settings = AppSettingsStore.Load(); settings.OverlayPreset = _hud.Preset; AppSettingsStore.Save(settings); }
        catch (Exception ex) { DebugLog.Write($"[HUD] Falha ao salvar preset do overlay: {ex.Message}"); }
        OnPropertyChanged(nameof(IsPerformanceHudEnabled));
    }

    public void UpdateHudTdpStatus(string mode)
    {
        var watts = HandheldLauncher.Core.Services.TdpProfiles.WattsFor(mode) ?? 0; // v0.18.5: inclui o personalizado
        _targetTdpWatts = watts;
        OnPropertyChanged(nameof(TopBarTdpText));
        OnPropertyChanged(nameof(TopBarTdpToolTip));
        if (_hud != null) _hud.TdpStatusText = watts > 0 ? $"{watts}W" : "";
    }

    public void UpdateHudColorMode(string modeText) { if (_hud != null) _hud.ColorModeText = modeText ?? ""; }
    public void UpdateHudRecordingState(bool isRecording) { if (_hud != null) _hud.IsRecording = isRecording; }

    public void InitializeHudFromSettings()
    {
        if (_hud is null) return;
        try
        {
            var settings = AppSettingsStore.Load();
            UpdateHudTdpStatus(string.IsNullOrWhiteSpace(settings.TdpMode) ? "Normal" : settings.TdpMode);
            UpdateHudColorMode(settings.ColorMode switch { 1 => "Vívido", 2 => "OLED", 3 => "Noturno", 4 => "Confortável", _ => "Padrão" });
            UpdateHudRecordingState(false);
        }
        catch (Exception ex) { DebugLog.Write($"[HUD] Falha ao inicializar HUD a partir das configurações: {ex.Message}"); }
    }

    private string _batteryStatusText = string.Empty;
    public string BatteryStatusText { get => _batteryStatusText; set => SetField(ref _batteryStatusText, value); }
    private string _clockText = string.Empty;
    public string ClockText { get => _clockText; set => SetField(ref _clockText, value); }

    private GameEntry? _runningGame;
    public GameEntry? RunningGame { get => _runningGame; private set => SetField(ref _runningGame, value); }

    private bool _isLaunching;
    public bool IsLaunching { get => _isLaunching; private set => SetField(ref _isLaunching, value); }

    private string _launchingGameTitle = string.Empty;
    public string LaunchingGameTitle { get => _launchingGameTitle; private set => SetField(ref _launchingGameTitle, value); }

    private string? _launchingGameIconPath;
    public string? LaunchingGameIconPath { get => _launchingGameIconPath; private set => SetField(ref _launchingGameIconPath, value); }

    private const int LaunchFeedbackMilliseconds = 2500;

    private bool _isFetchingArtwork;
    private bool _isFetchingOwnedArtwork;
    public bool IsFetchingArtwork
    {
        get => _isFetchingArtwork;
        private set { if (SetField(ref _isFetchingArtwork, value)) OnPropertyChanged(nameof(CanFetchArtwork)); }
    }
    public bool CanFetchArtwork => _artwork.HasApiKey && !IsFetchingArtwork;

    private bool _isContextMenuOpen;
    public bool IsContextMenuOpen { get => _isContextMenuOpen; private set => SetField(ref _isContextMenuOpen, value); }

    private GameTileViewModel? _contextMenuTarget;
    public GameTileViewModel? ContextMenuTarget { get => _contextMenuTarget; private set => SetField(ref _contextMenuTarget, value); }

    private int _contextMenuIndex;
    public ObservableCollection<ContextMenuItemViewModel> ContextMenuItems { get; } = new();
    private readonly List<Action> _contextMenuActions = new();

    private enum ArtworkPickerKind { Grid, WideGrid, Hero, Logo, Icon }
    private enum LibraryFilter { All, Favorites, Computer, Emulators, Applications, NotInstalled, Cloud }

    private static readonly LibraryFilter[] FilterOrder = { LibraryFilter.Favorites, LibraryFilter.All, LibraryFilter.Computer, LibraryFilter.Emulators, LibraryFilter.Applications, LibraryFilter.NotInstalled, LibraryFilter.Cloud };
    private LibraryFilter _activeFilter = LibraryFilter.All;

    public bool IsFilterAllActive => _activeFilter == LibraryFilter.All;
    public bool IsFilterFavoritesActive => _activeFilter == LibraryFilter.Favorites;
    public bool IsFilterComputerActive => _activeFilter == LibraryFilter.Computer;
    public bool IsFilterEmulatorsActive => _activeFilter == LibraryFilter.Emulators;
    public bool IsFilterApplicationsActive => _activeFilter == LibraryFilter.Applications;
    public bool IsFilterNotInstalledActive => _activeFilter == LibraryFilter.NotInstalled;
    public bool IsFilterCloudActive => _activeFilter == LibraryFilter.Cloud;

    public ObservableCollection<SubFilterViewModel> SubFilters { get; } = new();
    public bool HasSubFilters => SubFilters.Count > 0;

    private bool _isSubFilterRowFocused;
    public bool IsSubFilterRowFocused { get => _isSubFilterRowFocused; private set => SetField(ref _isSubFilterRowFocused, value); }
    private int _subFilterCursor;

    public void FocusSubFilterRow()
    {
        if (SubFilters.Count == 0) return;
        _subFilterCursor = Math.Clamp(_subFilterCursor, 0, SubFilters.Count - 1);
        IsSubFilterRowFocused = true; UpdateSubFilterCursorFlags();
    }

    public void LeaveSubFilterRow()
    {
        if (!IsSubFilterRowFocused) return;
        IsSubFilterRowFocused = false; UpdateSubFilterCursorFlags();
    }

    public void MoveSubFilterCursor(int delta)
    {
        if (!IsSubFilterRowFocused || SubFilters.Count == 0) return;
        _subFilterCursor = Math.Clamp(_subFilterCursor + delta, 0, SubFilters.Count - 1);
        UpdateSubFilterCursorFlags();
    }

    public void ToggleFocusedSubFilter()
    {
        if (!IsSubFilterRowFocused || SubFilters.Count == 0) return;
        ToggleSubFilter(SubFilters[Math.Clamp(_subFilterCursor, 0, SubFilters.Count - 1)]);
    }

    public void ToggleSubFilter(SubFilterViewModel subFilter) { subFilter.IsChecked = !subFilter.IsChecked; RebuildGamesFromLibrary(); }

    private void UpdateSubFilterCursorFlags()
    {
        for (var i = 0; i < SubFilters.Count; i++) SubFilters[i].IsCursor = IsSubFilterRowFocused && i == _subFilterCursor;
    }

    private void RebuildSubFilters()
    {
        SubFilters.Clear(); _subFilterCursor = 0; IsSubFilterRowFocused = false;

        switch (_activeFilter)
        {
            case LibraryFilter.Computer:
                SubFilters.Add(new SubFilterViewModel("Steam", "#66C0F4", "#0A1A2A", g => g.Source == GameSource.Steam));
                SubFilters.Add(new SubFilterViewModel("Xbox", "#3FB950", "#06220A", g => g.Source == GameSource.XboxApp));
                SubFilters.Add(new SubFilterViewModel("Epic", "#DCDCDC", "#1A1A1A", g => g.Source == GameSource.EpicGames));
                SubFilters.Add(new SubFilterViewModel("GOG", "#A06BE0", "#FFFFFF", g => g.Source == GameSource.Gog));
                SubFilters.Add(new SubFilterViewModel("EA", "#FF4455", "#FFFFFF", g => g.Source == GameSource.EaApp));
                SubFilters.Add(new SubFilterViewModel("Ubisoft", "#35A4FF", "#06203A", g => g.Source == GameSource.Ubisoft));
                SubFilters.Add(new SubFilterViewModel("Atalhos", "#B0B0B0", "#1A1A1A", g => g.Source == GameSource.Manual));
                break;

            case LibraryFilter.Emulators:
                SubFilters.Add(new SubFilterViewModel("Switch", "#FFFFFF", "#E4000F", g => g.Id.StartsWith("eden_", StringComparison.Ordinal)));
                break;
        }
        OnPropertyChanged(nameof(HasSubFilters));
        UpdateSubFilterCursorFlags();
    }

    private bool MatchesSubFilters(GameEntry game)
    {
        var anyChecked = false;
        foreach (var subFilter in SubFilters)
        {
            if (!subFilter.IsChecked) continue;
            anyChecked = true;
            if (subFilter.Matches(game)) return true;
        }
        return !anyChecked;
    }

    private bool _isGlobalMenuOpen;
    public bool IsGlobalMenuOpen { get => _isGlobalMenuOpen; private set => SetField(ref _isGlobalMenuOpen, value); }

    public ObservableCollection<ContextMenuItemViewModel> GlobalMenuItems { get; } = new();
    private readonly List<Action> _globalMenuActions = new();
    private int _globalMenuIndex;

    public event EventHandler? AddGameRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? QuitRequested;
    public event EventHandler<GameEntry>? GameDetailsRequested;
    public event EventHandler<GameEntry>? EditGameRequested;
    public event EventHandler? LayoutPickerRequested;
    public event EventHandler? TurnOffScreenRequested;
    public event EventHandler? RestartDeviceRequested;
    public event EventHandler? ShutdownDeviceRequested;
    /// <summary>v0.17 (pedido do usuário): reinicia só o processo do launcher, sem tocar no aparelho.</summary>
    public event EventHandler? RestartLauncherRequested;
    public event EventHandler? OpenOverlayRequested;
    public event EventHandler? SystemUpdateRequested;
    public event EventHandler? DriverUpdateRequested;
    public event EventHandler? HelpCenterRequested; // v0.16.2: Central de Ajuda
    public event EventHandler? DeviceHealthRequested; // v0.18: Saúde do Aparelho
    public event EventHandler? StorageManagerRequested; // v0.18: Gerenciador de Armazenamento
    /// <summary>v0.18 aba "Nuvem": abrir esta URL do xbox.com/play no Edge em tela cheia (ver MainWindow.OpenXCloud).</summary>
    public event EventHandler<string>? XCloudRequested;

    public void RequestOpenOverlay() { OpenOverlayRequested?.Invoke(this, EventArgs.Empty); }

    public void RefreshArtworkState()
    {
        _artwork.ReloadSettings();
        OnPropertyChanged(nameof(CanFetchArtwork));
        if (IsGlobalMenuOpen) BuildGlobalMenu();
    }

    public void ApplyEntryEdits(GameEntry game, string? title, bool isApplication) { _library.SaveEntryEdits(game.Id, title, isApplication); RebuildGamesFromLibrary(); }

    private static bool HasGameDetails(GameEntry game) => !game.IsOwnedNotInstalled && !game.IsApplication && !IsCloudEntry(game);

    // ---- v0.18 aba "Nuvem" (Xbox Cloud Gaming) -------------------------------------
    // Entradas SINTÉTICAS (como as de "Não instalados"): nunca entram na
    // biblioteca nem no library.json — existem só enquanto a aba está aberta.
    private const string CloudIdPrefix = "xcloud_";
    private const string CloudHomeId = "xcloud_home";
    public static bool IsCloudEntry(GameEntry game) => game.Id.StartsWith(CloudIdPrefix, StringComparison.Ordinal);

    private IReadOnlyList<XCloudGame> _cloudGames = Array.Empty<XCloudGame>();
    private bool _cloudCatalogLoaded;
    private bool _isLoadingCloudCatalog;
    private bool _cloudCatalogFailed;
    private DateTimeOffset _cloudFetchedUtc; // v0.18.3 (revisão): fica em memória — não relê o catalog.json a cada entrada na aba

    private IEnumerable<GameEntry> BuildCloudEntries()
    {
        if (!_cloudCatalogLoaded)
        {
            _cloudGames = XCloudCatalog.LoadCached(out _cloudFetchedUtc);
            _cloudCatalogLoaded = true;
        }

        var query = NormalizeForSearch(_cloudSearchText);
        var filtering = query.Length > 0 || _cloudLetter is not null;

        // O bloco fixo só aparece sem filtro (com filtro, a lista é só de jogos).
        if (!filtering)
        {
            yield return new GameEntry
            {
                Id = CloudHomeId,
                Title = "Xbox Cloud Gaming",
                Source = GameSource.XboxApp,
                LaunchUri = XCloudCatalog.HomeUrl,
            };
        }
        foreach (var g in _cloudGames)
        {
            if (filtering)
            {
                var title = NormalizeForSearch(g.Title);
                if (query.Length > 0 && !title.Contains(query, StringComparison.Ordinal)) continue;
                if (_cloudLetter is not null && LetterOf(title) != _cloudLetter) continue;
            }
            yield return new GameEntry
            {
                Id = CloudIdPrefix + g.ProductId,
                Title = g.Title,
                Source = GameSource.XboxApp,
                LaunchUri = XCloudCatalog.GamePageUrl(g),
                GridImagePath = g.PosterPath,
            };
        }
    }

    // ---- v0.18.3: busca e filtro por letra (SÓ na aba Nuvem) ---------------------
    // Pedido do usuário: "o Xbox é muito jogo" — ~560 títulos. Busca por nome
    // sem acento/maiúscula ("pokemon" acha "Pokémon") + faixa # A–Z que FILTRA
    // (mostra só os jogos daquela letra). Os dois juntos valem ao mesmo tempo.
    private string _cloudSearchText = "";
    public string CloudSearchText
    {
        get => _cloudSearchText;
        set
        {
            if (!SetField(ref _cloudSearchText, value ?? "")) return;
            OnPropertyChanged(nameof(HasCloudFilter));
            // Espera parar de digitar (250 ms) antes de remontar a grade.
            _cloudSearchDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _cloudSearchDebounce.Stop();
            _cloudSearchDebounce.Tick -= OnCloudSearchDebounce;
            _cloudSearchDebounce.Tick += OnCloudSearchDebounce;
            _cloudSearchDebounce.Start();
        }
    }
    private DispatcherTimer? _cloudSearchDebounce;
    private void OnCloudSearchDebounce(object? sender, EventArgs e)
    {
        _cloudSearchDebounce?.Stop();
        if (_activeFilter == LibraryFilter.Cloud) RebuildGamesFromLibrary();
    }

    private string? _cloudLetter;
    public string CloudLetterText => _cloudLetter is null ? "" : $"Letra: {_cloudLetter}";
    public bool HasCloudFilter => _cloudSearchText.Trim().Length > 0 || _cloudLetter is not null;

    public ObservableCollection<CloudLetterViewModel> CloudLetters { get; } = BuildCloudLetters();
    private static ObservableCollection<CloudLetterViewModel> BuildCloudLetters()
    {
        var list = new ObservableCollection<CloudLetterViewModel> { new("Todos", null), new("#", "#") };
        for (var c = 'A'; c <= 'Z'; c++) list.Add(new CloudLetterViewModel(c.ToString(), c.ToString()));
        list[0].IsActive = true;
        return list;
    }

    private bool _isCloudLetterBarFocused;
    public bool IsCloudLetterBarFocused { get => _isCloudLetterBarFocused; private set => SetField(ref _isCloudLetterBarFocused, value); }
    private int _cloudLetterCursor;

    public void SetCloudLetter(string? letter)
    {
        _cloudLetter = letter;
        foreach (var l in CloudLetters) l.IsActive = l.Value == letter;
        OnPropertyChanged(nameof(HasCloudFilter));
        OnPropertyChanged(nameof(CloudLetterText));
        if (_activeFilter == LibraryFilter.Cloud) RebuildGamesFromLibrary();
    }

    public void ClearCloudFilters()
    {
        _cloudSearchDebounce?.Stop();
        SetField(ref _cloudSearchText, "", nameof(CloudSearchText));
        LeaveCloudLetterBar();
        SetCloudLetter(null);
    }

    /// <summary>X na aba Nuvem: entra/sai da faixa de letras.</summary>
    public void ToggleCloudLetterBar()
    {
        if (IsCloudLetterBarFocused) { LeaveCloudLetterBar(); return; }
        var active = CloudLetters.ToList().FindIndex(l => l.IsActive);
        _cloudLetterCursor = active < 0 ? 0 : active;
        UpdateCloudLetterCursor();
        IsCloudLetterBarFocused = true;
    }

    public void MoveCloudLetterCursor(int delta)
    {
        _cloudLetterCursor = Math.Clamp(_cloudLetterCursor + delta, 0, CloudLetters.Count - 1);
        UpdateCloudLetterCursor();
    }

    public void ConfirmCloudLetterCursor()
    {
        var chosen = CloudLetters[_cloudLetterCursor].Value;
        LeaveCloudLetterBar();
        SetCloudLetter(chosen);
    }

    public void LeaveCloudLetterBar()
    {
        IsCloudLetterBarFocused = false;
        foreach (var l in CloudLetters) l.IsCursor = false;
    }

    private void UpdateCloudLetterCursor()
    {
        for (var i = 0; i < CloudLetters.Count; i++) CloudLetters[i].IsCursor = i == _cloudLetterCursor;
    }

    private string _cloudFilterEmptyText = "";
    public string CloudFilterEmptyText { get => _cloudFilterEmptyText; private set { if (SetField(ref _cloudFilterEmptyText, value)) OnPropertyChanged(nameof(HasCloudFilterEmptyText)); } }
    public bool HasCloudFilterEmptyText => _cloudFilterEmptyText.Length > 0;

    /// <summary>Minúsculas, sem acento, espaços colapsados — "Pokémon  Legends" → "pokemon legends".</summary>
    private static string NormalizeForSearch(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var decomposed = text.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposed.Length);
        var lastSpace = false;
        foreach (var ch in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(ch)) { if (!lastSpace) sb.Append(' '); lastSpace = true; continue; }
            sb.Append(ch);
            lastSpace = false;
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    /// <summary>"A"…"Z" pela primeira letra/número do nome (pula aspas, colchetes etc.); número = "#".</summary>
    private static string? LetterOf(string normalizedTitle)
    {
        foreach (var ch in normalizedTitle)
        {
            if (char.IsDigit(ch)) return "#";
            if (ch is >= 'a' and <= 'z') return char.ToUpperInvariant(ch).ToString();
            if (char.IsLetter(ch)) return "#"; // letra fora de A–Z (ex.: japonês) fica junto do "#"
        }
        return null;
    }

    private DateTime _lastCloudOpenUtc = DateTime.MinValue;

    private void OpenCloudEntry(GameEntry game)
    {
        // v0.18.3 (revisão): A duas vezes enquanto o Edge ainda abre = duas janelas.
        if (DateTime.UtcNow - _lastCloudOpenUtc < TimeSpan.FromSeconds(4)) return;
        _lastCloudOpenUtc = DateTime.UtcNow;
        var url = string.IsNullOrWhiteSpace(game.LaunchUri) ? XCloudCatalog.HomeUrl : game.LaunchUri!;
        DebugLog.Write($"[XCLOUD] Abrindo '{game.Title}' → {url}");
        XCloudRequested?.Invoke(this, url);
    }

    private async Task RefreshCloudCatalogAsync()
    {
        if (_isLoadingCloudCatalog) return;
        if (_cloudGames.Count > 0 && !XCloudCatalog.IsStale(_cloudFetchedUtc))
        {
            await DownloadCloudPostersAsync();
            return;
        }

        _isLoadingCloudCatalog = true;
        _cloudCatalogFailed = false;
        UpdateNotInstalledTabMessage();
        try
        {
            var fresh = await Task.Run(() => XCloudCatalog.RefreshAsync());
            if (fresh is not null)
            {
                _cloudGames = fresh;
                _cloudCatalogLoaded = true;
                _cloudFetchedUtc = DateTimeOffset.UtcNow;
            }
            else
            {
                _cloudCatalogFailed = true;
            }
        }
        finally
        {
            _isLoadingCloudCatalog = false;
        }
        if (_activeFilter == LibraryFilter.Cloud) RebuildGamesFromLibrary();
        else UpdateNotInstalledTabMessage();
        await DownloadCloudPostersAsync();
    }

    private async Task DownloadCloudPostersAsync()
    {
        var games = _cloudGames;
        if (!games.Any(g => g.PosterPath is null && g.PosterUrl is not null)) return;
        var got = await Task.Run(() => XCloudCatalog.DownloadMissingPostersAsync(games));
        if (got > 0 && _activeFilter == LibraryFilter.Cloud && ReferenceEquals(games, _cloudGames)) RebuildGamesFromLibrary();
    }

    private void OpenGameDetails(GameEntry game) { CloseContextMenu(); GameDetailsRequested?.Invoke(this, game); }

    public void OpenGameDetailsForSelected()
    {
        if (SelectedTile?.Game is { } game && HasGameDetails(game)) GameDetailsRequested?.Invoke(this, game);
    }

    public GameDetailsViewModel CreateGameDetailsViewModel(GameEntry game) => new(game, _steamOwnedGames, _steamAchievements);

    // v0.18 Detalhes 2.0: ações da página do jogo. Só repassam pros MESMOS
    // métodos que o menu do jogo já usa (nenhuma lógica nova de lançar/
    // favoritar), pra página e menu nunca se comportarem diferente.
    public void PlayGameFromDetails(GameEntry game) { EnsureTileVisible(game); OpenGameFromMenu(game); }

    /// <summary>
    /// v0.18.3 (revisão): na aba Favoritos, desfavoritar na página do jogo tira
    /// o bloco da lista na hora — aí Jogar/Mais opções não achavam o bloco e não
    /// faziam nada. Se o jogo não está na aba atual, vai pra "Todos" (onde ele
    /// sempre está) antes de agir.
    /// </summary>
    private void EnsureTileVisible(GameEntry game)
    {
        if (Games.Any(t => t.Game.Id == game.Id)) return;
        if (game.IsApplication || IsCloudEntry(game)) return;
        DebugLog.Write($"[DETALHES] '{game.Title}' não está na aba atual — indo pra Todos.");
        SetFilter(LibraryFilter.All);
    }

    public bool ToggleFavoriteFromDetails(GameEntry game)
    {
        var desired = !game.IsFavorite;
        ToggleFavorite(game);
        // Se a biblioteca foi recarregada com a página aberta, "game" pode ser
        // uma instância antiga — mantém ela coerente com o que foi salvo.
        game.IsFavorite = desired;
        return desired;
    }

    /// <summary>v0.18 Armazenamento: a biblioteca inteira (não só a aba de filtro aberta agora).</summary>
    public IReadOnlyList<GameEntry> AllLibraryGames => _library.Games;

    /// <summary>v0.18 Armazenamento: grava os caminhos novos de um jogo manual movido e recarrega a biblioteca.</summary>
    public bool ApplyMovedManualGame(GameEntry game, string newExecutablePath, string? newWorkingDirectory, string? newLaunchArguments)
    {
        // Nunca lança: true = library.json gravado com o caminho novo; false = nada mudou.
        bool ok;
        try { ok = _library.UpdateManualGamePaths(game.Id, newExecutablePath, newWorkingDirectory, newLaunchArguments); }
        catch (Exception ex) { DebugLog.Write($"[MOVER] Erro ao gravar atalho: {ex.Message}"); ok = false; }
        DebugLog.Write($"[MOVER] Atalho de '{game.Title}' {(ok ? "atualizado" : "NÃO atualizado")} → {newExecutablePath}");
        if (ok)
        {
            try { RebuildGamesFromLibrary(); }
            catch (Exception ex) { DebugLog.Write($"[MOVER] Recarregar a biblioteca falhou (atalho já gravado): {ex.Message}"); }
        }
        return ok;
    }

    public void OpenGameMenuFromDetails(GameEntry game)
    {
        EnsureTileVisible(game);
        var tile = Games.FirstOrDefault(t => t.Game.Id == game.Id);
        if (tile is null) return;
        SelectedIndex = Games.IndexOf(tile);
        ReopenContextMenu(tile);
    }

    private bool _isCoverPickerOpen;
    public bool IsCoverPickerOpen { get => _isCoverPickerOpen; private set => SetField(ref _isCoverPickerOpen, value); }

    private string _coverPickerTitle = string.Empty;
    public string CoverPickerTitle { get => _coverPickerTitle; private set => SetField(ref _coverPickerTitle, value); }

    private bool _isCoverPickerLoading;
    public bool IsCoverPickerLoading
    {
        get => _isCoverPickerLoading;
        private set { if (SetField(ref _isCoverPickerLoading, value)) OnPropertyChanged(nameof(CoverPickerIsEmpty)); }
    }

    public ObservableCollection<ArtworkCandidateViewModel> CoverPickerCandidates { get; } = new();
    public bool CoverPickerHasResults => CoverPickerCandidates.Count > 0;
    public bool CoverPickerIsEmpty => !IsCoverPickerLoading && !CoverPickerHasError && CoverPickerCandidates.Count == 0;

    private string? _coverPickerErrorMessage;
    public string? CoverPickerErrorMessage
    {
        get => _coverPickerErrorMessage;
        private set { if (SetField(ref _coverPickerErrorMessage, value)) { OnPropertyChanged(nameof(CoverPickerHasError)); OnPropertyChanged(nameof(CoverPickerIsEmpty)); } }
    }

    public bool CoverPickerHasError => !string.IsNullOrEmpty(CoverPickerErrorMessage);
    private int _coverPickerIndex;
    public int CoverPickerIndex => _coverPickerIndex;

    private bool _filterAnimatedOnly;
    public bool FilterAnimatedOnly
    {
        get => _filterAnimatedOnly;
        set { if (SetField(ref _filterAnimatedOnly, value)) { if (IsCoverPickerOpen && _coverPickerGame != null) _ = OpenCoverPicker(_coverPickerGame, _coverPickerKind); } }
    }

    private ArtworkPickerKind _coverPickerKind;
    private GameEntry? _coverPickerGame;

    public ICommand MoveNextCommand { get; }
    public ICommand MovePreviousCommand { get; }
    public ICommand LaunchSelectedCommand { get; }
    public ICommand CloseRunningGameCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand FetchArtworkCommand { get; }
    public ICommand SetFilterAllCommand { get; }
    public ICommand SetFilterFavoritesCommand { get; }
    public ICommand SetFilterComputerCommand { get; }
    public ICommand SetFilterEmulatorsCommand { get; }
    public ICommand SetFilterApplicationsCommand { get; }
    public ICommand SetFilterNotInstalledCommand { get; }
    public ICommand SetFilterCloudCommand { get; }

    public void MoveNext() => SelectedIndex++;
    public void MovePrevious() => SelectedIndex--;

    public async Task LaunchSelected()
    {
        if (SelectedTile is null) return;
        var game = SelectedTile.Game;
        if (game.IsOwnedNotInstalled) { if (game.SteamAppId is { } ownedAppId) _launcher.OpenSteamInstaller(ownedAppId); return; }
        if (IsCloudEntry(game)) { OpenCloudEntry(game); return; }

        LaunchingGameTitle = game.Title;
        LaunchingGameIconPath = ResolveIconPath(game);
        IsLaunching = true;

        if (_artwork.HasApiKey && string.IsNullOrEmpty(LaunchingGameIconPath))
        {
            try
            {
                var fetchTask = _artwork.FetchIconAsync(game);
                var completed = await Task.WhenAny(fetchTask, Task.Delay(3000));
                if (completed == fetchTask && await fetchTask) LaunchingGameIconPath = game.IconImagePath;
            }
            catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
        }

        if (string.IsNullOrEmpty(LaunchingGameIconPath)) LaunchingGameIconPath = ResolveFallbackImage(game);

        if (string.IsNullOrEmpty(LaunchingGameIconPath) && _artwork.HasApiKey)
            _ = FetchLaunchIconAsync(game);

        try
        {
            LaunchedProcessExitedEarly = false;
            _launcher.Launch(game);
            RunningGame = game;
            _library.MarkLaunched(game.Id);
            ReorderLastPlayedFirst();
            SelectedIndex = 0;

            await Task.Delay(LaunchFeedbackMilliseconds);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[JOGO] Falha ao lançar '{game.Title}': {ex.Message}");
        }
        finally
        {
            IsLaunching = false;
        }
    }

    private static string? ResolveIconPath(GameEntry game)
        => !string.IsNullOrEmpty(game.IconImagePath) && File.Exists(game.IconImagePath) ? game.IconImagePath : null;

    private static string? ResolveFallbackImage(GameEntry game)
    {
        string?[] candidates = { game.IconImagePath, game.GridImagePath, game.WideGridImagePath, game.HeroImagePath };
        foreach (var path in candidates)
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
        return null;
    }

    private async Task FetchLaunchIconAsync(GameEntry game)
    {
        if (IsCloudEntry(game)) return; // v0.18.2: Nuvem fora do SteamGridDB (ver FetchMissingArtworkCore)
        try
        {
            if (await _artwork.FetchIconAsync(game))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (IsLaunching && LaunchingGameTitle == game.Title) { LaunchingGameIconPath = null; LaunchingGameIconPath = game.IconImagePath; }
                });
            }
        }
        catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
    }

    public void CloseRunningGame() { if (RunningGame is null) return; _launcher.CloseGame(RunningGame); RunningGame = null; }
    public void NotifyGameExitedExternally()
    {
        if (RunningGame is null) return;
        DebugLog.Write($"[JOGO] '{RunningGame.Title}' liberado externamente — limpando RunningGame.");
        RunningGame = null;
    }

    public void ToggleContextMenu()
    {
        if (IsContextMenuOpen) { CloseContextMenu(); return; }
        if (SelectedTile is null) return;
        ContextMenuTarget = SelectedTile;
        _contextMenuIndex = 0;
        BuildContextMenu(SelectedTile);
        UpdateContextMenuSelection();
        IsContextMenuOpen = true;
    }

    private void BuildContextMenu(GameTileViewModel target)
    {
        ContextMenuItems.Clear(); _contextMenuActions.Clear();
        void Add(string label, Action action) { ContextMenuItems.Add(new ContextMenuItemViewModel(label)); _contextMenuActions.Add(action); }
        var game = target.Game;
        if (game.IsOwnedNotInstalled) { Add("📥 Instalar", () => OpenGameFromMenu(game)); return; }
        if (IsCloudEntry(game)) { Add("☁ Jogar na nuvem", () => { CloseContextMenu(); OpenCloudEntry(game); }); return; }

        Add("▶ Abrir jogo", () => OpenGameFromMenu(game));
        if (HasGameDetails(game)) Add("ℹ Detalhes do jogo", () => OpenGameDetails(game));
        Add(game.IsFavorite ? "💔 Desfavoritar" : "⭐ Favoritar", () => ToggleFavorite(game));
        Add("✏ Editar", () => EditGameRequested?.Invoke(this, game));

        if (game.Source == GameSource.Steam) { if (game.SteamAppId is { } appId) Add("🗑 Excluir jogo", () => RequestDeleteSteamGame(game, appId)); }
        else if (game.Id.StartsWith("xboxfolder_", StringComparison.Ordinal)) Add("🗑 Excluir jogo", () => RequestDeleteXboxGame(game));
        else Add("🗑 Excluir atalho", () => RequestDeleteShortcut(game));

        Add("🎨 Personalizar", () => OpenPersonalizeMenu(game));
    }

    private void OpenPersonalizeMenu(GameEntry game)
    {
        var tile = Games.FirstOrDefault(t => t.Game.Id == game.Id);
        if (tile is null) return;
        ContextMenuTarget = tile; ContextMenuItems.Clear(); _contextMenuActions.Clear();
        void Add(string label, Action action) { ContextMenuItems.Add(new ContextMenuItemViewModel(label)); _contextMenuActions.Add(action); }

        Add("🖼 Capa", () => _ = OpenCoverPicker(game, ArtworkPickerKind.Grid));
        Add("🖼 Capa larga", () => _ = OpenCoverPicker(game, ArtworkPickerKind.WideGrid));
        Add("🖼 Banner", () => _ = OpenCoverPicker(game, ArtworkPickerKind.Hero));
        Add("🖼 Logo", () => _ = OpenCoverPicker(game, ArtworkPickerKind.Logo));
        Add("🖼 Ícone", () => _ = OpenCoverPicker(game, ArtworkPickerKind.Icon));
        Add("↩ Voltar", () => ReopenContextMenu(tile));
        _contextMenuIndex = 0; UpdateContextMenuSelection(); IsContextMenuOpen = true;
    }

    private void ReopenContextMenu(GameTileViewModel tile) { ContextMenuTarget = tile; _contextMenuIndex = 0; BuildContextMenu(tile); UpdateContextMenuSelection(); IsContextMenuOpen = true; }
    private void ToggleFavorite(GameEntry game) { _library.SetFavorite(game.Id, !game.IsFavorite); RebuildGamesFromLibrary(); }

    private void OpenGameFromMenu(GameEntry game)
    {
        var tile = Games.FirstOrDefault(t => t.Game.Id == game.Id);
        if (tile is null) return;
        SelectedIndex = Games.IndexOf(tile);
        _ = LaunchSelected();
    }

    private void OpenConfirmMenu(GameEntry game, string confirmLabel, Action onConfirm)
    {
        ContextMenuTarget = Games.FirstOrDefault(t => t.Game.Id == game.Id); ContextMenuItems.Clear(); _contextMenuActions.Clear();
        void Add(string label, Action action, bool isDanger = false) { ContextMenuItems.Add(new ContextMenuItemViewModel(label, isDanger)); _contextMenuActions.Add(action); }
        Add("❌ Cancelar", () => { }); Add(confirmLabel, onConfirm, isDanger: true);
        _contextMenuIndex = 0; UpdateContextMenuSelection(); IsContextMenuOpen = true;
    }

    private void RequestDeleteShortcut(GameEntry game) { OpenConfirmMenu(game, "🗑 Sim, excluir atalho", () => { _library.RemoveManualGame(game.Id); Refresh(); }); }
    private void RequestDeleteSteamGame(GameEntry game, uint appId) { OpenConfirmMenu(game, "🗑 Sim, abrir desinstalador da Steam", () => _launcher.OpenSteamUninstaller(appId)); }
    private void RequestDeleteXboxGame(GameEntry game) { if (!OperatingSystem.IsWindows()) return; OpenConfirmMenu(game, "🗑 Sim, abrir desinstalador do Windows", () => _launcher.OpenWindowsAppUninstaller(game.InstallDirectory)); }

    public void CloseContextMenu() { IsContextMenuOpen = false; ContextMenuTarget = null; }

    public void MoveContextMenuSelection(int delta)
    {
        if (!IsContextMenuOpen || ContextMenuItems.Count == 0) return;
        _contextMenuIndex = ((_contextMenuIndex + delta) % ContextMenuItems.Count + ContextMenuItems.Count) % ContextMenuItems.Count;
        UpdateContextMenuSelection();
    }

    public void ConfirmContextMenuSelection()
    {
        if (!IsContextMenuOpen || ContextMenuTarget is null) { CloseContextMenu(); return; }
        var index = _contextMenuIndex;
        CloseContextMenu();
        if (index >= 0 && index < _contextMenuActions.Count) _contextMenuActions[index]();
    }

    public void HighlightContextMenuItem(ContextMenuItemViewModel item) { var index = ContextMenuItems.IndexOf(item); if (index < 0) return; _contextMenuIndex = index; UpdateContextMenuSelection(); }
    public void ActivateContextMenuItem(ContextMenuItemViewModel item) { HighlightContextMenuItem(item); if (ContextMenuItems.IndexOf(item) >= 0) ConfirmContextMenuSelection(); }
    public void HighlightGlobalMenuItem(ContextMenuItemViewModel item) { var index = GlobalMenuItems.IndexOf(item); if (index < 0) return; _globalMenuIndex = index; UpdateGlobalMenuSelection(); }

    // Resolve o item clicado pelo próprio índice em vez de delegar para
    // ConfirmGlobalMenuSelection(), que lê o _globalMenuIndex compartilhado
    // com teclado/gamepad — esse índice pode mudar entre o clique e o
    // despacho por reentrância de hover do ponteiro (branch fix/power-menu-dispatch).
    public void ActivateGlobalMenuItem(ContextMenuItemViewModel item)
    {
        var index = GlobalMenuItems.IndexOf(item);
        if (index < 0 || index >= _globalMenuActions.Count || index >= _globalMenuNavigates.Count)
        {
            DebugLog.Write($"Global menu: ignored stale pointer item '{item.Label}' on page {_globalMenuPage}.");
            return;
        }

        _globalMenuIndex = index;
        UpdateGlobalMenuSelection();
        var action = _globalMenuActions[index];
        var navigates = _globalMenuNavigates[index];
        if (!navigates) CloseGlobalMenu();
        action();
    }

    public void ActivateCoverCandidate(ArtworkCandidateViewModel candidate)
    {
        var index = CoverPickerCandidates.IndexOf(candidate);
        if (index < 0) return;
        _coverPickerIndex = index; UpdateCoverPickerSelection(); _ = ConfirmCoverPickerSelection();
    }

    private void UpdateContextMenuSelection() { for (var i = 0; i < ContextMenuItems.Count; i++) ContextMenuItems[i].IsSelected = i == _contextMenuIndex; }

    private enum GlobalMenuPage { Root, Library, Hud, Updates, Help, Power, ConfirmPower }
    private GlobalMenuPage _globalMenuPage = GlobalMenuPage.Root;
    private string _pendingPowerConfirmLabel = string.Empty;
    private Action? _pendingPowerAction;

    private void RequestPowerConfirm(string lowercaseLabel, Action action)
    {
        _pendingPowerConfirmLabel = lowercaseLabel; _pendingPowerAction = action;
        NavigateGlobalMenu(GlobalMenuPage.ConfirmPower); IsGlobalMenuOpen = true;
    }

    private readonly List<bool> _globalMenuNavigates = new();

    private string _globalMenuTitle = "Menu";
    public string GlobalMenuTitle { get => _globalMenuTitle; private set => SetField(ref _globalMenuTitle, value); }

    private string _globalMenuHint = string.Empty;
    public string GlobalMenuHint { get => _globalMenuHint; private set => SetField(ref _globalMenuHint, value); }
    public bool HasGlobalMenuHint => !string.IsNullOrEmpty(_globalMenuHint);

    public void ToggleGlobalMenu() { if (IsGlobalMenuOpen) { CloseGlobalMenu(); return; } OpenGlobalMenuAt(GlobalMenuPage.Root); }

    private void OpenGlobalMenuAt(GlobalMenuPage page)
    {
        _globalMenuPage = page; BuildGlobalMenu();
        if (GlobalMenuItems.Count == 0) return;
        _globalMenuIndex = 0; UpdateGlobalMenuSelection(); IsGlobalMenuOpen = true;
    }

    private void NavigateGlobalMenu(GlobalMenuPage page) { _globalMenuPage = page; BuildGlobalMenu(); _globalMenuIndex = 0; UpdateGlobalMenuSelection(); }
    public void CloseGlobalMenu() => IsGlobalMenuOpen = false;

    public void GlobalMenuBack()
    {
        if (_globalMenuPage != GlobalMenuPage.Root) { NavigateGlobalMenu(GlobalMenuPage.Root); return; }
        CloseGlobalMenu();
    }

    public void MoveGlobalMenuSelection(int delta)
    {
        if (!IsGlobalMenuOpen || GlobalMenuItems.Count == 0) return;
        _globalMenuIndex = ((_globalMenuIndex + delta) % GlobalMenuItems.Count + GlobalMenuItems.Count) % GlobalMenuItems.Count;
        UpdateGlobalMenuSelection();
    }

    public void ConfirmGlobalMenuSelection()
    {
        if (!IsGlobalMenuOpen) return;
        var index = _globalMenuIndex;
        if (index < 0 || index >= _globalMenuActions.Count) return;
        if (_globalMenuNavigates[index]) { _globalMenuActions[index](); return; }
        CloseGlobalMenu(); _globalMenuActions[index]();
    }

    private void UpdateGlobalMenuSelection() { for (var i = 0; i < GlobalMenuItems.Count; i++) GlobalMenuItems[i].IsSelected = i == _globalMenuIndex; }

    private void BuildGlobalMenu()
    {
        GlobalMenuItems.Clear(); _globalMenuActions.Clear(); _globalMenuNavigates.Clear();

        void Add(string label, Action action, bool isDanger = false) { GlobalMenuItems.Add(new ContextMenuItemViewModel(label, isDanger)); _globalMenuActions.Add(action); _globalMenuNavigates.Add(false); }
        void Folder(string label, GlobalMenuPage page) { GlobalMenuItems.Add(new ContextMenuItemViewModel(label)); _globalMenuActions.Add(() => NavigateGlobalMenu(page)); _globalMenuNavigates.Add(true); }

        switch (_globalMenuPage)
        {
            case GlobalMenuPage.Library:
                GlobalMenuTitle = "Biblioteca"; Add("🔄 Recarregar jogos", Refresh);
                if (IsFetchingArtwork) Add("⏳ Baixando capas…", () => { });
                else if (!_artwork.HasApiKey) Add("🖼 Capas (falta a chave)", () => SettingsRequested?.Invoke(this, EventArgs.Empty));
                else Add("🖼 Baixar capas", () =>
                {
                    DebugLog.Write($"[SGDB] MENU: 'Atualizar Capas Automático' clicado. HasApiKey={_artwork.HasApiKey}, IsFetchingArtwork={IsFetchingArtwork}, Games={_library.Games.Count}");
                    FetchMissingArtwork();
                });
                Add("➕ Adicionar jogo", () => AddGameRequested?.Invoke(this, EventArgs.Empty));
                Add("⬇ Downloads", ToggleDownloads); break;

            case GlobalMenuPage.Hud:
                GlobalMenuTitle = "HUD";
                if (_hud is not null && OperatingSystem.IsWindows())
                {
                    for (var i = 0; i < PerformanceHud.PresetNames.Length; i++)
                    {
                        var preset = i; var marker = _hud.Preset == preset ? "●" : "○";
                        Add($"{marker}  {PerformanceHud.PresetNames[preset]}", () => SetOverlayPreset(preset));
                    }
                }
                if (_performanceOverlay.IsInstalled) Add(_performanceOverlay.IsRunning ? "⚙ RTSS: rodando" : "⚙ RTSS: iniciar", () => { if (!_performanceOverlay.IsRunning) _performanceOverlay.EnsureRunning(); });
                else Add("⬇ RTSS: baixar (necessário pro HUD)", _performanceOverlay.OpenDownloadPage);
                break;

            case GlobalMenuPage.Updates:
                GlobalMenuTitle = "Atualizações";
                if (_updates is { IsInstalled: true } && !IsUpdateReady) Add("🔄 Verificar launcher", () => _ = CheckForUpdatesAsync(userInitiated: true));
                Add("🪟 Windows e drivers", () => SystemUpdateRequested?.Invoke(this, EventArgs.Empty));
                Add("🛠 Driver manual (.inf)", () => DriverUpdateRequested?.Invoke(this, EventArgs.Empty));
                break;

            case GlobalMenuPage.Help:
                GlobalMenuTitle = "Ajuda";
                Add("🛟 Central de Ajuda", () => HelpCenterRequested?.Invoke(this, EventArgs.Empty));
                Add("⌨ Atalhos", OpenShortcutsHelp);
                Add("ℹ Sobre", OpenAbout);
                break;

            case GlobalMenuPage.Power:
                GlobalMenuTitle = "Energia"; Add("🌙 Desligar tela", () => TurnOffScreenRequested?.Invoke(this, EventArgs.Empty));
                Add("🔄 Reiniciar", () => RequestPowerConfirm("reiniciar o aparelho", () => RestartDeviceRequested?.Invoke(this, EventArgs.Empty)));
                Add("⏻ Desligar", () => RequestPowerConfirm("desligar o aparelho", () => ShutdownDeviceRequested?.Invoke(this, EventArgs.Empty)));
                Add("🔁 Reiniciar Launcher", () => RestartLauncherRequested?.Invoke(this, EventArgs.Empty));
                Add("🚪 Fechar launcher", () => QuitRequested?.Invoke(this, EventArgs.Empty)); break;

            case GlobalMenuPage.ConfirmPower:
                GlobalMenuTitle = "Confirmar?"; Folder("❌ Cancelar", GlobalMenuPage.Power); Add($"⚠ Sim, {_pendingPowerConfirmLabel}", () => _pendingPowerAction?.Invoke(), isDanger: true); break;

            default:
                GlobalMenuTitle = "Menu"; Add("🎮 Quick Settings", () => OpenOverlayRequested?.Invoke(this, EventArgs.Empty));
                if (IsUpdateReady) Add(RunningGame is null ? "⬆ Atualizar agora" : "⬆ Atualização pronta (feche o jogo)", ApplyUpdate);
                if (RunningGame is not null) Add("⏹ Fechar o jogo", CloseRunningGame);
                Folder("📚 Biblioteca", GlobalMenuPage.Library); Folder("📊 HUD", GlobalMenuPage.Hud);
                // v0.17 (05/11, pedido do usuário): "Layout da tela" virou "TEMAS" e
                // subiu pro menu raiz (antes só aparecia dentro de Configurações); e
                // "Configurações" deixou de ser uma pasta com um único item de
                // verdade (Preferências) — agora é ação direta, economiza um clique.
                // v0.18 (pedido do usuário, 05/11): "TEMAS" (maiúsculo, plural) virou
                // "Tema" — mais curto e consistente com o resto dos itens do menu raiz.
                Add("🎨 Tema", () => LayoutPickerRequested?.Invoke(this, EventArgs.Empty));
                Add("⚙ Configurações", () => SettingsRequested?.Invoke(this, EventArgs.Empty));
                // v0.18 (pedido do usuário, 05/11): Gerenciador de armazenamento e
                // Saúde do aparelho, direto no menu raiz junto de TEMAS/Configurações
                // — são "sobre o aparelho", não pertencem dentro de outra pasta.
                Add("💾 Armazenamento", () => StorageManagerRequested?.Invoke(this, EventArgs.Empty));
                // v0.18: contador de alertas ainda não vistos (zera ao abrir a tela).
                var unseenAlerts = HealthAlerts.UnseenCount;
                Add(unseenAlerts > 0 ? $"🌡 Saúde do Aparelho ({unseenAlerts} {(unseenAlerts == 1 ? "alerta" : "alertas")})" : "🌡 Saúde do Aparelho",
                    () => DeviceHealthRequested?.Invoke(this, EventArgs.Empty));
                // v0.18: o atalho "☁ Xbox Cloud Gaming" saiu daqui (pedido do usuário: "tira
                // ele do menu") e virou a aba "Nuvem" da biblioteca. Aqui só aparece o
                // "Fechar", e só enquanto o xCloud aberto pelo launcher estiver rodando.
                if (XCloudLauncher.IsRunning) Add("⏹ Fechar Xbox Cloud Gaming", () => _ = Task.Run(XCloudLauncher.CloseAll));
                Folder("🔄 Atualizações", GlobalMenuPage.Updates);
                Folder("🛟 Ajuda", GlobalMenuPage.Help); Folder("⚡ Energia", GlobalMenuPage.Power);
                break;
        }
        GlobalMenuHint = _globalMenuPage switch
        {
            GlobalMenuPage.Hud => "No jogo: L3+R3 (ou Ctrl+Alt+O) troca o HUD",
            GlobalMenuPage.Updates when _updates is { IsInstalled: true } => $"Versão atual: v{_updates.CurrentVersionText}",
            GlobalMenuPage.Root when IsUpdateReady => $"Nova versão pronta: v{PendingUpdateVersion}",
            GlobalMenuPage.Root when RunningGame is not null => $"Jogando: {RunningGame.Title}",
            _ => string.Empty,
        };
        OnPropertyChanged(nameof(HasGlobalMenuHint));
    }

    private bool _isAboutOpen;
    public bool IsAboutOpen { get => _isAboutOpen; private set => SetField(ref _isAboutOpen, value); }
    public void OpenAbout() => IsAboutOpen = true;
    public void CloseAbout() => IsAboutOpen = false;

    public string AboutVersionText
    {
        get
        {
            var full = AppVersionText; var start = full.IndexOf('v'); var end = full.IndexOf(' ', Math.Max(start, 0));
            return start >= 0 ? "Versão " + (end > start ? full[(start + 1)..end] : full[(start + 1)..]) : full;
        }
    }

    public void TogglePowerMenu() { if (IsGlobalMenuOpen && _globalMenuPage == GlobalMenuPage.Power) { CloseGlobalMenu(); return; } OpenGlobalMenuAt(GlobalMenuPage.Power); }

    private bool _isDownloadsOpen;
    public bool IsDownloadsOpen { get => _isDownloadsOpen; private set => SetField(ref _isDownloadsOpen, value); }

    public ObservableCollection<DownloadProgressInfo> ActiveDownloads { get; } = new();
    public bool HasActiveDownloads => ActiveDownloads.Count > 0;
    public bool NoActiveDownloads => !HasActiveDownloads;

    private DownloadProgressInfo? PrimaryActiveDownload => ActiveDownloads.FirstOrDefault(d => d.BytesDownloaded > 0 && d.BytesDownloaded < d.BytesToDownload) ?? ActiveDownloads.FirstOrDefault();

    public double TopBarDownloadPercent => PrimaryActiveDownload?.PercentComplete ?? 0;
    public string TopBarDownloadPercentText => PrimaryActiveDownload is { } d ? $"{d.PercentComplete:0}%" : string.Empty;

    public string TopBarDownloadToolTip
    {
        get
        {
            var tip = "Downloads (Y no controle)";
            if (PrimaryActiveDownload is not { } d) return tip;
            tip += $"\nBaixando: {d.Title} — {d.PercentComplete:0}%";
            var queued = ActiveDownloads.Count - 1; if (queued > 0) tip += $" (+{queued} na fila)";
            return tip;
        }
    }

    public ObservableCollection<OwnedSteamGame> OwnedNotInstalledGames { get; } = new();
    public bool HasSteamWebApiCredentials => _library.HasSteamWebApiCredentials;
    public bool NoSteamWebApiCredentials => !HasSteamWebApiCredentials;

    private bool _isLoadingOwnedGames;
    public bool IsLoadingOwnedGames { get => _isLoadingOwnedGames; private set => SetField(ref _isLoadingOwnedGames, value); }

    private string? _ownedGamesErrorMessage;
    public string? OwnedGamesErrorMessage { get => _ownedGamesErrorMessage; private set { if (SetField(ref _ownedGamesErrorMessage, value)) OnPropertyChanged(nameof(HasOwnedGamesError)); } }
    public bool HasOwnedGamesError => !string.IsNullOrEmpty(OwnedGamesErrorMessage);

    private string? _notInstalledTabMessage;
    public string? NotInstalledTabMessage { get => _notInstalledTabMessage; private set { if (SetField(ref _notInstalledTabMessage, value)) OnPropertyChanged(nameof(HasNotInstalledTabMessage)); } }
    public bool HasNotInstalledTabMessage => !string.IsNullOrEmpty(NotInstalledTabMessage);

    private void UpdateNotInstalledTabMessage()
    {
        if (_activeFilter == LibraryFilter.Cloud)
        {
            CloudFilterEmptyText = HasCloudFilter && Games.Count == 0 && _cloudGames.Count > 0
                ? "Nenhum jogo da nuvem com esse filtro. B limpa a busca e a letra."
                : "";
            if (HasCloudFilter) { NotInstalledTabMessage = null; return; }
            // Só o bloco fixo na tela = catálogo ainda não chegou.
            NotInstalledTabMessage = Games.Count > 1 ? null
                : _isLoadingCloudCatalog ? "Carregando o catálogo do Xbox Cloud Gaming..."
                : _cloudCatalogFailed ? "Não consegui baixar o catálogo de nuvem agora (sem internet?). O bloco \"Xbox Cloud Gaming\" abre o site normalmente."
                : null;
            return;
        }
        if (_activeFilter != LibraryFilter.NotInstalled) { NotInstalledTabMessage = null; return; }
        if (!HasSteamWebApiCredentials) NotInstalledTabMessage = "Configure a Steam Web API key e o SteamID64 na tela de Configurações pra ver aqui os jogos da sua conta que ainda não estão instalados.";
        else if (IsLoadingOwnedGames) NotInstalledTabMessage = "Carregando jogos da sua conta...";
        else if (HasOwnedGamesError) NotInstalledTabMessage = OwnedGamesErrorMessage;
        else if (Games.Count == 0) NotInstalledTabMessage = "Nenhum jogo da sua conta Steam está faltando instalar aqui.";
        else NotInstalledTabMessage = null;
    }

    public void ToggleDownloads() { if (IsDownloadsOpen) { CloseDownloads(); return; } RefreshActiveDownloads(); IsDownloadsOpen = true; }

    public void OpenSteamDownloads()
    {
        try
        {
            if (OperatingSystem.IsWindows()) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "steam://open/downloads", UseShellExecute = true });
            else if (OperatingSystem.IsLinux()) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "xdg-open", Arguments = "steam://open/downloads", UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
    }

    public void CloseDownloads() => IsDownloadsOpen = false;
    private readonly Dictionary<string, (long Bytes, DateTime At, double? Speed)> _downloadSamples = new();

    // v0.18.3 (pente fino de desempenho): a varredura lê TODOS os manifestos da
    // Steam (um arquivo por jogo instalado) e rodava na thread da tela a cada 6 s
    // com o launcher parado — outro engasgo periódico. Agora lê em segundo plano
    // e só aplica o resultado na tela; nunca duas varreduras ao mesmo tempo.
    public void RefreshActiveDownloads()
    {
        if (_downloadsScanInFlight) return;
        _downloadsScanInFlight = true;
        _ = RefreshActiveDownloadsCoreAsync();
    }

    private async Task RefreshActiveDownloadsCoreAsync()
    {
        IReadOnlyList<DownloadProgressInfo> downloads;
        try { downloads = await Task.Run(() => _library.GetActiveDownloads()); }
        catch (Exception ex) { DebugLog.Swallowed(ex); _downloadsScanInFlight = false; return; }
        _downloadsScanInFlight = false;

        var now = DateTime.UtcNow; ActiveDownloads.Clear();
        foreach (var download in downloads)
        {
            if (_downloadSamples.TryGetValue(download.Title, out var previous))
            {
                var seconds = (now - previous.At).TotalSeconds;
                var delta = download.BytesDownloaded - previous.Bytes;
                if (delta > 0 && seconds > 0.5)
                {
                    var speed = delta / seconds;
                    download.BytesPerSecond = speed;
                    _downloadSamples[download.Title] = (download.BytesDownloaded, now, speed);
                }
                else download.BytesPerSecond = (now - previous.At).TotalSeconds < 30 ? previous.Speed : null;
            }
            else _downloadSamples[download.Title] = (download.BytesDownloaded, now, null);
            ActiveDownloads.Add(download);
        }
        OnPropertyChanged(nameof(HasActiveDownloads)); OnPropertyChanged(nameof(NoActiveDownloads));
        OnPropertyChanged(nameof(TopBarDownloadPercent)); OnPropertyChanged(nameof(TopBarDownloadPercentText));
        OnPropertyChanged(nameof(TopBarDownloadToolTip));
    }

    public async Task RefreshOwnedGames()
    {
        if (!HasSteamWebApiCredentials) return;
        IsLoadingOwnedGames = true; OwnedGamesErrorMessage = null; UpdateNotInstalledTabMessage();
        try
        {
            var owned = await _library.GetOwnedButNotInstalledSteamGamesAsync();
            if (!IsDownloadsOpen && _activeFilter != LibraryFilter.NotInstalled) return;
            OwnedNotInstalledGames.Clear(); foreach (var game in owned) OwnedNotInstalledGames.Add(game);
            if (_activeFilter == LibraryFilter.NotInstalled) { RebuildGamesFromLibrary(); _ = FetchMissingArtworkForOwnedGames(); }
        }
        catch (Exception ex) { OwnedGamesErrorMessage = $"Erro ao buscar jogos da conta: {ex.Message}"; }
        finally { IsLoadingOwnedGames = false; UpdateNotInstalledTabMessage(); }
    }

    private void FetchMissingArtwork() => _ = FetchMissingArtworkCore(userInitiated: true);
    private void FetchMissingArtworkBackground() => _ = FetchMissingArtworkCore(userInitiated: false);

    private async Task FetchMissingArtworkCore(bool userInitiated)
    {
        if (!_artwork.HasApiKey)
        {
            DebugLog.Write("[SGDB] ABORT: sem API key → abrindo Settings");
            if (userInitiated) SettingsRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (userInitiated)
        {
            try { _ownedArtworkCts?.Cancel(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
            DebugLog.Write("[SGDB] Download manual aguardando a fila atual (até 60s).");
            if (!await _artworkSemaphore.WaitAsync(TimeSpan.FromSeconds(60)))
            {
                DebugLog.Write("[SGDB] TIMEOUT aguardando fila de artwork após 60s — comando manual não iniciado.");
                return;
            }
        }
        else
        {
            if (!await _artworkSemaphore.WaitAsync(TimeSpan.FromSeconds(60)))
            {
                DebugLog.Write("[SGDB] TIMEOUT background aguardando outro download — abortando com segurança");
                return;
            }
        }

        _ownedArtworkCts = new CancellationTokenSource();
        var ct = _ownedArtworkCts.Token;
        _artworkPauseGate.Set();
        IsFetchingArtwork = true;
        
        _failedAssets.Clear();

        ArtworkDownloadPopupWindow? modal = null;
        Task? dialogTask = null;
        if (userInitiated)
        {
            try
            {
                modal = new ArtworkDownloadPopupWindow();
                modal.Configure(0);
                modal.CancelRequested += (_, _) =>
                {
                    DebugLog.Write("[SGDB] Cancel solicitado pelo usuário");
                    try { _ownedArtworkCts?.Cancel(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
                    _artworkPauseGate.Set();
                };
                modal.PauseToggled += (_, _) =>
                {
                    if (_artworkPauseGate.IsSet) { _artworkPauseGate.Reset(); DebugLog.Write("[SGDB] Download PAUSADO."); }
                    else { _artworkPauseGate.Set(); DebugLog.Write("[SGDB] Download RETOMADO."); }
                };
                var owner = GetMainWindow();
                if (owner is not null) dialogTask = modal.ShowDialog(owner);
                else { modal.Show(); dialogTask = Task.CompletedTask; }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[SGDB] Falha ao abrir modal: {ex.Message}");
                modal = null; dialogTask = null;
            }
        }

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int ok = 0, skipped = 0;

            _library.Refresh();
            // v0.18.2 (pedido do usuário): a Nuvem NUNCA entra na busca automática do
            // SteamGridDB — as capas dela vêm do catálogo da Microsoft, em pasta
            // própria. Hoje as entradas da Nuvem nem estão em _library.Games; o filtro
            // é trava extra pra isso não mudar sem querer no futuro.
            var installed = _library.Games.Where(g => !IsCloudEntry(g)).ToList();

            List<GameEntry> ownedEntries = new();
            if (userInitiated && HasSteamWebApiCredentials)
            {
                try
                {
                    var owned = await _library.GetOwnedButNotInstalledSteamGamesAsync(ct);
                    OwnedNotInstalledGames.Clear();
                    foreach (var og in owned) OwnedNotInstalledGames.Add(og);
                    ownedEntries = owned.Select(_library.BuildEntryForOwnedGame).ToList();
                    DebugLog.Write($"[SGDB] Owned games incluídos na checagem: {ownedEntries.Count}");
                }
                catch (OperationCanceledException swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
                catch (Exception ex) { DebugLog.Write($"[SGDB] Falha ao carregar owned games: {ex.Message}"); }
            }

            static bool HasPendingWork(GameEntry g) =>
                NeedsDownload(g.GridImagePath) || NeedsDownload(g.HeroImagePath) ||
                NeedsDownload(g.LogoImagePath) || NeedsDownload(g.WideGridImagePath) ||
                NeedsDownload(g.IconImagePath);

            var installedPending = installed.Where(HasPendingWork).ToList();
            var ownedPending = ownedEntries.Where(HasPendingWork).ToList();

            var total = installedPending.Count + ownedPending.Count;
            if (total == 0)
            {
                DebugLog.Write("[SGDB] Nada a baixar — todas as capas já estão no cache.");
                modal?.UpdateProgress(0, 0, "Todas as capas já estão atualizadas");
                modal?.CloseForReal();
                if (dialogTask is not null) { try { await dialogTask; } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); } }
                IsFetchingArtwork = false;
                _artworkPauseGate.Set();
                _ownedArtworkCts?.Dispose();
                _ownedArtworkCts = null;
                try { _artworkSemaphore.Release(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
                DebugLog.Write("[SGDB] Lock de artwork liberado (nenhuma capa pendente).");
                return;
            }

            DebugLog.Write($"[SGDB] Diagnóstico: {installedPending.Count} instalados pendentes, {ownedPending.Count} owned pendentes.");
            foreach (var g in installedPending)
            {
                DebugLog.Write($"[SGDB] Pendente: {g.Title} | Grid:{NeedsDownload(g.GridImagePath)} Hero:{NeedsDownload(g.HeroImagePath)} Logo:{NeedsDownload(g.LogoImagePath)} Wide:{NeedsDownload(g.WideGridImagePath)} Icon:{NeedsDownload(g.IconImagePath)}");
            }

            if (modal != null) modal.Configure(total);
            DebugLog.Write($"[SGDB] === Atualização iniciada ({installedPending.Count} instalados + {ownedPending.Count} owned = {total} pendentes) === userInitiated={userInitiated}");

            int processed = 0;
            bool cancelledInPhase1 = false;

            foreach (var game in installedPending)
            {
                if (ct.IsCancellationRequested) { DebugLog.Write("[SGDB] Cancelado (fase 1)."); cancelledInPhase1 = true; break; }
                try { await Task.Run(() => _artworkPauseGate.Wait(ct), ct); }
                catch (OperationCanceledException) { cancelledInPhase1 = true; break; }

                processed++;
                if (modal != null) modal.UpdateProgress(processed, total, game.Title);

                bool anyWork = false;
                try
                {
                    if (NeedsDownload(game.GridImagePath) && !_failedAssets.Contains($"{game.Id}_grid")) { anyWork = true; if (await _artwork.FetchGridAsync(game, ct)) ok++; else _failedAssets.Add($"{game.Id}_grid"); }
                    if (ct.IsCancellationRequested) break;
                    if (NeedsDownload(game.HeroImagePath) && !_failedAssets.Contains($"{game.Id}_hero")) { anyWork = true; if (await _artwork.FetchHeroAsync(game, ct)) ok++; else _failedAssets.Add($"{game.Id}_hero"); }
                    if (ct.IsCancellationRequested) break;
                    if (NeedsDownload(game.LogoImagePath) && !_failedAssets.Contains($"{game.Id}_logo")) { anyWork = true; if (await _artwork.FetchLogoAsync(game, ct)) ok++; else _failedAssets.Add($"{game.Id}_logo"); }
                    if (ct.IsCancellationRequested) break;
                    if (NeedsDownload(game.WideGridImagePath) && !_failedAssets.Contains($"{game.Id}_widegrid")) { anyWork = true; if (await _artwork.FetchWideGridAsync(game, ct)) ok++; else _failedAssets.Add($"{game.Id}_widegrid"); }
                    if (ct.IsCancellationRequested) break;
                    if (NeedsDownload(game.IconImagePath) && !_failedAssets.Contains($"{game.Id}_icon")) { anyWork = true; if (await _artwork.FetchIconAsync(game, ct)) ok++; else _failedAssets.Add($"{game.Id}_icon"); }
                }
                catch (OperationCanceledException) { DebugLog.Write("[SGDB] Cancelado (exceção fase 1)."); cancelledInPhase1 = true; break; }
                catch (Exception ex) { DebugLog.Write($"[SGDB] '{game.Title}' EXCEPTION: {ex.Message}"); }

                if (!anyWork) { skipped++; continue; }
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { cancelledInPhase1 = true; break; }
            }

            if (!cancelledInPhase1 && !ct.IsCancellationRequested)
            {
                foreach (var game in ownedPending)
                {
                    if (ct.IsCancellationRequested) { DebugLog.Write("[SGDB] Cancelado (fase 2)."); break; }
                    try { await Task.Run(() => _artworkPauseGate.Wait(ct), ct); }
                    catch (OperationCanceledException) { break; }

                    processed++;
                    if (modal != null) modal.UpdateProgress(processed, total, game.Title);

                    bool anyWork = false;
                    try
                    {
                        if (NeedsDownload(game.GridImagePath) && !_failedAssets.Contains($"{game.Id}_grid")) { anyWork = true; if (await _artwork.FetchGridAsync(game, ct)) ok++; else _failedAssets.Add($"{game.Id}_grid"); }
                        if (ct.IsCancellationRequested) break;
                        if (NeedsDownload(game.HeroImagePath) && !_failedAssets.Contains($"{game.Id}_hero")) { anyWork = true; if (await _artwork.FetchHeroAsync(game, ct)) ok++; else _failedAssets.Add($"{game.Id}_hero"); }
                        if (ct.IsCancellationRequested) break;
                        if (NeedsDownload(game.LogoImagePath) && !_failedAssets.Contains($"{game.Id}_logo")) { anyWork = true; if (await _artwork.FetchLogoAsync(game, ct)) ok++; else _failedAssets.Add($"{game.Id}_logo"); }
                        if (ct.IsCancellationRequested) break;
                        if (NeedsDownload(game.WideGridImagePath) && !_failedAssets.Contains($"{game.Id}_widegrid")) { anyWork = true; if (await _artwork.FetchWideGridAsync(game, ct)) ok++; else _failedAssets.Add($"{game.Id}_widegrid"); }
                        if (ct.IsCancellationRequested) break;
                        if (NeedsDownload(game.IconImagePath) && !_failedAssets.Contains($"{game.Id}_icon")) { anyWork = true; if (await _artwork.FetchIconAsync(game, ct)) ok++; else _failedAssets.Add($"{game.Id}_icon"); }
                    }
                    catch (OperationCanceledException) { DebugLog.Write("[SGDB] Cancelado (exceção fase 2)."); break; }
                    catch (Exception ex) { DebugLog.Write($"[SGDB] '{game.Title}' owned EXCEPTION: {ex.Message}"); }

                    if (!anyWork) { skipped++; continue; }
                    try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
                }
            }

            DebugLog.Write($"[SGDB] === Concluído em {sw.Elapsed.TotalSeconds:F1}s === Processados={processed}/{total}, Sucessos={ok}, Skipped={skipped}");
            if (ok > 0) Refresh();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[SGDB] EXCEPTION FATAL: {ex.GetType().Name} - {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            IsFetchingArtwork = false;
            _artworkPauseGate.Set();
            _ownedArtworkCts?.Dispose();
            _ownedArtworkCts = null;
            try { _artworkSemaphore.Release(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
            DebugLog.Write("[SGDB] Lock de artwork liberado.");
            modal?.CloseForReal();
            if (dialogTask is not null) { try { await dialogTask; } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); } }
        }
    }

    private async Task FetchMissingArtworkForOwnedGames()
    {
        if (!_artwork.HasApiKey) return;
        if (_isFetchingOwnedArtwork) return;

        if (!await _artworkSemaphore.WaitAsync(0))
        {
            DebugLog.Write("[SGDB] ABORT owned: lock ocupado");
            return;
        }

        _isFetchingOwnedArtwork = true;
        _ownedArtworkCts = new CancellationTokenSource();
        var ct = _ownedArtworkCts.Token;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int downloaded = 0, total = 0;
        DebugLog.Write($"[SGDB] === Owned-not-installed iniciado ({OwnedNotInstalledGames.Count} jogos) ===");

        try
        {
            foreach (var owned in OwnedNotInstalledGames.ToList())
            {
                if (ct.IsCancellationRequested) break;
                if (_activeFilter != LibraryFilter.NotInstalled) return;

                total++;
                var entry = _library.BuildEntryForOwnedGame(owned);
                bool anyWork = false;
                try
                {
                    if (NeedsDownload(entry.GridImagePath)) { anyWork = true; if (await _artwork.FetchGridAsync(entry, ct)) downloaded++; }
                    if (ct.IsCancellationRequested) break;
                    if (NeedsDownload(entry.HeroImagePath)) { anyWork = true; if (await _artwork.FetchHeroAsync(entry, ct)) downloaded++; }
                    if (ct.IsCancellationRequested) break;
                    if (NeedsDownload(entry.LogoImagePath)) { anyWork = true; if (await _artwork.FetchLogoAsync(entry, ct)) downloaded++; }
                    if (ct.IsCancellationRequested) break;
                    if (NeedsDownload(entry.WideGridImagePath)) { anyWork = true; if (await _artwork.FetchWideGridAsync(entry, ct)) downloaded++; }
                    if (ct.IsCancellationRequested) break;
                    if (NeedsDownload(entry.IconImagePath)) { anyWork = true; if (await _artwork.FetchIconAsync(entry, ct)) downloaded++; }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { DebugLog.Write($"[SGDB] '{entry.Title}' owned EXCEPTION: {ex.Message}"); }

                if (anyWork) { try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; } }
            }

            DebugLog.Write($"[SGDB] === Owned concluído em {sw.Elapsed.TotalSeconds:F1}s (total={total}, baixados={downloaded}) ===");
            if (_activeFilter == LibraryFilter.NotInstalled) RebuildGamesFromLibrary();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[SGDB] Owned FATAL: {ex.GetType().Name} - {ex.Message}");
        }
        finally
        {
            _isFetchingOwnedArtwork = false;
            _ownedArtworkCts?.Dispose();
            _ownedArtworkCts = null;
            try { _artworkSemaphore.Release(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
            DebugLog.Write("[SGDB] Owned lock liberado.");
        }
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
            return d.MainWindow;
        return null;
    }

    public void InstallOwnedGame(OwnedSteamGame game) => _launcher.OpenSteamInstaller(game.AppId);

    private async Task OpenCoverPicker(GameEntry game, ArtworkPickerKind kind)
    {
        if (IsCloudEntry(game)) return; // v0.18.2: Nuvem fora do SteamGridDB
        CoverPickerTitle = kind switch { ArtworkPickerKind.WideGrid => $"Capas largas e banners · {game.Title}", ArtworkPickerKind.Hero => $"Banner · {game.Title}", ArtworkPickerKind.Logo => $"Logo · {game.Title}", ArtworkPickerKind.Icon => $"Ícone · {game.Title}", _ => $"Capa · {game.Title}", };
        _coverPickerKind = kind; _coverPickerGame = game; _coverPickerIndex = 0; CoverPickerErrorMessage = null; CoverPickerCandidates.Clear(); NotifyCoverPickerResultFlags(); IsCoverPickerLoading = true; IsCoverPickerOpen = true;
        try
        {
            var candidates = new List<ArtworkCandidate>();
            
            if (kind == ArtworkPickerKind.WideGrid)
            {
                var wides = await _artwork.GetWideGridCandidatesAsync(game, FilterAnimatedOnly);
                var heroes = await _artwork.GetHeroCandidatesAsync(game, FilterAnimatedOnly);
                candidates.AddRange(wides);
                candidates.AddRange(heroes);
            }
            else
            {
                candidates = (kind switch
                {
                    ArtworkPickerKind.Hero => await _artwork.GetHeroCandidatesAsync(game, FilterAnimatedOnly),
                    ArtworkPickerKind.Logo => await _artwork.GetLogoCandidatesAsync(game, FilterAnimatedOnly),
                    ArtworkPickerKind.Icon => await _artwork.GetIconCandidatesAsync(game, FilterAnimatedOnly),
                    _ => await _artwork.GetGridCandidatesAsync(game, FilterAnimatedOnly),
                }).ToList();
            }

            if (!IsCoverPickerOpen || _coverPickerGame != game) return;
            foreach (var candidate in candidates) CoverPickerCandidates.Add(new ArtworkCandidateViewModel(candidate));
            UpdateCoverPickerSelection();
        }
        catch (Exception ex) { CoverPickerErrorMessage = $"Erro ao buscar no SteamGridDB: {ex.Message}"; }
        finally { IsCoverPickerLoading = false; NotifyCoverPickerResultFlags(); }
    }

    public void CloseCoverPicker() { IsCoverPickerOpen = false; _coverPickerGame = null; CoverPickerCandidates.Clear(); CoverPickerErrorMessage = null; NotifyCoverPickerResultFlags(); }
    private void NotifyCoverPickerResultFlags() { OnPropertyChanged(nameof(CoverPickerHasResults)); OnPropertyChanged(nameof(CoverPickerIsEmpty)); }
    public const int CoverPickerColumns = 5;

    public void MoveCoverPickerSelection(int delta) { if (!IsCoverPickerOpen || CoverPickerCandidates.Count == 0) return; _coverPickerIndex = ((_coverPickerIndex + delta) % CoverPickerCandidates.Count + CoverPickerCandidates.Count) % CoverPickerCandidates.Count; UpdateCoverPickerSelection(); }
    public void MoveCoverPickerSelectionVertical(int rowDelta) { if (!IsCoverPickerOpen || CoverPickerCandidates.Count == 0) return; var newIndex = _coverPickerIndex + rowDelta * CoverPickerColumns; if (newIndex < 0 || newIndex >= CoverPickerCandidates.Count) return; _coverPickerIndex = newIndex; UpdateCoverPickerSelection(); }

    public async Task ConfirmCoverPickerSelection()
    {
        if (!IsCoverPickerOpen || _coverPickerGame is null || CoverPickerCandidates.Count == 0) { CloseCoverPicker(); return; }
        var chosen = CoverPickerCandidates[_coverPickerIndex].Candidate; var game = _coverPickerGame; var kind = _coverPickerKind; CloseCoverPicker();
        try { var applied = kind switch { ArtworkPickerKind.WideGrid => await _artwork.ApplyWideGridCandidateAsync(game, chosen), ArtworkPickerKind.Hero => await _artwork.ApplyHeroCandidateAsync(game, chosen), ArtworkPickerKind.Logo => await _artwork.ApplyLogoCandidateAsync(game, chosen), ArtworkPickerKind.Icon => await _artwork.ApplyIconCandidateAsync(game, chosen), _ => await _artwork.ApplyGridCandidateAsync(game, chosen), }; }
        catch (Exception ex) { DebugLog.Write($"[SGDB] Falha ao aplicar capa escolhida para '{game.Title}': {ex.Message}"); }
        Refresh();
    }

    private void UpdateCoverPickerSelection() { for (var i = 0; i < CoverPickerCandidates.Count; i++) CoverPickerCandidates[i].IsSelected = i == _coverPickerIndex; }

    public void AddManualGame(GameEntry game) { _library.AddManualGame(game); Refresh(); var tile = Games.FirstOrDefault(t => t.Game.Id == game.Id); if (tile is not null) SelectedIndex = Games.IndexOf(tile); }

    public void Refresh() { _library.Refresh(); RebuildGamesFromLibrary(); }
    public void CycleNextFilter() => CycleFilter(1);
    public void CyclePreviousFilter() => CycleFilter(-1);

    private void CycleFilter(int delta)
    {
        var currentIndex = Array.IndexOf(FilterOrder, _activeFilter);
        if (currentIndex < 0) currentIndex = 0;
        var nextIndex = ((currentIndex + delta) % FilterOrder.Length + FilterOrder.Length) % FilterOrder.Length;
        SetFilter(FilterOrder[nextIndex]);
    }

    private void SetFilter(LibraryFilter filter)
    {
        if (_activeFilter == filter) return;
        _activeFilter = filter;
        OnPropertyChanged(nameof(IsFilterAllActive)); OnPropertyChanged(nameof(IsFilterFavoritesActive)); OnPropertyChanged(nameof(IsFilterComputerActive)); OnPropertyChanged(nameof(IsFilterEmulatorsActive)); OnPropertyChanged(nameof(IsFilterApplicationsActive)); OnPropertyChanged(nameof(IsFilterNotInstalledActive)); OnPropertyChanged(nameof(IsFilterCloudActive));
        RebuildSubFilters(); RebuildGamesFromLibrary();
        if (filter == LibraryFilter.NotInstalled) _ = RefreshOwnedGames();
        if (filter == LibraryFilter.Cloud) _ = RefreshCloudCatalogAsync();
    }

    private bool MatchesActiveFilter(GameEntry game) => _activeFilter switch
    {
        LibraryFilter.Favorites => game.IsFavorite, LibraryFilter.Applications => game.IsApplication, LibraryFilter.All => !game.IsApplication,
        LibraryFilter.Computer => !game.IsApplication && game.Source != GameSource.Emulator && MatchesSubFilters(game),
        LibraryFilter.Emulators => !game.IsApplication && game.Source == GameSource.Emulator && MatchesSubFilters(game), _ => true,
    };

    public void ReloadTiles() { var selected = SelectedIndex; RebuildGamesFromLibrary(); if (selected >= 0 && Games.Count > 0) SelectedIndex = Math.Min(selected, Games.Count - 1); }

    private void RebuildGamesFromLibrary()
    {
        var previouslySelectedId = SelectedTile?.Game.Id;
        Games.Clear();
        
        if (_activeFilter == LibraryFilter.NotInstalled) 
        { 
            foreach (var owned in OwnedNotInstalledGames) 
                Games.Add(new GameTileViewModel(_library.BuildEntryForOwnedGame(owned), _steamStore)); 
        }
        else if (_activeFilter == LibraryFilter.Cloud)
        {
            foreach (var entry in BuildCloudEntries())
                Games.Add(new GameTileViewModel(entry, _steamStore));
        }
        else 
        { 
            foreach (var game in _library.Games) 
            { 
                if (MatchesActiveFilter(game)) 
                    Games.Add(new GameTileViewModel(game, _steamStore)); // 🟢 Passa o _steamStore
            } 
        }
        
        ReorderLastPlayedFirst(); UpdateNotInstalledTabMessage();
        var restoredIndex = previouslySelectedId is null ? 0 : Math.Max(0, Games.ToList().FindIndex(t => t.Game.Id == previouslySelectedId));
        SelectedIndex = Games.Count > 0 ? restoredIndex : -1;
        if (Games.Count > 0) SelectedTile = Games[SelectedIndex];
    }

    private void UpdateFocus() { for (var i = 0; i < Games.Count; i++) { Games[i].IsFocused = i == _selectedIndex; Games[i].CarouselDistance = i - _selectedIndex; } }

    private void ReorderLastPlayedFirst()
    {
        if (Games.Count == 0) return;
        GameTileViewModel? lastPlayedTile = null; DateTimeOffset? latest = null;
        foreach (var tile in Games) { tile.IsLastPlayed = false; if (tile.Game.LastPlayedUtc is { } played && (latest is null || played > latest)) { latest = played; lastPlayedTile = tile; } }
        if (lastPlayedTile is null) return;
        lastPlayedTile.IsLastPlayed = true;
        var index = Games.IndexOf(lastPlayedTile);
        if (index > 0) Games.Move(index, 0);
    }

    /// <summary>
    /// v0.18.4 (crash "Call from invalid thread" ao fechar/abrir jogo — nos logs
    /// desde 05/11 de manhã: Pokémon/Eden ao fechar, PvZ do Game Pass ao abrir):
    /// GameExited vem do Process.Exited, que roda numa thread do pool, não na
    /// da tela. Limpar RunningGame ali disparava o PropertyChanged da
    /// MainWindow fora da thread da tela (lê IsVisible, restaura da bandeja) e
    /// o Avalonia derrubava o app. Agora tudo roda na thread da tela.
    /// </summary>
    private void OnGameExited(object? sender, GameExitedEventArgs e)
        => Dispatcher.UIThread.Post(() => HandleGameExited(e));

    // Processo lançado que sai em menos que isso não é o jogo: é um lançador
    // intermediário (jogo do Game Pass abre pelo gamelaunchhelper, que sai na
    // hora e deixa o jogo rodando). Antes isso limpava RunningGame meio
    // segundo depois de abrir, com o jogo ainda na tela.
    private static readonly TimeSpan IntermediateLauncherExit = TimeSpan.FromSeconds(15);

    /// <summary>
    /// v0.18.4: o processo lançado saiu cedo e o jogo ficou a cargo do vigia de
    /// janela (igual jogo da Steam). A MainWindow usa isto pra liberar o
    /// launcher se a janela do jogo nunca aparecer.
    /// </summary>
    public bool LaunchedProcessExitedEarly { get; private set; }

    private void HandleGameExited(GameExitedEventArgs e)
    {
        try
        {
            if (RunningGame?.Id == e.Game.Id && e.Duration < IntermediateLauncherExit)
            {
                LaunchedProcessExitedEarly = true;
                DebugLog.Write($"[JOGO] Processo lançado por '{e.Game.Title}' saiu em {e.Duration.TotalSeconds:F1}s — tratado como lançador intermediário; o vigia de janela decide quando o jogo fechou.");
                return;
            }

            _library.RecordPlaySession(e.Game.Id, e.Duration);
            if (RunningGame?.Id == e.Game.Id) RunningGame = null;
            Refresh();
        }
        catch (Exception ex) { DebugLog.Write($"[JOGO] Falha ao tratar saída de '{e.Game.Title}': {ex}"); }
    }
}