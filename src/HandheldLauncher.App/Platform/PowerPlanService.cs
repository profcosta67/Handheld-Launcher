using System;
using System.Diagnostics;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Troca o plano de energia ativo do Windows por <c>powercfg /setactive</c>.
///
/// v0.25.6: na bateria o Windows troca para o plano "Equilibrado", que reduz a
/// frequência do CPU/GPU e o brilho — o jogo perde desempenho em relação à
/// tomada. O botão "Desempenho (bateria)" do Quick Settings ativa o plano
/// "Alto desempenho" mesmo na bateria. <c>powercfg /setactive</c> NÃO exige
/// administrador (confirmado no Legion Go 2), por isso não há UAC.
/// </summary>
internal static class PowerPlanService
{
    // GUIDs nativos do Windows (existem em todas as edições, mesmo ocultos no /list).
    private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    public static bool SetHighPerformance() => SetActive(HighPerformanceGuid, "Alto desempenho");
    public static bool SetBalanced() => SetActive(BalancedGuid, "Equilibrado");

    /// <summary>True quando o plano ativo é o "Alto desempenho".</summary>
    public static bool IsHighPerformanceActive()
    {
        if (!OperatingSystem.IsWindows()) return false;
        var output = Run("powercfg", "/getactivescheme", out _);
        return output.Contains(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SetActive(string guid, string label)
    {
        if (!OperatingSystem.IsWindows()) return false;

        _ = Run("powercfg", $"/setactive {guid}", out var exit);
        DebugLog.Write(exit == 0
            ? $"[PLANO] Plano \"{label}\" ativado ({guid})."
            : $"[PLANO] Falha ao ativar \"{label}\" ({guid}); exit={exit}.");
        return exit == 0;
    }

    private static string Run(string file, string args, out int exitCode)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null) { exitCode = -1; return string.Empty; }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            exitCode = process.ExitCode;
            return stdout + "\n" + stderr;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[PLANO] Falha ao executar {file} {args}: {ex.Message}");
            exitCode = -1;
            return string.Empty;
        }
    }
}
