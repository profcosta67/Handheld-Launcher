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

// Parte da MainWindow: bandeja do sistema, foco/topmost, hotkeys nativos,
// ações de energia (desligar tela/reiniciar/desligar) e saída do Kiosk Mode.
// Extraído de MainWindow.axaml.cs (v0.16) — nenhuma mudança de comportamento.
public partial class MainWindow : Window
{
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

            // Durante o arranque, outros processos do Windows podem ganhar
            // foco por alguns instantes. Continuamos a tentar por até 16 s;
            // qualquer interação do usuário cancela as tentativas.
            ForceForeground(forceIfGameRunning: true);

            if (attempts >= 20)
            {
                DebugLog.Write($"[STARTUP] Tentativas de foco concluídas. IsActive={IsActive}.");
                _foregroundFighterTimer?.Stop();
            }
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

    private bool IsOurWindowInForeground() { if (!OperatingSystem.IsWindows()) return true; try { var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero; return handle != IntPtr.Zero && GetForegroundWindow() == handle; } catch { return true; } }

}
