using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Input;
using HandheldLauncher.App.Platform;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.App.Views.Layouts;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;
using HandheldLauncher.Services.HidHide;
using HandheldLauncher.Services.Syncthing;

namespace HandheldLauncher.App.Views;

public partial class MainWindow : Window
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    private const int SW_RESTORE = 9;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);

    private bool _externalSettingsOpen;

    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private const int RestoreHotkeyId = 0xB00;
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkH = 0x48;

    private const int OverlayHotkeyId = 0xB01;
    private const uint VkO = 0x4F;
    private const uint WmHotkey = 0x0312;

    private const uint TrayCallbackMessage = 0x8000 + 0x51;
    private const uint NIM_ADD = 0, NIM_DELETE = 2, NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    private const uint TPM_RETURNCMD = 0x0100, TPM_RIGHTBUTTON = 0x0002, TPM_NONOTIFY = 0x0080;
    private const uint MF_STRING = 0, MF_SEPARATOR = 0x00000800;
    private const int TrayMenuOpen = 1, TrayMenuExit = 2;
    private const int WmLButtonUp = 0x0202, WmLButtonDoubleClick = 0x0203, WmRButtonUp = 0x0205;
    private const uint WmSysCommand = 0x0112;
    private const int ScMonitorPower = 0xF170;

    // 🟢 Declarações únicas dos serviços
    private readonly IHidHideService _hidHideService;
    private readonly SyncthingService _syncthingService;

    private static readonly IntPtr HwndBroadcast = new(0xffff);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, out IntPtr largeIcon, out IntPtr smallIcon, uint iconCount);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, uint itemId, string? itemText);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);
    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    private IntPtr _trayIconHandle;
    private IntPtr _unusedTrayIconHandle;
    private bool _trayIconAdded;

    private readonly GamepadPoller _gamepad = new();
    private readonly IgdbService _igdbService = new IgdbService("", "");

    private readonly DispatcherTimer _gamepadTimer;
    private DispatcherTimer? _topmostEnforcerTimer;
    private DispatcherTimer? _foregroundFighterTimer;
    private DispatcherTimer? _gameWatcherTimer;
    private GameLaunchPopupWindow? _launchPopup;
    private DispatcherTimer? _launchPopupTimer;
    private DispatcherTimer? _batteryTimer;
    private bool _hotkeyRegistered;

    private ItemsControl GamesItemsControl = null!;
    private ScrollViewer GameRowScrollViewer = null!;
    private Image HeroImage = null!;
    private Image HeroBackImage = null!;
    private ILibraryLayout _libraryLayout = null!;
    private OverlayWindow? _overlayWindow;
    private DateTime _lastOverlayToggle = DateTime.MinValue;

    private OverlayHotkeyWatcher? _overlayHotkeyWatcher;
    private OverlayHotkeyWatcher? _inGameXInputObserver;
    private bool? _gamepadComboModeIsGame;

    public MainWindow(MainViewModel viewModel, IHidHideService hidHideService, SyncthingService syncthingService)
    {
        _hidHideService = hidHideService;
        _syncthingService = syncthingService;
        InitializeComponent();

        ApplyLayoutTheme();

        GamesItemsControl.ContainerPrepared += (_, e) => e.Container.Margin = ContainerMargin();

        HeroImage.PropertyChanged += OnHeroImageChanged;

        AddHandler(KeyDownEvent, OnWindowKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        _gamepadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _gamepadTimer.Tick += OnGamepadTick;
        _gamepadTimer.Start();

        this.PropertyChanged += (s, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                if (IsVisible)
                {
                    if (!_gamepadTimer.IsEnabled)
                    {
                        _gamepadTimer.Start();
                        DebugLog.Write("[PERF] Launcher visível. Timer do gamepad LIGADO.");
                    }
                }
                else
                {
                    // v0.25.3: com o QS aberto, o timer tem de continuar ligado
                    // mesmo que o launcher esteja minimizado (jogo em foco) —
                    // senão o controle não navega o painel.
                    if (_gamepadTimer.IsEnabled && _overlayWindow is not { IsVisible: true })
                    {
                        _gamepadTimer.Stop();
                        DebugLog.Write("[PERF] Launcher minimizado/escondido. Timer do gamepad PAUSADO.");
                    }
                }
            }
        };

        OverlayWindow.OnOverlayVisibilityChanged += (isOverlayVisible) =>
        {
            DebugLog.Write($"[PERF] QS visível={isOverlayVisible}; timer={_gamepadTimer.IsEnabled}; launcher visível={IsVisible}.");
            ResetOverlayInputState(); // v0.25.9: ponte HID e diagnóstico recomeçam a cada abertura
            if (isOverlayVisible)
            {
                if (!_gamepadTimer.IsEnabled)
                {
                    _gamepadTimer.Start();
                    DebugLog.Write("[PERF] Quick Settings abriu. Timer do gamepad LIGADO.");
                }
            }
            else
            {
                if (IsVisible)
                {
                    Activate();
                    DebugLog.Write("[PERF] Quick Settings fechou. Launcher re-focado para navegação.");
                }
                else if (_gamepadTimer.IsEnabled)
                {
                    _gamepadTimer.Stop();
                    DebugLog.Write("[PERF] Quick Settings fechou. Timer do gamepad PAUSADO (jogo em foco).");
                }
            }
        };

        Closing += (_, _) => DebugLog.Write("[STARTUP] Fechamento detectado.");

        Closed += (_, _) =>
        {
            _gamepadTimer.Stop();
            RemoveTrayIcon();
            try { InputBlockService.UnblockGamepadForGame(); }
            catch (Exception ex) { DebugLog.Write($"[InputBlock] Falha ao desbloquear gamepad no fechamento: {ex.Message}"); }
            
            if (_overlayWindow != null)
            {
                _overlayWindow.IsAppClosing = true;
                _overlayWindow.Close();
            }
            
            UnregisterRestoreHotkeyIfNeeded();
        };

        DataContextChanged += OnDataContextChanged;

        Opened += (_, _) =>
        {
            try { InputBlockService.RecoverOrphanedSuspension(); }
            catch (Exception ex) { DebugLog.Write($"[InputBlock] Falha ao recuperar suspensão órfã na abertura: {ex.Message}"); }

            StartTopmostEnforcer();
            RegisterRestoreHotkey();
            CreateTrayIcon();

            ForceForeground(allowRestore: true, forceIfGameRunning: true);

            StartForegroundFighter();

            PointerPressed += (_, _) => StopForegroundFighter();
            KeyDown += (_, _) => StopForegroundFighter();
        };

        Activated += (_, _) =>
        {
            if (OperatingSystem.IsWindows())
            {
                PowerThrottling.DisableForThisProcess();
                if (_externalSettingsOpen)
                {
                    _externalSettingsOpen = false;
                    Topmost = true;
                    DebugLog.Write("[CONNECTIVITY] Launcher retomou o foco; prioridade de janela restaurada.");
                }
            }
        };
    }

    public void ToggleOverlay()
    {
        try
        {
            var delta = (DateTime.Now - _lastOverlayToggle).TotalMilliseconds;
            if (delta < 1500) { DebugLog.Write($"[OVERLAY] Debounce {delta:F0}ms, ignorado."); return; }
            _lastOverlayToggle = DateTime.Now;

            DebugLog.Write($"[OVERLAY] Toggle. Janela={(_overlayWindow == null ? "null" : $"visible={_overlayWindow.IsVisible}")}");

            if (_overlayWindow != null && _overlayWindow.IsVisible)
            {
                DebugLog.Write("[OVERLAY] → HideWithUnblock()");
                _overlayWindow.HideWithUnblock();
            }
            else if (DataContext is MainViewModel vm)
            {
                DebugLog.Write("[OVERLAY] → RequestOpenOverlay()");
                vm.RequestOpenOverlay();
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[OVERLAY] ToggleOverlay EXCEPTION: {ex}");
        }
    }

    public void InitializeGamepadComboDetection()
    {
        SetGamepadComboDetectionForGame(gameRunning: false);
        Closed += (_, _) =>
        {
            _overlayHotkeyWatcher?.Dispose();
            _overlayHotkeyWatcher = null;
            _inGameXInputObserver?.Dispose();
            _inGameXInputObserver = null;
            GameInputHelper.StopHelperProcess();
        };
    }

    // v0.25.9: vigia do ajudante do L3+R3. Com o jogo na frente, o QS só
    // abre pelo ajudante (processo separado). Se ele morria no meio do jogo,
    // ninguém o reabria e o L3+R3 parava de funcionar até voltar pra
    // biblioteca. Agora, se ele morreu SOZINHO, é reaberto — no máximo uma
    // vez a cada 15 s, pra nunca virar um laço se ele morrer na partida.
    private DispatcherTimer? _helperWatchdog;
    private DateTime _lastHelperRevive = DateTime.MinValue;
    private int _helperRevivesThisGame;
    private const int MaxHelperRevivesPerGame = 3;

    private void EnsureHelperWatchdog()
    {
        if (_helperWatchdog is not null || !OperatingSystem.IsWindows()) return;
        _helperWatchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _helperWatchdog.Tick += (_, _) =>
        {
            try
            {
                if (_gamepadComboModeIsGame != true || !GameInputHelper.HelperDiedUnexpectedly) return;
                if (DateTime.UtcNow - _lastHelperRevive < TimeSpan.FromSeconds(15)) return;
                var code = GameInputHelper.CurrentHelperExitCode is { } c ? $"0x{c:X8}" : "?";
                if (_helperRevivesThisGame >= MaxHelperRevivesPerGame)
                {
                    if (_helperRevivesThisGame == MaxHelperRevivesPerGame)
                    {
                        _helperRevivesThisGame++;
                        DebugLog.Write($"[OVERLAY] Ajudante do L3+R3 morreu de novo (código={code}) — {MaxHelperRevivesPerGame} tentativas neste jogo, desistindo até o próximo jogo.");
                    }
                    return;
                }
                _lastHelperRevive = DateTime.UtcNow;
                _helperRevivesThisGame++;
                DebugLog.Write($"[OVERLAY] Ajudante do L3+R3 morreu com o jogo aberto (código={code}) — reabrindo ({_helperRevivesThisGame}/{MaxHelperRevivesPerGame}).");
                GameInputHelper.StartHelperProcess();
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[OVERLAY] Vigia do ajudante: {ex.GetType().Name}: {ex.Message}");
            }
        };
        _helperWatchdog.Start();
        Closed += (_, _) => { _helperWatchdog?.Stop(); _helperWatchdog = null; };
    }

    private void SetGamepadComboDetectionForGame(bool gameRunning)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (_gamepadComboModeIsGame == gameRunning) return;
        _gamepadComboModeIsGame = gameRunning;

        if (gameRunning)
        {
            EnsureHelperWatchdog();
            _helperRevivesThisGame = 0;
            _overlayHotkeyWatcher?.Dispose();
            _overlayHotkeyWatcher = null;
            GameInputHelper.StartHelperProcess();
            DebugLog.Write("[OVERLAY] Detector de L3+R3: GameInputHelper (jogo rodando).");

            _inGameXInputObserver?.Dispose();
            _inGameXInputObserver = new OverlayHotkeyWatcher(
                () => DebugLog.Write("[DIAG] XInput viu L3+R3 com o jogo rodando (só observação — overlay NÃO foi aberto por este detector)."),
                observeOnly: true);
        }
        else
        {
            _inGameXInputObserver?.Dispose();
            _inGameXInputObserver = null;
            GameInputHelper.StopHelperProcess();
            _overlayHotkeyWatcher ??= new OverlayHotkeyWatcher(() =>
            {
                Dispatcher.UIThread.Post(ToggleOverlay);
            });
            DebugLog.Write("[OVERLAY] Detector de L3+R3: OverlayHotkeyWatcher (biblioteca).");
        }
    }

    public void OpenExternalUrl(string url)
    {
        try
        {
            _topmostEnforcerTimer?.Stop();
            Topmost = false;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
            DispatcherTimer.RunOnce(() =>
            {
                if (IsVisible && _activeDialog is null)
                {
                    Topmost = true;
                    _topmostEnforcerTimer?.Start();
                }
            }, TimeSpan.FromSeconds(3));
        }
        catch (Exception ex) { DebugLog.Write($"[MAIN] Falha ao abrir URL externa: {ex.Message}"); }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (_overlayWindow == null)
        {
            try
            {
                DebugLog.Write("[OVERLAY] Criando OverlayWindow na inicialização (restaura TDP/ventoinha).");
                _overlayWindow = new OverlayWindow(vm, _hidHideService);
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[OVERLAY] Falha ao criar OverlayWindow na inicialização: {ex.Message}");
                _overlayWindow = null;
            }
        }

        SuspendGuardianService.Start(() =>
        {
            _overlayWindow?.ReapplyAfterSuspendResume();
            Dispatcher.UIThread.Post(RestoreGameFocusAfterResume);
        });
        Closed += (_, _) => SuspendGuardianService.Stop();

        vm.AddGameRequested += (_, _) => _ = OpenAddGameDialogAsync(vm);
        vm.EditGameRequested += (_, game) => _ = OpenEditGameDialogAsync(vm, game);
        vm.LayoutPickerRequested += (_, _) => _ = OpenLayoutPickerAsync();
        vm.SettingsRequested += (_, _) => _ = OpenSettingsDialogAsync(vm);
        vm.SystemUpdateRequested += (_, _) => _ = OpenSystemUpdateDialogAsync();
        vm.DriverUpdateRequested += (_, _) => _ = OpenDriverUpdateDialogAsync();
        vm.HelpCenterRequested += (_, _) => _ = OpenHelpCenterDialogAsync();
        vm.DeviceHealthRequested += (_, _) => _ = OpenDeviceHealthDialogAsync(vm);
        vm.StorageManagerRequested += (_, _) => _ = OpenStorageManagerDialogAsync(vm);
        vm.XCloudRequested += (_, url) => OpenXCloud(url);
        vm.HealthAlerts.AlertRaised += alert => ShowHealthAlertPopup(vm, alert);

        vm.QuitRequested += (_, _) =>
        {
            WatchdogGate.MarkDeliberateQuit();
            vm.ClearOverlayNow();
            if (_overlayWindow != null) { _overlayWindow.IsAppClosing = true; _overlayWindow.Close(); }
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
            else Close();
        };
        vm.TurnOffScreenRequested += (_, _) => TurnOffScreen();
        vm.RestartDeviceRequested += (_, _) => RestartDevice();
        vm.ShutdownDeviceRequested += (_, _) => ShutdownDevice();
        vm.RestartLauncherRequested += (_, _) =>
        {
            WatchdogGate.MarkDeliberateQuit();
            vm.ClearOverlayNow();
            if (_overlayWindow != null) { _overlayWindow.IsAppClosing = true; _overlayWindow.Close(); }
            RelaunchSelfAfterExit();
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
            else Close();
        };
        vm.ElevationGuardRequested += async (_, req) =>
        {
            // v0.25.5 (Camada 1): sem admin, avisa antes de lançar o jogo e oferece
            // reiniciar elevado (UAC uma vez). true = reiniciar elevado.
            var restartAsAdmin = await ShowElevationGuardDialogAsync(req);
            vm.ResolveElevationGuard(restartAsAdmin);
            if (restartAsAdmin) RestartElevated(vm);
        };
        vm.GameDetailsRequested += (_, game) => _ = OpenGameDetailsDialogAsync(vm, game);

        vm.OpenOverlayRequested += (_, _) =>
        {
            try
            {
                DebugLog.Write("[OVERLAY] Handler disparado.");

                IntPtr gameHwnd = IntPtr.Zero;
                if (OperatingSystem.IsWindows())
                {
                    try { gameHwnd = GetForegroundWindow(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
                }

                if (_overlayWindow == null)
                {
                    DebugLog.Write("[OVERLAY] Criando nova OverlayWindow (primeira vez).");
                    _overlayWindow = new OverlayWindow(vm, _hidHideService);
                }
                else
                {
                    DebugLog.Write("[OVERLAY] Reutilizando OverlayWindow existente.");
                }

                _overlayWindow.SetGameHwnd(gameHwnd);
                _overlayWindow.SetGamePid(_detectedGamePid);

                                DebugLog.Write($"[OVERLAY] Antes Show: IsVisible={_overlayWindow.IsVisible}, Pos={_overlayWindow.Position}, GameHwnd=0x{gameHwnd:X}");

                _overlayWindow.Opacity = 0;
                _overlayWindow.SetInitialPosition(); 
                _overlayWindow.Show();

                // 🟢 REMOVIDO: O bloco que forçava Activate() e SetForegroundWindow()
                // O WS_EX_NOACTIVATE dentro da OverlayWindow já garante que ela apareça por cima sem roubar o foco.

                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        _overlayWindow.Opacity = 1;
                        DebugLog.Write($"[OVERLAY] Mostrado (reposicionado pelo OnOpened). Pos={_overlayWindow.Position}, IsVisible={_overlayWindow.IsVisible}");
                        // v0.25.9: foto do estado de entrada (depois de mostrar, pra não atrasar o painel).
                        DebugLog.Write($"[QS-INPUT] Aberto · {DescribeInputState()}");
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Write($"[OVERLAY] PostShow EXCEPTION: {ex.Message}");
                        _overlayWindow.Opacity = 1;
                    }
                }, DispatcherPriority.Loaded);

                DebugLog.Write($"[OVERLAY] Depois Show: IsVisible={_overlayWindow.IsVisible}, Pos={_overlayWindow.Position}");
            }
            catch (Exception ex)
            {
                DebugLog.Write("========== ERRO AO ABRIR OVERLAY ==========");
                DebugLog.Write(ex.ToString());
                DebugLog.Write("===========================================");
                try { _overlayWindow?.Close(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
                _overlayWindow = null;
            }
        };

        StartGameWatcher(vm);
        StartBatteryMonitor(vm);
        StartDownloadsRefreshTimer(vm);

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsFilterApplicationsActive))
            {
                if (_addButtonFocused) SetAddButtonFocused(false);
            }

            if (e.PropertyName == nameof(MainViewModel.IsFilterAllActive))
                FadeIn(GamesItemsControl, 220);

            if (e.PropertyName == nameof(MainViewModel.IsFilterCloudActive))
                UpdateCloudGrid(vm);

            if (e.PropertyName == nameof(MainViewModel.IsLaunching))
            {
                if (vm.IsLaunching && !string.IsNullOrEmpty(vm.LaunchingGameTitle))
                {
                    try { _healthPopup?.Close(); } catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); }
                    DebugLog.Write($"[JOGO] '{vm.LaunchingGameTitle}' abriu — minimizando launcher + popup.");
                    if (IsVisible) MinimizeToTray();
                    ShowLaunchPopup(vm.LaunchingGameTitle, vm.LaunchingGameIconPath);
                }
                return;
            }

            if (e.PropertyName == nameof(MainViewModel.LaunchingGameIconPath))
            {
                _launchPopup?.UpdateIcon(vm.LaunchingGameIconPath);
                return;
            }

            if (e.PropertyName == nameof(MainViewModel.RunningGame))
            {
                SetGamepadComboDetectionForGame(vm.RunningGame is not null);

                if (vm.RunningGame is not null)
                {
                    if (IsVisible) MinimizeToTray();
                }
                else
                {
                    DebugLog.Write("[JOGO] Jogo fechou — restaurando launcher.");
                    if (!IsVisible) RestoreFromTray();
                }
            }
        };

        ApplyAppearance(vm, AppSettingsStore.Load());
        _ = vm.RunUpdateLoopAsync();

        try
        {
            var settings = AppSettingsStore.Load();
            if (vm.UpdatesInstalled && !settings.ModeChosen && !settings.KioskMode) Dispatcher.UIThread.Post(() => _ = OpenSettingsDialogAsync(vm), DispatcherPriority.Background);
        }
        catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
    }

    private async Task<bool> ShowElevationGuardDialogAsync(MainViewModel.ElevationGuardRequest request)
    {
        var dialog = new ElevationGuardWindow(request.GameTitle, request.TdpMode) { Topmost = true };
        _activeDialog = dialog;
        try { return await dialog.ShowDialog<bool>(this); }
        finally { _activeDialog = null; }
    }

    private void RestartElevated(MainViewModel vm)
    {
        WatchdogGate.MarkDeliberateQuit();
        vm.ClearOverlayNow();
        if (_overlayWindow != null) { _overlayWindow.IsAppClosing = true; _overlayWindow.Close(); }
        LauncherRestart.TryStartReplacementAsAdmin();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
        else Close();
    }

    private Window? _activeDialog;
}