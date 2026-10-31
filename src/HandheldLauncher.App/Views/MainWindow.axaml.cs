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
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int RestoreHotkeyId = 0xB00;
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkH = 0x48;
    private const int OverlayHotkeyId = 0xB01;
    private const uint VkO = 0x4F;
    private const uint WmHotkey = 0x0312;
    private const uint WmSysCommand = 0x0112;
    private const int ScMonitorPower = 0xF170;
    private static readonly IntPtr HwndBroadcast = new(0xffff);

    private readonly GamepadPoller _gamepad = new();
    private readonly DispatcherTimer _gamepadTimer;
    private DispatcherTimer? _topmostEnforcerTimer;
    private DispatcherTimer? _batteryTimer;
    private bool _hotkeyRegistered;

    private ItemsControl GamesItemsControl = null!;
    private ScrollViewer GameRowScrollViewer = null!;
    private Image HeroImage = null!;
    private Image HeroBackImage = null!;

    private ILibraryLayout _libraryLayout = null!;

    // Instância única da janela de Overlay
    private OverlayWindow? _overlayWindow;

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

        Closed += (_, _) => 
        {
            _gamepadTimer.Stop();
            // Avisa a overlay que o app está fechando de verdade
            if (_overlayWindow != null)
            {
                _overlayWindow.IsAppClosing = true;
                _overlayWindow.Close(); 
            }
        };

        DataContextChanged += OnDataContextChanged;

        Opened += (_, _) =>
        {
            if (Topmost) StartTopmostEnforcer();
            RegisterRestoreHotkey();
        };
        Closed += (_, _) => UnregisterRestoreHotkeyIfNeeded();

        // Quando a janela ganha foco, garante que o controle volte a funcionar
        Activated += (_, _) => { if (OperatingSystem.IsWindows()) PowerThrottling.DisableForThisProcess(); };
    }

    private static readonly TimeSpan TopmostEnforcerGracePeriod = TimeSpan.FromSeconds(120);

    private void StartTopmostEnforcer()
    {
        _topmostEnforcerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _topmostEnforcerTimer.Tick += (_, _) =>
        {
            if (!Topmost || !IsVisible) return;
            Topmost = false;
            Topmost = true;
        };
        _topmostEnforcerTimer.Start();
        Closed += (_, _) => _topmostEnforcerTimer?.Stop();

        var graceTimer = new DispatcherTimer { Interval = TopmostEnforcerGracePeriod };
        graceTimer.Tick += (_, _) =>
        {
            _topmostEnforcerTimer?.Stop();
            graceTimer.Stop();
        };
        graceTimer.Start();
        Closed += (_, _) => graceTimer.Stop();
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
        if (msg == WmHotkey && wParam.ToInt32() == RestoreHotkeyId)
        {
            RestoreFromTray();
            handled = true;
        }
        else if (msg == WmHotkey && wParam.ToInt32() == OverlayHotkeyId)
        {
            (DataContext as MainViewModel)?.CycleOverlayPreset();
            handled = true;
        }
        else if (msg == SingleInstance.RestoreMessageId)
        {
            RestoreFromTray();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void MinimizeToTray()
    {
        Hide();
    }

    private bool AnimationsEnabled => !Classes.Contains("noAnim");

    private void FadeIn(Control control, int milliseconds)
    {
        if (!AnimationsEnabled)
        {
            control.Transitions = null;
            control.Opacity = 1;
            return;
        }

        control.Transitions = null;
        control.Opacity = 0;
        Dispatcher.UIThread.Post(() =>
        {
            control.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(milliseconds),
                    Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
                },
            };
            control.Opacity = 1;
        }, DispatcherPriority.Background);
    }

    private void OnHeroImageChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Image.SourceProperty) return;

        if (!AnimationsEnabled || e.OldValue is null || e.NewValue is null)
        {
            HeroBackImage.Source = null;
            HeroImage.Transitions = null;
            HeroImage.Opacity = 1;
            return;
        }

        HeroBackImage.Source = e.OldValue as Avalonia.Media.IImage;
        FadeIn(HeroImage, 260);
    }

    private void OnContextMenuItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is Control { DataContext: ContextMenuItemViewModel item })
        {
            vm.ActivateContextMenuItem(item);
            e.Handled = true;
        }
    }

    private void OnGlobalMenuItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is Control { DataContext: ContextMenuItemViewModel item })
        {
            vm.ActivateGlobalMenuItem(item);
            e.Handled = true;
        }
    }

    private void OnCoverCandidatePressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is Control { DataContext: ArtworkCandidateViewModel candidate })
        {
            vm.ActivateCoverCandidate(candidate);
            e.Handled = true;
        }
    }

    private void OnMenuOverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.IsCoverPickerOpen) vm.CloseCoverPicker();
        else if (vm.IsContextMenuOpen) vm.CloseContextMenu();
        else if (vm.IsGlobalMenuOpen) vm.CloseGlobalMenu();
        else if (vm.IsDownloadsOpen) vm.CloseDownloads();
        e.Handled = true;
    }

    private void OnAboutOverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        (DataContext as MainViewModel)?.CloseAbout();
    }

    private void OnOpenSteamDownloadsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.OpenSteamDownloads();
    }

    public void QuitFromTray()
    {
        WatchdogGate.MarkDeliberateQuit();
        Close();
    }

    public void RestoreFromTray()
    {
        Show();
        if (Topmost)
        {
            WindowState = WindowState.FullScreen;
        }
        Activate();
    }

    private static void TurnOffScreen()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            PostMessage(HwndBroadcast, WmSysCommand, (IntPtr)ScMonitorPower, (IntPtr)2);
        }
        catch { }
    }

    private static void RestartDevice()
    {
        if (!OperatingSystem.IsWindows()) return;
        RunShutdownExe("/r /f /t 0");
    }
    
    private static void ShutdownDevice()
    {
        if (!OperatingSystem.IsWindows()) return;
        RunShutdownExe("/s /f /t 0");
    }

    private static void RunShutdownExe(string arguments)
    {
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var fullPath = Path.Combine(systemRoot, "shutdown.exe");
        var exe = File.Exists(fullPath) ? fullPath : "shutdown.exe";

        DebugLog.Write($"RunShutdownExe: '{exe}' {arguments}");

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe, arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                DebugLog.Write("RunShutdownExe: Process.Start devolveu null.");
                return;
            }

            var stderr = process.StandardError.ReadToEnd();
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            DebugLog.Write($"RunShutdownExe: exit={process.ExitCode} stdout='{stdout.Trim()}' stderr='{stderr.Trim()}'");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"RunShutdownExe EXCEPTION ({exe}): {ex.Message} — tentando via shell.");

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, arguments)
                {
                    UseShellExecute = true,
                    CreateNoWindow = true,
                });
            }
            catch (Exception shellEx)
            {
                DebugLog.Write($"RunShutdownExe FALHOU também via shell: {shellEx.Message}");
            }
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        vm.AddGameRequested += (_, _) => _ = OpenAddGameDialogAsync(vm);
        vm.EditGameRequested += (_, game) => _ = OpenEditGameDialogAsync(vm, game);
        vm.LayoutPickerRequested += (_, _) => _ = OpenLayoutPickerAsync();
        vm.SettingsRequested += (_, _) => _ = OpenSettingsDialogAsync(vm);
        vm.QuitRequested += (_, _) =>
        {
            WatchdogGate.MarkDeliberateQuit();
            vm.ClearOverlayNow();
            
            if (_overlayWindow != null)
            {
                _overlayWindow.IsAppClosing = true;
                _overlayWindow.Close();
            }
            
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
            else
            {
                Close();
            }
        };
        vm.TurnOffScreenRequested += (_, _) => TurnOffScreen();
        vm.RestartDeviceRequested += (_, _) => RestartDevice();
        vm.ShutdownDeviceRequested += (_, _) => ShutdownDevice();
        vm.RestoreShellRequested += (_, _) => RestoreWindowsShell();
        vm.GameDetailsRequested += (_, game) => _ = OpenGameDetailsDialogAsync(vm, game);

        // A PONTE DO OVERLAY BLINDADA (AIRBAG)
        vm.OpenOverlayRequested += (_, _) =>
        {
            try
            {
                if (_overlayWindow == null)
                {
                    _overlayWindow = new OverlayWindow(vm);

                    // 🟢 Para o Topmost do launcher enquanto o overlay estiver aberto
                    _overlayWindow.PropertyChanged += (_, args) =>
                    {
                        if (args.Property == Visual.IsVisibleProperty)
                        {
                            if (_overlayWindow!.IsVisible)
                            {
                                _topmostEnforcerTimer?.Stop();
                                Topmost = false;
                            }
                            else
                            {
                                if (Topmost) _topmostEnforcerTimer?.Start();
                                Topmost = true;
                            }
                        }
                    };
                }

                if (!_overlayWindow.IsVisible)
                {
                    _overlayWindow.Show();
                }
                
                _overlayWindow.Activate();
            }
            catch (Exception ex)
            {
                DebugLog.Write("========== ERRO AO ABRIR OVERLAY ==========");
                DebugLog.Write(ex.ToString());
                DebugLog.Write("===========================================");
                _overlayWindow = null; 
            }
        };
        
        StartBatteryMonitor(vm);
        StartDownloadsRefreshTimer(vm);

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsFilterAllActive))
            {
                FadeIn(GamesItemsControl, 220);
            }
        };

        ApplyAppearance(vm, AppSettingsStore.Load());

        _ = vm.RunUpdateLoopAsync();

        try
        {
            var settings = AppSettingsStore.Load();
            if (vm.UpdatesInstalled && !settings.ModeChosen && !settings.KioskMode)
            {
                Dispatcher.UIThread.Post(() => _ = OpenSettingsDialogAsync(vm), DispatcherPriority.Background);
            }
        }
        catch { }
    }

    private void RestoreWindowsShell()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var regPath = @"HKEY_CURRENT_USER\Software\Microsoft\Windows NT\CurrentVersion\Winlogon";
            var originalShell = Microsoft.Win32.Registry.GetValue(regPath, "OriginalShell", "explorer.exe")?.ToString() ?? "explorer.exe";
            
            Microsoft.Win32.Registry.SetValue(regPath, "Shell", originalShell);
            DebugLog.Write($"RestoreWindowsShell: Restaurando shell para {originalShell}");

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = originalShell,
                UseShellExecute = true
            });
            
            Close(); 
        }
        catch (Exception ex)
        {
            DebugLog.Write($"RestoreWindowsShell: EXCEPTION {ex.Message}");
        }
    }

    private void OnUpdateBadgeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) _ = OpenUpdateDialogAsync(vm);
    }

    private async Task OpenUpdateDialogAsync(MainViewModel vm)
    {
        if (!vm.IsUpdateReady) return;

        var dialog = new UpdateWindow(vm);
        _activeDialog = dialog;
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            _activeDialog = null;
        }
    }

    private DispatcherTimer? _downloadsRefreshTimer;

    private void StartDownloadsRefreshTimer(MainViewModel vm)
    {
        var ticksSinceBackgroundRefresh = 0;

        _downloadsRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _downloadsRefreshTimer.Tick += (_, _) =>
        {
            if (vm.IsDownloadsOpen)
            {
                vm.RefreshActiveDownloads();
                ticksSinceBackgroundRefresh = 0;
                return;
            }

            if (vm.RunningGame is not null) return;

            ticksSinceBackgroundRefresh++;
            if (ticksSinceBackgroundRefresh >= 3)
            {
                ticksSinceBackgroundRefresh = 0;
                vm.RefreshActiveDownloads();
            }
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
        _batteryTimer.Tick += (_, _) =>
        {
            vm.BatteryStatusText = BatteryStatus.GetDisplayText();
            vm.ClockText = DateTime.Now.ToString("HH:mm");
        };
        _batteryTimer.Start();
        Closed += (_, _) => _batteryTimer?.Stop();
    }

       private void OnGamepadTick(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (vm.RunningGame is null && !IsActive)
        {
            return;
        }

        var actions = _gamepad.Poll();

        if (_activeDialog is { } dialog)
        {
            foreach (var dialogAction in actions)
            {
                UiSounds.PlayForAction(dialogAction);
                DialogGamepadNavigator.Handle(dialog, dialogAction);
            }
            return;
        }

        foreach (var action in actions)
        {
            // 🟢 O atalho do Overlay fica AQUI, antes de verificar o foco do jogo!
            if (action == GamepadAction.ToggleQuickSettings)
            {
                UiSounds.PlayForAction(action);
                if (_overlayWindow != null && _overlayWindow.IsVisible)
                {
                    _overlayWindow.Hide();
                }
                else
                {
                vm.RequestOpenOverlay();
                }
                continue;
            }

            if (vm.IsAboutOpen)
            {
                if (action is GamepadAction.Back or GamepadAction.Confirm or GamepadAction.GlobalMenu or GamepadAction.Menu)
                {
                    UiSounds.PlayBack();
                    vm.CloseAbout();
                }
                continue;
            }

            // 🟢 APAGAMOS O CYCLEOVERLAY ANTIGO DAQUI! 🟢

            if (vm.RunningGame is not null && !IsOurWindowInForeground())
            {
                continue;
            }

            DebugLog.Write($"action={action} pickerOpen={vm.IsCoverPickerOpen} ctxOpen={vm.IsContextMenuOpen} globalOpen={vm.IsGlobalMenuOpen} downloadsOpen={vm.IsDownloadsOpen}");

            UiSounds.PlayForAction(action);

            if (vm.IsCoverPickerOpen)
            {
                switch (action)
                {
                    case GamepadAction.MoveLeft:
                        vm.MoveCoverPickerSelection(-1);
                        ScrollCoverPickerSelectionIntoView(vm);
                        break;
                    case GamepadAction.MoveRight:
                        vm.MoveCoverPickerSelection(1);
                        ScrollCoverPickerSelectionIntoView(vm);
                        break;
                    case GamepadAction.MoveUp:
                        vm.MoveCoverPickerSelectionVertical(-1);
                        ScrollCoverPickerSelectionIntoView(vm);
                        break;
                    case GamepadAction.MoveDown:
                        vm.MoveCoverPickerSelectionVertical(1);
                        ScrollCoverPickerSelectionIntoView(vm);
                        break;
                    case GamepadAction.Confirm:
                        vm.ConfirmCoverPickerSelection();
                        break;
                    case GamepadAction.Back:
                    case GamepadAction.Menu:
                    case GamepadAction.GlobalMenu:
                        vm.CloseCoverPicker();
                        break;
                }
                continue;
            }

            if (vm.IsContextMenuOpen)
            {
                switch (action)
                {
                    case GamepadAction.MoveUp:
                        vm.MoveContextMenuSelection(-1);
                        break;
                    case GamepadAction.MoveDown:
                        vm.MoveContextMenuSelection(1);
                        break;
                    case GamepadAction.Confirm:
                        vm.ConfirmContextMenuSelection();
                        break;
                    case GamepadAction.Back:
                    case GamepadAction.Menu:
                        vm.CloseContextMenu();
                        break;
                }
                continue;
            }

            if (vm.IsGlobalMenuOpen)
            {
                switch (action)
                {
                    case GamepadAction.MoveUp:
                        vm.MoveGlobalMenuSelection(-1);
                        break;
                    case GamepadAction.MoveDown:
                        vm.MoveGlobalMenuSelection(1);
                        break;
                    case GamepadAction.Confirm:
                        vm.ConfirmGlobalMenuSelection();
                        break;
                    case GamepadAction.Back:
                        vm.GlobalMenuBack();
                        break;
                    case GamepadAction.GlobalMenu:
                        vm.CloseGlobalMenu();
                        break;
                }
                continue;
            }

            if (vm.IsDownloadsOpen)
            {
                switch (action)
                {
                    case GamepadAction.Back:
                    case GamepadAction.Menu:
                    case GamepadAction.GlobalMenu:
                    case GamepadAction.Downloads:
                        vm.CloseDownloads();
                        break;
                    case GamepadAction.Confirm:
                        vm.OpenSteamDownloads();
                        break;
                }
                continue;
            }

            if (vm.IsSubFilterRowFocused)
            {
                switch (action)
                {
                    case GamepadAction.MoveLeft:
                        vm.MoveSubFilterCursor(-1);
                        continue;
                    case GamepadAction.MoveRight:
                        vm.MoveSubFilterCursor(1);
                        continue;
                    case GamepadAction.Confirm:
                        vm.ToggleFocusedSubFilter();
                        continue;
                    case GamepadAction.MoveDown:
                    case GamepadAction.Back:
                        vm.LeaveSubFilterRow();
                        continue;
                    case GamepadAction.MoveUp:
                        continue;
                    case GamepadAction.PreviousFilter:
                    case GamepadAction.NextFilter:
                        break;
                    default:
                        continue;
                }
            }

            switch (action)
            {
                case GamepadAction.Downloads:
                    vm.ToggleDownloads();
                    break;
                case GamepadAction.MoveLeft:
                    vm.MovePrevious();
                    ScrollGameRowSelectionIntoView(vm);
                    break;
                case GamepadAction.MoveRight:
                    vm.MoveNext();
                    ScrollGameRowSelectionIntoView(vm);
                    break;
                case GamepadAction.MoveUp:
                    if (!MoveSelectionVertical(vm, -1)) vm.FocusSubFilterRow();
                    break;
                case GamepadAction.MoveDown:
                    MoveSelectionVertical(vm, 1);
                    break;
                case GamepadAction.Confirm:
                    vm.LaunchSelected();
                    ScrollGameRowSelectionIntoView(vm);
                    break;
                case GamepadAction.Back:
                    vm.CloseRunningGame();
                    break;
                case GamepadAction.Menu:
                    vm.ToggleContextMenu();
                    break;
                case GamepadAction.GlobalMenu:
                    vm.ToggleGlobalMenu();
                    break;
                case GamepadAction.PreviousFilter:
                    vm.CyclePreviousFilter();
                    break;
                case GamepadAction.NextFilter:
                    vm.CycleNextFilter();
                    break;
                case GamepadAction.GameDetails:
                    vm.OpenGameDetailsForSelected();
                    break;
            }
        }
    }
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (vm.IsCoverPickerOpen)
        {
            switch (e.Key)
            {
                case Key.Left:
                    vm.MoveCoverPickerSelection(-1);
                    ScrollCoverPickerSelectionIntoView(vm);
                    e.Handled = true;
                    break;
                case Key.Right:
                    vm.MoveCoverPickerSelection(1);
                    ScrollCoverPickerSelectionIntoView(vm);
                    e.Handled = true;
                    break;
                case Key.Up:
                    vm.MoveCoverPickerSelectionVertical(-1);
                    ScrollCoverPickerSelectionIntoView(vm);
                    e.Handled = true;
                    break;
                case Key.Down:
                    vm.MoveCoverPickerSelectionVertical(1);
                    ScrollCoverPickerSelectionIntoView(vm);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    vm.ConfirmCoverPickerSelection();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    vm.CloseCoverPicker();
                    e.Handled = true;
                    break;
            }
            return;
        }

        if (vm.IsContextMenuOpen)
        {
            switch (e.Key)
            {
                case Key.Up:
                    vm.MoveContextMenuSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Down:
                    vm.MoveContextMenuSelection(1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    vm.ConfirmContextMenuSelection();
                    e.Handled = true;
                    break;
                case Key.Escape:
                case Key.Space:
                    vm.CloseContextMenu();
                    e.Handled = true;
                    break;
            }
            return;
        }

        if (vm.IsAboutOpen)
        {
            if (e.Key is Key.Escape or Key.Enter or Key.Tab)
            {
                vm.CloseAbout();
                e.Handled = true;
            }
            return;
        }

        if (vm.IsGlobalMenuOpen)
        {
            switch (e.Key)
            {
                case Key.Up:
                    vm.MoveGlobalMenuSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Down:
                    vm.MoveGlobalMenuSelection(1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    vm.ConfirmGlobalMenuSelection();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    vm.GlobalMenuBack();
                    e.Handled = true;
                    break;
                case Key.Tab:
                    vm.CloseGlobalMenu();
                    e.Handled = true;
                    break;
            }
            return;
        }

        if (vm.IsDownloadsOpen)
        {
            switch (e.Key)
            {
                case Key.Escape:
                case Key.Tab:
                case Key.D:
                    vm.CloseDownloads();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    vm.OpenSteamDownloads();
                    e.Handled = true;
                    break;
            }
            return;
        }

        if (vm.IsSubFilterRowFocused)
        {
            switch (e.Key)
            {
                case Key.Left:
                    vm.MoveSubFilterCursor(-1);
                    e.Handled = true;
                    return;
                case Key.Right:
                    vm.MoveSubFilterCursor(1);
                    e.Handled = true;
                    return;
                case Key.Enter:
                case Key.Space:
                    vm.ToggleFocusedSubFilter();
                    e.Handled = true;
                    return;
                case Key.Down:
                case Key.Escape:
                    vm.LeaveSubFilterRow();
                    e.Handled = true;
                    return;
                case Key.Up:
                    e.Handled = true;
                    return;
            }
        }

        switch (e.Key)
        {
            case Key.Left:
                vm.MovePrevious();
                ScrollGameRowSelectionIntoView(vm);
                e.Handled = true;
                break;
            case Key.Right:
                vm.MoveNext();
                ScrollGameRowSelectionIntoView(vm);
                e.Handled = true;
                break;
            case Key.Up:
                if (!MoveSelectionVertical(vm, -1)) vm.FocusSubFilterRow();
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelectionVertical(vm, 1);
                e.Handled = true;
                break;
            case Key.Enter:
                vm.LaunchSelected();
                ScrollGameRowSelectionIntoView(vm);
                e.Handled = true;
                break;
            case Key.Escape:
            case Key.M:
            case Key.Tab:
                vm.ToggleGlobalMenu();
                e.Handled = true;
                break;
            case Key.W when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                vm.CloseRunningGame();
                e.Handled = true;
                break;
            case Key.Space:
            case Key.Apps:
                vm.ToggleContextMenu();
                e.Handled = true;
                break;
            case Key.Q:
                vm.CyclePreviousFilter();
                e.Handled = true;
                break;
            case Key.E:
                vm.CycleNextFilter();
                e.Handled = true;
                break;
            case Key.D:
                vm.ToggleDownloads();
                e.Handled = true;
                break;
        }
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

        if (itemTop < viewTop)
        {
            CoverPickerScrollViewer.Offset = new Vector(CoverPickerScrollViewer.Offset.X, itemTop);
        }
        else if (itemBottom > viewBottom)
        {
            CoverPickerScrollViewer.Offset = new Vector(CoverPickerScrollViewer.Offset.X, itemBottom - CoverPickerScrollViewer.Viewport.Height);
        }
    }

    private void ScrollGameRowSelectionIntoView(MainViewModel vm)
    {
        Dispatcher.UIThread.Post(() => ScrollSelectionIntoViewNow(vm), DispatcherPriority.Background);
    }

    private void ScrollSelectionIntoViewNow(MainViewModel vm)
    {
        if (GameRowScrollViewer is null) return;
        if (vm.Games.Count == 0 || vm.SelectedIndex < 0 || vm.SelectedIndex >= vm.Games.Count) return;

        var container = GamesItemsControl?.ContainerFromIndex(vm.SelectedIndex);
        var position = container?.TranslatePoint(new Point(0, 0), GamesItemsControl!);
        if (container is not null && position is { } p)
        {
            var offset = GameRowScrollViewer.Offset;
            var viewport = GameRowScrollViewer.Viewport;

            if (_verticalLayout)
            {
                var top = p.Y - 12;
                var bottom = p.Y + container.Bounds.Height + 12;
                if (top < offset.Y) GameRowScrollViewer.Offset = new Vector(offset.X, Math.Max(0, top));
                else if (bottom > offset.Y + viewport.Height) GameRowScrollViewer.Offset = new Vector(offset.X, bottom - viewport.Height);
            }
            else if (_libraryLayout.CentersSelection)
            {
                var target = p.X + container.Bounds.Width / 2 - viewport.Width / 2;
                var maxOffset = Math.Max(0, GameRowScrollViewer.Extent.Width - viewport.Width);
                GameRowScrollViewer.Offset = new Vector(Math.Clamp(target, 0, maxOffset), offset.Y);
            }
            else
            {
                var left = p.X;
                var right = p.X + container.Bounds.Width;
                if (left < offset.X) GameRowScrollViewer.Offset = new Vector(left, offset.Y);
                else if (right > offset.X + viewport.Width) GameRowScrollViewer.Offset = new Vector(right - viewport.Width, offset.Y);
            }
            return;
        }

        if (_verticalLayout) return;

        var scale = GameTileViewModel.SizeScale;
        var normalWidth = 220 * scale;
        var lastPlayedWidth = 580 * scale;
        const double spacing = 120;

        double itemLeft = 0;
        for (var i = 0; i < vm.SelectedIndex; i++)
        {
            itemLeft += (vm.Games[i].IsLastPlayed ? lastPlayedWidth : normalWidth) + spacing;
        }

        var itemRight = itemLeft + (vm.Games[vm.SelectedIndex].IsLastPlayed ? lastPlayedWidth : normalWidth);
        var viewLeft = GameRowScrollViewer.Offset.X;
        var viewRight = viewLeft + GameRowScrollViewer.Viewport.Width;

        if (itemLeft < viewLeft)
        {
            GameRowScrollViewer.Offset = new Vector(itemLeft, GameRowScrollViewer.Offset.Y);
        }
        else if (itemRight > viewRight)
        {
            GameRowScrollViewer.Offset = new Vector(itemRight - GameRowScrollViewer.Viewport.Width, GameRowScrollViewer.Offset.Y);
        }
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
            
            if (OperatingSystem.IsWindows())
            {
                DisplayColorManager.Apply(settings.ColorMode);
            }

            var scale = GameTileViewModel.ScaleForSetting(settings.TileSize);
            var scaleChanged = Math.Abs(scale - GameTileViewModel.SizeScale) > 0.001;
            GameTileViewModel.SizeScale = scale;

            if (!_layoutInitialized || settings.VerticalLayout != _verticalLayout)
            {
                SetLayout(settings.VerticalLayout);
                _layoutInitialized = true;
            }

            if (scaleChanged) vm.ReloadTiles();

            ScrollGameRowSelectionIntoView(vm);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"ApplyAppearance EXCEPTION {ex}");
        }
    }

    private void ApplyLayoutTheme()
    {
        UserControl layout;
        try
        {
            var settings = AppSettingsStore.Load();
            layout = settings.LayoutTheme == 1
                ? new Layouts.SwitchLibraryLayout()
                : new Layouts.ClassicLibraryLayout();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"ApplyLayoutTheme: falhou ao ler settings, usando Clássico. {ex}");
            layout = new Layouts.ClassicLibraryLayout();
        }

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

        GamesItemsControl.ItemsPanel = vertical
            ? new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Orientation.Horizontal })
            : _horizontalPanelTemplate;

        GameRowScrollViewer.HorizontalScrollBarVisibility = vertical || !_libraryLayout.SupportsVerticalGrid ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;
        GameRowScrollViewer.VerticalScrollBarVisibility = vertical ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        GameRowScrollViewer.Offset = default;

        foreach (var container in GamesItemsControl.GetRealizedContainers())
        {
            container.Margin = ContainerMargin();
        }
    }

    private bool MoveSelectionVertical(MainViewModel vm, int direction)
    {
        if (!_verticalLayout || vm.SelectedIndex < 0) return false;

        var current = GamesItemsControl.ContainerFromIndex(vm.SelectedIndex);
        if (current is null) return false;

        var currentBounds = current.Bounds;
        var currentCenterX = currentBounds.X + currentBounds.Width / 2;

        var best = -1;
        var bestRowY = direction > 0 ? double.MaxValue : double.MinValue;
        var bestDx = double.MaxValue;

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

            if (isCloserRow || (isSameRow && dx < bestDx))
            {
                best = i;
                bestRowY = b.Y;
                bestDx = dx;
            }
        }

        if (best < 0) return false;
        vm.SelectedIndex = best;
        ScrollGameRowSelectionIntoView(vm);
        return true;
    }

    public void HandleSubFilterClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is Button { DataContext: SubFilterViewModel subFilter })
        {
            vm.ToggleSubFilter(subFilter);
        }
    }

    public void HandleTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not Control { DataContext: GameTileViewModel tile }) return;

        var index = vm.Games.IndexOf(tile);
        if (index >= 0) vm.SelectedIndex = index;

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed && !vm.IsContextMenuOpen)
        {
            vm.ToggleContextMenu();
            e.Handled = true;
        }
    }

    public void HandleTileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.LaunchSelected();
        ScrollGameRowSelectionIntoView(vm);
    }

    private void OnGearMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.ToggleGlobalMenu();
    }

    private void OnDownloadsShortcutClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.ToggleDownloads();
    }

    private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        MinimizeToTray();
    }

    private void OnPowerMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.TogglePowerMenu();
    }

    private void OnCloseGameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.CloseRunningGame();
    }

    private void OnCloseDownloadsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.CloseDownloads();
    }

    private void OnInstallOwnedGameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OwnedSteamGame game } && DataContext is MainViewModel vm)
        {
            vm.InstallOwnedGame(game);
        }
    }

    private Window? _activeDialog;

    public void HandleAddProgramClick()
    {
        if (DataContext is MainViewModel vm) _ = OpenAddProgramDialogAsync(vm);
    }

    private async Task OpenAddProgramDialogAsync(MainViewModel vm)
    {
        var dialog = new AddGameWindow(isApplication: true);
        _activeDialog = dialog;
        GameEntry? result;
        try { result = await dialog.ShowDialog<GameEntry?>(this); }
        finally { _activeDialog = null; }
        if (result is null) return;
        vm.AddManualGame(result);
        vm.SetFilterApplicationsCommand.Execute(null);
    }

    private async Task OpenAddGameDialogAsync(MainViewModel vm)
    {
        var dialog = new AddGameWindow();
        _activeDialog = dialog;
        GameEntry? result;
        try
        {
            result = await dialog.ShowDialog<GameEntry?>(this);
        }
        finally
        {
            _activeDialog = null;
        }
        if (result is null) return;

        vm.AddManualGame(result);
    }

    private async Task OpenLayoutPickerAsync()
    {
        AppSettings settings;
        try
        {
            settings = AppSettingsStore.Load();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"OpenLayoutPicker: falhou ao ler settings: {ex.Message}");
            return;
        }

        var dialog = new LayoutPickerWindow(settings.LayoutTheme);
        _activeDialog = dialog;
        bool apply;
        try
        {
            apply = await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            _activeDialog = null;
        }

        if (!apply || dialog.SelectedLayout == settings.LayoutTheme) return;

        settings.LayoutTheme = dialog.SelectedLayout;
        try
        {
            AppSettingsStore.Save(settings);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"OpenLayoutPicker: falhou ao salvar: {ex.Message}");
            return;
        }

        if (LauncherRestart.TryStartReplacement())
        {
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        else
        {
            DebugLog.Write("OpenLayoutPicker: layout salvo, mas não deu pra reiniciar — vale na próxima abertura.");
        }
    }

    private async Task OpenEditGameDialogAsync(MainViewModel vm, GameEntry game)
    {
        var dialog = new EditGameWindow(game.TitleOverride ?? game.Title, game.IsApplication);
        _activeDialog = dialog;
        bool saved;
        try
        {
            saved = await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            _activeDialog = null;
        }
        if (!saved) return;

        vm.ApplyEntryEdits(game, dialog.EditedTitle, dialog.IsApplication);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private bool IsOurWindowInForeground()
    {
        if (!OperatingSystem.IsWindows()) return true;

        try
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            return handle != IntPtr.Zero && GetForegroundWindow() == handle;
        }
        catch
        {
            return true;
        }
    }

    private async Task OpenSettingsDialogAsync(MainViewModel vm)
    {
        var dialog = new SettingsWindow();
        dialog.ViewModel.VersionText = vm.AppVersionText;
        dialog.ViewModel.UpdatesAvailable = vm.UpdatesInstalled;
        dialog.CheckForUpdates = vm.CheckForUpdatesWithResultAsync;
        _activeDialog = dialog;
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            _activeDialog = null;
        }

        ApplyAppearance(vm, AppSettingsStore.Load());
    }

    private async Task OpenGameDetailsDialogAsync(MainViewModel vm, GameEntry game)
    {
        var dialog = new GameDetailsWindow
        {
            DataContext = vm.CreateGameDetailsViewModel(game),
        };
        _activeDialog = dialog;
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            _activeDialog = null;
        }
    }
}