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

public sealed partial class MainViewModel : ViewModelBase
{
    private readonly SessionTrackerService _sessionTracker = new();
    private readonly ILibraryService _library;
    private readonly IGameLauncher _launcher;
    private readonly IArtworkService _artwork;
    private readonly IPerformanceOverlayService _performanceOverlay;
    private readonly UpdateService? _updates;
    private readonly PerformanceHud? _hud;
    private readonly ISteamOwnedGamesClient? _steamOwnedGames;
    private readonly ISteamAchievementsClient? _steamAchievements;

    private readonly HandheldLauncher.Services.Syncthing.SyncthingService _syncthingService;
    private readonly DispatcherTimer _syncthingTimer;
   
    
    // 🟢 Instância única do SteamStoreService
   private readonly SteamStoreService _steamStore;
    private readonly IgdbService _igdbService;

    // No Windows: LibreHardwareMonitor. No Linux/macOS: substituto inerte — o
    // pacote NuGet só traz binários para Windows e o publish linux-x64 não o
    // copiava (crash do beta.5). Ver Services/HardwareMonitor.cs.
    private readonly IHardwareMonitorService _monitorService = HardwareMonitor.Create();
    private readonly HWiNFOSharedMemoryService _hwInfoService = new();
    private DateTime _lastHwInfoPollTime = DateTime.MinValue;
    private bool _hwInfoWasAvailable;
    private DispatcherTimer? _hardwareTimer;
    private long _hardwareTicks;

    /// <summary>v0.28: a tela informa se o launcher está na frente (sensores lidos a cada 2 s só então).</summary>
    public Func<bool>? SensorsFastCheck { get; set; }

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
    private int _realTimeGpuWatts = 0;
    private int _currentFps = 0;   // opcional: HWiNFO expõe FPS do RTSS como sensor

