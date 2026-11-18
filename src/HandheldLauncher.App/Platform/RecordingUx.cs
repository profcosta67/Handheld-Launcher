using Avalonia.Threading;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Views;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// 18/11 (pedido do usuário): o que aparece na tela ao gravar — contagem "3, 2, 1" antes de
/// começar e o selo "GRAVANDO" com o tempo enquanto durar, em qualquer tela (inicial do launcher
/// ou jogo). Tudo numa janela decorativa que não pega foco nem clique
/// (<see cref="RecordingOverlayWindow"/>); só o estado da gravação manda nela.
/// </summary>
internal static class RecordingUx
{
    public static readonly TimeSpan CountdownTotal = TimeSpan.FromSeconds(3);

    private static RecordingOverlayWindow? _window;
    private static DispatcherTimer? _elapsedTimer;
    private static DateTime _startedAtUtc;

    /// <summary>Contagem 3, 2, 1 (uma por segundo). Volta quando a contagem termina.</summary>
    public static async Task CountdownAsync()
    {
        try
        {
            var window = Ensure();
            if (window is null)
            {
                await Task.Delay(CountdownTotal);
                return;
            }

            for (var number = (int)CountdownTotal.TotalSeconds; number >= 1; number--)
            {
                window.ShowCountdown(number);
                await Task.Delay(1000);
            }
            window.HideCountdown();
            CloseIfIdle();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[GRAVADOR] Contagem falhou ({ex.GetType().Name}: {ex.Message}) — seguindo direto.");
        }
    }

    /// <summary>Liga o selo "GRAVANDO" e o relógio.</summary>
    public static void ShowRecording(DateTime startedAtUtc)
    {
        try
        {
            _startedAtUtc = startedAtUtc;
            var window = Ensure();
            if (window is null) return;

            window.SetElapsed(TimeSpan.Zero);
            window.ShowBadge();

            _elapsedTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _elapsedTimer.Tick -= OnElapsedTick;
            _elapsedTimer.Tick += OnElapsedTick;
            _elapsedTimer.Start();
        }
        catch (Exception ex) { DebugLog.Write($"[GRAVADOR] Selo de gravação falhou: {ex.Message}"); }
    }

    public static void HideRecording()
    {
        try
        {
            _elapsedTimer?.Stop();
            _window?.HideBadge();
            CloseIfIdle();
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
    }

    /// <summary>Fecha a janela decorativa (saída do launcher).</summary>
    public static void Shutdown()
    {
        try
        {
            _elapsedTimer?.Stop();
            _elapsedTimer = null;
            _window?.Close();
            _window = null;
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
    }

    private static void OnElapsedTick(object? sender, EventArgs e)
    {
        try { _window?.SetElapsed(DateTime.UtcNow - _startedAtUtc); }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
    }

    private static RecordingOverlayWindow? Ensure()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            DebugLog.Write("[GRAVADOR] Aviso de gravação pedido fora da thread da tela — ignorado.");
            return null;
        }
        if (_window is not null) return _window;

        _window = new RecordingOverlayWindow();
        _window.Closed += (_, _) => _window = null;
        _window.Show();
        return _window;
    }

    private static void CloseIfIdle()
    {
        var window = _window;
        if (window is null || window.HasAnythingVisible) return;
        _window = null;
        window.Close();
    }
}
