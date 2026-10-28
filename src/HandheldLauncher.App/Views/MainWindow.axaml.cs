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
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        vm.AddGameRequested += (_, _) => _ = OpenAddGameDialogAsync(vm);
        vm.QuitRequested += (_, _) => Close();
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
                        break;
                    case GamepadAction.MoveRight:
                        vm.MoveCoverPickerSelection(1);
                        break;
                    case GamepadAction.MoveUp:
                        vm.MoveCoverPickerSelectionVertical(-1);
                        break;
                    case GamepadAction.MoveDown:
                        vm.MoveCoverPickerSelectionVertical(1);
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
                    e.Handled = true;
                    break;
                case Key.Right:
                    vm.MoveCoverPickerSelection(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    vm.MoveCoverPickerSelectionVertical(-1);
                    e.Handled = true;
                    break;
                case Key.Down:
                    vm.MoveCoverPickerSelectionVertical(1);
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

    private void OnQuitClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
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
