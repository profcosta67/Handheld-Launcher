using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Views;

/// <summary>
/// v0.16.3: o Quick Settings aparecia NO MEIO da tela, sobre um fundo cinza
/// (prints do usuário, 05/11, Eden). O log mostrou que o launcher colocava a
/// janela na direita (1460,175) e, entre uma abertura e outra, ela estava em
/// 740,175 — o centro exato de uma tela 1920x1200. Nenhum código do launcher
/// centraliza o overlay, então algo de fora move a janela.
///
/// Aqui: (1) a cada segundo com o overlay visível, se ele não está onde o
/// launcher colocou, volta pra direita e registra no log; (2) na primeira vez
/// em cada abertura, registra quais janelas cobrem a tela inteira nesse
/// momento — pra descobrir de quem é o fundo cinza.
/// </summary>
public partial class OverlayWindow
{
    private PixelPoint? _expectedPosition;
    private bool _moveLoggedThisShow;
    private static int _fullscreenDumps;

    private void RememberExpectedPosition(PixelPoint position) => _expectedPosition = position;

    /// <summary>Chamado pelo vigia de topmost (1x por segundo) enquanto o overlay está visível.</summary>
    private void GuardPosition()
    {
        try
        {
            if (!IsVisible || _expectedPosition is not { } expected) return;
            var current = Position;
            if (Math.Abs(current.X - expected.X) <= 4 && Math.Abs(current.Y - expected.Y) <= 4) return;

            if (!_moveLoggedThisShow)
            {
                _moveLoggedThisShow = true;
                DebugLog.Write($"[OVERLAY] Janela movida por fora para {current.X},{current.Y} (esperado {expected.X},{expected.Y}) — voltando pra direita.");
                LogFullscreenWindows();
            }
            PositionOnRightEdge();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[OVERLAY] GuardPosition EXCEPTION: {ex.Message}");
        }
    }

    /// <summary>Novo ciclo de abertura: volta a registrar uma mudança de posição.</summary>
    private void ResetPositionGuardForShow() => _moveLoggedThisShow = false;

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>
    /// Lista (em ordem Z, de cima pra baixo) as janelas visíveis que cobrem
    /// quase a tela toda. No máximo 5 vezes por sessão pra não encher o log.
    /// </summary>
    private void LogFullscreenWindows()
    {
        if (_fullscreenDumps >= 5) return;
        _fullscreenDumps++;
        try
        {
            var screen = Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
            if (screen is null) return;
            var bounds = screen.Bounds;
            var lines = new List<string>();
            EnumWindows((hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
                if (!GetWindowRect(hwnd, out var r)) return true;
                var w = r.Right - r.Left;
                var h = r.Bottom - r.Top;
                if (w < bounds.Width * 0.9 || h < bounds.Height * 0.9) return true;

                var title = new StringBuilder(128);
                GetWindowText(hwnd, title, title.Capacity);
                var cls = new StringBuilder(128);
                GetClassName(hwnd, cls, cls.Capacity);
                GetWindowThreadProcessId(hwnd, out var pid);
                string process;
                try { using var p = Process.GetProcessById((int)pid); process = p.ProcessName; }
                catch { process = $"pid {pid}"; }
                lines.Add($"'{title}' ({process}, classe {cls}) {r.Left},{r.Top} {w}x{h}");
                return lines.Count < 15;
            }, IntPtr.Zero);

            DebugLog.Write(lines.Count == 0
                ? "[OVERLAY] Nenhuma janela de tela cheia visível agora."
                : "[OVERLAY] Janelas de tela cheia (de cima pra baixo): " + string.Join(" | ", lines));
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[OVERLAY] LogFullscreenWindows EXCEPTION: {ex.Message}");
        }
    }
}
