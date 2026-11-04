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

// Parte da MainWindow: polling do gamepad, atalhos de teclado, navegação da
// biblioteca e o watcher que detecta a janela do jogo em execução.
// Extraído de MainWindow.axaml.cs (v0.16) — nenhuma mudança de comportamento.
public partial class MainWindow : Window
{
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
                    // v0.16.2: Central de Ajuda — LB/RB trocam de aba e B dentro de
                    // um artigo volta pra lista (em vez de fechar a janela).
                    if (dialog is HelpCenterWindow hc && hc.HandleGamepadAction(dialogAction))
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
                        case GamepadAction.Confirm: _ = vm.ConfirmCoverPickerSelection(); break;
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
                    case GamepadAction.Confirm: _ = vm.LaunchSelected(); ScrollGameRowSelectionIntoView(vm); break;
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
                case Key.Down: case Key.Escape: vm.LeaveSubFilterRow(); e.Handled = true; return;
                case Key.Up: e.Handled = true; return;
            }
        }

        var settings = AppSettingsStore.Load();

        if (TryMatch(settings.KeyLaunchGame, e)) { _ = vm.LaunchSelected(); ScrollGameRowSelectionIntoView(vm); e.Handled = true; return; }
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

    public void HandleTileDoubleTapped(object? sender, TappedEventArgs e) { if (DataContext is not MainViewModel vm) return; _ = vm.LaunchSelected(); ScrollGameRowSelectionIntoView(vm); }

}
