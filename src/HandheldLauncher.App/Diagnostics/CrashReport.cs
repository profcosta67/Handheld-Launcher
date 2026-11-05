using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace HandheldLauncher.App.Diagnostics;

/// <summary>
/// v0.16.4 (plano da v0.16, "relatório de crash local"): qualquer erro não
/// tratado grava um arquivo em %AppData%\HandheldLauncher\crashes com versão,
/// aparelho e a pilha do erro. Não muda o comportamento do app — só registra.
/// O Reportar erro da Central de Ajuda inclui os 3 mais recentes no .zip.
/// </summary>
public static class CrashReport
{
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HandheldLauncher", "crashes");

    private const int MaxFilesKept = 10;
    private static int _installed;

    public static void Install(string processRole)
    {
        if (System.Threading.Interlocked.Exchange(ref _installed, 1) == 1) return;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write(processRole, "AppDomain.UnhandledException" + (e.IsTerminating ? " (encerrando)" : ""), e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
            Write(processRole, "TaskScheduler.UnobservedTaskException", e.Exception);
    }

    /// <summary>Erros da thread da interface (Avalonia). Só registra; não marca como tratado.</summary>
    public static void InstallUiThread()
    {
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
                Write("launcher", "Dispatcher.UIThread.UnhandledException", e.Exception);
        }
        catch
        {
            // Melhor esforço: se o Dispatcher não estiver pronto, os outros ganchos cobrem.
        }
    }

    public static void Write(string processRole, string source, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var now = DateTime.Now;
            var path = Path.Combine(Folder, $"crash-{now:yyyyMMdd-HHmmss}-{processRole}.txt");

            var sb = new StringBuilder();
            sb.AppendLine("Handheld Launcher — relatório de crash");
            sb.AppendLine($"Quando: {now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Versão: {Version()}");
            sb.AppendLine($"Processo: {processRole} (pid {Environment.ProcessId})");
            sb.AppendLine($"Aparelho: {Device()}");
            sb.AppendLine($"Windows: {Environment.OSVersion.VersionString} · .NET {Environment.Version}");
            sb.AppendLine($"Origem: {source}");
            sb.AppendLine();
            sb.AppendLine(exception?.ToString() ?? "(sem exceção)");
            File.WriteAllText(path, sb.ToString());

            DebugLog.Write($"[CRASH] Erro não tratado ({source}) registrado em crashes\\{Path.GetFileName(path)}: {exception?.GetType().Name}: {exception?.Message}");
            DebugLog.Flush(); // v0.18.3: o log agora grava em lote — num crash, grava já.
            Prune();
        }
        catch
        {
            // Nunca deixar o registro do crash gerar outro crash.
        }
    }

    public static string[] Latest(int count) =>
        Directory.Exists(Folder)
            ? Directory.GetFiles(Folder, "crash-*.txt").OrderByDescending(f => f).Take(count).ToArray()
            : Array.Empty<string>();

    private static void Prune()
    {
        foreach (var old in Latest(int.MaxValue).Skip(MaxFilesKept))
        {
            try { File.Delete(old); } catch { /* melhor esforço */ }
        }
    }

    private static string Version() =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";

    private static string Device()
    {
        if (!OperatingSystem.IsWindows()) return Environment.OSVersion.Platform.ToString();
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            var maker = key?.GetValue("SystemManufacturer") as string;
            var model = key?.GetValue("SystemProductName") as string;
            var family = key?.GetValue("SystemFamily") as string;
            return string.Join(" ", new[] { maker, family, model }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
        catch
        {
            return "?";
        }
    }
}
