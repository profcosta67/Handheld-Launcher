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
using HandheldLauncher.Core.Services.Power;
using HandheldLauncher.Core.Services;
using Avalonia.VisualTree; 
using Avalonia.LogicalTree;

namespace HandheldLauncher.App.Views;

/// <summary>v0.28: atalhos de teclado e rolagem da biblioteca. Separado do GameplayWatcher.</summary>
public partial class MainWindow
{
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // v0.18.3: digitando na busca da aba Nuvem — as teclas são do campo, não
        // atalhos do launcher (senão "D" abriria Downloads, setas mudariam o jogo...).
        // Enter/Esc/↓ terminam a busca e voltam pra grade.
        if (_cloudGridActive && IsCloudSearchFocused())
        {
            if (e.Key is Key.Enter or Key.Escape or Key.Down) { EndCloudSearch(); e.Handled = true; }
            return;
        }

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
                case Key.Enter: _ = vm.ConfirmCoverPickerSelection(); e.Handled = true; break;
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
                // v0.25.2: no Cartoon os subfiltros ficam no rodapé (sai-se por cima ou por baixo).
                case Key.Up when _filtersBelowGrid:
                case Key.Down when _filtersBelowGrid:
                case Key.Down: case Key.Escape: vm.LeaveSubFilterRow(); e.Handled = true; return;
                case Key.Up: e.Handled = true; return;
            }
        }

        var settings = AppSettingsStore.Load();

        // v0.25.4: paridade de teclado com o controle no botão "+".
        if (_addButtonFocused)
        {
            if (e.Key == Key.Escape) { SetAddButtonFocused(false); e.Handled = true; return; }
            if (e.Key == Key.Enter || TryMatch(settings.KeyLaunchGame, e))
            {
                SetAddButtonFocused(false);
                HandleAddProgramClick();
                e.Handled = true;
                return;
            }
            if (e.Key is Key.Up or Key.Left)
            {
                ReturnFromAddProgramButton(vm);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Down)
            {
                SetAddButtonFocused(false);
                if (_filtersBelowGrid) vm.FocusSubFilterRow();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Right)
            {
                SetAddButtonFocused(false);
                e.Handled = true;
                return;
            }
        }

        // v0.25.2: ecrã de lançamento (Cartoon). Com ele aberto é modal: Enter
        // joga, X/Pespaço abrem os detalhes e Esc/B fecham — o resto é ignorado.
        if (_libraryLayout is ILaunchScreenHost launchHost && launchHost.IsLaunchScreenOpen)
        {
            if (e.Key == Key.Escape || TryMatch(settings.KeyGlobalMenu, e)) launchHost.CloseLaunchScreen();
            else if (TryMatch(settings.KeyLaunchGame, e) || e.Key == Key.Enter) { _ = vm.LaunchSelected(); launchHost.CloseLaunchScreen(); }
            else if (e.Key == Key.Space || TryMatch(settings.KeyContextMenu, e)) { vm.OpenGameDetailsForSelected(); launchHost.CloseLaunchScreen(); }
            e.Handled = true;
            return;
        }

        if (TryMatch(settings.KeyLaunchGame, e))
        {
            if (_libraryLayout is ILaunchScreenHost host) host.OpenLaunchScreen();
            else { _ = vm.LaunchSelected(); ScrollGameRowSelectionIntoView(vm); }
            e.Handled = true; return;
        }
        if (TryMatch(settings.KeyGlobalMenu, e)) { vm.ToggleGlobalMenu(); e.Handled = true; return; }
        if (TryMatch(settings.KeyCloseGame, e)) { vm.CloseRunningGame(); e.Handled = true; return; }
        if (TryMatch(settings.KeyContextMenu, e)) { vm.ToggleContextMenu(); e.Handled = true; return; }
        if (TryMatch(settings.KeyPrevFilter, e)) { vm.CyclePreviousFilter(); e.Handled = true; return; }
        if (TryMatch(settings.KeyNextFilter, e)) { vm.CycleNextFilter(); e.Handled = true; return; }
        if (TryMatch(settings.KeyDownloads, e)) { vm.ToggleDownloads(); e.Handled = true; return; }

        switch (e.Key)
        {
            case Key.Left: vm.MovePrevious(); ScrollGameRowSelectionIntoView(vm); e.Handled = true; break;
            case Key.Right:
                if (!_addButtonFocused && vm.IsFilterApplicationsActive)
                {
                    var games = vm.Games;
                    var noGames = games.Count == 0;
                    var atLast = games.Count > 0 && vm.SelectedTile == games[^1];
                    if ((noGames || (!IsGridNav && atLast)) && TryFocusAddProgramButton(vm))
                    { e.Handled = true; break; }
                }
                vm.MoveNext(); ScrollGameRowSelectionIntoView(vm); e.Handled = true; break;
            case Key.Up:
                if (_filtersBelowGrid) MoveSelectionVertical(vm, -1);
                else if (!MoveSelectionVertical(vm, -1)) vm.FocusSubFilterRow();
                e.Handled = true; break;
            case Key.Down:
                if (_filtersBelowGrid)
                {
                    if (!_addButtonFocused && vm.Games.Count == 0 && TryFocusAddProgramButton(vm))
                    { e.Handled = true; break; }
                    if (!MoveSelectionVertical(vm, 1))
                    {
                        if (!_addButtonFocused && TryFocusAddProgramButton(vm))
                        { e.Handled = true; break; }
                        vm.FocusSubFilterRow();
                    }
                }
                else if (IsGridNav)
                {
                    if (!MoveSelectionVertical(vm, 1) && TryFocusAddProgramButton(vm))
                    { e.Handled = true; break; }
                }
                else
                {
                    MoveSelectionVertical(vm, 1);
                }
                e.Handled = true; break;
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

            if (IsGridNav)
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
        if (IsGridNav) return;
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

    private bool MoveSelectionVertical(MainViewModel vm, int direction)
    {
        if (!IsGridNav || vm.SelectedIndex < 0) return false;
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

    public void HandleTileDoubleTapped(object? sender, TappedEventArgs e) { if (DataContext is not MainViewModel vm) return; _ = vm.LaunchSelected(); ScrollGameRowSelectionIntoView(vm); }
}
