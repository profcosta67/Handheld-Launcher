using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Input;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.App.Views;

public partial class MainWindow : Window
{
    // --- Win32 interop: global restore hotkey + forced screen-off. See
    // RegisterRestoreHotkey/TurnOffScreen below for why each one exists. ---
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int RestoreHotkeyId = 0xB00; // arbitrary, only needs to be unique within our own process
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint ModNoRepeat = 0x4000; // don't refire while the keys are held down
    private const uint VkH = 0x48; // 'H' - as in "Handheld"/"Home"
    private const uint WmHotkey = 0x0312;
    private const uint WmSysCommand = 0x0112;
    private const int ScMonitorPower = 0xF170;
    private static readonly IntPtr HwndBroadcast = new(0xffff);

    private readonly GamepadPoller _gamepad = new();
    private readonly DispatcherTimer _gamepadTimer;
    private DispatcherTimer? _topmostEnforcerTimer;
    private bool _hotkeyRegistered;

    public MainWindow()
    {
        InitializeComponent();

        // 20Hz is plenty for menu navigation and cheap enough to just leave
        // running the whole time the launcher is open.
        _gamepadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _gamepadTimer.Tick += OnGamepadTick;
        _gamepadTimer.Start();

        Closed += (_, _) => _gamepadTimer.Stop();

        // DataContext is assigned by the composition root (App.axaml.cs)
        // AFTER this constructor runs, so the ViewModel isn't available yet
        // here — wire its view-level requests (opening a dialog, closing
        // the window) once it actually shows up.
        DataContextChanged += OnDataContextChanged;

        // App.axaml.cs sets Topmost=true BEFORE Show() happens (it's a
        // property set right after `new MainWindow { ... }`, before
        // desktop.MainWindow assignment triggers the actual window show),
        // so by the time Opened fires here, Topmost already reflects
        // whether this is a kiosk install. See StartTopmostEnforcer below
        // for why a one-time Topmost=true isn't enough on the real
        // hardware.
        Opened += (_, _) =>
        {
            if (Topmost) StartTopmostEnforcer();
            RegisterRestoreHotkey();
        };
        Closed += (_, _) => UnregisterRestoreHotkeyIfNeeded();
    }

    /// <summary>
    /// Reported on the Legion Go 2, specifically in Windows "Modo Xbox" /
    /// full-screen-experience boot (not in a normal desktop boot, where
    /// the one-time Topmost=true from App.axaml.cs is enough on its own):
    /// Legion Space still ends up in front of us even though our window is
    /// Topmost. Likely explanation: Windows only guarantees "most recently
    /// asserted" ordering among several Topmost windows, not a fixed
    /// winner — and Legion Space, being the FSE "home app" being replaced,
    /// apparently keeps re-asserting its own foreground/topmost status
    /// after boot settles, which pushes us back regardless of our own
    /// one-time Topmost flag.
    ///
    /// This toggles Topmost off/on (which re-inserts the window at the
    /// front of that band) plus Activate() every couple seconds, so even
    /// if Legion Space "wins" a round, we win the next one shortly after.
    /// It's a mitigation, not a guaranteed fix — if Modo Xbox's shell runs
    /// its own composited layer above ordinary Win32 topmost windows
    /// entirely, no amount of toggling from our side will beat it, and the
    /// real fix would have to come from OmniConsole's side instead. Only
    /// starts when Topmost is already on (kiosk installs) — never runs on
    /// a plain `dotnet run` dev window.
    /// </summary>
    private void StartTopmostEnforcer()
    {
        _topmostEnforcerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _topmostEnforcerTimer.Tick += (_, _) =>
        {
            // Two reasons to bail without touching anything:
            // - Topmost off on purpose: something else (not us) turned it
            //   off, so stop fighting it.
            // - !IsVisible: the window is hidden via "Minimizar"
            //   (MinimizeToTray/Hide()) — Activate() below would undo that
            //   by forcing it back on screen every 2 seconds, which is
            //   exactly the "fica abrindo toda hora" problem this whole
            //   feature exists to fix. Only reassert while actually shown.
            if (!Topmost || !IsVisible) return;
            Topmost = false;
            Topmost = true;
            Activate();
        };
        _topmostEnforcerTimer.Start();
        Closed += (_, _) => _topmostEnforcerTimer?.Stop();
    }

