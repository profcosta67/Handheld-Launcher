using System.Diagnostics;
using System.Runtime.InteropServices;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Decide, logo no começo do Program.Main, se uma abertura AUTOMÁTICA
/// deve ser ignorada. Evita loops de abertura e respeita o botão "Sair".
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
            var isAutomatic = args.Any(a => string.Equals(a, "--watchdog", StringComparison.OrdinalIgnoreCase));

            if (isAutomatic && LauncherRestart.IsRestartPending())
            {
                DebugLog.Write("[STARTUP] Abertura automática ignorada: reinício do launcher em curso.");
                return true;
            }

            if (!isAutomatic)
            {
                ClearQuitFlag();
                return false;
            }

            var settings = AppSettingsStore.Load();
            if (!settings.AutoRestart)
            {
                DebugLog.Write("[STARTUP] Abertura automática ignorada: \"Abrir sozinho no login\" está desligado.");
                return true;
            }

            if (QuitFlagIsFromThisBoot())
            {
                DebugLog.Write("[STARTUP] Abertura automática ignorada: o launcher foi fechado de propósito.");
                return true;
            }

            DebugLog.Write($"[STARTUP] Abertura automática (--watchdog), processo pai: {DescribeParent()}.");
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static void MarkDeliberateQuit()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FlagPath)!);
            File.WriteAllText(FlagPath, GetSessionStartUtc().ToString("O"));
        }
        catch { }
    }

    private static void ClearQuitFlag()
    {
        try
        {
            if (File.Exists(FlagPath)) File.Delete(FlagPath);
        }
        catch { }
    }

    private static bool QuitFlagIsFromThisBoot()
    {
        if (!File.Exists(FlagPath)) return false;
        if (!DateTime.TryParse(File.ReadAllText(FlagPath).Trim(), null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var flaggedBoot))
        {
            return false;
        }

        return Math.Abs((GetSessionStartUtc() - flaggedBoot.ToUniversalTime()).TotalMinutes) < 2;
    }

    private static DateTime GetBootTimeUtc()
        => DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

    private static DateTime GetSessionStartUtc()
    {
        try
        {
            var sessionId = Process.GetCurrentProcess().SessionId;
            DateTime? earliest = null;
            foreach (var process in Process.GetProcessesByName("winlogon"))
            {
                try
                {
                    if (process.SessionId != sessionId) continue;
                    var started = process.StartTime.ToUniversalTime();
                    if (earliest is null || started < earliest) earliest = started;
                }
                catch { }
                finally { process.Dispose(); }
            }
            if (earliest is not null) return earliest.Value;
        }
        catch { }
        return GetBootTimeUtc();
    }

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
}