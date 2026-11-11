#if WINDOWS
using System;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

internal static class HardwareTask
{
    public static int ApplyTdp(string mode)
    {
        if (!OperatingSystem.IsWindows()) return 1;
        if (string.IsNullOrWhiteSpace(mode)) return 1;

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
        if (!OperatingSystem.IsWindows()) return 1;
        if (string.IsNullOrWhiteSpace(mode) || string.Equals(mode, "Automático", StringComparison.Ordinal)) return 0;

        try
        {
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