    public string TopBarTdpText => _targetTdpWatts > 0 ? $"{_targetTdpWatts} W" : "-- W";
    public string TopBarTdpToolTip
{
    get
    {
        var parts = new System.Collections.Generic.List<string> { $"Alvo: {_targetTdpWatts} W" };
        if (_realTimeTdpWatts > 0) parts.Add($"CPU: {_realTimeTdpWatts} W");
        if (_realTimeGpuWatts > 0) parts.Add($"GPU: {_realTimeGpuWatts} W");
        return parts.Count == 1 ? $"TDP Configurado: {_targetTdpWatts} W" : string.Join(" | ", parts);
    }
}
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
        ISteamAchievementsClient? steamAchievements = null,
        HandheldLauncher.Services.Syncthing.SyncthingService? syncthingService = null) // 🟢 CORRIGIDO AQUI
    {
        _syncthingService = syncthingService;
        _library = library; _launcher = launcher; _artwork = artwork;
        _performanceOverlay = performanceOverlay; _updates = updates; _hud = hud;
        _steamOwnedGames = steamOwnedGames; _steamAchievements = steamAchievements;

        _syncthingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _syncthingTimer.Tick += (_, _) =>
        {
            using var __meter = HandheldLauncher.Core.Services.Power.ActivityMeter.Measure("syncthing");
            UpdateSyncthingStatus();
        };
        _syncthingTimer.Start();

        var _savedSettingsForIgdb = HandheldLauncher.Core.Services.AppSettingsStore.Load();
        _syncthingEnabled = _savedSettingsForIgdb.SyncthingEnabled;
        UpdateSyncthingStatus();

        _igdbService = new IgdbService(
            _savedSettingsForIgdb.IgdbClientId ?? "",
            _savedSettingsForIgdb.IgdbClientSecret ?? ""
        );

        // 🟢 INICIALIZA O STEAM STORE COM AS CHAVES AQUI:
        _steamStore = new SteamStoreService(_savedSettingsForIgdb.SteamWebApiKey, _savedSettingsForIgdb.SteamId64);

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

        _hwInfoOnlyWithHud = OperatingSystem.IsWindows() && AppSettingsStore.Load().HwInfoOnlyWithHud;
        if (!_hwInfoOnlyWithHud) _ = Task.Run(() => EnsureHwInfoRunning());

                _hardwareTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _hardwareTimer.Tick += async (_, _) =>
        {
            // v0.18.3: leitura de CPU fora da thread da tela; se a anterior
            // ainda não terminou, pula este tick.
            SyncHwInfoWithHud();
            if (_hardwareReadInFlight) return;
            // v0.28 (bateria): os sensores (LibreHardwareMonitor) eram lidos a cada 2 s para sempre,
            // mesmo com o launcher escondido. Rápido só com o launcher na frente ou o HUD ligado;
            // no resto, a cada 10 s (o alerta de temperatura continua valendo).
            _hardwareTicks++;
            var fast = (_hud?.Preset ?? 0) > 0 || (SensorsFastCheck?.Invoke() ?? true);
            if (!fast && _hardwareTicks % 5 != 0) return;
            _hardwareReadInFlight = true;
            float? temp;
            try
            {
                temp = await Task.Run(() =>
                {
                    using var __meter = HandheldLauncher.Core.Services.Power.ActivityMeter.Measure("sensores (temperatura)");
                    return _monitorService.GetCpuTemperature();
                });
            }
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

            // v0.20 — Watts CPU/GPU via HWiNFO Shared Memory. ExtractMetrics
            // faz matching por hint + bounds; HUD só atualiza quando muda.
            using var __meter = HandheldLauncher.Core.Services.Power.ActivityMeter.Measure("sensores (HWiNFO)");
            if (_hwInfoService.TryRead(out var hwInfo) && hwInfo is not null)
            {
                _hwInfoWasAvailable = true;
                if (hwInfo.PollTime != _lastHwInfoPollTime)
                {
                    _lastHwInfoPollTime = hwInfo.PollTime;
                    var metrics = HWiNFOSharedMemoryService.ExtractMetrics(hwInfo);
                    var changed = false;

                    if (metrics.CpuPackageWatts is { } cpuW)
                    {
                        var v = (int)Math.Round(cpuW);
                        if (v != _realTimeTdpWatts) { _realTimeTdpWatts = v; changed = true; }
                    }
                    if (metrics.GpuWatts is { } gpuW)
                    {
                        var v = (int)Math.Round(gpuW);
                        if (v != _realTimeGpuWatts) { _realTimeGpuWatts = v; changed = true; }
                    }

                    if (changed)
                    {
                        OnPropertyChanged(nameof(TopBarTdpToolTip));
                        if (_hud != null)
                        {
                            if (metrics.CpuPackageWatts.HasValue) _hud.RealTdpText  = $"{_realTimeTdpWatts} W";
                            if (metrics.GpuWatts.HasValue)        _hud.RealGpuWatts = $"{_realTimeGpuWatts} W";
                        }
                    }
                }
            }
            else if (_hwInfoWasAvailable)
            {
                _hwInfoWasAvailable = false;
                _realTimeTdpWatts = 0;
                _realTimeGpuWatts = 0;
                OnPropertyChanged(nameof(TopBarTdpToolTip));
                if (_hud != null) { _hud.RealTdpText = ""; _hud.RealGpuWatts = ""; }
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
    private string _syncthingStatusText = "Parado";
    public string SyncthingStatusText
    {
        get => _syncthingStatusText;
        set => SetField(ref _syncthingStatusText, value);
    }

    private string _syncthingDetailsText = "Sincronização desativada.";
    public string SyncthingDetailsText
    {
        get => _syncthingDetailsText;
        set => SetField(ref _syncthingDetailsText, value);
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
                
                try
                {
                    var settings = AppSettingsStore.Load();
                    settings.SyncthingEnabled = value;
                    AppSettingsStore.Save(settings);
                }
                catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
            }
        }
    }


    private void UpdateSyncthingStatus()
    {
        if (_syncthingService != null && _syncthingService.IsRunning)
        {
            SyncthingStatusText = "🟢 Rodando";
            SyncthingDetailsText = "Sincronização ativa em segundo plano.";
        }
        else
        {
            SyncthingStatusText = "🔴 Parado";
            SyncthingDetailsText = "Sincronização desativada.";
        }
    }
    
    private static bool NeedsDownload(string? path)
        => string.IsNullOrEmpty(path) || !File.Exists(path);

    // v0.28 (bateria): HWiNFO só com o HUD ligado (ver AppSettings.HwInfoOnlyWithHud).
    private bool _hwInfoOnlyWithHud;
    private bool _hwInfoWanted;
    private DateTime? _hudOffSince;
    private volatile int _hwInfoPidStartedByUs;
    private static readonly TimeSpan HwInfoCloseAfterHudOff = TimeSpan.FromMinutes(1);

    private void SyncHwInfoWithHud()
    {
        if (!_hwInfoOnlyWithHud) return;
        var hudOn = (_hud?.Preset ?? 0) > 0;
        if (hudOn)
        {
            _hudOffSince = null;
            if (_hwInfoWanted) return;
            _hwInfoWanted = true;
            _ = Task.Run(() => EnsureHwInfoRunning());
        }
        else if (_hwInfoWanted)
        {
            _hudOffSince ??= DateTime.UtcNow;
            if (DateTime.UtcNow - _hudOffSince.Value < HwInfoCloseAfterHudOff) return;
            _hwInfoWanted = false;
            _hudOffSince = null;
            _ = Task.Run(CloseHwInfoIfOurs);
        }
    }

    /// <summary>Fecha o HWiNFO só se foi o launcher que abriu (o do usuário fica).</summary>
    private void CloseHwInfoIfOurs()
    {
        var pid = _hwInfoPidStartedByUs;
        if (pid == 0) return;
        _hwInfoPidStartedByUs = 0;
        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.ProcessName.StartsWith("HWiNFO", StringComparison.OrdinalIgnoreCase)) return;
            p.Kill();
            DebugLog.Write("[HWiNFO] Fechado (HUD desligado) — economiza bateria.");
        }
        catch (ArgumentException) { /* já tinha fechado */ }
        catch (Exception ex) { DebugLog.Write($"[HWiNFO] Não consegui fechar: {ex.Message}"); }
    }

    private void EnsureHwInfoRunning()
    {
        try
        {
            // v0.24.3-beta.6: o HWiNFO64 é um .exe de Windows. No Linux não há
            // nada para arrancar — e sem esta guarda a procura pelos caminhos
            // "C:\Program Files\..." acabava por gravar HwInfoDisabled=true nas
            // settings do utilizador logo no primeiro arranque.
            if (!OperatingSystem.IsWindows()) return;

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

            var running = Process.GetProcessesByName("HWiNFO64");
            var alreadyOpen = running.Length > 0;
            foreach (var r in running) r.Dispose();
            if (_hwInfoOnlyWithHud && alreadyOpen)
            {
                DebugLog.Write("[HWiNFO] Já estava aberto (pelo usuário) — usando esse, sem abrir outro.");
                return;
            }

            DebugLog.Write($"[HWiNFO] Iniciando em segundo plano: {exePath}");
            var psi = new ProcessStartInfo
            {
                FileName = exePath, Arguments = "-s", UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory
            };
            using var started = Process.Start(psi);
            if (started is not null) _hwInfoPidStartedByUs = started.Id;
        }
        catch (Exception ex) { DebugLog.Write($"[HWiNFO] Erro ao tentar iniciar: {ex.Message}"); }
    }

        public void Dispose()
    {
        _hardwareTimer?.Stop();
        if (_hwInfoOnlyWithHud) CloseHwInfoIfOurs();
        _healthCheckTimer?.Stop();
        _syncthingTimer?.Stop(); 
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
        if (SetField(ref _selectedTile, value))
        {
            OnPropertyChanged(nameof(CanOpenSelectedDetails));
            if (value is not null)
            {
                _ = value.LoadDescriptionAsync();
                value.RefreshEnergy(); // v0.31: o resumo do diário pode ter chegado depois
                _ = value.LoadReviewAsync();   // 🟢 NOVO
            }
        }
    }
}

