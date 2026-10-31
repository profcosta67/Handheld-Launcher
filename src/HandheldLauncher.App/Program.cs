using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using HandheldLauncher.App.Platform;
using Velopack;

namespace HandheldLauncher.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // O Velopack precisa processar primeiro install/update/uninstall.
        // A proteção de instância única só deve valer para a execução normal.
        VelopackApp.Build()
            .OnAfterInstallFastCallback((version) =>
            {
                var scriptPath = Path.Combine(AppContext.BaseDirectory, "InstallTask.ps1");
                if (!File.Exists(scriptPath)) return;

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -SkipReboot",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                    };
                    using var process = Process.Start(psi);
                    if (process is null)
                    {
                        Console.Error.WriteLine("Não foi possível iniciar InstallTask.ps1.");
                        return;
                    }

                    if (!process.WaitForExit(30000))
                    {
                        process.Kill(entireProcessTree: true);
                        Console.Error.WriteLine("InstallTask.ps1 excedeu o limite de 30 segundos.");
                    }
                    else if (process.ExitCode != 0)
                    {
                        Console.Error.WriteLine($"InstallTask.ps1 terminou com código {process.ExitCode}.");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Erro ao executar InstallTask.ps1: {ex.Message}");
                }
            })
            .Run();

        // Uma tarefa automática que já esteja em execução não deve abrir
        // uma segunda janela nem disputar o foco com a instância existente.
        if (WatchdogGate.ShouldExitImmediately(args))
            return;

        if (!SingleInstance.TryAcquire())
        {
            SingleInstance.NotifyExistingInstance();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
