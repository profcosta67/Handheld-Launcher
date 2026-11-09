using System;
using System.Runtime.InteropServices;
using HandheldLauncher.Core.Services;
using Microsoft.Win32;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Lê o estado da janela do jogo (rect, estilos, monitor) e o valor
/// `GameDVR_FSEBehaviorMode` do Windows, e delega a decisão no Core
/// (<see cref="FullscreenHeuristic"/>).
///
/// <para>Só LEITURA: não move, não redimensiona e não fecha nada. Serve para o
/// Quick Settings avisar que, em ecrã inteiro, o painel pode deixar o jogo
/// preto — a limitação conhecida da 0.24.1.</para>
/// </summary>
internal static class FullscreenDetector
{
    private const int GwlStyle = -16;
    private const uint WsCaption = 0x00C00000;
    private const uint WsThickFrame = 0x00040000;
    private const uint WsPopup = 0x80000000;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    /// <summary>Modo da janela + explicação pronta para o `debug.log`.</summary>
    public static (GameWindowMode Mode, string Detail) Detect(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero)
            return (GameWindowMode.Windowed, "sem janela de jogo");

        try
        {
            if (!GetWindowRect(hwnd, out var window))
                return (GameWindowMode.Windowed, "GetWindowRect falhou");

            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            var monitorWidth = window.Right - window.Left;
            var monitorHeight = window.Bottom - window.Top;

            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                monitorWidth = info.Monitor.Right - info.Monitor.Left;
                monitorHeight = info.Monitor.Bottom - info.Monitor.Top;
            }

            var style = (uint)GetWindowLong(hwnd, GwlStyle);
            var mode = FullscreenHeuristic.Classify(
                window.Right - window.Left,
                window.Bottom - window.Top,
                monitorWidth,
                monitorHeight,
                hasCaption: (style & WsCaption) == WsCaption,
                hasThickFrame: (style & WsThickFrame) == WsThickFrame,
                isPopup: (style & WsPopup) == WsPopup);

            var detail =
                $"{mode} ({window.Right - window.Left}x{window.Bottom - window.Top} em {monitorWidth}x{monitorHeight}); " +
                FullscreenHeuristic.DescribeFullscreenOptimizations(ReadFseBehaviorMode());

            return (mode, detail);
        }
        catch (Exception ex)
        {
            return (GameWindowMode.Windowed, $"deteção falhou: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// `HKCU\System\GameConfigStore\GameDVR_FSEBehaviorMode`: 2 = otimizações de
    /// ecrã inteiro desligadas (o jogo fica em exclusivo verdadeiro).
    /// </summary>
    private static int? ReadFseBehaviorMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore");
            return key?.GetValue("GameDVR_FSEBehaviorMode") is int value ? value : null;
        }
        catch
        {
            return null;
        }
    }
}