/// <summary>
/// v0.25.2: o popup de lançamento do Cartoon só mostra o botão "Info" quando o
/// jogo tem página de detalhes (nuvem/atalhos/não instalados ficam sem ele).
/// </summary>
public bool CanOpenSelectedDetails => SelectedTile?.Game is { } game && HasGameDetails(game);

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
        try { var settings = AppSettingsStore.Load(); settings.OverlayPreset = _hud.Preset; settings.OverlayPresetChosen = true; AppSettingsStore.Save(settings); }
        catch (Exception ex) { DebugLog.Write($"[HUD] Falha ao salvar preset do overlay: {ex.Message}"); }
    }

    public void SetOverlayPreset(int preset)
    {
        if (_hud is null || !OperatingSystem.IsWindows()) return;
        _hud.Preset = preset;
        try { var settings = AppSettingsStore.Load(); settings.OverlayPreset = _hud.Preset; settings.OverlayPresetChosen = true; AppSettingsStore.Save(settings); }
        catch (Exception ex) { DebugLog.Write($"[HUD] Falha ao salvar preset do overlay: {ex.Message}"); }
    }

    public void ClearOverlayNow() { if (OperatingSystem.IsWindows()) _hud?.ClearNow(); }
    public bool IsPerformanceHudEnabled => _hud is not null && _hud.Preset > 0;
    public int CurrentHudPreset => _hud?.Preset ?? 0;

    public void SetPerformanceHudEnabled(bool enabled)
    {
        if (_hud is null || !OperatingSystem.IsWindows()) return;
        _hud.Preset = enabled ? Math.Max(1, _hud.Preset) : 0;
        try { var settings = AppSettingsStore.Load(); settings.OverlayPreset = _hud.Preset; settings.OverlayPresetChosen = true; AppSettingsStore.Save(settings); }
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
                // 18/11: jogos manuais de emulador de Switch (ex.: Ryujinx) também entram aqui.
                SubFilters.Add(new SubFilterViewModel("Switch", "#FFFFFF", "#E4000F", g => g.Id.StartsWith("eden_", StringComparison.Ordinal)
                    || (g.Source == GameSource.Emulator && HandheldLauncher.Core.Services.ManualLaunchTarget.IsSwitchEmulator(g.ExecutablePath))));
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

    /// <summary>
    /// Pede à janela principal que abra as Configurações. Usado pela
    /// engrenagem do Quick Settings (não tem referência ao MainWindow).
    /// </summary>
    public void RequestSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);
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

    /// <summary>
    /// v0.25.5 (Camada 1): pedido da guarda de elevação no lançamento de um jogo.
    /// A MainWindow mostra o diálogo e resolve via <see cref="ResolveElevationGuard"/>.
    /// </summary>
    public sealed record ElevationGuardRequest(string GameTitle, string TdpMode);
    public event EventHandler<ElevationGuardRequest>? ElevationGuardRequested;

    public void RequestOpenOverlay() { OpenOverlayRequested?.Invoke(this, EventArgs.Empty); }

    public void RefreshArtworkState()
    {
        _artwork.ReloadSettings();
        OnPropertyChanged(nameof(CanFetchArtwork));
        if (IsGlobalMenuOpen) BuildGlobalMenu();
    }

    public void ApplyEntryEdits(GameEntry game, string? title, bool isApplication) { _library.SaveEntryEdits(game.Id, title, isApplication); RebuildGamesFromLibrary(); }

    private static bool HasGameDetails(GameEntry game) => !game.IsOwnedNotInstalled && !game.IsApplication && !IsCloudEntry(game);

    private void OpenGameDetails(GameEntry game) { CloseContextMenu(); GameDetailsRequested?.Invoke(this, game); }

    public void OpenGameDetailsForSelected()
    {
        if (SelectedTile?.Game is { } game && HasGameDetails(game)) GameDetailsRequested?.Invoke(this, game);
    }

    public GameDetailsViewModel CreateGameDetailsViewModel(GameEntry game) => new(game, _steamOwnedGames, _steamStore);
    
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

        // v0.25.5 (Camada 1): sem administrador o TDP/ventoinha não aplicam — avisa
        // antes de lançar e oferece reiniciar elevado (UAC uma vez).
        if (await GuardElevationForLaunchAsync(game)) return;

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
            var settings = AppSettingsStore.Load();
            var tdpMode = settings?.TdpMode ?? "Auto";
            _sessionTracker.StartSession(game.Title, tdpMode);
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

    private bool _elevationGuardShownThisSession;
    private TaskCompletionSource<bool>? _elevationGuardTcs;

    /// <summary>
    /// Devolve true quando o launcher vai reiniciar elevado (e portanto NÃO deve
    /// lançar o jogo agora). Pergunta uma vez por sessão e só quando o TDP está
    /// disponível neste aparelho e o launcher corre sem administrador.
    /// </summary>
    private async Task<bool> GuardElevationForLaunchAsync(GameEntry game)
    {
        if (_elevationGuardShownThisSession) return false;
        if (!OperatingSystem.IsWindows()) return false;
        if (ElevationService.IsElevated) return false;
        if (!RyzenSmuService.IsSupportedPlatform() || !RyzenSmuService.IsSupportedCpu()) return false;

        _elevationGuardShownThisSession = true;

        var settings = AppSettingsStore.Load();
        var tdp = string.IsNullOrWhiteSpace(settings.TdpMode) ? "Normal" : settings.TdpMode;

        DebugLog.Write($"[ELEVACAO] Guarda de lançamento: sem admin, TDP '{tdp}' pode não aplicar — a perguntar ao usuário.");
        return await RequestElevationGuardAsync(game.Title, tdp);
    }

    private async Task<bool> RequestElevationGuardAsync(string gameTitle, string tdpMode)
    {
        var handler = ElevationGuardRequested;
        if (handler is null) return false;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _elevationGuardTcs = tcs;
        try
        {
            handler(this, new ElevationGuardRequest(gameTitle, tdpMode));
            return await tcs.Task;
        }
        finally
        {
            if (ReferenceEquals(_elevationGuardTcs, tcs)) _elevationGuardTcs = null;
        }
    }

    /// <summary>Chamado pela MainWindow com o resultado do diálogo (true = reiniciar elevado).</summary>
    public void ResolveElevationGuard(bool restartAsAdmin)
    {
        var tcs = _elevationGuardTcs;
        _elevationGuardTcs = null;
        tcs?.TrySetResult(restartAsAdmin);
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

        public void CloseRunningGame()
    {
        if (RunningGame is null) return;
        var game = RunningGame;
        _launcher.CloseGame(game);
        // Re-checa: se CloseGame disparou GameExited de forma síncrona,
        // HandleGameExited já limpou RunningGame e encerrou a sessão.
        if (RunningGame?.Id == game.Id)
        {
            _sessionTracker.EndSession(game.Title);
            RunningGame = null;
        }
    }    
        public void NotifyGameExitedExternally()
    {
        if (RunningGame is null) return;
        DebugLog.Write($"[JOGO] '{RunningGame.Title}' liberado externamente — limpando RunningGame.");
        _sessionTracker.EndSession(RunningGame.Title);
        RunningGame = null;
    }

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
            {
                var entry = _library.BuildEntryForOwnedGame(owned);
                Games.Add(new GameTileViewModel(entry, _steamStore, _igdbService, _steamOwnedGames)); // 🟢 Adicionado _steamOwnedGames
            }
        }
        else if (_activeFilter == LibraryFilter.Cloud)
        {
            foreach (var entry in BuildCloudEntries())
                Games.Add(new GameTileViewModel(entry, _steamStore, _igdbService, _steamOwnedGames)); // 🟢 Adicionado _steamOwnedGames
        }
        else
        {
            foreach (var game in _library.Games)
            {
                if (MatchesActiveFilter(game))
                    Games.Add(new GameTileViewModel(game, _steamStore, _igdbService, _steamOwnedGames)); // 🟢 Adicionado _steamOwnedGames
            }
        }

        ReorderLastPlayedFirst();
        UpdateNotInstalledTabMessage();
        var restoredIndex = previouslySelectedId is null
            ? 0
            : Math.Max(0, Games.ToList().FindIndex(t => t.Game.Id == previouslySelectedId));
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
            if (RunningGame?.Id == e.Game.Id)
            {
                _sessionTracker.EndSession(e.Game.Title);
                RunningGame = null;
            }
            Refresh();
        }
        catch (Exception ex) { DebugLog.Write($"[JOGO] Falha ao tratar saída de '{e.Game.Title}': {ex}"); }
    }
}