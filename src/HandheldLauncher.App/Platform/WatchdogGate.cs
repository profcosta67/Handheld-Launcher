using System.Diagnostics;
using System.Runtime.InteropServices;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Decide, logo no começo do Program.Main, se uma abertura AUTOMÁTICA
/// (Tarefa Agendada "HandheldLauncherWatchdog": login + checagem a cada
/// 1 min) deve ser ignorada. Bug reportado: "no modo desktop ainda fica
/// abrindo toda hora mesmo clicando em Sair". Antes, o "Sair" só tentava
/// desativar a tarefa via schtasks — se isso falhasse (permissão, tarefa
/// antiga, etc.) o watchdog reabria em até 1 minuto. Agora quem decide é o
/// próprio launcher, sem depender de mexer na tarefa:
/// <list type="bullet">
/// <item>"Sair" grava um marcador com a hora do boot atual. Aberturas
/// automáticas nesse mesmo boot saem na hora (sem janela). Reiniciou o
/// aparelho → marcador perde a validade → abre normal no login.</item>
/// <item>Configurações → "Abrir sozinho no login e reabrir se fechar"
/// desmarcado (uso no PC): toda abertura automática sai na hora.</item>
/// <item>Abertura MANUAL (atalho, menu Iniciar) sempre abre e limpa o
/// marcador.</item>
/// </list>
/// "Automática" = argumento --watchdog (tarefas registradas pelo
/// Configurar-Modo-Console.ps1 novo) OU processo pai é o serviço do
/// Agendador de Tarefas (svchost/taskhostw) — cobre tarefas antigas
/// registradas sem o argumento.
/// </summary>
internal static class WatchdogGate
{
    private static readonly string FlagPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HandheldLauncher",
        "quit-flag.txt");

    /// <summary>True = este processo deve encerrar imediatamente.</summary>
    public static bool ShouldExitImmediately(string[] args)
    {
        try
        {
            // Identificação explícita evita classificar incorretamente processos
            // iniciados por svchost como tarefas agendadas.
            var isAutomatic = args.Any(a => string.Equals(a, "--watchdog", StringComparison.OrdinalIgnoreCase));

            if (!isAutomatic)
            {
                ClearQuitFlag();
                return false;
            }

            var settings = AppSettingsStore.Load();
            if (!settings.AutoRestart) return true;

            return QuitFlagIsFromThisBoot();
        }
        catch
        {
            // Na dúvida, abre normal — nunca impedir o launcher de abrir por erro aqui.
            return false;
        }
    }

    /// <summary>Chamado no "Sair" deliberado (menu Power, bandeja).</summary>
    public static void MarkDeliberateQuit()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FlagPath)!);
            File.WriteAllText(FlagPath, GetBootTimeUtc().ToString("O"));
        }
        catch
        {
            // Melhor esforço.
        }
    }

    private static void ClearQuitFlag()
    {
        try
        {
            if (File.Exists(FlagPath)) File.Delete(FlagPath);
        }
        catch
        {
            // Melhor esforço.
        }
    }

    private static bool QuitFlagIsFromThisBoot()
    {
        if (!File.Exists(FlagPath)) return false;
        if (!DateTime.TryParse(File.ReadAllText(FlagPath).Trim(), null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var flaggedBoot))
        {
            return false;
        }

        // Hora do boot calculada por TickCount varia alguns ms entre
        // processos — tolerância folgada de 2 minutos.
        return Math.Abs((GetBootTimeUtc() - flaggedBoot.ToUniversalTime()).TotalMinutes) < 2;
    }

    private static DateTime GetBootTimeUtc()
        => DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

    // --- processo pai -----------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);

    /// <summary>Nome do processo pai ("explorer", "svchost"...), só pra diagnóstico no debug.log.</summary>
    public static string DescribeParent()
    {
        try
        {
            var info = new ProcessBasicInformation();
            using var current = Process.GetCurrentProcess();
            if (NtQueryInformationProcess(current.Handle, 0, ref info, Marshal.SizeOf(info), out _) != 0) return "?";
            using var parent = Process.GetProcessById(info.InheritedFromUniqueProcessId.ToInt32());
            return parent.ProcessName;
        }
        catch
        {
            return "(pai já encerrou)";
        }
    }

    private static bool IsLaunchedByTaskScheduler()
    {
        try
        {
            var info = new ProcessBasicInformation();
            using var current = Process.GetCurrentProcess();
            var status = NtQueryInformationProcess(current.Handle, 0, ref info, Marshal.SizeOf(info), out _);
            if (status != 0) return false;

            var parentId = info.InheritedFromUniqueProcessId.ToInt32();
            using var parent = Process.GetProcessById(parentId);
            var name = parent.ProcessName;
            return string.Equals(name, "svchost", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, "taskhostw", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, "taskeng", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Pai já fechou / sem acesso → trata como abertura manual.
            return false;
        }
    }
}
