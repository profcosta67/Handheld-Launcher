#if WINDOWS
using System;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Aplica TDP e curva de ventoinha numa instância ELEVADA do launcher — as
/// tarefas <c>--elevated-task tdp-apply --mode &lt;modo&gt;</c> e
/// <c>--elevated-task fan-apply --mode &lt;modo&gt;</c> (ver <see cref="ElevationService"/>).
///
/// <para>Sem administrador, o driver do PawnIO (TDP) e o WMI da Lenovo
/// (ventoinha) recusam a escrita. Este runner cobre o caso "modo padrão":
/// quando a escrita direta falha, o launcher relega ao processo elevado — o UAC
/// aparece uma vez por alteração.</para>
/// </summary>
internal static class HardwareTask
{
    public static int ApplyTdp(string mode)
    {
        if (!OperatingSystem.IsWindows())
        {
            DebugLog.Write("[HARDWARE-TASK] TDP ignorado: não é Windows.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(mode))
        {
            DebugLog.Write("[HARDWARE-TASK] TDP: modo vazio — nada a fazer.");
            return 1;
        }

        try
        {
            var service = new WindowsHardwareService();
            var ok = service.SetTdpMode(mode);
            DebugLog.Write(ok
                ? $"[HARDWARE-TASK] TDP {mode} aplicado com sucesso (instância elevada)."
                : $"[HARDWARE-TASK] TDP {mode} não aplicado na instância elevada.");
            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[HARDWARE-TASK] TDP {mode} falhou: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    public static int ApplyFan(string mode)
    {
        if (!OperatingSystem.IsWindows())
        {
            DebugLog.Write("[HARDWARE-TASK] Ventoinha ignorada: não é Windows.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(mode) || string.Equals(mode, "Automático", StringComparison.Ordinal))
        {
            DebugLog.Write("[HARDWARE-TASK] Ventoinha: modo vazio/Automático — nada a escrever.");
            return 0;
        }

        try
        {
            // NÃO usar `using` aqui: o Dispose() do LenovoFanCurveService chama
            // RestoreOriginal(), que reverteria a curva que acabámos de aplicar.
            // O processo elevado sai logo a seguir (Environment.Exit), sem dispose.
            var service = new LenovoFanCurveService();
            var ok = service.TryApplyPreset(mode, out var message);
            DebugLog.Write(ok
                ? $"[HARDWARE-TASK] Ventoinha {mode} aplicada: {message}"
                : $"[HARDWARE-TASK] Ventoinha {mode} não aplicou: {message}");
            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[HARDWARE-TASK] Ventoinha {mode} falhou: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
}
#endif
