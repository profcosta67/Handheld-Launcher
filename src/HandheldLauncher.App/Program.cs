using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Velopack;

namespace HandheldLauncher.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 🟢 Configura o Velopack para rodar o InstallTask.ps1 durante a instalação
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
                        Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -SkipReboot",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                    };
                    using var process = Process.Start(psi);
                    process?.WaitForExit(30000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao executar InstallTask.ps1: {ex.Message}");
                }
            })
            .Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}