    /// <summary>
    /// Registers Ctrl+Alt+H as a global hotkey — unlike a normal KeyDown
    /// handler, RegisterHotKey keeps working even while this window is
    /// hidden (via "Minimizar") or not focused, which is the whole point:
    /// it's how you get the launcher back after minimizing it. Needs the
    /// real native HWND, which only exists once the window has actually
    /// been shown once — hooked off Opened like StartTopmostEnforcer
    /// above, same reason. MOD_NOREPEAT so holding the keys down doesn't
    /// fire RestoreFromTray() over and over. Always on (not gated to
    /// kiosk/Topmost like the enforcer) since "Minimizar" itself is a
    /// normal bottom-bar button in every mode, not just kiosk.
    /// </summary>
    private void RegisterRestoreHotkey()
    {
        if (TryGetPlatformHandle() is not { } handle) return;

        Win32Properties.AddWndProcHookCallback(this, OnNativeWndProcHook);
        _hotkeyRegistered = RegisterHotKey(handle.Handle, RestoreHotkeyId, ModControl | ModAlt | ModNoRepeat, VkH);
    }

    private void UnregisterRestoreHotkeyIfNeeded()
    {
        if (_hotkeyRegistered && TryGetPlatformHandle() is { } handle)
        {
            UnregisterHotKey(handle.Handle, RestoreHotkeyId);
        }
        Win32Properties.RemoveWndProcHookCallback(this, OnNativeWndProcHook);
        _hotkeyRegistered = false;
    }

