using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// O launcher arranca **sem** administrador (`app.manifest` = asInvoker). Quando
/// uma ação precisa de privilégios, reabre o MESMO executável com
/// <c>--elevated-task &lt;ação&gt;</c> e o verbo <c>runas</c>: o UAC aparece uma vez
/// por ação, a instância elevada faz só aquela tarefa e sai.
///
/// A instância elevada é despachada no <c>Program.Main</c> ANTES do mutex de
/// instância única — senão ela perderia a disputa e só traria a janela existente
/// à frente (mesmo cuidado que o processo ajudante do GameInput).
/// </summary>
public static class ElevationService
{
    /// <summary>Argumento que marca "sou a instância elevada, só vou executar uma tarefa".</summary>
    public const string TaskArgument = "--elevated-task";

    /// <summary>Estamos a correr como Administrador?</summary>
    public static bool IsElevated { get; } = DetectElevated();

    private static bool DetectElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Chamado pelo <c>Program.Main</c>: se a linha de comando traz uma tarefa
    /// elevada, executa-a e devolve true (o chamador sai com <paramref name="exitCode"/>).
    /// </summary>
    public static bool TryRunTask(string[] args, out int exitCode)
    {
        exitCode = 0;

        var index = Array.IndexOf(args, TaskArgument);
        if (index < 0 || index + 1 >= args.Length) return false;

        exitCode = RunTask(args[index + 1], args, index + 2);
        return true;
    }

    /// <summary>
    /// Pede elevação (UAC) para uma tarefa e espera terminar.
    /// Devolve true quando a tarefa correu e saiu com código 0.
    /// </summary>
    public static bool RunElevated(string task, params string[] extraArgs)
    {
        if (!OperatingSystem.IsWindows()) return false;

        // Se já somos administradores, não vale a pena abrir outro processo.
        if (IsElevated) return RunTask(task, BuildArgs(task, extraArgs), 0) == 0;

        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                exe = Path.Combine(AppContext.BaseDirectory, "HandheldLauncher.exe");

            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,   // obrigatório para o verbo "runas"
                Verb = "runas",           // ← é aqui que o UAC aparece
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
            // HRESULT 0x800704C7 (1223) = o utilizador cancelou o UAC.
            DebugLog.Write($"[ELEVACAO] Sem elevação para '{task}': {ex.GetType().Name} hr=0x{ex.HResult:X8}");
            return false;
        }
    }

    // ---------------------------------------------------------------- tarefas

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
        DebugLog.Write($"[ELEVACAO] Tarefa '{task}' (name='{name}') elevada={IsElevated}");

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

                // Tarefa do watchdog: corre o mesmo código do launcher, já elevado
                // (o schtasks /Create exige administrador).
                case "watchdog-register":
                    return WatchdogTask.Apply(true) ? 0 : 1;

                case "watchdog-remove":
                    return WatchdogTask.Apply(false) ? 0 : 1;

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

    // Liga/desliga um dispositivo Bluetooth pelo PnP (o WinRT não expõe isso).
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

        var ok = output.Contains("OK", StringComparison.Ordinal);
        if (!ok) DebugLog.Write($"[ELEVACAO] PnP sem sucesso: {output.Trim()}");
        return ok;
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
