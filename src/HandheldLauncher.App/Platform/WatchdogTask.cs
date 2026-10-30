using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Runtime.Versioning;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Cria/remove a Tarefa Agendada "HandheldLauncherWatchdog" direto do
/// launcher. Usada pelo seletor Desktop/Handheld nas Configurações:
/// - Handheld: tarefa com gatilho "ao fazer login" + repetição a cada 1 min,
///   rodando também na bateria (handheld!), sem limite de tempo, sem
///   instâncias duplicadas, argumento --watchdog (ver WatchdogGate).
/// - Desktop: tarefa removida.
/// Via XML porque o `schtasks /Create` simples não consegue desligar o
/// "não iniciar/parar na bateria" nem combinar login + repetição.
/// 
/// v1.0.0 (Linux prep): Adicionado suporte para Linux. No Linux, essa
/// funcionalidade é ignorada por enquanto (retorna false/true sem fazer nada),
/// pois o systemd seria o equivalente ao schtasks e exigiria configuração extra.
/// </summary>
internal static class WatchdogTask
{
    private const string TaskName = "HandheldLauncherWatchdog";

    public static bool Apply(bool handheldMode)
    {
        if (!OperatingSystem.IsWindows())
        {
            DebugLog.Write("WatchdogTask: Ignorado no Linux (não implementado via systemd).");
            return false;
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
                  <RegistrationInfo><Description>Mantém o Handheld Launcher aberto (modo Handheld).</Description></RegistrationInfo>
                  <Triggers>
                    <LogonTrigger><Enabled>true</Enabled><UserId>{Escape(user)}</UserId></LogonTrigger>
                    <TimeTrigger>
                      <StartBoundary>{start}</StartBoundary>
                      <Enabled>true</Enabled>
                      <Repetition><Interval>PT1M</Interval></Repetition>
                    </TimeTrigger>
                  </Triggers>
                  <Principals>
                    <Principal id="Author"><UserId>{Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal>
                  </Principals>
                  <Settings>
                    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                    <StartWhenAvailable>true</StartWhenAvailable>
                    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                    <Enabled>true</Enabled>
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
        // Código de saída 1 = "tarefa não existe": já é o estado desejado.
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