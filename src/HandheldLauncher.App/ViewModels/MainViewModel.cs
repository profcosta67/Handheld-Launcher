using System.Collections.ObjectModel;
using System.Windows.Input;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Platform;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;
using HandheldLauncher.Core.Services.Artwork;
using Avalonia.Threading;
using HandheldLauncher.App.Services;

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

    // Monitor de Temperatura
    private readonly HardwareMonitorService _monitorService = new();
    private DispatcherTimer? _hardwareTimer;

    private string _cpuTemperature = "--°C";
    public string CpuTemperature
    {
        get => _cpuTemperature;
        private set => SetField(ref _cpuTemperature, value);
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
        _library = library;
        _launcher = launcher;
        _artwork = artwork;
        _performanceOverlay = performanceOverlay;
        _updates = updates;
        _hud = hud;
        _steamOwnedGames = steamOwnedGames;
        _steamAchievements = steamAchievements;
        _launcher.GameExited += OnGameExited;

        Games = new ObservableCollection<GameTileViewModel>();

        MoveNextCommand = new RelayCommand(MoveNext, () => Games.Count > 1);
        MovePreviousCommand = new RelayCommand(MovePrevious, () => Games.Count > 1);
        LaunchSelectedCommand = new RelayCommand(LaunchSelected, () => SelectedTile is not null);
        CloseRunningGameCommand = new RelayCommand(CloseRunningGame, () => RunningGame is not null);
        RefreshCommand = new RelayCommand(Refresh);
        FetchArtworkCommand = new RelayCommand(FetchMissingArtwork, () => CanFetchArtwork);

        SetFilterAllCommand = new RelayCommand(() => SetFilter(LibraryFilter.All));
        SetFilterFavoritesCommand = new RelayCommand(() => SetFilter(LibraryFilter.Favorites));
        SetFilterComputerCommand = new RelayCommand(() => SetFilter(LibraryFilter.Computer));
        SetFilterEmulatorsCommand = new RelayCommand(() => SetFilter(LibraryFilter.Emulators));
        SetFilterApplicationsCommand = new RelayCommand(() => SetFilter(LibraryFilter.Applications));
        SetFilterNotInstalledCommand = new RelayCommand(() => SetFilter(LibraryFilter.NotInstalled));

        // 🟢 INICIA O MONITOR DE TEMPERATURA AQUI 🟢
                _hardwareTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _hardwareTimer.Tick += (_, _) =>
        {
            var temp = _monitorService.GetCpuTemperature();
            if (temp.HasValue && temp.Value > 0)
            {
                CpuTemperature = $"{temp.Value:F0}°C";
                DebugLog.Write($"[TEMPERATURA] CPU: {CpuTemperature}");
                
                // 🟢 ESSA É A LINHA MÁGICA QUE MANDA PRO HUD
                if (_hud != null)
                {
                    _hud.CpuTemperature = CpuTemperature;
                }
            }
        };
        _hardwareTimer.Start();
        // 🟢 FIM DO MONITOR DE TEMPERATURA 🟢

        RebuildGamesFromLibrary();
    }

    public ObservableCollection<GameTileViewModel> Games { get; }

    private int _selectedIndex = -1;
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (Games.Count == 0)
            {
                SetField(ref _selectedIndex, -1);
                SelectedTile = null;
                return;
            }

            var clamped = Math.Clamp(value, 0, Games.Count - 1);
            if (SetField(ref _selectedIndex, clamped))
            {
                UpdateFocus();
                SelectedTile = Games[clamped];
            }
        }
    }

    private GameTileViewModel? _selectedTile;
    public GameTileViewModel? SelectedTile
    {
        get => _selectedTile;
        private set => SetField(ref _selectedTile, value);
    }

    private string _updateStatusText = string.Empty;

    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set
        {
            if (SetField(ref _updateStatusText, value))
            {
                OnPropertyChanged(nameof(HasUpdateStatus));
            }
        }
    }

    public bool HasUpdateStatus => !string.IsNullOrEmpty(_updateStatusText);
    public bool IsUpdateReady => _updates?.IsUpdateReady ?? false;
    public string? PendingUpdateVersion => _updates?.PendingVersionText;

    public string AppVersionText
    {
        get
        {
            string version;
            if (_updates is { IsInstalled: true })
            {
                version = _updates.CurrentVersionText;
            }
            else
            {
                var info = System.Reflection.Assembly.GetExecutingAssembly()
                    .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                    .FirstOrDefault()?.InformationalVersion ?? "dev";
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
        while (true)
        {
            await CheckForUpdatesAsync(userInitiated: false);
            await Task.Delay(TimeSpan.FromHours(6));
        }
    }

    public async Task CheckForUpdatesAsync(bool userInitiated)
    {
        if (_updates is not { IsInstalled: true }) return;
        if (IsUpdateReady) return;
        if (!userInitiated && RunningGame is not null) return;

        if (userInitiated) UpdateStatusText = "Verificando atualizações…";

        var version = await _updates.CheckAndDownloadAsync();

        if (version is not null)
        {
            UpdateStatusText = $"⬆ v{version} pronta — toque pra reiniciar";
            OnPropertyChanged(nameof(IsUpdateReady));
            OnPropertyChanged(nameof(PendingUpdateVersion));
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
        if (_updates is not { IsInstalled: true })
        {
            return "Atualização automática só funciona no launcher instalado pelo Setup.exe.";
        }

        await CheckForUpdatesAsync(userInitiated: true);
        return IsUpdateReady
            ? $"v{PendingUpdateVersion} baixada — feche as Configurações e toque em ⬆ na barra de cima (ou no menu) pra reiniciar."
            : "Você já está na versão mais recente (ou sem internet).";
    }

    public bool UpdatesInstalled => _updates is { IsInstalled: true };
    public string UpdateWindowVersionText => $"Versão v{PendingUpdateVersion} pronta pra instalar";

    public string PendingUpdateNotesText
    {
        get
        {
            var markdown = _updates?.PendingReleaseNotesMarkdown;
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return "Essa versão não trouxe notas detalhadas — confira no GitHub (botão abaixo) se quiser ver o que mudou.";
            }

            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            var kept = new List<string>();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith('#')) continue;

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

        if (RunningGame is not null)
        {
            UpdateStatusText = $"⬆ v{PendingUpdateVersion} pronta — feche o jogo antes de reiniciar";
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            _hud?.ClearNow();
            HandheldLauncher.App.Input.GameInputHelper.StopHelperProcess();
        }
        
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

        try
        {
            var settings = AppSettingsStore.Load();
            settings.OverlayPreset = _hud.Preset;
            AppSettingsStore.Save(settings);
        }
        catch { }
    }

    public void SetOverlayPreset(int preset)
    {
        if (_hud is null || !OperatingSystem.IsWindows()) return;
        _hud.Preset = preset;
        try
        {
            var settings = AppSettingsStore.Load();
            settings.OverlayPreset = _hud.Preset;
            AppSettingsStore.Save(settings);
        }
        catch { }
    }

    public void ClearOverlayNow()
    {
        if (OperatingSystem.IsWindows())
        {
            _hud?.ClearNow();
        }
    }

    private string _batteryStatusText = string.Empty;
    public string BatteryStatusText
    {
        get => _batteryStatusText;
        set => SetField(ref _batteryStatusText, value);
    }

    private string _clockText = string.Empty;
    public string ClockText
    {
        get => _clockText;
        set => SetField(ref _clockText, value);
    }

    private GameEntry? _runningGame;
    public GameEntry? RunningGame
    {
        get => _runningGame;
        private set => SetField(ref _runningGame, value);
    }

    private bool _isLaunching;
    public bool IsLaunching
    {
        get => _isLaunching;
        private set => SetField(ref _isLaunching, value);
    }

    private string _launchingGameTitle = string.Empty;
    public string LaunchingGameTitle
    {
        get => _launchingGameTitle;
        private set => SetField(ref _launchingGameTitle, value);
    }

    private string? _launchingGameIconPath;
    public string? LaunchingGameIconPath
    {
        get => _launchingGameIconPath;
        private set => SetField(ref _launchingGameIconPath, value);
    }

    private const int LaunchFeedbackMilliseconds = 2500;

    private bool _isFetchingArtwork;
    public bool IsFetchingArtwork
    {
        get => _isFetchingArtwork;
        private set
        {
            if (SetField(ref _isFetchingArtwork, value))
            {
                OnPropertyChanged(nameof(CanFetchArtwork));
            }
        }
    }

    public bool CanFetchArtwork => _artwork.HasApiKey && !IsFetchingArtwork;

    private bool _isContextMenuOpen;
    public bool IsContextMenuOpen
    {
        get => _isContextMenuOpen;
        private set => SetField(ref _isContextMenuOpen, value);
    }

    private GameTileViewModel? _contextMenuTarget;
    public GameTileViewModel? ContextMenuTarget
    {
        get => _contextMenuTarget;
        private set => SetField(ref _contextMenuTarget, value);
    }

    private int _contextMenuIndex;

    public ObservableCollection<ContextMenuItemViewModel> ContextMenuItems { get; } = new();
    private readonly List<Action> _contextMenuActions = new();
    private enum ArtworkPickerKind { Grid, WideGrid, Hero, Logo, Icon }
    private enum LibraryFilter { All, Favorites, Computer, Emulators, Applications, NotInstalled }

    private static readonly LibraryFilter[] FilterOrder =
    {
        LibraryFilter.Favorites,
        LibraryFilter.All,
        LibraryFilter.Computer,
        LibraryFilter.Emulators,
        LibraryFilter.Applications,
        LibraryFilter.NotInstalled,
    };

    private LibraryFilter _activeFilter = LibraryFilter.All;

    public bool IsFilterAllActive => _activeFilter == LibraryFilter.All;
    public bool IsFilterFavoritesActive => _activeFilter == LibraryFilter.Favorites;
    public bool IsFilterComputerActive => _activeFilter == LibraryFilter.Computer;
    public bool IsFilterEmulatorsActive => _activeFilter == LibraryFilter.Emulators;
    public bool IsFilterApplicationsActive => _activeFilter == LibraryFilter.Applications;
    public bool IsFilterNotInstalledActive => _activeFilter == LibraryFilter.NotInstalled;

    public ObservableCollection<SubFilterViewModel> SubFilters { get; } = new();
    public bool HasSubFilters => SubFilters.Count > 0;

    private bool _isSubFilterRowFocused;
    public bool IsSubFilterRowFocused
    {
        get => _isSubFilterRowFocused;
        private set => SetField(ref _isSubFilterRowFocused, value);
    }

    private int _subFilterCursor;

    public void FocusSubFilterRow()
    {
        if (SubFilters.Count == 0) return;
        _subFilterCursor = Math.Clamp(_subFilterCursor, 0, SubFilters.Count - 1);
        IsSubFilterRowFocused = true;
        UpdateSubFilterCursorFlags();
    }

    public void LeaveSubFilterRow()
    {
        if (!IsSubFilterRowFocused) return;
        IsSubFilterRowFocused = false;
        UpdateSubFilterCursorFlags();
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

    public void ToggleSubFilter(SubFilterViewModel subFilter)
    {
        subFilter.IsChecked = !subFilter.IsChecked;
        RebuildGamesFromLibrary();
    }

    private void UpdateSubFilterCursorFlags()
    {
        for (var i = 0; i < SubFilters.Count; i++)
        {
            SubFilters[i].IsCursor = IsSubFilterRowFocused && i == _subFilterCursor;
        }
    }

    private void RebuildSubFilters()
    {
        SubFilters.Clear();
        _subFilterCursor = 0;
        IsSubFilterRowFocused = false;

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
    public bool IsGlobalMenuOpen
    {
        get => _isGlobalMenuOpen;
        private set => SetField(ref _isGlobalMenuOpen, value);
    }

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
    public event EventHandler? RestoreShellRequested;
    public event EventHandler? OpenOverlayRequested;

    public void RequestOpenOverlay()
    {
        OpenOverlayRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyEntryEdits(GameEntry game, string? title, bool isApplication)
    {
        _library.SaveEntryEdits(game.Id, title, isApplication);
        RebuildGamesFromLibrary();
    }

    private static bool HasGameDetails(GameEntry game) => game.Source == GameSource.Steam && game.SteamAppId is not null;

    private void OpenGameDetails(GameEntry game)
    {
        CloseContextMenu();
        GameDetailsRequested?.Invoke(this, game);
    }

    public void OpenGameDetailsForSelected()
    {
        if (SelectedTile?.Game is { } game && HasGameDetails(game))
        {
            GameDetailsRequested?.Invoke(this, game);
        }
    }

    public GameDetailsViewModel CreateGameDetailsViewModel(GameEntry game)
        => new(game, _steamOwnedGames, _steamAchievements);

    private bool _isCoverPickerOpen;
    public bool IsCoverPickerOpen
    {
        get => _isCoverPickerOpen;
        private set => SetField(ref _isCoverPickerOpen, value);
    }

    private string _coverPickerTitle = string.Empty;
    public string CoverPickerTitle
    {
        get => _coverPickerTitle;
        private set => SetField(ref _coverPickerTitle, value);
    }

    private bool _isCoverPickerLoading;
    public bool IsCoverPickerLoading
    {
        get => _isCoverPickerLoading;
        private set
        {
            if (SetField(ref _isCoverPickerLoading, value))
            {
                OnPropertyChanged(nameof(CoverPickerIsEmpty));
            }
        }
    }

    public ObservableCollection<ArtworkCandidateViewModel> CoverPickerCandidates { get; } = new();
    public bool CoverPickerHasResults => CoverPickerCandidates.Count > 0;
    public bool CoverPickerIsEmpty => !IsCoverPickerLoading && !CoverPickerHasError && CoverPickerCandidates.Count == 0;

    private string? _coverPickerErrorMessage;
    public string? CoverPickerErrorMessage
    {
        get => _coverPickerErrorMessage;
        private set
        {
            if (SetField(ref _coverPickerErrorMessage, value))
            {
                OnPropertyChanged(nameof(CoverPickerHasError));
                OnPropertyChanged(nameof(CoverPickerIsEmpty));
            }
        }
    }

    public bool CoverPickerHasError => !string.IsNullOrEmpty(CoverPickerErrorMessage);

    private int _coverPickerIndex;
    public int CoverPickerIndex => _coverPickerIndex;

    private bool _filterAnimatedOnly;
    public bool FilterAnimatedOnly
    {
        get => _filterAnimatedOnly;
        set
        {
            if (SetField(ref _filterAnimatedOnly, value))
            {
                if (IsCoverPickerOpen && _coverPickerGame != null)
                {
                    OpenCoverPicker(_coverPickerGame, _coverPickerKind);
                }
            }
        }
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

    public void MoveNext() => SelectedIndex++;
    public void MovePrevious() => SelectedIndex--;

    public async void LaunchSelected()
    {
        if (SelectedTile is null) return;
        var game = SelectedTile.Game;

        if (game.IsOwnedNotInstalled)
        {
            if (game.SteamAppId is { } ownedAppId) _launcher.OpenSteamInstaller(ownedAppId);
            return;
        }

        LaunchingGameTitle = game.Title;
        LaunchingGameIconPath = game.IconImagePath;
        IsLaunching = true;

        if (string.IsNullOrEmpty(game.IconImagePath) && _artwork.HasApiKey)
        {
            _ = FetchLaunchIconAsync(game);
        }

        _launcher.Launch(game);
        RunningGame = game;

        _library.MarkLaunched(game.Id);
        ReorderLastPlayedFirst();
        SelectedIndex = 0;

        await Task.Delay(LaunchFeedbackMilliseconds);
        IsLaunching = false;
    }

    private async Task FetchLaunchIconAsync(GameEntry game)
    {
        try
        {
            if (await _artwork.FetchIconAsync(game) && IsLaunching && LaunchingGameTitle == game.Title)
            {
                LaunchingGameIconPath = game.IconImagePath;
            }
        }
        catch { }
    }

    public void CloseRunningGame()
    {
        if (RunningGame is null) return;
        _launcher.CloseGame(RunningGame);
        RunningGame = null;
    }

    public void ToggleContextMenu()
    {
        if (IsContextMenuOpen)
        {
            CloseContextMenu();
            return;
        }

        if (SelectedTile is null) return;
        ContextMenuTarget = SelectedTile;
        _contextMenuIndex = 0;
        BuildContextMenu(SelectedTile);
        UpdateContextMenuSelection();
        IsContextMenuOpen = true;
    }

    private void BuildContextMenu(GameTileViewModel target)
    {
        ContextMenuItems.Clear();
        _contextMenuActions.Clear();

        void Add(string label, Action action)
        {
            ContextMenuItems.Add(new ContextMenuItemViewModel(label));
            _contextMenuActions.Add(action);
        }

        var game = target.Game;

        if (game.IsOwnedNotInstalled)
        {
            Add("Instalar", () => OpenGameFromMenu(game));
            return;
        }

        Add("Abrir jogo", () => OpenGameFromMenu(game));
        if (HasGameDetails(game)) Add("Detalhes do jogo", () => OpenGameDetails(game));
        Add(game.IsFavorite ? "Desfavoritar" : "Favoritar", () => ToggleFavorite(game));
        Add("Editar", () => EditGameRequested?.Invoke(this, game));

        if (game.Source == GameSource.Steam)
        {
            if (game.SteamAppId is { } appId) Add("Excluir jogo", () => RequestDeleteSteamGame(game, appId));
        }
        else if (game.Id.StartsWith("xboxfolder_", StringComparison.Ordinal))
        {
            Add("Excluir jogo", () => RequestDeleteXboxGame(game));
        }
        else
        {
            Add("Excluir atalho", () => RequestDeleteShortcut(game));
        }

        Add("Personalizar", () => OpenPersonalizeMenu(game));
    }

    private void OpenPersonalizeMenu(GameEntry game)
    {
        var tile = Games.FirstOrDefault(t => t.Game.Id == game.Id);
        if (tile is null) return;
        ContextMenuTarget = tile;
        ContextMenuItems.Clear();
        _contextMenuActions.Clear();

        void Add(string label, Action action)
        {
            ContextMenuItems.Add(new ContextMenuItemViewModel(label));
            _contextMenuActions.Add(action);
        }

        Add("Capa", () => OpenCoverPicker(game, ArtworkPickerKind.Grid));
        Add("Capa larga", () => OpenCoverPicker(game, ArtworkPickerKind.WideGrid));
        Add("Banner", () => OpenCoverPicker(game, ArtworkPickerKind.Hero));
        Add("Logo", () => OpenCoverPicker(game, ArtworkPickerKind.Logo));
        Add("Ícone", () => OpenCoverPicker(game, ArtworkPickerKind.Icon));
        Add("Voltar", () => ReopenContextMenu(tile));

        _contextMenuIndex = 0;
        UpdateContextMenuSelection();
        IsContextMenuOpen = true;
    }

    private void ReopenContextMenu(GameTileViewModel tile)
    {
        ContextMenuTarget = tile;
        _contextMenuIndex = 0;
        BuildContextMenu(tile);
        UpdateContextMenuSelection();
        IsContextMenuOpen = true;
    }

    private void ToggleFavorite(GameEntry game)
    {
        _library.SetFavorite(game.Id, !game.IsFavorite);
        RebuildGamesFromLibrary();
    }

    private void OpenGameFromMenu(GameEntry game)
    {
        var tile = Games.FirstOrDefault(t => t.Game.Id == game.Id);
        if (tile is null) return;
        SelectedIndex = Games.IndexOf(tile);
        LaunchSelected();
    }

    private void OpenConfirmMenu(GameEntry game, string confirmLabel, Action onConfirm)
    {
        ContextMenuTarget = Games.FirstOrDefault(t => t.Game.Id == game.Id);
        ContextMenuItems.Clear();
        _contextMenuActions.Clear();

        void Add(string label, Action action, bool isDanger = false)
        {
            ContextMenuItems.Add(new ContextMenuItemViewModel(label, isDanger));
            _contextMenuActions.Add(action);
        }

        Add("Cancelar", () => { });
        Add(confirmLabel, onConfirm, isDanger: true);

        _contextMenuIndex = 0;
        UpdateContextMenuSelection();
        IsContextMenuOpen = true;
    }

    private void RequestDeleteShortcut(GameEntry game)
    {
        OpenConfirmMenu(game, "Sim, excluir atalho", () =>
        {
            _library.RemoveManualGame(game.Id);
            Refresh();
        });
    }

    private void RequestDeleteSteamGame(GameEntry game, uint appId)
    {
        OpenConfirmMenu(game, "Sim, abrir desinstalador da Steam", () => _launcher.OpenSteamUninstaller(appId));
    }

    private void RequestDeleteXboxGame(GameEntry game)
    {
        if (!OperatingSystem.IsWindows()) return;
        OpenConfirmMenu(game, "Sim, abrir desinstalador do Windows", () => _launcher.OpenWindowsAppUninstaller(game.InstallDirectory));
    }

    public void CloseContextMenu()
    {
        IsContextMenuOpen = false;
        ContextMenuTarget = null;
    }

    public void MoveContextMenuSelection(int delta)
    {
        if (!IsContextMenuOpen || ContextMenuItems.Count == 0) return;
        _contextMenuIndex = ((_contextMenuIndex + delta) % ContextMenuItems.Count + ContextMenuItems.Count) % ContextMenuItems.Count;
        UpdateContextMenuSelection();
    }

    public void ConfirmContextMenuSelection()
    {
        if (!IsContextMenuOpen || ContextMenuTarget is null)
        {
            CloseContextMenu();
            return;
        }

        var index = _contextMenuIndex;
        CloseContextMenu();

        if (index >= 0 && index < _contextMenuActions.Count)
        {
            _contextMenuActions[index]();
        }
    }

    public void HighlightContextMenuItem(ContextMenuItemViewModel item)
    {
        var index = ContextMenuItems.IndexOf(item);
        if (index < 0) return;
        _contextMenuIndex = index;
        UpdateContextMenuSelection();
    }

    public void ActivateContextMenuItem(ContextMenuItemViewModel item)
    {
        HighlightContextMenuItem(item);
        if (ContextMenuItems.IndexOf(item) >= 0) ConfirmContextMenuSelection();
    }

    public void HighlightGlobalMenuItem(ContextMenuItemViewModel item)
    {
        var index = GlobalMenuItems.IndexOf(item);
        if (index < 0) return;
        _globalMenuIndex = index;
        UpdateGlobalMenuSelection();
    }

    public void ActivateGlobalMenuItem(ContextMenuItemViewModel item)
    {
        HighlightGlobalMenuItem(item);
        if (GlobalMenuItems.IndexOf(item) >= 0) ConfirmGlobalMenuSelection();
    }

    public void ActivateCoverCandidate(ArtworkCandidateViewModel candidate)
    {
        var index = CoverPickerCandidates.IndexOf(candidate);
        if (index < 0) return;
        _coverPickerIndex = index;
        UpdateCoverPickerSelection();
        ConfirmCoverPickerSelection();
    }

    private void UpdateContextMenuSelection()
    {
        for (var i = 0; i < ContextMenuItems.Count; i++)
        {
            ContextMenuItems[i].IsSelected = i == _contextMenuIndex;
        }
    }

    private enum GlobalMenuPage { Root, Library, Hud, Settings, Power, ConfirmPower }
    private GlobalMenuPage _globalMenuPage = GlobalMenuPage.Root;

    private string _pendingPowerConfirmLabel = string.Empty;
    private Action? _pendingPowerAction;

    private void RequestPowerConfirm(string lowercaseLabel, Action action)
    {
        _pendingPowerConfirmLabel = lowercaseLabel;
        _pendingPowerAction = action;
        NavigateGlobalMenu(GlobalMenuPage.ConfirmPower);
        IsGlobalMenuOpen = true;
    }

    private readonly List<bool> _globalMenuNavigates = new();

    private string _globalMenuTitle = "Menu";
    public string GlobalMenuTitle
    {
        get => _globalMenuTitle;
        private set => SetField(ref _globalMenuTitle, value);
    }

    private string _globalMenuHint = string.Empty;
    public string GlobalMenuHint
    {
        get => _globalMenuHint;
        private set => SetField(ref _globalMenuHint, value);
    }

    public bool HasGlobalMenuHint => !string.IsNullOrEmpty(_globalMenuHint);

    public void ToggleGlobalMenu()
    {
        if (IsGlobalMenuOpen)
        {
            CloseGlobalMenu();
            return;
        }
        OpenGlobalMenuAt(GlobalMenuPage.Root);
    }

    private void OpenGlobalMenuAt(GlobalMenuPage page)
    {
        _globalMenuPage = page;
        BuildGlobalMenu();
        if (GlobalMenuItems.Count == 0) return;
        _globalMenuIndex = 0;
        UpdateGlobalMenuSelection();
        IsGlobalMenuOpen = true;
    }

    private void NavigateGlobalMenu(GlobalMenuPage page)
    {
        _globalMenuPage = page;
        BuildGlobalMenu();
        _globalMenuIndex = 0;
        UpdateGlobalMenuSelection();
    }

    public void CloseGlobalMenu() => IsGlobalMenuOpen = false;

    public void GlobalMenuBack()
    {
        if (_globalMenuPage != GlobalMenuPage.Root)
        {
            NavigateGlobalMenu(GlobalMenuPage.Root);
            return;
        }
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
        if (_globalMenuNavigates[index])
        {
            _globalMenuActions[index]();
            return;
        }
        CloseGlobalMenu();
        _globalMenuActions[index]();
    }

    private void UpdateGlobalMenuSelection()
    {
        for (var i = 0; i < GlobalMenuItems.Count; i++)
        {
            GlobalMenuItems[i].IsSelected = i == _globalMenuIndex;
        }
    }

    private void BuildGlobalMenu()
    {
        GlobalMenuItems.Clear();
        _globalMenuActions.Clear();
        _globalMenuNavigates.Clear();

        void Add(string label, Action action, bool isDanger = false)
        {
            GlobalMenuItems.Add(new ContextMenuItemViewModel(label, isDanger));
            _globalMenuActions.Add(action);
            _globalMenuNavigates.Add(false);
        }

        void Folder(string label, GlobalMenuPage page)
        {
            GlobalMenuItems.Add(new ContextMenuItemViewModel(label));
            _globalMenuActions.Add(() => NavigateGlobalMenu(page));
            _globalMenuNavigates.Add(true);
        }

        switch (_globalMenuPage)
        {
            case GlobalMenuPage.Library:
                GlobalMenuTitle = "Biblioteca";
                Add("Atualizar Biblioteca", Refresh);
                if (CanFetchArtwork) Add("Atualizar Capas Automático", FetchMissingArtwork);
                Add("Adicionar jogo", () => AddGameRequested?.Invoke(this, EventArgs.Empty));
                Add("Downloads", ToggleDownloads);
                break;

            case GlobalMenuPage.Hud:
                GlobalMenuTitle = "HUD";
                if (_hud is not null && OperatingSystem.IsWindows())
                {
                    for (var i = 0; i < PerformanceHud.PresetNames.Length; i++)
                    {
                        var preset = i;
                        var marker = _hud.Preset == preset ? "●" : "○";
                        Add($"{marker}  {PerformanceHud.PresetNames[preset]}", () => SetOverlayPreset(preset));
                    }
                }
                if (_performanceOverlay.IsInstalled)
                {
                    Add(_performanceOverlay.IsRunning ? "RTSS: rodando" : "RTSS: iniciar",
                        () =>
                        {
                            if (!_performanceOverlay.IsRunning) _performanceOverlay.EnsureRunning();
                        });
                }
                else
                {
                    Add("RTSS: baixar (necessário pro HUD)", _performanceOverlay.OpenDownloadPage);
                }
                break;

            case GlobalMenuPage.Settings:
                GlobalMenuTitle = "Configurações";
                Add("Preferências", () => SettingsRequested?.Invoke(this, EventArgs.Empty));
                Add("Layout da tela", () => LayoutPickerRequested?.Invoke(this, EventArgs.Empty));
                if (_updates is { IsInstalled: true } && !IsUpdateReady)
                {
                    Add($"Verificar atualizações (v{_updates.CurrentVersionText})", () => _ = CheckForUpdatesAsync(userInitiated: true));
                }
                break;

            case GlobalMenuPage.Power:
                GlobalMenuTitle = "Energia";
                Add("Desligar tela", () => TurnOffScreenRequested?.Invoke(this, EventArgs.Empty));
                Add("Reiniciar o aparelho", () => RequestPowerConfirm("reiniciar o aparelho", () => RestartDeviceRequested?.Invoke(this, EventArgs.Empty)));
                Add("Desligar o aparelho", () => RequestPowerConfirm("desligar o aparelho", () => ShutdownDeviceRequested?.Invoke(this, EventArgs.Empty)));
                Add("Sair para o Windows", () => RestoreShellRequested?.Invoke(this, EventArgs.Empty));
                Add("Sair", () => QuitRequested?.Invoke(this, EventArgs.Empty));
                break;

            case GlobalMenuPage.ConfirmPower:
                GlobalMenuTitle = "Confirmar?";
                Folder("Cancelar", GlobalMenuPage.Power);
                Add($"Sim, {_pendingPowerConfirmLabel}", () => _pendingPowerAction?.Invoke(), isDanger: true);
                break;

            default:
                GlobalMenuTitle = "Menu";
                
                Add("Abrir Quick Settings (Teste)", () => OpenOverlayRequested?.Invoke(this, EventArgs.Empty));

                if (IsUpdateReady)
                {
                    Add(RunningGame is null ? $"Reiniciar pra atualizar (v{PendingUpdateVersion})" : $"Atualização v{PendingUpdateVersion} pronta — feche o jogo pra aplicar", ApplyUpdate);
                }
                if (RunningGame is not null) Add($"Fechar: {RunningGame.Title}", CloseRunningGame);
                Folder("Biblioteca", GlobalMenuPage.Library);
                Folder("HUD", GlobalMenuPage.Hud);
                Folder("Configurações", GlobalMenuPage.Settings);
                Folder("Energia", GlobalMenuPage.Power);
                Add("Sobre", OpenAbout);
                break;
        }

        GlobalMenuHint = _globalMenuPage == GlobalMenuPage.Hud ? "No jogo: L3+R3 (ou Ctrl+Alt+O) troca o HUD" : string.Empty;
        OnPropertyChanged(nameof(HasGlobalMenuHint));
    }

    private bool _isAboutOpen;
    public bool IsAboutOpen
    {
        get => _isAboutOpen;
        private set => SetField(ref _isAboutOpen, value);
    }

    public void OpenAbout() => IsAboutOpen = true;
    public void CloseAbout() => IsAboutOpen = false;

    public string AboutVersionText
    {
        get
        {
            var full = AppVersionText;
            var start = full.IndexOf('v');
            var end = full.IndexOf(' ', Math.Max(start, 0));
            return start >= 0 ? "Versão " + (end > start ? full[(start + 1)..end] : full[(start + 1)..]) : full;
        }
    }

    public void TogglePowerMenu()
    {
        if (IsGlobalMenuOpen && _globalMenuPage == GlobalMenuPage.Power)
        {
            CloseGlobalMenu();
            return;
        }
        OpenGlobalMenuAt(GlobalMenuPage.Power);
    }

    private bool _isDownloadsOpen;
    public bool IsDownloadsOpen
    {
        get => _isDownloadsOpen;
        private set => SetField(ref _isDownloadsOpen, value);
    }

    public ObservableCollection<DownloadProgressInfo> ActiveDownloads { get; } = new();
    public bool HasActiveDownloads => ActiveDownloads.Count > 0;
    public bool NoActiveDownloads => !HasActiveDownloads;

    private DownloadProgressInfo? PrimaryActiveDownload =>
        ActiveDownloads.FirstOrDefault(d => d.BytesDownloaded > 0 && d.BytesDownloaded < d.BytesToDownload)
        ?? ActiveDownloads.FirstOrDefault();

    public double TopBarDownloadPercent => PrimaryActiveDownload?.PercentComplete ?? 0;
    public string TopBarDownloadPercentText => PrimaryActiveDownload is { } d ? $"{d.PercentComplete:0}%" : string.Empty;

    public string TopBarDownloadToolTip
    {
        get
        {
            var tip = "Downloads (Y no controle)";
            if (PrimaryActiveDownload is not { } d) return tip;
            tip += $"\nBaixando: {d.Title} — {d.PercentComplete:0}%";
            var queued = ActiveDownloads.Count - 1;
            if (queued > 0) tip += $" (+{queued} na fila)";
            return tip;
        }
    }

    public ObservableCollection<OwnedSteamGame> OwnedNotInstalledGames { get; } = new();
    public bool HasSteamWebApiCredentials => _library.HasSteamWebApiCredentials;
    public bool NoSteamWebApiCredentials => !HasSteamWebApiCredentials;

    private bool _isLoadingOwnedGames;
    public bool IsLoadingOwnedGames
    {
        get => _isLoadingOwnedGames;
        private set => SetField(ref _isLoadingOwnedGames, value);
    }

    private string? _ownedGamesErrorMessage;
    public string? OwnedGamesErrorMessage
    {
        get => _ownedGamesErrorMessage;
        private set
        {
            if (SetField(ref _ownedGamesErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasOwnedGamesError));
            }
        }
    }

    public bool HasOwnedGamesError => !string.IsNullOrEmpty(OwnedGamesErrorMessage);

    private string? _notInstalledTabMessage;
    public string? NotInstalledTabMessage
    {
        get => _notInstalledTabMessage;
        private set
        {
            if (SetField(ref _notInstalledTabMessage, value))
            {
                OnPropertyChanged(nameof(HasNotInstalledTabMessage));
            }
        }
    }

    public bool HasNotInstalledTabMessage => !string.IsNullOrEmpty(NotInstalledTabMessage);

    private void UpdateNotInstalledTabMessage()
    {
        if (_activeFilter != LibraryFilter.NotInstalled)
        {
            NotInstalledTabMessage = null;
            return;
        }

        if (!HasSteamWebApiCredentials) NotInstalledTabMessage = "Configure a Steam Web API key e o SteamID64 na tela de Configurações pra ver aqui os jogos da sua conta que ainda não estão instalados.";
        else if (IsLoadingOwnedGames) NotInstalledTabMessage = "Carregando jogos da sua conta...";
        else if (HasOwnedGamesError) NotInstalledTabMessage = OwnedGamesErrorMessage;
        else if (Games.Count == 0) NotInstalledTabMessage = "Nenhum jogo da sua conta Steam está faltando instalar aqui.";
        else NotInstalledTabMessage = null;
    }

    public void ToggleDownloads()
    {
        if (IsDownloadsOpen)
        {
            CloseDownloads();
            return;
        }
        RefreshActiveDownloads();
        IsDownloadsOpen = true;
    }

    public void OpenSteamDownloads()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "steam://open/downloads",
                    UseShellExecute = true,
                });
            }
            else if (OperatingSystem.IsLinux())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = "steam://open/downloads",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
        }
        catch { }
    }

    public void CloseDownloads() => IsDownloadsOpen = false;

    private readonly Dictionary<string, (long Bytes, DateTime At, double? Speed)> _downloadSamples = new();

    public void RefreshActiveDownloads()
    {
        var now = DateTime.UtcNow;
        ActiveDownloads.Clear();
        foreach (var download in _library.GetActiveDownloads())
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
                else
                {
                    download.BytesPerSecond = (now - previous.At).TotalSeconds < 30 ? previous.Speed : null;
                }
            }
            else
            {
                _downloadSamples[download.Title] = (download.BytesDownloaded, now, null);
            }

            ActiveDownloads.Add(download);
        }
        OnPropertyChanged(nameof(HasActiveDownloads));
        OnPropertyChanged(nameof(NoActiveDownloads));
        OnPropertyChanged(nameof(TopBarDownloadPercent));
        OnPropertyChanged(nameof(TopBarDownloadPercentText));
        OnPropertyChanged(nameof(TopBarDownloadToolTip));
    }

    public async void RefreshOwnedGames()
    {
        if (!HasSteamWebApiCredentials) return;

        IsLoadingOwnedGames = true;
        OwnedGamesErrorMessage = null;
        UpdateNotInstalledTabMessage();

        try
        {
            var owned = await _library.GetOwnedButNotInstalledSteamGamesAsync();
            if (!IsDownloadsOpen && _activeFilter != LibraryFilter.NotInstalled) return;

            OwnedNotInstalledGames.Clear();
            foreach (var game in owned) OwnedNotInstalledGames.Add(game);

            if (_activeFilter == LibraryFilter.NotInstalled)
            {
                RebuildGamesFromLibrary();
                FetchMissingArtworkForOwnedGames();
            }
        }
        catch (Exception ex)
        {
            OwnedGamesErrorMessage = $"Erro ao buscar jogos da conta: {ex.Message}";
        }
        finally
        {
            IsLoadingOwnedGames = false;
            UpdateNotInstalledTabMessage();
        }
    }

    private async void FetchMissingArtworkForOwnedGames()
    {
        if (!_artwork.HasApiKey) return;

        foreach (var owned in OwnedNotInstalledGames.ToList())
        {
            if (_activeFilter != LibraryFilter.NotInstalled) return;
            var entry = _library.BuildEntryForOwnedGame(owned);

            if (string.IsNullOrEmpty(entry.GridImagePath)) await _artwork.FetchGridAsync(entry);
            if (_activeFilter != LibraryFilter.NotInstalled) return;
            if (string.IsNullOrEmpty(entry.LogoImagePath)) await _artwork.FetchLogoAsync(entry);
        }

        if (_activeFilter == LibraryFilter.NotInstalled) RebuildGamesFromLibrary();
    }

    public void InstallOwnedGame(OwnedSteamGame game) => _launcher.OpenSteamInstaller(game.AppId);

    private async void OpenCoverPicker(GameEntry game, ArtworkPickerKind kind)
    {
        CoverPickerTitle = kind switch
        {
            ArtworkPickerKind.WideGrid => $"Capa larga · {game.Title}",
            ArtworkPickerKind.Hero => $"Banner · {game.Title}",
            ArtworkPickerKind.Logo => $"Logo · {game.Title}",
            ArtworkPickerKind.Icon => $"Ícone · {game.Title}",
            _ => $"Capa · {game.Title}",
        };
        _coverPickerKind = kind;
        _coverPickerGame = game;
        _coverPickerIndex = 0;
        CoverPickerErrorMessage = null;
        CoverPickerCandidates.Clear();
        NotifyCoverPickerResultFlags();
        IsCoverPickerLoading = true;
        IsCoverPickerOpen = true;

        try
        {
            var candidates = kind switch
            {
                ArtworkPickerKind.WideGrid => await _artwork.GetWideGridCandidatesAsync(game, FilterAnimatedOnly),
                ArtworkPickerKind.Hero => await _artwork.GetHeroCandidatesAsync(game, FilterAnimatedOnly),
                ArtworkPickerKind.Logo => await _artwork.GetLogoCandidatesAsync(game, FilterAnimatedOnly),
                ArtworkPickerKind.Icon => await _artwork.GetIconCandidatesAsync(game, FilterAnimatedOnly),
                _ => await _artwork.GetGridCandidatesAsync(game, FilterAnimatedOnly),
            };

            if (!IsCoverPickerOpen || _coverPickerGame != game) return;

            foreach (var candidate in candidates) CoverPickerCandidates.Add(new ArtworkCandidateViewModel(candidate));
            UpdateCoverPickerSelection();
        }
        catch (Exception ex)
        {
            CoverPickerErrorMessage = $"Erro ao buscar no SteamGridDB: {ex.Message}";
        }
        finally
        {
            IsCoverPickerLoading = false;
            NotifyCoverPickerResultFlags();
        }
    }

    public void CloseCoverPicker()
    {
        IsCoverPickerOpen = false;
        _coverPickerGame = null;
        CoverPickerCandidates.Clear();
        CoverPickerErrorMessage = null;
        NotifyCoverPickerResultFlags();
    }

    private void NotifyCoverPickerResultFlags()
    {
        OnPropertyChanged(nameof(CoverPickerHasResults));
        OnPropertyChanged(nameof(CoverPickerIsEmpty));
    }

    public const int CoverPickerColumns = 5;

    public void MoveCoverPickerSelection(int delta)
    {
        if (!IsCoverPickerOpen || CoverPickerCandidates.Count == 0) return;
        _coverPickerIndex = ((_coverPickerIndex + delta) % CoverPickerCandidates.Count + CoverPickerCandidates.Count) % CoverPickerCandidates.Count;
        UpdateCoverPickerSelection();
    }

    public void MoveCoverPickerSelectionVertical(int rowDelta)
    {
        if (!IsCoverPickerOpen || CoverPickerCandidates.Count == 0) return;
        var newIndex = _coverPickerIndex + rowDelta * CoverPickerColumns;
        if (newIndex < 0 || newIndex >= CoverPickerCandidates.Count) return;
        _coverPickerIndex = newIndex;
        UpdateCoverPickerSelection();
    }

    public async void ConfirmCoverPickerSelection()
    {
        if (!IsCoverPickerOpen || _coverPickerGame is null || CoverPickerCandidates.Count == 0)
        {
            CloseCoverPicker();
            return;
        }

        var chosen = CoverPickerCandidates[_coverPickerIndex].Candidate;
        var game = _coverPickerGame;
        var kind = _coverPickerKind;
        CloseCoverPicker();

        try
        {
            var applied = kind switch
            {
                ArtworkPickerKind.WideGrid => await _artwork.ApplyWideGridCandidateAsync(game, chosen),
                ArtworkPickerKind.Hero => await _artwork.ApplyHeroCandidateAsync(game, chosen),
                ArtworkPickerKind.Logo => await _artwork.ApplyLogoCandidateAsync(game, chosen),
                ArtworkPickerKind.Icon => await _artwork.ApplyIconCandidateAsync(game, chosen),
                _ => await _artwork.ApplyGridCandidateAsync(game, chosen),
            };
        }
        catch { }

        Refresh();
    }

    private void UpdateCoverPickerSelection()
    {
        for (var i = 0; i < CoverPickerCandidates.Count; i++)
        {
            CoverPickerCandidates[i].IsSelected = i == _coverPickerIndex;
        }
    }

    public void AddManualGame(GameEntry game)
    {
        _library.AddManualGame(game);
        Refresh();

        var tile = Games.FirstOrDefault(t => t.Game.Id == game.Id);
        if (tile is not null) SelectedIndex = Games.IndexOf(tile);
    }

    public void Refresh()
    {
        _library.Refresh();
        RebuildGamesFromLibrary();
    }

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

        OnPropertyChanged(nameof(IsFilterAllActive));
        OnPropertyChanged(nameof(IsFilterFavoritesActive));
        OnPropertyChanged(nameof(IsFilterComputerActive));
        OnPropertyChanged(nameof(IsFilterEmulatorsActive));
        OnPropertyChanged(nameof(IsFilterApplicationsActive));
        OnPropertyChanged(nameof(IsFilterNotInstalledActive));

        RebuildSubFilters();
        RebuildGamesFromLibrary();

        if (filter == LibraryFilter.NotInstalled) RefreshOwnedGames();
    }

    private bool MatchesActiveFilter(GameEntry game) => _activeFilter switch
    {
        LibraryFilter.Favorites => game.IsFavorite,
        LibraryFilter.Applications => game.IsApplication,
        LibraryFilter.All => !game.IsApplication,
        LibraryFilter.Computer => !game.IsApplication && game.Source != GameSource.Emulator && MatchesSubFilters(game),
        LibraryFilter.Emulators => !game.IsApplication && game.Source == GameSource.Emulator && MatchesSubFilters(game),
        _ => true,
    };

    public void ReloadTiles()
    {
        var selected = SelectedIndex;
        RebuildGamesFromLibrary();
        if (selected >= 0 && Games.Count > 0) SelectedIndex = Math.Min(selected, Games.Count - 1);
    }

    private void RebuildGamesFromLibrary()
    {
        var previouslySelectedId = SelectedTile?.Game.Id;
        Games.Clear();

        if (_activeFilter == LibraryFilter.NotInstalled)
        {
            foreach (var owned in OwnedNotInstalledGames) Games.Add(new GameTileViewModel(_library.BuildEntryForOwnedGame(owned)));
        }
        else
        {
            foreach (var game in _library.Games)
            {
                if (MatchesActiveFilter(game)) Games.Add(new GameTileViewModel(game));
            }
        }
        ReorderLastPlayedFirst();
        UpdateNotInstalledTabMessage();

        var restoredIndex = previouslySelectedId is null ? 0 : Math.Max(0, Games.ToList().FindIndex(t => t.Game.Id == previouslySelectedId));
        SelectedIndex = Games.Count > 0 ? restoredIndex : -1;
        if (Games.Count > 0) SelectedTile = Games[SelectedIndex];
    }

    private async void FetchMissingArtwork()
    {
        if (!CanFetchArtwork) return;
        IsFetchingArtwork = true;

        try
        {
            foreach (var game in _library.Games.ToList())
            {
                if (string.IsNullOrEmpty(game.GridImagePath)) await _artwork.FetchGridAsync(game);
                if (string.IsNullOrEmpty(game.HeroImagePath)) await _artwork.FetchHeroAsync(game);
                if (string.IsNullOrEmpty(game.LogoImagePath)) await _artwork.FetchLogoAsync(game);
                if (string.IsNullOrEmpty(game.WideGridImagePath)) await _artwork.FetchWideGridAsync(game);
                if (string.IsNullOrEmpty(game.IconImagePath)) await _artwork.FetchIconAsync(game);
            }
            Refresh();
        }
        finally
        {
            IsFetchingArtwork = false;
        }
    }

    private void UpdateFocus()
    {
        for (var i = 0; i < Games.Count; i++)
        {
            Games[i].IsFocused = i == _selectedIndex;
            Games[i].CarouselDistance = i - _selectedIndex;
        }
    }

    private void ReorderLastPlayedFirst()
    {
        if (Games.Count == 0) return;
        GameTileViewModel? lastPlayedTile = null;
        DateTimeOffset? latest = null;

        foreach (var tile in Games)
        {
            tile.IsLastPlayed = false;
            if (tile.Game.LastPlayedUtc is { } played && (latest is null || played > latest))
            {
                latest = played;
                lastPlayedTile = tile;
            }
        }

        if (lastPlayedTile is null) return;
        lastPlayedTile.IsLastPlayed = true;
        var index = Games.IndexOf(lastPlayedTile);
        if (index > 0) Games.Move(index, 0);
    }

    private void OnGameExited(object? sender, GameExitedEventArgs e)
    {
        _library.RecordPlaySession(e.Game.Id, e.Duration);
        if (RunningGame?.Id == e.Game.Id) RunningGame = null;
        Refresh();
    }
}