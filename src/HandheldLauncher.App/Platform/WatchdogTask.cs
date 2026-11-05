using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Runtime.Versioning;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Cria/remove a Tarefa Agendada "HandheldLauncherWatchdog" direto do launcher.
/// Usada pelo seletor Desktop/Handheld nas Configurações.
///
/// <para>v0.14.2 — Fim do loop de boot. A versão anterior tinha um
/// <c>TimeTrigger</c> com <c>Repetition=PT1M</c> que abria o launcher a
/// cada 1 minuto PARA SEMPRE. Combinado com um LogonTrigger disparando ao
/// mesmo tempo, causava o "loop infinito de abertura" que o usuário
/// relatou. A correção: substituir o TimeTrigger por <c>RestartOnFailure</c>
/// com <b>count limitado</b> (só reabre se o app crashar, não se fechar
/// normalmente), e adicionar um <c>Delay</c> de 20s no LogonTrigger (espera
/// o Legion Space e o Xbox app carregarem antes de abrir).</para>
///
/// <para>Resultado prático:
/// <list type="bullet">
/// <item>Boot normal → 1 abertura (delay 20s).</item>
/// <item>App crasha sozinho → reabre até 3x, 30s entre cada; depois desiste (evita loop).</item>
/// <item>Usuário fecha pelo menu Power → não reabre (exit 0, RestartOnFailure não dispara).</item>
/// <item>Usuário fecha por crash → reabre até 3x.</item>
/// </list>
/// </para>
/// </summary>
internal static class WatchdogTask
{
    private const string TaskName = "HandheldLauncherWatchdog";

    /// <summary>
    /// v0.16.2: a tarefa agendada existe? Pra Central de Ajuda conferir se ela
    /// bate com o modo escolhido. null = não deu pra saber.
    /// </summary>
    public static bool? Exists() => Exists(TaskName);

    /// <summary>
    /// Tarefa criada pelo InstallTask.ps1 do instalador (abre o launcher no
    /// login com privilégio de administrador, em qualquer modo).
    /// </summary>
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
            // 🟢 CORREÇÃO: StartBoundary no passado é OK, mas a LogonTrigger tem
            // <Delay>PT20S</Delay> que faz o "espera 20s" corretamente. Sem
            // TimeTrigger, não há repetição infinita.
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
                    <Principal id="Author"><UserId>{Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal>
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