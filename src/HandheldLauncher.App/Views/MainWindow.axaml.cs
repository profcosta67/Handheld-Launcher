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

    private static System.Collections.Generic.List<(IntPtr Hwnd, string Title, uint Pid)> GetAllVisibleWindows()
    {
        var results = new System.Collections.Generic.List<(IntPtr, string, uint)>();
        try
        {
            EnumWindows((hWnd, lParam) =>
            {
                try
                {
                    if (!IsWindowVisible(hWnd)) return true;
                    var sb = new System.Text.StringBuilder(512);
                    var len = GetWindowText(hWnd, sb, sb.Capacity);
                    if (len <= 0) return true;
                    var title = sb.ToString();
                    if (string.IsNullOrWhiteSpace(title)) return true;
                    GetWindowThreadProcessId(hWnd, out var pid);
                    results.Add((hWnd, title, pid));
                }
                catch { }
                return true;
            }, IntPtr.Zero);
        }
        catch { }
        return results;
    }

    private const int SW_RESTORE = 9;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
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
            try { InputBlockService.UnblockGamepadForGame(); } catch { }
            if (_overlayWindow != null)
            {
                _overlayWindow.IsAppClosing = true;
                _overlayWindow.Close();
            }
        };

        DataContextChanged += OnDataContextChanged;

        Opened += (_, _) =>
        {
            try { InputBlockService.RecoverOrphanedSuspension(); } catch { }

            StartTopmostEnforcer();
            RegisterRestoreHotkey();
            CreateTrayIcon();

            ForceForeground(allowRestore: true, forceIfGameRunning: true);

            StartForegroundFighter();

            PointerPressed += (_, _) => StopForegroundFighter();
            KeyDown += (_, _) => StopForegroundFighter();
        };

        Closed += (_, _) => UnregisterRestoreHotkeyIfNeeded();
        Activated += (_, _) => { if (OperatingSystem.IsWindows()) PowerThrottling.DisableForThisProcess(); };
    }

    private void ForceForeground(bool allowRestore = false, bool forceIfGameRunning = false)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!IsVisible) return;
        if (WindowState == WindowState.Minimized) return;

        try
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero) return;

            if (!forceIfGameRunning)
            {
                var fg = GetForegroundWindow();
                if (fg != IntPtr.Zero && fg != handle)
                    return;
            }

            if (allowRestore && WindowState == WindowState.Normal)
                ShowWindow(handle, SW_RESTORE);

            var foregroundHandle = GetForegroundWindow();
            if (foregroundHandle == handle)
            {
                SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                return;
            }

            if (foregroundHandle != IntPtr.Zero)
            {
                var fgThread = GetWindowThreadProcessId(foregroundHandle, out _);
                var myThread = GetCurrentThreadId();
                if (fgThread != 0 && fgThread != myThread)
                {
                    AttachThreadInput(fgThread, myThread, true);
                    try
                    {
                        BringWindowToTop(handle);
                        SetForegroundWindow(handle);
                        SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                    }
                    finally
                    {
                        AttachThreadInput(fgThread, myThread, false);
                    }
                    return;
                }
            }

            BringWindowToTop(handle);
            SetForegroundWindow(handle);
            SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[STARTUP] ForceForeground EXCEPTION: {ex.Message}");
        }
    }

    private void StartForegroundFighter()
    {
        _foregroundFighterTimer?.Stop();
        _foregroundFighterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        var attempts = 0;
        _foregroundFighterTimer.Tick += (_, _) =>
        {
            attempts++;

            if (!IsVisible) { _foregroundFighterTimer?.Stop(); return; }
            if (WindowState == WindowState.Minimized) { _foregroundFighterTimer?.Stop(); return; }
            if (IsActive) { _foregroundFighterTimer?.Stop(); return; }
            if (_activeDialog != null) { _foregroundFighterTimer?.Stop(); return; }

            try
            {
                var fg = GetForegroundWindow();
                var myHandle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (fg != IntPtr.Zero && myHandle != IntPtr.Zero && fg != myHandle)
                {
                    _foregroundFighterTimer?.Stop();
                    return;
                }
            }
            catch { }

            ForceForeground();

            if (attempts >= 20) _foregroundFighterTimer?.Stop();
        };
        _foregroundFighterTimer.Start();
        Closed += (_, _) => _foregroundFighterTimer?.Stop();
    }

    private static readonly TimeSpan TopmostEnforcerGracePeriod = TimeSpan.FromSeconds(120);

    private void StartTopmostEnforcer()
    {
        _topmostEnforcerTimer?.Stop();
        _topmostEnforcerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _topmostEnforcerTimer.Tick += (_, _) =>
        {
            if (!IsVisible) return;
            if (WindowState == WindowState.Minimized) return;
            if (_activeDialog != null) return;

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var fg = GetForegroundWindow();
                    var myHandle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                    if (fg != IntPtr.Zero && myHandle != IntPtr.Zero && fg != myHandle)
                        return;
                }
            }
            catch { }

            try
            {
                var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (handle != IntPtr.Zero)
                    SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            }
            catch { }
        };
        _topmostEnforcerTimer.Start();
        Closed += (_, _) => _topmostEnforcerTimer?.Stop();

        var graceTimer = new DispatcherTimer { Interval = TopmostEnforcerGracePeriod };
        graceTimer.Tick += (_, _) => { _topmostEnforcerTimer?.Stop(); graceTimer.Stop(); };
        graceTimer.Start();
        Closed += (_, _) => graceTimer.Stop();
    }

    private void StopForegroundFighter()
    {
        _foregroundFighterTimer?.Stop();
    }

    private void RegisterRestoreHotkey()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (TryGetPlatformHandle() is not { } handle) return;

        Win32Properties.AddWndProcHookCallback(this, OnNativeWndProcHook);
        _hotkeyRegistered = RegisterHotKey(handle.Handle, RestoreHotkeyId, ModControl | ModAlt | ModNoRepeat, VkH);
        RegisterHotKey(handle.Handle, OverlayHotkeyId, ModControl | ModAlt | ModNoRepeat, VkO);
    }

    private void UnregisterRestoreHotkeyIfNeeded()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (_hotkeyRegistered && TryGetPlatformHandle() is { } handle)
        {
            UnregisterHotKey(handle.Handle, RestoreHotkeyId);
            UnregisterHotKey(handle.Handle, OverlayHotkeyId);
        }
        Win32Properties.RemoveWndProcHookCallback(this, OnNativeWndProcHook);
        _hotkeyRegistered = false;
    }

    private IntPtr OnNativeWndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == RestoreHotkeyId) { RestoreFromTray(); handled = true; }
        else if (msg == WmHotkey && wParam.ToInt32() == OverlayHotkeyId) { Dispatcher.UIThread.Post(() => ToggleOverlay()); handled = true; }
        else if (msg == SingleInstance.RestoreMessageId) { RestoreFromTray(); handled = true; }
        else if (msg == TrayCallbackMessage)
        {
            var trayEvent = lParam.ToInt32();
            if (trayEvent == WmLButtonUp || trayEvent == WmLButtonDoubleClick)
                Dispatcher.UIThread.Post(RestoreFromTray);
            else if (trayEvent == WmRButtonUp)
                Dispatcher.UIThread.Post(ShowTrayContextMenu);
            handled = true;
        }
        return IntPtr.Zero;
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

    private void CreateTrayIcon()
    {
        if (!OperatingSystem.IsWindows() || _trayIconAdded) return;
        try
        {
            var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            var executable = Environment.ProcessPath;
            if (hwnd == IntPtr.Zero || string.IsNullOrWhiteSpace(executable)) return;
            if (ExtractIconEx(executable, 0, out var largeIcon, out var smallIcon, 1) == 0) return;

            _trayIconHandle = smallIcon != IntPtr.Zero ? smallIcon : largeIcon;
            _unusedTrayIconHandle = smallIcon != IntPtr.Zero ? largeIcon : IntPtr.Zero;
            if (_trayIconHandle == IntPtr.Zero) return;

            var data = new NotifyIconData
            {
                cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
                hWnd = hwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = TrayCallbackMessage,
                hIcon = _trayIconHandle,
                szTip = "Handheld Launcher",
                szInfo = string.Empty,
                szInfoTitle = string.Empty,
                guidItem = Guid.Empty
            };

            _trayIconAdded = Shell_NotifyIcon(NIM_ADD, ref data);
            if (!_trayIconAdded)
            {
                DebugLog.Write("[TRAY] Shell_NotifyIcon(NIM_ADD) falhou.");
                ReleaseTrayIconHandles();
                return;
            }
            DebugLog.Write("[TRAY] Ícone da bandeja registrado.");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[TRAY] Falha ao registrar ícone: {ex}");
            ReleaseTrayIconHandles();
        }
    }

    private void RemoveTrayIcon()
    {
        if (_trayIconAdded)
        {
            try
            {
                var data = new NotifyIconData
                {
                    cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
                    hWnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero,
                    uID = 1,
                    szTip = string.Empty,
                    szInfo = string.Empty,
                    szInfoTitle = string.Empty
                };
                Shell_NotifyIcon(NIM_DELETE, ref data);
            }
            catch (Exception ex) { DebugLog.Write($"[TRAY] Falha ao remover ícone: {ex.Message}"); }
        }
        _trayIconAdded = false;
        ReleaseTrayIconHandles();
    }

    private void ReleaseTrayIconHandles()
    {
        if (_trayIconHandle != IntPtr.Zero) { DestroyIcon(_trayIconHandle); _trayIconHandle = IntPtr.Zero; }
        if (_unusedTrayIconHandle != IntPtr.Zero) { DestroyIcon(_unusedTrayIconHandle); _unusedTrayIconHandle = IntPtr.Zero; }
    }

    private void ShowTrayContextMenu()
    {
        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return;
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenu(menu, MF_STRING, TrayMenuOpen, "Abrir Handheld Launcher");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, TrayMenuExit, "Sair");
            GetCursorPos(out var point);
            SetForegroundWindow(hwnd);
            var command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY, point.X, point.Y, hwnd, IntPtr.Zero);
            PostMessage(hwnd, 0, IntPtr.Zero, IntPtr.Zero);
            if (command == TrayMenuOpen) RestoreFromTray();
            else if (command == TrayMenuExit) QuitFromTray();
        }
        catch (Exception ex) { DebugLog.Write($"[TRAY] Falha ao exibir menu: {ex}"); }
        finally { DestroyMenu(menu); }
    }

    private void MinimizeToTray()
    {
        if (OperatingSystem.IsWindows())
        {
            CreateTrayIcon();
            if (_trayIconAdded) { Hide(); return; }
        }
        // Nunca esconder a janela se o shell não aceitar o ícone da bandeja.
        WindowState = WindowState.Minimized;
    }
    private bool AnimationsEnabled => !Classes.Contains("noAnim");

    private void FadeIn(Control control, int milliseconds)
    {
        if (!AnimationsEnabled) { control.Transitions = null; control.Opacity = 1; return; }
        control.Transitions = null; control.Opacity = 0;
        Dispatcher.UIThread.Post(() =>
        {
            control.Transitions = new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(milliseconds), Easing = new Avalonia.Animation.Easings.CubicEaseOut() } };
            control.Opacity = 1;
        }, DispatcherPriority.Background);
    }

    private void OnHeroImageChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Image.SourceProperty) return;
        if (!AnimationsEnabled || e.OldValue is null || e.NewValue is null) { HeroBackImage.Source = null; HeroImage.Transitions = null; HeroImage.Opacity = 1; return; }
        HeroBackImage.Source = e.OldValue as Avalonia.Media.IImage;
        FadeIn(HeroImage, 260);
    }

    private void OnContextMenuItemPressed(object? sender, PointerPressedEventArgs e) { if (DataContext is MainViewModel vm && sender is Control { DataContext: ContextMenuItemViewModel item }) { vm.ActivateContextMenuItem(item); e.Handled = true; } }
    private void OnGlobalMenuItemPressed(object? sender, PointerPressedEventArgs e) { if (DataContext is MainViewModel vm && sender is Control { DataContext: ContextMenuItemViewModel item }) { vm.ActivateGlobalMenuItem(item); e.Handled = true; } }
    private void OnCoverCandidatePressed(object? sender, PointerPressedEventArgs e) { if (DataContext is MainViewModel vm && sender is Control { DataContext: ArtworkCandidateViewModel candidate }) { vm.ActivateCoverCandidate(candidate); e.Handled = true; } }

    private void OnMenuOverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.IsCoverPickerOpen) vm.CloseCoverPicker();
        else if (vm.IsContextMenuOpen) vm.CloseContextMenu();
        else if (vm.IsGlobalMenuOpen) vm.CloseGlobalMenu();
        else if (vm.IsDownloadsOpen) vm.CloseDownloads();
        e.Handled = true;
    }

    private void OnAboutOverlayPressed(object? sender, PointerPressedEventArgs e) => (DataContext as MainViewModel)?.CloseAbout();
    private void OnOpenSteamDownloadsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.OpenSteamDownloads();
    private void OnShortcutsHelpClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.OpenShortcutsHelp();
    private void OnShortcutsOverlayPressed(object? sender, PointerPressedEventArgs e) => (DataContext as MainViewModel)?.CloseShortcutsHelp();
    private void OnShortcutsCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.CloseShortcutsHelp();

    public void QuitFromTray() { WatchdogGate.MarkDeliberateQuit(); Close(); }

    public void RestoreFromTray()
    {
        try
        {
            DebugLog.Write("[RESTORE] RestoreFromTray chamado.");
            Show();
            if (WindowState == WindowState.Minimized) WindowState = Topmost ? WindowState.FullScreen : WindowState.Normal;
            else if (Topmost) WindowState = WindowState.FullScreen;
            Activate();

            try
            {
                var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (handle != IntPtr.Zero)
                    SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            }
            catch { }

            DebugLog.Write("[RESTORE] RestoreFromTray concluído.");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[RESTORE] EXCEPTION: {ex.Message}");
        }
    }

    private static void TurnOffScreen() { if (!OperatingSystem.IsWindows()) return; try { PostMessage(HwndBroadcast, WmSysCommand, (IntPtr)ScMonitorPower, (IntPtr)2); } catch { } }
    private static void RestartDevice() { if (!OperatingSystem.IsWindows()) return; RunShutdownExe("/r /f /t 0"); }
    private static void ShutdownDevice() { if (!OperatingSystem.IsWindows()) return; RunShutdownExe("/s /f /t 0"); }

    private static void RunShutdownExe(string arguments)
    {
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var fullPath = Path.Combine(systemRoot, "shutdown.exe");
        var exe = File.Exists(fullPath) ? fullPath : "shutdown.exe";
        DebugLog.Write($"RunShutdownExe: '{exe}' {arguments}");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe, arguments) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) return;
            var stderr = process.StandardError.ReadToEnd(); var stdout = process.StandardOutput.ReadToEnd(); process.WaitForExit(5000);
            DebugLog.Write($"RunShutdownExe: exit={process.ExitCode} stdout='{stdout.Trim()}' stderr='{stderr.Trim()}'");
        }
        catch (Exception ex) { DebugLog.Write($"RunShutdownExe EXCEPTION ({exe}): {ex.Message} — tentando via shell."); try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, arguments) { UseShellExecute = true, CreateNoWindow = true }); } catch (Exception shellEx) { DebugLog.Write($"RunShutdownExe FALHOU também via shell: {shellEx.Message}"); } }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        vm.AddGameRequested += (_, _) => _ = OpenAddGameDialogAsync(vm);
        vm.EditGameRequested += (_, game) => _ = OpenEditGameDialogAsync(vm, game);
        vm.LayoutPickerRequested += (_, _) => _ = OpenLayoutPickerAsync();
        vm.SettingsRequested += (_, _) => _ = OpenSettingsDialogAsync(vm);
        vm.SystemUpdateRequested += (_, _) => _ = OpenSystemUpdateDialogAsync();
        vm.DriverUpdateRequested += (_, _) => _ = OpenDriverUpdateDialogAsync();

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
        vm.GameDetailsRequested += (_, game) => _ = OpenGameDetailsDialogAsync(vm, game);

        vm.OpenOverlayRequested += (_, _) =>
        {
            try
            {
                DebugLog.Write("[OVERLAY] Handler disparado.");

                IntPtr gameHwnd = IntPtr.Zero;
                if (OperatingSystem.IsWindows())
                {
                    try { gameHwnd = GetForegroundWindow(); } catch { }
                }

                if (_overlayWindow != null && !_overlayWindow.IsLoaded)
                {
                    DebugLog.Write("[OVERLAY] Janela destruída, recriando.");
                    try { _overlayWindow.Close(); } catch { }
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

                _overlayWindow.Show();

                var hwnd = _overlayWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, 0);

                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            _overlayWindow.PositionOnRightEdge();
                            ShowWindow(hwnd, 1);
                            _overlayWindow.ForceTopmost();
                            DebugLog.Write($"[OVERLAY] Reposicionado e mostrado. Pos={_overlayWindow.Position}");
                        }
                        catch (Exception ex)
                        {
                            DebugLog.Write($"[OVERLAY] PostShow EXCEPTION: {ex.Message}");
                        }
                    }, DispatcherPriority.Loaded);
                }

                DebugLog.Write($"[OVERLAY] Depois Show: IsVisible={_overlayWindow.IsVisible}, Pos={_overlayWindow.Position}");
            }
            catch (Exception ex)
            {
                DebugLog.Write("========== ERRO AO ABRIR OVERLAY ==========");
                DebugLog.Write(ex.ToString());
                DebugLog.Write("===========================================");
                try { _overlayWindow?.Close(); } catch { }
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
        catch { }
    }

    private void RestoreWindowsShell()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            WatchdogGate.MarkDeliberateQuit();

            var settings = AppSettingsStore.Load();
            settings.KioskMode = false;
            settings.ModeChosen = true;
            AppSettingsStore.Save(settings);
            DebugLog.Write("RestoreWindowsShell: Kiosk Mode desativado.");

            var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

            if (Process.GetProcessesByName("explorer").Length == 0 && File.Exists(explorerPath))
            {
                Process.Start(new ProcessStartInfo { FileName = explorerPath, UseShellExecute = true });
                DebugLog.Write("RestoreWindowsShell: explorer.exe iniciado.");
            }

            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_D, 0, 0, UIntPtr.Zero);
            keybd_event(VK_D, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            DebugLog.Write("RestoreWindowsShell: Atalho Win+D enviado.");

            Close();
        }
        catch (Exception ex) { DebugLog.Write($"RestoreWindowsShell: EXCEPTION {ex.Message}"); }
    }

    private void OnUpdateBadgeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { if (DataContext is MainViewModel vm) _ = OpenUpdateDialogAsync(vm); }

    private async Task OpenUpdateDialogAsync(MainViewModel vm)
    {
        if (!vm.IsUpdateReady) return;
        var dialog = new UpdateWindow(vm);
        _activeDialog = dialog;
        try { await dialog.ShowDialog(this); } finally { _activeDialog = null; }
    }

    private DispatcherTimer? _downloadsRefreshTimer;

    private bool _gameWindowSeen = false;
    private int _gameWindowMissingSeconds = 0;
    private int _gameWatcherTicks = 0;
    private bool _gameWatcherDiagnosticLogged = false;
    private int _detectedGamePid = 0;
    private string _lastLoggedExeHint = string.Empty;

    private void ShowLaunchPopup(string title, string? iconPath)
    {
        try
        {
            try { _launchPopup?.Close(); } catch { }
            _launchPopup = null;
            _launchPopupTimer?.Stop();
            _launchPopupTimer = null;

            _launchPopup = new GameLaunchPopupWindow(title, iconPath);
            _launchPopup.Show();

            var thisPopup = _launchPopup;

            _launchPopupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
            _launchPopupTimer.Tick += (_, _) =>
            {
                _launchPopupTimer?.Stop();

                bool isSame = ReferenceEquals(_launchPopup, thisPopup);
                DebugLog.Write($"[POPUP] Timeout 90s disparado. isSame={isSame}, visible={_launchPopup?.IsVisible}, loaded={_launchPopup?.IsLoaded}");

                if (isSame && _launchPopup is not null)
                {
                    try
                    {
                        _launchPopup.Close();
                        DebugLog.Write("[POPUP] Close() executado.");
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Write($"[POPUP] Close() EXCEPTION: {ex.Message}");
                    }
                    _launchPopup = null;
                }
                else
                {
                    DebugLog.Write("[POPUP] Timer disparou, mas janela já não é a mesma (já foi fechada).");
                }
            };
            _launchPopupTimer.Start();

            DebugLog.Write($"[POPUP] Aberto: '{title}' (watchdog 90s iniciado)");
        }
        catch (Exception ex) { DebugLog.Write($"[POPUP] EXCEPTION: {ex.Message}"); }
    }

    private void CloseLaunchPopupWithGrace()
    {
        DebugLog.Write($"[POPUP] CloseLaunchPopupWithGrace chamado. popup_null={_launchPopup is null}");
        if (_launchPopup is null) return;

        _launchPopupTimer?.Stop();
        _launchPopupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _launchPopupTimer.Tick += (_, _) =>
        {
            _launchPopupTimer?.Stop();
            try { _launchPopup?.Close(); } catch { }
            _launchPopup = null;
            DebugLog.Write("[POPUP] Fechado após 3s de detecção da janela.");
        };
        _launchPopupTimer.Start();
        DebugLog.Write("[POPUP] Janela detectada — fechando em 3s.");
    }

    private void StartGameWatcher(MainViewModel vm)
    {
        _gameWatcherTimer?.Stop();
        _gameWindowSeen = false;
        _gameWindowMissingSeconds = 0;
        _gameWatcherTicks = 0;
        _gameWatcherDiagnosticLogged = false;
        _detectedGamePid = 0;
        _lastLoggedExeHint = string.Empty;

        DebugLog.Write("[JOGO-WATCHER] Iniciado.");

        _gameWatcherTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _gameWatcherTimer.Tick += (_, _) =>
        {
            if (vm.RunningGame is null) { _gameWindowSeen = false; _gameWindowMissingSeconds = 0; _gameWatcherTicks = 0; return; }
            if (!OperatingSystem.IsWindows()) return;
            if (_activeDialog != null) { _gameWindowMissingSeconds = 0; return; }

            _gameWatcherTicks++;

            try
            {
                var game = vm.RunningGame;
                var gameTitle = game.Title;
                if (string.IsNullOrEmpty(gameTitle)) return;

                static string Normalize(string s)
                {
                    if (string.IsNullOrEmpty(s)) return string.Empty;
                    var sb = new System.Text.StringBuilder(s.Length);
                    foreach (var c in s)
                        if (char.IsLetterOrDigit(c) || c == ' ')
                            sb.Append(char.ToLowerInvariant(c));
                    return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
                }

                var normalizedTarget = Normalize(gameTitle);

                var exeHint = string.Empty;
                try
                {
                    if (!string.IsNullOrEmpty(game.InstallDirectory) && Directory.Exists(game.InstallDirectory))
                    {
                        var badNames = new[] { "unins", "crash", "report", "installer", "setup", "ubisoft", "upc", "connect", "launcher", "prereq", "vcredist", "directx", "vulkan", "dxsetup", "dotnet" };

                        bool IsGameExe(string path)
                        {
                            var name = Path.GetFileName(path).ToLowerInvariant();
                            return !badNames.Any(b => name.Contains(b));
                        }

                        var candidates = Directory.EnumerateFiles(game.InstallDirectory, "*.exe", SearchOption.AllDirectories)
                            .Where(IsGameExe)
                            .OrderByDescending(f => new FileInfo(f).Length)
                            .ToList();

                        if (candidates.Count > 0)
                            exeHint = Path.GetFileNameWithoutExtension(candidates[0]).ToLowerInvariant();

                        if (!string.Equals(_lastLoggedExeHint, exeHint, StringComparison.OrdinalIgnoreCase))
                        {
                            _lastLoggedExeHint = exeHint;
                            DebugLog.Write($"[JOGO-WATCHER] exeHint detectado: '{exeHint}' (de {candidates.Count} candidatos)");
                        }
                    }
                }
                catch { }

                var windows = GetAllVisibleWindows();
                bool found = false;
                IntPtr foundHwnd = IntPtr.Zero;
                uint foundPid = 0;

                foreach (var w in windows)
                {
                    try
                    {
                        var norm = Normalize(w.Title);
                        if (!string.IsNullOrEmpty(normalizedTarget) && norm.Contains(normalizedTarget))
                        {
                            found = true;
                            foundHwnd = w.Hwnd;
                            foundPid = w.Pid;
                            break;
                        }
                    }
                    catch { }
                }

                if (!found && !string.IsNullOrEmpty(exeHint))
                {
                    foreach (var w in windows)
                    {
                        try
                        {
                            var proc = Process.GetProcessById((int)w.Pid);
                            var procName = proc.ProcessName.ToLowerInvariant();
                            if (procName == exeHint || procName.Contains(exeHint) || exeHint.Contains(procName))
                            {
                                found = true;
                                foundHwnd = w.Hwnd;
                                foundPid = w.Pid;
                                break;
                            }
                        }
                        catch { }
                    }
                }

                // Verificação rápida de processo morto — restaura em ~1s em vez de esperar 8s
                if (_gameWindowSeen && _detectedGamePid != 0)
                {
                    bool procDead = false;
                    try
                    {
                        using var proc = Process.GetProcessById(_detectedGamePid);
                        procDead = proc.HasExited;
                    }
                    catch (ArgumentException) { procDead = true; }
                    catch { }

                    if (procDead)
                    {
                        DebugLog.Write($"[JOGO-WATCHER] Processo do jogo morreu — restaurando launcher imediatamente.");
                        LosslessScalingService.SetGameHwnd(IntPtr.Zero);
                        vm.NotifyGameExitedExternally();
                        _gameWindowSeen = false;
                        _gameWindowMissingSeconds = 0;
                        _detectedGamePid = 0;
                        return;
                    }
                }

                if (!found && !_gameWindowSeen && !_gameWatcherDiagnosticLogged && _gameWatcherTicks >= 10)
                {
                    _gameWatcherDiagnosticLogged = true;
                    DebugLog.Write($"[JOGO-WATCHER] DIAGNÓSTICO (tick={_gameWatcherTicks}): '{gameTitle}' não detectado. exeHint='{exeHint}', installDir='{game.InstallDirectory}'.");
                    DebugLog.Write($"[JOGO-WATCHER] Janelas visíveis no sistema ({windows.Count}):");
                    foreach (var w in windows)
                        DebugLog.Write($"[JOGO-WATCHER]   - '{w.Title}' (pid={w.Pid}, hwnd=0x{w.Hwnd:X})");
                }

                if (found)
                {
                    if (!_gameWindowSeen)
                    {
                        DebugLog.Write($"[JOGO-WATCHER] Janela de '{gameTitle}' detectada pela primeira vez (hwnd=0x{foundHwnd:X}, pid={foundPid}).");
                        LosslessScalingService.SetGameHwnd(foundHwnd);
                        _detectedGamePid = (int)foundPid;
                        CloseLaunchPopupWithGrace();
                        _gameWindowSeen = true;
                    }
                    _gameWindowMissingSeconds = 0;
                }
                else if (_gameWindowSeen)
                {
                    _gameWindowMissingSeconds += 1;
                    DebugLog.Write($"[JOGO-WATCHER] Janela de '{gameTitle}' ausente há {_gameWindowMissingSeconds}s.");
                    if (_gameWindowMissingSeconds >= 8)
                    {
                        DebugLog.Write($"[JOGO-WATCHER] Janela de '{gameTitle}' sumiu há {_gameWindowMissingSeconds}s — liberando launcher.");
                        LosslessScalingService.SetGameHwnd(IntPtr.Zero);
                        vm.NotifyGameExitedExternally();
                        _gameWindowSeen = false;
                        _gameWindowMissingSeconds = 0;
                        _detectedGamePid = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[JOGO-WATCHER] EXCEPTION: {ex.Message}");
            }
        };
        _gameWatcherTimer.Start();
        Closed += (_, _) => _gameWatcherTimer?.Stop();
    }

    private void StartDownloadsRefreshTimer(MainViewModel vm)
    {
        var ticksSinceBackgroundRefresh = 0;
        _downloadsRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _downloadsRefreshTimer.Tick += (_, _) =>
        {
            if (vm.IsDownloadsOpen) { vm.RefreshActiveDownloads(); ticksSinceBackgroundRefresh = 0; return; }
            if (vm.RunningGame is not null) return;
            ticksSinceBackgroundRefresh++;
            if (ticksSinceBackgroundRefresh >= 3) { ticksSinceBackgroundRefresh = 0; vm.RefreshActiveDownloads(); }
        };
        _downloadsRefreshTimer.Start();
        Closed += (_, _) => _downloadsRefreshTimer?.Stop();
        vm.RefreshActiveDownloads();
    }

    private void StartBatteryMonitor(MainViewModel vm)
    {
        vm.BatteryStatusText = BatteryStatus.GetDisplayText();
        vm.ClockText = DateTime.Now.ToString("HH:mm");
        _batteryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _batteryTimer.Tick += (_, _) => { vm.BatteryStatusText = BatteryStatus.GetDisplayText(); vm.ClockText = DateTime.Now.ToString("HH:mm"); };
        _batteryTimer.Start();
        Closed += (_, _) => _batteryTimer?.Stop();
    }

    private void OnGamepadTick(object? sender, EventArgs e)
    {
        try
        {
            if (DataContext is not MainViewModel vm) return;
            var actions = _gamepad.Poll();

            if (_activeDialog is { } dialog)
            {
                foreach (var dialogAction in actions)
                {
                    UiSounds.PlayForAction(dialogAction);
                    if (dialog is SettingsWindow sw && sw.HandleSettingsGamepadAction(dialogAction))
                        continue;
                    DialogGamepadNavigator.Handle(dialog, dialogAction);
                }
                return;
            }

            foreach (var action in actions)
            {
                if (action == GamepadAction.ToggleQuickSettings) { UiSounds.PlayForAction(action); ToggleOverlay(); continue; }
                if (_overlayWindow != null && _overlayWindow.IsVisible) { UiSounds.PlayForAction(action); _overlayWindow.HandleGamepadAction(action); continue; }

                if (vm.IsAboutOpen)
                {
                    if (action is GamepadAction.Back or GamepadAction.Confirm or GamepadAction.GlobalMenu or GamepadAction.Menu) { UiSounds.PlayBack(); vm.CloseAbout(); }
                    continue;
                }

                if (vm.IsShortcutsHelpOpen)
                {
                    if (action is GamepadAction.Back or GamepadAction.Confirm or GamepadAction.GlobalMenu or GamepadAction.Menu) { UiSounds.PlayBack(); vm.CloseShortcutsHelp(); }
                    continue;
                }

                if (vm.RunningGame is not null && !IsOurWindowInForeground()) continue;
                UiSounds.PlayForAction(action);

                if (vm.IsCoverPickerOpen)
                {
                    switch (action)
                    {
                        case GamepadAction.MoveLeft: vm.MoveCoverPickerSelection(-1); ScrollCoverPickerSelectionIntoView(vm); break;
                        case GamepadAction.MoveRight: vm.MoveCoverPickerSelection(1); ScrollCoverPickerSelectionIntoView(vm); break;
                        case GamepadAction.MoveUp: vm.MoveCoverPickerSelectionVertical(-1); ScrollCoverPickerSelectionIntoView(vm); break;
                        case GamepadAction.MoveDown: vm.MoveCoverPickerSelectionVertical(1); ScrollCoverPickerSelectionIntoView(vm); break;
                        case GamepadAction.Confirm: vm.ConfirmCoverPickerSelection(); break;
                        case GamepadAction.Back: case GamepadAction.Menu: case GamepadAction.GlobalMenu: vm.CloseCoverPicker(); break;
                    }
                    continue;
                }

                if (vm.IsContextMenuOpen)
                {
                    switch (action)
                    {
                        case GamepadAction.MoveUp: vm.MoveContextMenuSelection(-1); break;
                        case GamepadAction.MoveDown: vm.MoveContextMenuSelection(1); break;
                        case GamepadAction.Confirm: vm.ConfirmContextMenuSelection(); break;
                        case GamepadAction.Back: case GamepadAction.Menu: vm.CloseContextMenu(); break;
                    }
                    continue;
                }

                if (vm.IsGlobalMenuOpen)
                {
                    switch (action)
                    {
                        case GamepadAction.MoveUp: vm.MoveGlobalMenuSelection(-1); break;
                        case GamepadAction.MoveDown: vm.MoveGlobalMenuSelection(1); break;
                        case GamepadAction.Confirm: vm.ConfirmGlobalMenuSelection(); break;
                        case GamepadAction.Back: vm.GlobalMenuBack(); break;
                        case GamepadAction.GlobalMenu: vm.CloseGlobalMenu(); break;
                    }
                    continue;
                }

                if (vm.IsDownloadsOpen)
                {
                    switch (action)
                    {
                        case GamepadAction.Back: case GamepadAction.Menu: case GamepadAction.GlobalMenu: case GamepadAction.Downloads: vm.CloseDownloads(); break;
                        case GamepadAction.Confirm: vm.OpenSteamDownloads(); break;
                    }
                    continue;
                }

                if (vm.IsSubFilterRowFocused)
                {
                    switch (action)
                    {
                        case GamepadAction.MoveLeft: vm.MoveSubFilterCursor(-1); continue;
                        case GamepadAction.MoveRight: vm.MoveSubFilterCursor(1); continue;
                        case GamepadAction.Confirm: vm.ToggleFocusedSubFilter(); continue;
                        case GamepadAction.MoveDown: case GamepadAction.Back: vm.LeaveSubFilterRow(); continue;
                        case GamepadAction.MoveUp: continue;
                        case GamepadAction.PreviousFilter: case GamepadAction.NextFilter: break;
                        default: continue;
                    }
                }

                switch (action)
                {
                    case GamepadAction.Downloads: vm.ToggleDownloads(); break;
                    case GamepadAction.MoveLeft: vm.MovePrevious(); ScrollGameRowSelectionIntoView(vm); break;
                    case GamepadAction.MoveRight: vm.MoveNext(); ScrollGameRowSelectionIntoView(vm); break;
                    case GamepadAction.MoveUp: if (!MoveSelectionVertical(vm, -1)) vm.FocusSubFilterRow(); break;
                    case GamepadAction.MoveDown: MoveSelectionVertical(vm, 1); break;
                    case GamepadAction.Confirm: vm.LaunchSelected(); ScrollGameRowSelectionIntoView(vm); break;
                    case GamepadAction.Back: vm.CloseRunningGame(); break;
                    case GamepadAction.Menu: vm.ToggleContextMenu(); break;
                    case GamepadAction.GlobalMenu: vm.ToggleGlobalMenu(); break;
                    case GamepadAction.PreviousFilter: vm.CyclePreviousFilter(); break;
                    case GamepadAction.NextFilter: vm.CycleNextFilter(); break;
                    case GamepadAction.GameDetails: vm.OpenGameDetailsForSelected(); break;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Tick] EXCEPTION no handler do gamepad: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.Key == Key.F1) { vm.ToggleShortcutsHelp(); e.Handled = true; return; }
        if (vm.IsShortcutsHelpOpen) { vm.CloseShortcutsHelp(); e.Handled = true; return; }

        if (vm.IsCoverPickerOpen)
        {
            switch (e.Key)
            {
                case Key.Left: vm.MoveCoverPickerSelection(-1); ScrollCoverPickerSelectionIntoView(vm); e.Handled = true; break;
                case Key.Right: vm.MoveCoverPickerSelection(1); ScrollCoverPickerSelectionIntoView(vm); e.Handled = true; break;
                case Key.Up: vm.MoveCoverPickerSelectionVertical(-1); ScrollCoverPickerSelectionIntoView(vm); e.Handled = true; break;
                case Key.Down: vm.MoveCoverPickerSelectionVertical(1); ScrollCoverPickerSelectionIntoView(vm); e.Handled = true; break;
                case Key.Enter: vm.ConfirmCoverPickerSelection(); e.Handled = true; break;
                case Key.Escape: vm.CloseCoverPicker(); e.Handled = true; break;
            }
            return;
        }

        if (vm.IsContextMenuOpen)
        {
            switch (e.Key)
            {
                case Key.Up: vm.MoveContextMenuSelection(-1); e.Handled = true; break;
                case Key.Down: vm.MoveContextMenuSelection(1); e.Handled = true; break;
                case Key.Enter: vm.ConfirmContextMenuSelection(); e.Handled = true; break;
                case Key.Escape: case Key.Space: vm.CloseContextMenu(); e.Handled = true; break;
            }
            return;
        }

        if (vm.IsAboutOpen) { if (e.Key is Key.Escape or Key.Enter or Key.Tab) { vm.CloseAbout(); e.Handled = true; } return; }

        if (vm.IsGlobalMenuOpen)
        {
            switch (e.Key)
            {
                case Key.Up: vm.MoveGlobalMenuSelection(-1); e.Handled = true; break;
                case Key.Down: vm.MoveGlobalMenuSelection(1); e.Handled = true; break;
                case Key.Enter: vm.ConfirmGlobalMenuSelection(); e.Handled = true; break;
                case Key.Escape: vm.GlobalMenuBack(); e.Handled = true; break;
                case Key.Tab: vm.CloseGlobalMenu(); e.Handled = true; break;
            }
            return;
        }

        if (vm.IsDownloadsOpen)
        {
            switch (e.Key)
            {
                case Key.Escape: case Key.Tab: case Key.D: vm.CloseDownloads(); e.Handled = true; break;
                case Key.Enter: vm.OpenSteamDownloads(); e.Handled = true; break;
            }
            return;
        }

        if (vm.IsSubFilterRowFocused)
        {
            switch (e.Key)
            {
                case Key.Left: vm.MoveSubFilterCursor(-1); e.Handled = true; return;
                case Key.Right: vm.MoveSubFilterCursor(1); e.Handled = true; return;
                case Key.Enter: case Key.Space: vm.ToggleFocusedSubFilter(); e.Handled = true; return;
                case Key.Down: case Key.Escape: vm.LeaveSubFilterRow(); e.Handled = true; return;
                case Key.Up: e.Handled = true; return;
            }
        }

        var settings = AppSettingsStore.Load();

        if (TryMatch(settings.KeyLaunchGame, e)) { vm.LaunchSelected(); ScrollGameRowSelectionIntoView(vm); e.Handled = true; return; }
        if (TryMatch(settings.KeyGlobalMenu, e)) { vm.ToggleGlobalMenu(); e.Handled = true; return; }
        if (TryMatch(settings.KeyCloseGame, e)) { vm.CloseRunningGame(); e.Handled = true; return; }
        if (TryMatch(settings.KeyContextMenu, e)) { vm.ToggleContextMenu(); e.Handled = true; return; }
        if (TryMatch(settings.KeyPrevFilter, e)) { vm.CyclePreviousFilter(); e.Handled = true; return; }
        if (TryMatch(settings.KeyNextFilter, e)) { vm.CycleNextFilter(); e.Handled = true; return; }
        if (TryMatch(settings.KeyDownloads, e)) { vm.ToggleDownloads(); e.Handled = true; return; }

        switch (e.Key)
        {
            case Key.Left: vm.MovePrevious(); ScrollGameRowSelectionIntoView(vm); e.Handled = true; break;
            case Key.Right: vm.MoveNext(); ScrollGameRowSelectionIntoView(vm); e.Handled = true; break;
            case Key.Up: if (!MoveSelectionVertical(vm, -1)) vm.FocusSubFilterRow(); e.Handled = true; break;
            case Key.Down: MoveSelectionVertical(vm, 1); e.Handled = true; break;
        }
    }

    private bool TryMatch(string gestureString, KeyEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(gestureString)) return false;
        try { var gesture = KeyGesture.Parse(gestureString); return gesture.Matches(e); }
        catch { return false; }
    }

    private void ScrollCoverPickerSelectionIntoView(MainViewModel vm)
    {
        if (CoverPickerScrollViewer is null) return;
        if (!vm.IsCoverPickerOpen || vm.CoverPickerCandidates.Count == 0) return;
        const double itemHeight = 170 + 12;
        var row = vm.CoverPickerIndex / MainViewModel.CoverPickerColumns;
        var itemTop = row * itemHeight;
        var itemBottom = itemTop + itemHeight;
        var viewTop = CoverPickerScrollViewer.Offset.Y;
        var viewBottom = viewTop + CoverPickerScrollViewer.Viewport.Height;
        if (itemTop < viewTop) CoverPickerScrollViewer.Offset = new Vector(CoverPickerScrollViewer.Offset.X, itemTop);
        else if (itemBottom > viewBottom) CoverPickerScrollViewer.Offset = new Vector(CoverPickerScrollViewer.Offset.X, itemBottom - CoverPickerScrollViewer.Viewport.Height);
    }

    private void ScrollGameRowSelectionIntoView(MainViewModel vm) => Dispatcher.UIThread.Post(() => ScrollSelectionIntoViewNow(vm), DispatcherPriority.Background);

    private void ScrollSelectionIntoViewNow(MainViewModel vm)
    {
        if (GameRowScrollViewer is null || GamesItemsControl is null) return;
        if (vm.Games.Count == 0 || vm.SelectedIndex < 0 || vm.SelectedIndex >= vm.Games.Count) return;

        var container = GamesItemsControl.ContainerFromIndex(vm.SelectedIndex);
        // Medir a tile em relação à janela visível do ScrollViewer (não ao ItemsControl).
        // Assim a posição já considera o deslocamento atual e funciona mesmo quando
        // o ItemsControl ocupa toda a largura do layout.
        var position = container?.TranslatePoint(new Point(0, 0), GameRowScrollViewer);
        if (container is not null && position is { } p)
        {
            var offset = GameRowScrollViewer.Offset;
            var viewport = GameRowScrollViewer.Viewport;
            var maxX = Math.Max(0, GameRowScrollViewer.Extent.Width - viewport.Width);
            var maxY = Math.Max(0, GameRowScrollViewer.Extent.Height - viewport.Height);

            if (_verticalLayout)
            {
                var top = p.Y - 12;
                var bottom = p.Y + container.Bounds.Height + 12;
                var nextY = offset.Y;
                if (top < 0) nextY += top;
                else if (bottom > viewport.Height) nextY += bottom - viewport.Height;
                GameRowScrollViewer.Offset = new Vector(offset.X, Math.Clamp(nextY, 0, maxY));
            }
            else if (_libraryLayout.CentersSelection)
            {
                var target = offset.X + p.X + container.Bounds.Width / 2 - viewport.Width / 2;
                GameRowScrollViewer.Offset = new Vector(Math.Clamp(target, 0, maxX), offset.Y);
            }
            else
            {
                // Clássico: deslocamento mínimo, somente quando a capa sair da área visível.
                var left = p.X;
                var right = p.X + container.Bounds.Width;
                var nextX = offset.X;
                if (left < 0) nextX += left;
                else if (right > viewport.Width) nextX += right - viewport.Width;
                GameRowScrollViewer.Offset = new Vector(Math.Clamp(nextX, 0, maxX), offset.Y);
            }
            return;
        }

        // Fallback para o caso em que o container ainda não foi realizado pelo ItemsControl.
        if (_verticalLayout) return;
        const double spacing = 28;
        double itemLeft = 0;
        for (var i = 0; i < vm.SelectedIndex; i++)
            itemLeft += vm.Games[i].TileWidth + spacing;

        var itemRight = itemLeft + vm.Games[vm.SelectedIndex].TileWidth;
        var viewLeft = GameRowScrollViewer.Offset.X;
        var viewRight = viewLeft + GameRowScrollViewer.Viewport.Width;
        var fallbackMaxOffset = Math.Max(0, GameRowScrollViewer.Extent.Width - GameRowScrollViewer.Viewport.Width);
        if (itemLeft < viewLeft)
            GameRowScrollViewer.Offset = new Vector(Math.Clamp(itemLeft, 0, fallbackMaxOffset), GameRowScrollViewer.Offset.Y);
        else if (itemRight > viewRight)
            GameRowScrollViewer.Offset = new Vector(Math.Clamp(itemRight - GameRowScrollViewer.Viewport.Width, 0, fallbackMaxOffset), GameRowScrollViewer.Offset.Y);
    }

    private bool _verticalLayout;
    private bool _layoutInitialized;
    private ITemplate<Panel?>? _horizontalPanelTemplate;
    private Transitions? _rowScrollTransitions;

    private Thickness ContainerMargin() => _verticalLayout ? new Thickness(0, 0, 18, 24) : default;

    private void ApplyAppearance(MainViewModel vm, AppSettings settings)
    {
        try
        {
            ThemeManager.Apply(settings.Theme);
            ThemeManager.ApplyFont(this, settings.UiFont);
            ThemeManager.ApplyFontSize(settings.UiFontSize);
            Classes.Set("noAnim", !settings.Animations);
            _rowScrollTransitions ??= GameRowScrollViewer.Transitions;
            GameRowScrollViewer.Transitions = settings.Animations ? _rowScrollTransitions : null;
            UiSounds.Enabled = settings.SoundEffects;
            UiSounds.Pack = settings.SoundTheme;
            UiSounds.Volume = settings.SoundVolume;
            if (OperatingSystem.IsWindows()) DisplayColorManager.Apply(settings.ColorMode);
            var scale = GameTileViewModel.ScaleForSetting(settings.TileSize);
            var scaleChanged = Math.Abs(scale - GameTileViewModel.SizeScale) > 0.001;
            GameTileViewModel.SizeScale = scale;
            if (!_layoutInitialized || settings.VerticalLayout != _verticalLayout) { SetLayout(settings.VerticalLayout); _layoutInitialized = true; }
            if (scaleChanged) vm.ReloadTiles();
            ScrollGameRowSelectionIntoView(vm);
        }
        catch (Exception ex) { DebugLog.Write($"ApplyAppearance EXCEPTION {ex}"); }
    }

    private void ApplyLayoutTheme()
    {
        UserControl layout;
        try
        {
            var settings = AppSettingsStore.Load();
            layout = settings.LayoutTheme switch
            {
                2 => new Layouts.BigScreenLibraryLayout(),
                1 => new Layouts.SwitchLibraryLayout(), // índice legado mantido para instalações existentes (agora chamado Clean)
                _ => new Layouts.ClassicLibraryLayout()
            };
        }
        catch (Exception ex) { DebugLog.Write($"ApplyLayoutTheme: falhou ao ler settings, usando Clássico. {ex}"); layout = new Layouts.ClassicLibraryLayout(); }
        _libraryLayout = (ILibraryLayout)layout;
        GamesItemsControl = _libraryLayout.GamesItemsControl;
        GameRowScrollViewer = _libraryLayout.GameRowScrollViewer;
        HeroImage = _libraryLayout.HeroImage;
        HeroBackImage = _libraryLayout.HeroBackImage;
        LibraryLayoutHost.Content = layout;
    }

    private void SetLayout(bool vertical)
    {
        if (vertical && !_libraryLayout.SupportsVerticalGrid) return;
        _verticalLayout = vertical;
        _horizontalPanelTemplate ??= GamesItemsControl.ItemsPanel;
        GamesItemsControl.ItemsPanel = vertical ? new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Orientation.Horizontal }) : _horizontalPanelTemplate;
        GameRowScrollViewer.HorizontalScrollBarVisibility = vertical || !_libraryLayout.SupportsVerticalGrid ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;
        GameRowScrollViewer.VerticalScrollBarVisibility = vertical ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        GameRowScrollViewer.Offset = default;
        foreach (var container in GamesItemsControl.GetRealizedContainers()) container.Margin = ContainerMargin();
    }

    private bool MoveSelectionVertical(MainViewModel vm, int direction)
    {
        if (!_verticalLayout || vm.SelectedIndex < 0) return false;
        var current = GamesItemsControl.ContainerFromIndex(vm.SelectedIndex);
        if (current is null) return false;
        var currentBounds = current.Bounds;
        var currentCenterX = currentBounds.X + currentBounds.Width / 2;
        var best = -1; var bestRowY = direction > 0 ? double.MaxValue : double.MinValue; var bestDx = double.MaxValue;
        for (var i = 0; i < vm.Games.Count; i++)
        {
            var container = GamesItemsControl.ContainerFromIndex(i);
            if (container is null) continue;
            var b = container.Bounds;
            var isInDirection = direction > 0 ? b.Y > currentBounds.Y + 1 : b.Y < currentBounds.Y - 1;
            if (!isInDirection) continue;
            var isCloserRow = direction > 0 ? b.Y < bestRowY - 1 : b.Y > bestRowY + 1;
            var isSameRow = Math.Abs(b.Y - bestRowY) <= 1;
            var dx = Math.Abs(b.X + b.Width / 2 - currentCenterX);
            if (isCloserRow || (isSameRow && dx < bestDx)) { best = i; bestRowY = b.Y; bestDx = dx; }
        }
        if (best < 0) return false;
        vm.SelectedIndex = best;
        ScrollGameRowSelectionIntoView(vm);
        return true;
    }

    public void HandleSubFilterClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is Button { DataContext: SubFilterViewModel subFilter }) vm.ToggleSubFilter(subFilter);
    }

    public void HandleTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not Control { DataContext: GameTileViewModel tile }) return;
        var index = vm.Games.IndexOf(tile);
        if (index >= 0) vm.SelectedIndex = index;
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed && !vm.IsContextMenuOpen) { vm.ToggleContextMenu(); e.Handled = true; }
    }

    public void HandleTileDoubleTapped(object? sender, TappedEventArgs e) { if (DataContext is not MainViewModel vm) return; vm.LaunchSelected(); ScrollGameRowSelectionIntoView(vm); }

    private void OnGearMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.ToggleGlobalMenu();
    private void OnDownloadsShortcutClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.ToggleDownloads();
    private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => MinimizeToTray();
    private void OnPowerMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.TogglePowerMenu();
    private void OnCloseGameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.CloseRunningGame();
    private void OnCloseDownloadsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.CloseDownloads();

    private void OnInstallOwnedGameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OwnedSteamGame game } && DataContext is MainViewModel vm) vm.InstallOwnedGame(game);
    }

    private Window? _activeDialog;
    public void HandleAddProgramClick() { if (DataContext is MainViewModel vm) _ = OpenAddProgramDialogAsync(vm); }

    private async Task OpenAddProgramDialogAsync(MainViewModel vm)
    {
        var dialog = new AddGameWindow(isApplication: true); _activeDialog = dialog; dialog.Topmost = true;
        GameEntry? result; try { result = await dialog.ShowDialog<GameEntry?>(this); } finally { _activeDialog = null; }
        if (result is null) return; vm.AddManualGame(result); vm.SetFilterApplicationsCommand.Execute(null);
    }

    private async Task OpenAddGameDialogAsync(MainViewModel vm)
    {
        var dialog = new AddGameWindow(); _activeDialog = dialog; dialog.Topmost = true;
        GameEntry? result; try { result = await dialog.ShowDialog<GameEntry?>(this); } finally { _activeDialog = null; }
        if (result is null) return; vm.AddManualGame(result);
    }

    private async Task OpenLayoutPickerAsync()
    {
        AppSettings settings;
        try { settings = AppSettingsStore.Load(); } catch (Exception ex) { DebugLog.Write($"OpenLayoutPicker: falhou ao ler settings: {ex.Message}"); return; }
        var dialog = new LayoutPickerWindow(settings.LayoutTheme); _activeDialog = dialog; dialog.Topmost = true;
        bool apply; try { apply = await dialog.ShowDialog<bool>(this); } finally { _activeDialog = null; }
        if (!apply || dialog.SelectedLayout == settings.LayoutTheme) return;
        settings.LayoutTheme = dialog.SelectedLayout;
        try { AppSettingsStore.Save(settings); } catch (Exception ex) { DebugLog.Write($"OpenLayoutPicker: falhou ao salvar: {ex.Message}"); return; }
        if (LauncherRestart.TryStartReplacement()) (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        else DebugLog.Write("OpenLayoutPicker: layout salvo, mas não deu pra reiniciar — vale na próxima abertura.");
    }

    private async Task OpenEditGameDialogAsync(MainViewModel vm, GameEntry game)
    {
        var dialog = new EditGameWindow(game.TitleOverride ?? game.Title, game.IsApplication); _activeDialog = dialog; dialog.Topmost = true;
        bool saved; try { saved = await dialog.ShowDialog<bool>(this); } finally { _activeDialog = null; }
        if (!saved) return; vm.ApplyEntryEdits(game, dialog.EditedTitle, dialog.IsApplication);
    }

    private bool IsOurWindowInForeground() { if (!OperatingSystem.IsWindows()) return true; try { var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero; return handle != IntPtr.Zero && GetForegroundWindow() == handle; } catch { return true; } }

    private async Task OpenSettingsDialogAsync(MainViewModel vm)
    {
        var dialog = new SettingsWindow();
        dialog.ViewModel.VersionText = vm.AppVersionText;
        dialog.ViewModel.UpdatesAvailable = vm.UpdatesInstalled;
        dialog.CheckForUpdates = vm.CheckForUpdatesWithResultAsync;
        dialog.Topmost = true;
        _activeDialog = dialog;
        try { await dialog.ShowDialog(this); }
        finally
        {
            _activeDialog = null;
            // A janela principal volta a ficar Topmost somente depois que o modal fecha.
            if (IsVisible && WindowState != WindowState.Minimized)
            {
                Topmost = true;
                _topmostEnforcerTimer?.Start();
            }
        }
        ApplyAppearance(vm, AppSettingsStore.Load());
        vm.RefreshArtworkState();
    }

    private async Task OpenGameDetailsDialogAsync(MainViewModel vm, GameEntry game)
    {
        var dialog = new GameDetailsWindow { DataContext = vm.CreateGameDetailsViewModel(game) };
        var settings = AppSettingsStore.Load();
        ThemeManager.ApplyFont(dialog, settings.UiFont); dialog.Topmost = true;
        _activeDialog = dialog;
        try { await dialog.ShowDialog(this); } finally { _activeDialog = null; }
    }

    private async Task OpenSystemUpdateDialogAsync()
    {
        var dialog = new SystemUpdateWindow(); _activeDialog = dialog; dialog.Topmost = true;
        try { await dialog.ShowDialog(this); } finally { _activeDialog = null; }
    }

    private async Task OpenDriverUpdateDialogAsync()
    {
        var dialog = new DriverUpdateWindow(); _activeDialog = dialog; dialog.Topmost = true;
        try { await dialog.ShowDialog(this); } finally { _activeDialog = null; }
    }
}