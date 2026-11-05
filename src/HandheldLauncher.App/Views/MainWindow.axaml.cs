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

namespace HandheldLauncher.App.Views;

public partial class MainWindow : Window
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
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

    private const byte VK_LWIN = 0x5B;

    private const byte VK_D = 0x44;

    private const uint KEYEVENTF_KEYUP = 0x0002;

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

    // v0.16: só um detector de L3+R3 fica ativo por vez — ver
    // SetGamepadComboDetectionForGame.

    private OverlayHotkeyWatcher? _overlayHotkeyWatcher;
    private OverlayHotkeyWatcher? _inGameXInputObserver; // v0.16.2 diagnóstico (só log)

    private bool? _gamepadComboModeIsGame;

    public MainWindow()
    {
        InitializeComponent();

        ApplyLayoutTheme();

        GamesItemsControl.ContainerPrepared += (_, e) => e.Container.Margin = ContainerMargin();

        HeroImage.PropertyChanged += OnHeroImageChanged;

        AddHandler(KeyDownEvent, OnWindowKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        _gamepadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _gamepadTimer.Tick += OnGamepadTick;
        _gamepadTimer.Start();

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

        Closed += (_, _) => UnregisterRestoreHotkeyIfNeeded();
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
            if (delta < 500) { DebugLog.Write($"[OVERLAY] Debounce {delta:F0}ms, ignorado."); return; }
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

    /// <summary>
    /// v0.16: chamado uma vez no startup (de App.axaml.cs). Liga o detector
    /// de L3+R3 correto pro estado atual (sem jogo rodando = biblioteca) e
    /// garante a limpeza de ambos ao fechar o launcher.
    /// </summary>

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

    /// <summary>
    /// v0.16: alterna qual detector de L3+R3 está ativo, conforme um jogo
    /// está rodando ou não. Os dois nunca ficam ligados ao mesmo tempo:
    ///
    /// <list type="bullet">
    /// <item>Sem jogo (biblioteca): <see cref="OverlayHotkeyWatcher"/> — lê
    /// XInput comum; funciona porque o launcher tem o foco normal aqui.</item>
    /// <item>Com jogo rodando: <see cref="GameInputHelper"/> — processo
    /// ajudante com entrega de input em segundo plano, única forma de
    /// detectar L3+R3 com o jogo em foreground.</item>
    /// </list>
    ///
    /// Antes da v0.16 os dois ficavam ligados o tempo todo, o que podia
    /// disparar o mesmo ToggleOverlay() duas vezes em corrida (bug
    /// intermitente do Quick Settings) e mantinha o pedido de entrega de
    /// input em segundo plano ativo mesmo parado na biblioteca — suspeito de
    /// bloquear a suspensão/Modern Standby do aparelho.
    /// </summary>

    private void SetGamepadComboDetectionForGame(bool gameRunning)
    {
        // Os dois detectores (XInput e GameInput) só existem no Windows —
        // igual ao comportamento original, de antes da v0.16, em que ambos
        // só eram criados dentro de um "if (OperatingSystem.IsWindows())"
        // no App.axaml.cs.
        if (!OperatingSystem.IsWindows()) return;

        if (_gamepadComboModeIsGame == gameRunning) return;
        _gamepadComboModeIsGame = gameRunning;

        if (gameRunning)
        {
            _overlayHotkeyWatcher?.Dispose();
            _overlayHotkeyWatcher = null;
            GameInputHelper.StartHelperProcess();
            DebugLog.Write("[OVERLAY] Detector de L3+R3: GameInputHelper (jogo rodando).");

            // v0.16.2 (diagnóstico): no Days Gone e no Eden o GameInput entrega
            // o controle "em repouso" com o jogo em foco, mas antes da v0.16.0
            // o overlay abria nesses jogos — quando o detector XInput ficava
            // ligado o tempo todo. Hipótese: era ele que pegava o L3+R3. Este
            // observador SÓ registra no debug.log o que o XInput vê; nunca abre
            // o overlay (o detector do jogo continua sendo só o GameInput, pra
            // não voltar a corrida de toggle que a v0.16.0 removeu).
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
                // Não voltar para Topmost enquanto uma janela modal estiver aberta:
                // isso cobria o navegador aberto a partir de Contas/Configurações.
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

        // v0.16: cria a OverlayWindow já na inicialização (em vez de só na
        // primeira vez que o usuário abre o Quick Settings). O construtor da
        // OverlayWindow é quem restaura TDP e curva de ventoinha salvos — se
        // a janela só nascesse no primeiro L3+R3, o aparelho ficava no perfil
        // padrão do firmware até o usuário abrir o overlay manualmente depois
        // de reiniciar/desligar o console. A janela continua oculta (nunca
        // chamamos Show() aqui) até o usuário pedir o Quick Settings.
        if (_overlayWindow == null)
        {
            try
            {
                DebugLog.Write("[OVERLAY] Criando OverlayWindow na inicialização (restaura TDP/ventoinha).");
                _overlayWindow = new OverlayWindow(vm);
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[OVERLAY] Falha ao criar OverlayWindow na inicialização: {ex.Message}");
                _overlayWindow = null;
            }
        }

        vm.AddGameRequested += (_, _) => _ = OpenAddGameDialogAsync(vm);
        vm.EditGameRequested += (_, game) => _ = OpenEditGameDialogAsync(vm, game);
        vm.LayoutPickerRequested += (_, _) => _ = OpenLayoutPickerAsync();
        vm.SettingsRequested += (_, _) => _ = OpenSettingsDialogAsync(vm);
        vm.SystemUpdateRequested += (_, _) => _ = OpenSystemUpdateDialogAsync();
        vm.DriverUpdateRequested += (_, _) => _ = OpenDriverUpdateDialogAsync();
        vm.HelpCenterRequested += (_, _) => _ = OpenHelpCenterDialogAsync();

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
        vm.RestoreShellRequested += (_, _) => RestoreWindowsShell();
        vm.RestartLauncherRequested += (_, _) =>
        {
            WatchdogGate.MarkDeliberateQuit();
            vm.ClearOverlayNow();
            if (_overlayWindow != null) { _overlayWindow.IsAppClosing = true; _overlayWindow.Close(); }
            RelaunchSelfAfterExit();
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
            else Close();
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

                if (_overlayWindow != null && !_overlayWindow.IsLoaded)
                {
                    DebugLog.Write("[OVERLAY] Janela destruída, recriando.");
                    try { _overlayWindow.Close(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
                    _overlayWindow = null;
                }

                if (_overlayWindow == null)
                {
                    DebugLog.Write("[OVERLAY] Criando nova OverlayWindow.");
                    _overlayWindow = new OverlayWindow(vm);
                }

                _overlayWindow.SetGameHwnd(gameHwnd);
                _overlayWindow.SetGamePid(_detectedGamePid);

                DebugLog.Write($"[OVERLAY] Antes Show: IsVisible={_overlayWindow.IsVisible}, Pos={_overlayWindow.Position}, GameHwnd=0x{gameHwnd:X}");

                // v0.16.1: antes disto, o "flash" na posição errada era evitado
                // chamando ShowWindow() nativo (SW_HIDE / SW_SHOWNORMAL) por fora
                // do Avalonia. Isso muda a visibilidade da janela direto no
                // Windows, sem passar pelo Avalonia — e o OnGamepadTick decide
                // se o gamepad vai pro overlay ou pro launcher checando
                // _overlayWindow.IsVisible (a propriedade do Avalonia). Suspeito
                // de ser a causa do bug intermitente em que o Quick Settings
                // aparece na frente, mas a navegação continua presa no launcher
                // atrás: usar Opacity em vez de ShowWindow() nativo evita esconder
                // a janela "por fora" do Avalonia, então IsVisible fica estável
                // durante todo o processo de reposicionamento.
                _overlayWindow.Opacity = 0;
                _overlayWindow.Show();

                // v0.16.2: Show() real (acima) dispara OnOpened da própria
                // OverlayWindow em TODO open agora — não só no primeiro, como
                // antes de trocar pro Show()/Opacity real (v0.16.1). OnOpened
                // já faz seu próprio PositionOnRightEdge()+ForceTopmost() (mais
                // um retry de 200ms de segurança). Chamar os dois de novo aqui
                // duplicava o reposicionamento e o ForceTopmost em toda
                // abertura, competindo em ~200ms um com o outro — isso é o que
                // causava o overlay "brigando" com o launcher pela frente,
                // relatado depois do beta.1. Aqui só esperamos (mesma
                // prioridade de Dispatcher, então entra na fila depois do post
                // que o OnOpened já fez durante o Show() acima) e restauramos
                // a opacidade.
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        _overlayWindow.Opacity = 1;
                        DebugLog.Write($"[OVERLAY] Mostrado (reposicionado pelo OnOpened). Pos={_overlayWindow.Position}, IsVisible={_overlayWindow.IsVisible}");
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
            if (e.PropertyName == nameof(MainViewModel.IsFilterAllActive))
                FadeIn(GamesItemsControl, 220);

            if (e.PropertyName == nameof(MainViewModel.IsLaunching))
            {
                if (vm.IsLaunching && !string.IsNullOrEmpty(vm.LaunchingGameTitle))
                {
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

    private Window? _activeDialog;

}