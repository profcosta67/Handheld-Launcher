using System.Diagnostics;

namespace HandheldLauncher.App.Diagnostics;

/// <summary>
/// Cronômetro da abertura (0.28): grava no log quantos milissegundos cada etapa levou desde
/// que o processo começou. Só mede; não muda nada no que o launcher faz. Procure `[ABERTURA]`.
/// </summary>
public static class StartupTimeline
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static long _processOffsetMs = -1;
    private static long _lastMs;
    private static int _lastIsFirstWindow;

    /// <param name="step">Nome curto da etapa que acabou de terminar.</param>
    public static void Mark(string step)
    {
        try
        {
            if (_processOffsetMs < 0)
            {
                // Tempo entre o Windows criar o processo e o primeiro ponto nosso (runtime, JIT, bibliotecas).
                using var process = Process.GetCurrentProcess();
                _processOffsetMs = (long)Math.Max(0, (DateTime.Now - process.StartTime).TotalMilliseconds - Clock.ElapsedMilliseconds);
            }
            var now = Clock.ElapsedMilliseconds;
            var since = now - _lastMs;
            _lastMs = now;
            DebugLog.Write($"[ABERTURA] {step}: +{since} ms (total {now + _processOffsetMs} ms desde o início do processo)");
        }
        catch (Exception ex)
        {
            DebugLog.Swallowed(ex);
        }
    }

    /// <summary>Marca só a primeira vez (ex.: a lista de jogos aparece várias vezes ao longo da sessão).</summary>
    public static void MarkOnce(string step)
    {
        if (Interlocked.Exchange(ref _lastIsFirstWindow, 1) == 1) return;
        Mark(step);
    }
}
