using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

internal sealed record PowerPlanInfo(string Guid, string Name, bool IsActive);

/// <summary>
/// Lists and activates Windows power plans with <c>powercfg</c>.
/// </summary>
internal static class PowerPlanService
{
    private static readonly Regex PlanLine = new(
        @"(?<guid>[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12})\s+\((?<name>.+)\)\s*(?<active>\*)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<PowerPlanInfo> GetAvailablePlans()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<PowerPlanInfo>();

        var output = Run("/list", out var exitCode);
        if (exitCode != 0)
        {
            DebugLog.Write($"[PLANO] Falha ao listar planos de energia; exit={exitCode}.");
            return Array.Empty<PowerPlanInfo>();
        }

        var plans = new List<PowerPlanInfo>();
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = PlanLine.Match(line);
            if (!match.Success) continue;

            var guid = Guid.Parse(match.Groups["guid"].Value).ToString("D");
            plans.Add(new PowerPlanInfo(guid, match.Groups["name"].Value.Trim(), match.Groups["active"].Success));
        }

        if (plans.Count == 0)
            DebugLog.Write("[PLANO] powercfg não retornou planos de energia reconhecíveis.");

        return plans;
    }

    public static bool SetActivePlan(string guid)
    {
        if (!OperatingSystem.IsWindows() || !Guid.TryParse(guid, out var parsedGuid)) return false;

        var normalizedGuid = parsedGuid.ToString("D");
        _ = Run($"/setactive {normalizedGuid}", out var exitCode);
        DebugLog.Write(exitCode == 0
            ? $"[PLANO] Plano {normalizedGuid} ativado."
            : $"[PLANO] Falha ao ativar o plano {normalizedGuid}; exit={exitCode}.");
        return exitCode == 0;
    }

    private static string Run(string arguments, out int exitCode)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = arguments,
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
            DebugLog.Write($"[PLANO] Falha ao executar powercfg {arguments}: {ex.Message}");
            exitCode = -1;
            return string.Empty;
        }
    }
}