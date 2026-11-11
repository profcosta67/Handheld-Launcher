using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Runtime.Versioning;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

internal static class WatchdogTask
{
    private const string TaskName = "HandheldLauncherWatchdog";

    public static bool? Exists() => Exists(TaskName);

    public const string InstallerTaskName = "HandheldLauncher_Startup";

    public static bool? Exists(string taskName)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{taskName}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return null;
            _ = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(5000)) return null;
            return process.ExitCode == 0;
        }
        catch
        {
            return null;
        }
    }

    public static bool Apply(bool handheldMode)
    {
        if (!OperatingSystem.IsWindows())
        {
            DebugLog.Write("WatchdogTask: Ignorado no Linux.");
            return false;
        }

        if (!ElevationService.IsElevated)
        {
            DebugLog.Write($"WatchdogTask: sem administrador — a delegar '{handheldMode}' ao processo elevado.");
            return ElevationService.RunElevated(handheldMode ? "watchdog-register" : "watchdog-remove");
        }

        return handheldMode ? Register() : Remove();
    }

    [SupportedOSPlatform("windows")]
    private static bool Register()
    {
        string? xmlPath = null;
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                DebugLog.Write("WatchdogTask: ProcessPath vazio, nada registrado.");
                return false;
            }

            var user = WindowsIdentity.GetCurrent().Name;
            var start = DateTime.Now.AddMinutes(-1).ToString("yyyy-MM-dd'T'HH:mm:ss");

            var xml = $"""
                <?xml version="1.0" encoding="UTF-16"?>
                <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                  <RegistrationInfo><Description>Handheld Launcher — modo Handheld (abre no login, reabre se crashar até 3x).</Description></RegistrationInfo>
                  <Triggers>
                    <LogonTrigger>
                      <Enabled>true</Enabled>
                      <UserId>{Escape(user)}</UserId>
                      <Delay>PT20S</Delay>
                    </LogonTrigger>
                  </Triggers>
                  <Principals>
                    <Principal id="Author"><UserId>{Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>
                  </Principals>
                  <Settings>
                    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                    <StartWhenAvailable>true</StartWhenAvailable>
                    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                    <Enabled>true</Enabled>
                    <RestartOnFailure>
                        <Interval>PT1M</Interval>
                        <Count>3</Count>
                    </RestartOnFailure>
                  </Settings>
                  <Actions Context="Author">
                    <Exec>
                      <Command>{Escape(exe)}</Command>
                      <Arguments>--watchdog</Arguments>
                      <WorkingDirectory>{Escape(Path.GetDirectoryName(exe) ?? string.Empty)}</WorkingDirectory>
                    </Exec>
                  </Actions>
                </Task>
                """;

            xmlPath = Path.Combine(Path.GetTempPath(), "HandheldLauncherWatchdog.xml");
            File.WriteAllText(xmlPath, xml, Encoding.Unicode);

            return RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"WatchdogTask.Register EXCEPTION {ex.Message}");
            return false;
        }
        finally
        {
            try { if (xmlPath is not null && File.Exists(xmlPath)) File.Delete(xmlPath); } catch { }
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool Remove()
    {
        RunSchtasks($"/Delete /TN \"{TaskName}\" /F", okExitCodes: [0, 1]);
        return true;
    }

    [SupportedOSPlatform("windows")]
    private static bool RunSchtasks(string arguments, int[]? okExitCodes = null)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return false;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(8000);
            DebugLog.Write($"WatchdogTask: schtasks {arguments.Split(' ')[0]} exit={process.ExitCode} out=\"{stdout.Trim()}\" err=\"{stderr.Trim()}\"");
            return (okExitCodes ?? [0]).Contains(process.ExitCode);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"WatchdogTask: schtasks EXCEPTION {ex.Message}");
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}