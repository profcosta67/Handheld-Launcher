using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// 18/11 — controle mudo na tela da senha do Windows. O pedido HID "liga a saída XInput" resolveu
/// o controle depois do login (teste de 18/11, beta.72), mas na tela do PIN o launcher ainda não
/// está aberto e o controle continua mudo. Aqui registramos uma tarefa do Windows que roda NA
/// INICIALIZAÇÃO (antes do login, como SYSTEM) e abre este mesmo executável só para mandar aquele
/// pedido ao controle — sem janela, sem launcher, sai em seguida.
///
/// O resultado de cada boot fica em C:\ProgramData\HandheldLauncher\controle-boot.log (o
/// %AppData% de quem roda como SYSTEM não é o do usuário); o launcher copia a última linha para o
/// debug.log ao abrir, para o teste poder ser conferido.
/// </summary>
internal static class ControllerWakeTask
{
    public const string Argument = "--acordar-controle";
    private const string TaskName = "HandheldLauncherControllerWake";

    private static readonly string BootLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "HandheldLauncher", "controle-boot.log");

    // Na inicialização o controle pode levar alguns segundos para ser enumerado pelo Windows.
    private const int MaxAttempts = 10;
    private static readonly TimeSpan AttemptGap = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Modo tarefa (<c>--acordar-controle</c>): tenta acordar o controle e sai. Nunca abre janela
    /// e nunca lança exceção; devolve o código de saída do processo.
    /// </summary>
    public static int RunFromTask()
    {
        var started = DateTime.Now;
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                AppendBootLog(started, "não é Windows — nada feito.");
                return 0;
            }

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var result = HandheldLauncher.App.Input.LegionControllerHid.TryEnablePhysicalXInput();
                if (result.FoundController)
                {
                    AppendBootLog(started, $"tentativa {attempt}/{MaxAttempts}: {result.Message}");
                    return 0;
                }
                if (attempt == MaxAttempts)
                {
                    AppendBootLog(started, $"desisti depois de {MaxAttempts} tentativas: {result.Message}");
                    return 0;
                }
                Thread.Sleep(AttemptGap);
            }
            return 0;
        }
        catch (Exception ex)
        {
            AppendBootLog(started, $"falhou ({ex.GetType().Name}: {ex.Message}).");
            return 0;   // uma tarefa que "falha" enche o visualizador de eventos; o log já conta.
        }
    }

    /// <summary>Copia para o debug.log a última linha escrita pela tarefa (uma vez por abertura).</summary>
    public static void ReportLastBootResult()
    {
        try
        {
            if (!File.Exists(BootLogPath)) return;
            var lines = File.ReadAllLines(BootLogPath);
            var last = lines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l));
            if (last is not null) DebugLog.Write($"[CONTROLE-BOOT] Última tentativa antes do login → {last}");
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
    }

    private static void AppendBootLog(DateTime started, string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(BootLogPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // Mantém o arquivo curto: só as últimas 40 linhas interessam.
            var lines = File.Exists(BootLogPath) ? File.ReadAllLines(BootLogPath).ToList() : new List<string>();
            lines.Add($"{started:yyyy-MM-dd HH:mm:ss} {message}");
            if (lines.Count > 40) lines = lines.Skip(lines.Count - 40).ToList();
            File.WriteAllLines(BootLogPath, lines);
        }
        catch
        {
            // Sem log não há nada a fazer: a tarefa não pode falhar por causa disso.
        }
    }

    public static bool? Exists() => WatchdogTask.Exists(TaskName);

    /// <summary>Cria (ligado) ou remove (desligado) a tarefa de inicialização. Precisa de administrador.</summary>
    public static bool Apply(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (!ElevationService.IsElevated)
        {
            DebugLog.Write("[CONTROLE-BOOT] Sem administrador — a tarefa de inicialização não foi mexida.");
            return false;
        }
        return enabled ? Register() : Remove();
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
                DebugLog.Write("[CONTROLE-BOOT] Caminho do executável vazio — nada registrado.");
                return false;
            }

            var xml = $"""
                <?xml version="1.0" encoding="UTF-16"?>
                <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                  <RegistrationInfo><Description>Handheld Launcher — acorda o controle do Legion na inicialização (antes do login).</Description></RegistrationInfo>
                  <Triggers>
                    <BootTrigger><Enabled>true</Enabled><Delay>PT5S</Delay></BootTrigger>
                  </Triggers>
                  <Principals>
                    <Principal id="Author"><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal>
                  </Principals>
                  <Settings>
                    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                    <StartWhenAvailable>true</StartWhenAvailable>
                    <ExecutionTimeLimit>PT2M</ExecutionTimeLimit>
                    <Hidden>true</Hidden>
                    <Enabled>true</Enabled>
                  </Settings>
                  <Actions Context="Author">
                    <Exec>
                      <Command>{Escape(exe)}</Command>
                      <Arguments>{Argument}</Arguments>
                      <WorkingDirectory>{Escape(Path.GetDirectoryName(exe) ?? string.Empty)}</WorkingDirectory>
                    </Exec>
                  </Actions>
                </Task>
                """;

            xmlPath = Path.Combine(Path.GetTempPath(), "HandheldLauncherControllerWake.xml");
            File.WriteAllText(xmlPath, xml, Encoding.Unicode);
            return RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[CONTROLE-BOOT] Falha ao registrar a tarefa: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            try { if (xmlPath is not null && File.Exists(xmlPath)) File.Delete(xmlPath); }
            catch (Exception ex) { DebugLog.Swallowed(ex); }
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
            DebugLog.Write($"[CONTROLE-BOOT] schtasks {arguments.Split(' ')[0]} exit={process.ExitCode} out=\"{stdout.Trim()}\" err=\"{stderr.Trim()}\"");
            return (okExitCodes ?? [0]).Contains(process.ExitCode);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[CONTROLE-BOOT] schtasks falhou: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