    /// <summary>Raw Win32 message hook (see RegisterRestoreHotkey) — the only thing we care about here is our own WM_HOTKEY.</summary>
    private IntPtr OnNativeWndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == RestoreHotkeyId)
        {
            RestoreFromTray();
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Hides the window instead of closing it — the process keeps
    /// running, so the watchdog's "is it running?" check (see
    /// installer/Install-Launcher.ps1) never has a reason to relaunch it.
    /// This is the fix for "fica abrindo toda hora, principalmente no
    /// modo desktop": Alt+F4/crash is for when you want the watchdog to
    /// kick back in; this is for when you just want it out of the way for
    /// a while without fighting the watchdog over it. Bound to the
    /// bottom-bar "Minimizar" button (OnMinimizeClick).
    /// </summary>
    private void MinimizeToTray()
    {
        Hide();
    }

    /// <summary>Called by the tray icon's "Sair" menu item (App.axaml.cs) — same deliberate-quit behavior as the gear/global menu's own "Sair".</summary>
    public void QuitFromTray()
    {
        DisableWatchdogIfPresent();
        Close();
    }

    /// <summary>Called by the tray icon's click/"Abrir" menu item (App.axaml.cs) and by the Ctrl+Alt+H hotkey above.</summary>
    public void RestoreFromTray()
    {
        Show();
        if (Topmost)
        {
            // Hide()/Show() shouldn't normally lose WindowState, but this
            // costs nothing and removes any doubt for the one case that
            // actually matters (kiosk installs).
            WindowState = WindowState.FullScreen;
        }
        Activate();
    }

    /// <summary>
    /// SC_MONITORPOWER via a broadcast WM_SYSCOMMAND — the standard Win32
    /// trick to force the display off without touching sleep/hibernate.
    /// lParam=2 means "off" (1=low power, -1=on). A real key press, mouse
    /// move, or controller button on the Legion Go wakes it back up
    /// through Windows' own normal input handling — nothing else needed
    /// on our side for that.
    /// </summary>
    private static void TurnOffScreen()
    {
        try
        {
            PostMessage(HwndBroadcast, WmSysCommand, (IntPtr)ScMonitorPower, (IntPtr)2);
        }
        catch
        {
            // Best-effort — never let this crash the app.
        }
    }

    /// <summary>
    /// `shutdown /r /t 0` — immediate restart. No in-app confirmation step
    /// before this fires (see chat/README for that open question — a
    /// stray tap on "Power" then this item currently restarts right
    /// away, which could lose game progress).
    /// </summary>
    private static void RestartDevice() => RunShutdownExe("/r /t 0");

    /// <summary>`shutdown /s /t 0` — immediate shutdown, same no-confirmation caveat as RestartDevice.</summary>
    private static void ShutdownDevice() => RunShutdownExe("/s /t 0");

    private static void RunShutdownExe(string arguments)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("shutdown.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
            // Best-effort — never let this crash the app.
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        vm.AddGameRequested += (_, _) => _ = OpenAddGameDialogAsync(vm);
        vm.QuitRequested += (_, _) =>
        {
            DisableWatchdogIfPresent();
            Close();
        };
        vm.TurnOffScreenRequested += (_, _) => TurnOffScreen();
        vm.RestartDeviceRequested += (_, _) => RestartDevice();
        vm.ShutdownDeviceRequested += (_, _) => ShutdownDevice();
    }

    /// <summary>
    /// Best-effort: if the installer's watchdog Scheduled Task is present
    /// (kiosk installs that chose auto-restart — see
    /// installer/Install-Launcher.ps1), disable it before closing so a
    /// deliberate "Sair" from the global menu actually sticks instead of
    /// the watchdog reopening the app within a minute. Alt+F4 or a crash
    /// skip this on purpose — recovering from exactly those is the whole
    /// point of the watchdog. On a plain `dotnet run` dev session (no
    /// task registered), schtasks just fails and this quietly does
    /// nothing — never blocks quitting over it either way.
    /// </summary>
    private static void DisableWatchdogIfPresent()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe",
                "/Change /TN \"HandheldLauncherWatchdog\" /Disable")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit(2000);
        }
        catch
        {
            // Best-effort only - never let this stop the app from quitting.
        }
    }

    private void OnGamepadTick(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        foreach (var action in _gamepad.Poll())
        {
            // Temporary diagnostic (see Diagnostics/DebugLog.cs) — logs
            // every action AND the exact overlay state at the moment it's
            // dispatched, so a "nothing happens" report can be checked
            // against what actually fired instead of guessed at blind.
            DebugLog.Write($"action={action} pickerOpen={vm.IsCoverPickerOpen} ctxOpen={vm.IsContextMenuOpen} globalOpen={vm.IsGlobalMenuOpen} powerOpen={vm.IsPowerMenuOpen}");

            // Cover/banner picker takes over input first — it can be opened
            // from inside the per-tile context menu (which closes itself
            // before opening the picker), so checking it ahead of
            // IsContextMenuOpen below is just belt-and-suspenders; only one
            // of the three overlay bools is ever true at once in practice.
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

            // While the Start-button context menu is open, every action
            // means something different (navigate/confirm/close THAT menu)
            // instead of the normal tile navigation — swallow it here so
            // e.g. Confirm doesn't also launch a game behind the menu.
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

            // Same idea for the global menu (View/Back button, or the
            // gear icon in the top bar for mouse/touch — see
            // OnGearMenuClick) — this is what reaches "Atualizar
            // Biblioteca"/"Atualizar Capas Automático"/"Adicionar
            // jogo"/"Sair" without a mouse. Only reachable from the
            // plain-navigation branch below, so the menus below can never
            // both be open from gamepad input alone.
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
                    case GamepadAction.GlobalMenu:
                        vm.CloseGlobalMenu();
                        break;
                }
                continue;
            }

            // The "Power" menu (bottom-bar button, mouse/touch-only for
            // now — no gamepad button opens it yet) gets the same
            // "swallow input while open" treatment as the other overlays,
            // for when it's navigated by keyboard/gamepad once already
            // open.
            if (vm.IsPowerMenuOpen)
            {
                switch (action)
                {
                    case GamepadAction.MoveUp:
                        vm.MovePowerMenuSelection(-1);
                        break;
                    case GamepadAction.MoveDown:
                        vm.MovePowerMenuSelection(1);
                        break;
                    case GamepadAction.Confirm:
                        vm.ConfirmPowerMenuSelection();
                        break;
                    case GamepadAction.Back:
                    case GamepadAction.Menu:
                    case GamepadAction.GlobalMenu:
                        vm.ClosePowerMenu();
                        break;
                }
                continue;
            }

            switch (action)
            {
                case GamepadAction.MoveLeft:
                    vm.MovePrevious();
                    break;
                case GamepadAction.MoveRight:
                    vm.MoveNext();
                    break;
                case GamepadAction.Confirm:
                    vm.LaunchSelected();
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
            }
        }
    }

    // Keyboard nav stays as a fallback for dev-time testing on desktop —
    // the gamepad poller above is what actually matters on the handheld.
    // Space stands in for Start (per-tile menu), Tab stands in for
    // View/Back (global menu) — neither has a literal keyboard equivalent.
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
                case Key.Tab:
                    vm.CloseGlobalMenu();
                    e.Handled = true;
                    break;
            }
            return;
        }

        if (vm.IsPowerMenuOpen)
        {
            switch (e.Key)
            {
                case Key.Up:
                    vm.MovePowerMenuSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Down:
                    vm.MovePowerMenuSelection(1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    vm.ConfirmPowerMenuSelection();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    vm.ClosePowerMenu();
                    e.Handled = true;
                    break;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                vm.MovePrevious();
                e.Handled = true;
                break;
            case Key.Right:
                vm.MoveNext();
                e.Handled = true;
                break;
            case Key.Enter:
                vm.LaunchSelected();
                e.Handled = true;
                break;
            case Key.Escape:
                vm.CloseRunningGame();
                e.Handled = true;
                break;
            case Key.Space:
                vm.ToggleContextMenu();
                e.Handled = true;
                break;
            case Key.Tab:
                vm.ToggleGlobalMenu();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// The cover/banner picker is a plain ItemsControl with hand-rolled
    /// IsSelected flags (not a ListBox), so there's no built-in "scroll
    /// selected item into view" tied to selection changes — has to be done
    /// by hand here after every move. Deliberately uses the well-known
    /// ScrollViewer.Offset/.Viewport properties rather than
    /// ItemsControl.ContainerFromIndex/Control.BringIntoView(), since this
    /// project can't be compiled here to verify a less-certain API choice.
    /// </summary>
    private void ScrollCoverPickerSelectionIntoView(MainViewModel vm)
    {
        if (CoverPickerScrollViewer is null) return;
        if (!vm.IsCoverPickerOpen || vm.CoverPickerCandidates.Count == 0) return;

        // Must match MainWindow.axaml's coverCandidate cell: Border
        // Height="170" plus Margin="6" on each side (top+bottom = 12).
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

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not Control { DataContext: GameTileViewModel tile }) return;

        var index = vm.Games.IndexOf(tile);
        if (index >= 0) vm.SelectedIndex = index;
    }

    private void OnTileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.LaunchSelected();
    }

    /// <summary>
    /// Gear icon in the top bar — opens the exact same menu as View/Back
    /// on the controller (vm.ToggleGlobalMenu()), not a second separate
    /// menu, so mouse/touch and controller always see the same options.
    /// </summary>
    private void OnGearMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.ToggleGlobalMenu();
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

    /// <summary>Called by the global menu's "Adicionar jogo" entry (vm.AddGameRequested, wired in OnDataContextChanged).</summary>
    private async Task OpenAddGameDialogAsync(MainViewModel vm)
    {
        var dialog = new AddGameWindow();
        var result = await dialog.ShowDialog<GameEntry?>(this);
        if (result is null) return;

        vm.AddManualGame(result);
    }
}
