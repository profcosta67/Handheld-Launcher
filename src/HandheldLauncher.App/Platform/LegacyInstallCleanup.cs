using System.Diagnostics;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Elimina a instalação ANTIGA (feita pelo Install-Launcher.ps1, em
/// %LocalAppData%\Programs\HandheldLauncher) quando este processo é o
/// instalado pelo Setup.exe (Velopack, em %LocalAppData%\HandheldLauncher\current).
///
/// Bug reportado depois da v0.3.2: "clicando em Sair ele fechou, demorou mas
/// abriu... uma versão antiga". A Tarefa Agendada antiga continuava abrindo
/// o exe velho a cada minuto (sem nenhuma correção nova), e com o velho
/// aberto o novo nem chegava a abrir — o SingleInstance via o mutex do velho
/// e só trazia a janela DELE pra frente.
///
/// Então, sempre que o launcher novo abre e a pasta antiga existe:
/// <list type="number">
/// <item><see cref="KillLegacyProcesses"/> (no Program.Main, ANTES do
/// mutex): fecha qualquer HandheldLauncher.exe rodando da pasta antiga.</item>
/// <item><see cref="RunInBackground"/>: aponta a Tarefa Agendada pro exe
/// novo (com --watchdog), copia o backup de registro pra %AppData% e apaga a
/// pasta antiga — sem o exe velho, a tarefa antiga não tem mais o que abrir.</item>
/// </list>
/// Tudo melhor esforço: nunca impede o launcher de abrir.
/// </summary>
internal static class LegacyInstallCleanup
{
    private static readonly string LocalAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string LegacyDir => Path.Combine(LocalAppData, "Programs", "HandheldLauncher");

    private static string VelopackCurrentDir => Path.Combine(LocalAppData, "HandheldLauncher", "current");

    private static bool IsVelopackInstall
    {
        get
        {
            var exe = Environment.ProcessPath;
            return !string.IsNullOrEmpty(exe)
                   && exe.StartsWith(VelopackCurrentDir, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool ShouldRun => IsVelopackInstall && Directory.Exists(LegacyDir);

    public static void KillLegacyProcesses()
    {
        try
        {
            if (!ShouldRun) return;

            var currentId = Environment.ProcessId;
            foreach (var p in Process.GetProcessesByName("HandheldLauncher"))
            {
                using (p)
                {
                    try
                    {
                        if (p.Id == currentId) continue;
                        var path = p.MainModule?.FileName;
                        if (path is null || !path.StartsWith(LegacyDir, StringComparison.OrdinalIgnoreCase)) continue;

                        p.Kill();
                        p.WaitForExit(3000);
                        DebugLog.Write($"LegacyInstallCleanup: launcher antigo fechado (PID {p.Id}).");
                    }
                    catch
                    {
                        // Sem acesso / já fechou — segue.
                    }
                }
            }
        }
        catch
        {
            // Melhor esforço.
        }
    }

    public static void RunInBackground()
    {
        if (!ShouldRun) return;

        _ = Task.Run(() =>
        {
            RepointScheduledTask();
            MigrateRegistryBackup();
            DeleteLegacyDir();
        });
    }

    private static void RepointScheduledTask()
    {
        try
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe)) return;

            var psi = new ProcessStartInfo("schtasks.exe",
                $"/Change /TN \"HandheldLauncherWatchdog\" /TR \"\\\"{currentExe}\\\" --watchdog\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);
            DebugLog.Write($"LegacyInstallCleanup: schtasks /TR exit={process.ExitCode} out=\"{stdout.Trim()}\" err=\"{stderr.Trim()}\"");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"LegacyInstallCleanup: schtasks EXCEPTION {ex.Message}");
        }
    }

    /// <summary>
    /// kiosk-backup.json guarda os valores ORIGINAIS do registro (Xbox Game
    /// Bar etc.) que o desinstalador usa pra desfazer — não pode sumir junto
    /// com a pasta antiga. Mesmo destino usado pelo Configurar-Modo-Console.ps1.
    /// </summary>
    private static void MigrateRegistryBackup()
    {
        try
        {
            var source = Path.Combine(LegacyDir, "kiosk-backup.json");
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HandheldLauncher");
            var target = Path.Combine(dataDir, "kiosk-backup.json");
            if (!File.Exists(source) || File.Exists(target)) return;

            Directory.CreateDirectory(dataDir);
            File.Copy(source, target);
        }
        catch
        {
            // Melhor esforço.
        }
    }

    private static void DeleteLegacyDir()
    {
        // Algumas tentativas: o processo antigo acabou de ser morto e o
        // Windows pode segurar o lock do exe por um instante.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!Directory.Exists(LegacyDir)) return;
                Directory.Delete(LegacyDir, recursive: true);
                DebugLog.Write("LegacyInstallCleanup: pasta antiga removida.");
                return;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"LegacyInstallCleanup: falha ao apagar pasta antiga ({ex.Message}), tentativa {attempt + 1}.");
                Thread.Sleep(2000);
            }
        }
    }
}
