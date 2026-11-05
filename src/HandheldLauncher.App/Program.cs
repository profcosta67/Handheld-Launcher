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

        // Processo ajudante do GameInput (L3+R3 com o jogo na frente) — o
        // mesmo exe, aberto por GameInputHelper.StartHelperProcess() com
        // "--gameinput-helper <pid>". Sai daqui antes de qualquer outra
        // coisa (mutex de instância única, janela): não é uma abertura do
        // launcher, é o processo ajudante escutando o combo em segundo
        // plano enquanto o jogo está em primeiro plano. Ver Input/GameInputHelper.
        //
        // v0.16.2: esse despacho existia desde a v0.6.3 (commit e7d3773) mas
        // foi perdido na reescrita completa deste Main() em c3ef109 (v0.13.7,
        // 31/10) e nunca foi restaurado. Sem ele, o processo ajudante sempre
        // perdia a disputa do mutex de instância única contra o launcher
        // principal já aberto, caía direto no NotifyExistingInstance() lá
        // embaixo (que restaura a janela principal da bandeja — o "segundo
        // popup" maior que aparecia e sumia logo, relatado pelo usuário) e
        // saía sem nunca chegar a rodar — por isso o Quick Settings parava
        // de responder ao L3+R3 assim que um jogo era aberto.
        if (args.Length > 0 && args[0] == HandheldLauncher.App.Input.GameInputHelper.Argument)
        {
            HandheldLauncher.App.Diagnostics.CrashReport.Install("ajudante");
            HandheldLauncher.App.Input.GameInputHelper.RunHelper(args);
            return;
        }

        // 🟢 CORREÇÃO BUG 1: Se estamos reiniciando (layout picker), espera o
        // processo antigo morrer ANTES de disputar o mutex. Sem isso, o novo
        // processo perde o mutex e sai, deixando o usuário sem launcher.
        // v0.16.4: relatório de crash local (erros não tratados → arquivo).
        HandheldLauncher.App.Diagnostics.CrashReport.Install("launcher");

        LauncherRestart.WaitForPreviousInstance(args);

        // Uma tarefa automática que já esteja em execução não deve abrir
        // uma segunda janela nem disputar o foco com a instância existente.
        if (WatchdogGate.ShouldExitImmediately(args))
            return;

        if (!SingleInstance.TryAcquire())
        {
            HandheldLauncher.App.Diagnostics.DebugLog.Write("[STARTUP] Já existe um launcher aberto — esta abertura só avisou a janela existente e saiu.");
            SingleInstance.NotifyExistingInstance();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont() // v0.16.4: fonte "Limpa (Inter)", do pacote Avalonia.Fonts.Inter
            .LogToTrace();
}