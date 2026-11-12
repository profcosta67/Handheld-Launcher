using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// v0.25.9 — diagnóstico do "problema de iniciar com admin". Uma linha no
/// debug.log a cada abertura dizendo se o launcher está elevado e QUEM o
/// abriu (Agendador de Tarefas, Explorer, atualizador do Velopack, modo Xbox,
/// reinício do próprio launcher…). Só lê informação; não muda nada.
/// </summary>
public static class StartupDiagnostics
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle, int processInformationClass,
        ref ProcessBasicInformation processInformation, int processInformationLength, out int returnLength);

    public static void LogLaunchContext(string[] args)
    {
        try
        {
            var version = typeof(StartupDiagnostics).Assembly.GetName().Version?.ToString(3) ?? "?";
            var parent = OperatingSystem.IsWindows() ? DescribeParent() : "n/d";
            var argText = args.Length == 0 ? "(nenhum)" : string.Join(' ', args);
            DebugLog.Write(
                $"[STARTUP] Abertura v{version}: elevado={ElevationService.IsElevated}, aberto por={parent}, " +
                $"args={argText}, sessão={Process.GetCurrentProcess().SessionId}");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[STARTUP] Contexto de abertura indisponível: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string DescribeParent()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            var info = new ProcessBasicInformation();
            var status = NtQueryInformationProcess(self.Handle, 0, ref info, Marshal.SizeOf(info), out _);
            if (status != 0) return $"? (status=0x{status:X})";

            var parentPid = info.InheritedFromUniqueProcessId.ToInt32();
            try
            {
                using var parent = Process.GetProcessById(parentPid);
                return $"{parent.ProcessName} (pid={parentPid})";
            }
            catch
            {
                // O pai já saiu (comum quando quem abriu foi um atalho/atualizador).
                return $"processo já encerrado (pid={parentPid})";
            }
        }
        catch (Exception ex)
        {
            return $"? ({ex.GetType().Name})";
        }
    }
}
