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
    private readonly GamepadPoller _gamepad = new();
    private readonly DispatcherTimer _gamepadTimer;
    private DispatcherTimer? _topmostEnforcerTimer;

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
        };
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
            if (!Topmost) return; // safety: if something turned this off on purpose, stop fighting it
            Topmost = false;
            Topmost = true;
            Activate();
        };
        _topmostEnforcerTimer.Start();
        Closed += (_, _) => _topmostEnforcerTimer?.Stop();
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
            DebugLog.Write($"action={action} pickerOpen={vm.IsCoverPickerOpen} ctxOpen={vm.IsContextMenuOpen} globalOpen={vm.IsGlobalMenuOpen}");

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

            // Same idea for the global menu (View/Back button) — this is
            // what reaches "Buscar capas"/"Adicionar jogo"/"Atualizar"/"Sair"
            // without a mouse. Only reachable from the plain-navigation
            // branch below, so the two menus can never both be open from
            // gamepad input alone.
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

    private void OnRefreshClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.Refresh();
    }

    /// <summary>
    /// Bottom-bar "Sair" (mouse-only — see the comment on its XAML
    /// counterpart for why it exists alongside the global menu's own
    /// "Sair"). Must do the same watchdog-disable as
    /// vm.QuitRequested below; this one bypassed it until now — same
    /// bug class, both are a deliberate quit and both need to stick.
    /// </summary>
    private void OnQuitClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DisableWatchdogIfPresent();
        Close();
    }

    private void OnCloseGameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.CloseRunningGame();
    }

    private async void OnAddGameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        await OpenAddGameDialogAsync(vm);
    }

    /// <summary>Shared by the bottom-bar button and the global menu's "Adicionar jogo" entry.</summary>
    private async Task OpenAddGameDialogAsync(MainViewModel vm)
    {
        var dialog = new AddGameWindow();
        var result = await dialog.ShowDialog<GameEntry?>(this);
        if (result is null) return;

        vm.AddManualGame(result);
    }
}
