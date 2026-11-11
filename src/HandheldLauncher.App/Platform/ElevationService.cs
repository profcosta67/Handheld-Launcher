using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

public static class ElevationService
{
    public const string TaskArgument = "--elevated-task";

    public static bool IsElevated { get; } = DetectElevated();

    private static bool DetectElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static bool TryRunTask(string[] args, out int exitCode)
    {
        exitCode = 0;
        var index = Array.IndexOf(args, TaskArgument);
        if (index < 0 || index + 1 >= args.Length) return false;

        exitCode = RunTask(args[index + 1], args, index + 2);
        return true;
    }

    public static bool RunElevated(string task, params string[] extraArgs)
    {
        if (!OperatingSystem.IsWindows()) return false;

        // Como o app agora exige admin no manifest, ele sempre cairá aqui (evitando o UAC duplo).
        if (IsElevated) return RunTask(task, BuildArgs(task, extraArgs), 0) == 0;

        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                exe = Path.Combine(AppContext.BaseDirectory, "HandheldLauncher.exe");

            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            startInfo.ArgumentList.Add(TaskArgument);
            startInfo.ArgumentList.Add(task);
            foreach (var extra in extraArgs) startInfo.ArgumentList.Add(extra);

            using var process = Process.Start(startInfo);
            if (process is null) return false;

            if (!process.WaitForExit(60_000))
            {
                DebugLog.Write($"[ELEVACAO] Tarefa '{task}' excedeu 60s — segue em segundo plano.");
                return false;
            }

            DebugLog.Write($"[ELEVACAO] Tarefa '{task}' terminou com código {process.ExitCode}.");
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[ELEVACAO] Sem elevação para '{task}': {ex.GetType().Name} hr=0x{ex.HResult:X8}");
            return false;
        }
    }

    private static string[] BuildArgs(string task, string[] extraArgs)
    {
        var result = new string[extraArgs.Length + 2];
        result[0] = TaskArgument;
        result[1] = task;
        extraArgs.CopyTo(result, 2);
        return result;
    }

    private static int RunTask(string task, string[] args, int firstArg)
    {
        var name = GetArgument(args, firstArg, "--name");
        var pidText = GetArgument(args, firstArg, "--pid");
        var pid = int.TryParse(pidText, out var parsedPid) ? parsedPid : 0;
        DebugLog.Write($"[ELEVACAO] Tarefa '{task}' (name='{name}', pid={pid}) elevada={IsElevated}");

        try
        {
            switch (task)
            {
                case "pnp-enable":
                    return RunPowerShell(PnpScript(name, enable: true)) ? 0 : 1;
                case "pnp-disable":
                    return RunPowerShell(PnpScript(name, enable: false)) ? 0 : 1;
                case "check":
                    return 0;
                case "watchdog-register":
                    return WatchdogTask.Apply(true) ? 0 : 1;
                case "watchdog-remove":
                    return WatchdogTask.Apply(false) ? 0 : 1;
#if WINDOWS
                case "hidhide-hide":
                    return HidHideTask.Apply(hide: true, pid);
                case "hidhide-restore":
                    return HidHideTask.Apply(hide: false, pid);
                case "tdp-apply":
                    return HardwareTask.ApplyTdp(GetArgument(args, firstArg, "--mode"));
                case "fan-apply":
                    return HardwareTask.ApplyFan(GetArgument(args, firstArg, "--mode"));
#endif
                default:
                    DebugLog.Write($"[ELEVACAO] Tarefa desconhecida: {task}");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[ELEVACAO] Falhou '{task}': {ex.GetType().Name}: {ex.Message}");
            return 3;
        }
    }

    private static string PnpScript(string name, bool enable)
    {
        var safe = (name ?? string.Empty).Replace("'", "''");
        var cmdlet = enable ? "Enable-PnpDevice" : "Disable-PnpDevice";
        return $"$d = Get-PnpDevice -Class Bluetooth -FriendlyName '{safe}' -ErrorAction SilentlyContinue; " +
               $"if (-not $d) {{ 'NAO_ENCONTRADO' }} else {{ $d | {cmdlet} -Confirm:$false -ErrorAction Stop; 'OK' }}";
    }

    private static bool RunPowerShell(string script)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output.Contains("OK", StringComparison.Ordinal);
    }

    private static string GetArgument(string[] args, int start, string key)
    {
        for (var i = start; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        return string.Empty;
    }
}