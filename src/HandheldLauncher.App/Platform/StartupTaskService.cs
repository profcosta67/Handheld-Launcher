using System;
using System.Diagnostics;
using System.IO;

namespace HandheldLauncher.App.Platform;

public static class StartupTaskService
{
    private const string TaskName = "HandheldLauncher_Startup";

    public static void EnsureStartupTask()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            if (TaskExists()) return;

            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

            string arguments = $"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F";

            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit();
                string stdout = process.StandardOutput.ReadToEnd().Trim();
                string stderr = process.StandardError.ReadToEnd().Trim();
                Diagnostics.DebugLog.Write($"[STARTUP TASK] Exit={process.ExitCode} Stdout='{stdout}' Stderr='{stderr}'");
            }
        }
        catch (Exception ex)
        {
            Diagnostics.DebugLog.Write($"[STARTUP TASK] Erro ao criar tarefa: {ex.Message}");
        }
    }

    private static bool TaskExists()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Query /TN \"{TaskName}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch { return false; }
    }
}