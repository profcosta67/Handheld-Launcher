using System.Runtime.InteropServices;
using Avalonia.Threading;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Views;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// v0.28: liga o HUD (que roda numa thread própria) à janela desenhada pelo launcher, que vive
/// na thread da tela. Só posta para a tela quando algo muda (texto novo, mostrar, esconder).
/// </summary>
internal sealed class NativeHudSurface : IDisposable
{
    private NativeHudWindow? _window;
    private volatile bool _shown;
    private string? _lastPosted;

    public bool IsShown => _shown;

    public void Show(string text)
    {
        if (_shown && text == _lastPosted) { Post(w => w.SetText(text)); return; }   // reafirma "no topo"
        _lastPosted = text;
        _shown = true;
        Post(w =>
        {
            if (!w.IsVisible) w.Show();
            w.SetText(text);
        });
    }

    public void Hide()
    {
        if (!_shown) return;
        _shown = false;
        _lastPosted = null;
        Dispatcher.UIThread.Post(() =>
        {
            try { _window?.Hide(); }
            catch (Exception ex) { DebugLog.Swallowed(ex); }
        });
    }

    private void Post(Action<NativeHudWindow> action)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _window ??= new NativeHudWindow();
                action(_window);
            }
            catch (Exception ex) { DebugLog.Write($"[HUD] Falha na janela do HUD: {ex.Message}"); }
        });
    }

    public void Dispose()
    {
        _shown = false;
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try { _window?.Close(); } catch (Exception ex) { DebugLog.Swallowed(ex); }
                _window = null;
            });
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
    }

    // --- onde está o jogo ----------------------------------------------------------------

    /// <summary>
    /// Jogo em tela cheia EXCLUSIVA (o Windows informa "Direct3D em tela cheia"): nada desenha por
    /// cima dele, só quem entra no próprio jogo (o RTSS).
    /// </summary>
    public static bool IsExclusiveFullscreen()
    {
        try { return SHQueryUserNotificationState(out var state) == 0 && state == QunsRunningD3dFullScreen; }
        catch { return false; }
    }

    /// <summary>A janela da frente ocupa a tela toda (jogo em tela cheia sem borda).</summary>
    public static bool ForegroundCoversScreen()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return false;
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return false;
            var m = info.rcMonitor;
            if (r.Left > m.Left || r.Top > m.Top || r.Right < m.Right || r.Bottom < m.Bottom) return false;
            var cls = new System.Text.StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            var name = cls.ToString();
            return name is not ("Progman" or "WorkerW" or "Shell_TrayWnd");   // área de trabalho
        }
        catch { return false; }
    }

    private const int QunsRunningD3dFullScreen = 3;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int max);
}
