using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
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
    }

    private void OnGamepadTick(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        foreach (var action in _gamepad.Poll())
        {
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
            }
        }
    }

    // Keyboard nav stays as a fallback for dev-time testing on desktop —
    // the gamepad poller above is what actually matters on the handheld.
    // Space stands in for the controller's Start button (no literal
    // equivalent on a keyboard).
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

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

        var dialog = new AddGameWindow();
        var result = await dialog.ShowDialog<GameEntry?>(this);
        if (result is null) return;

        vm.AddManualGame(result);
    }
}
