using Avalonia;
using HandheldLauncher.App.Platform;

namespace HandheldLauncher.App;

internal static class Program
{
    // Avalonia needs a static Main and this exact AppBuilder shape so the
    // designer/previewer can find it via reflection — don't inline this
    // into App.axaml.cs.
    //
    // Single-instance check happens FIRST, before BuildAvaloniaApp even
    // runs — see SingleInstance.cs for why. A second launch attempt never
    // gets as far as constructing a MainWindow; it just pings the real
    // instance to come forward and returns immediately.
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack PRECISA ser a primeira coisa: o instalador/atualizador
        // reabre o exe com argumentos especiais (ex: "--veloapp-install")
        // e este Run() trata e encerra o processo ali mesmo, antes de
        // qualquer coisa nossa (mutex, janela) rodar.
        Velopack.VelopackApp.Build().Run();

        // Diagnóstico: quem abriu este processo e com quais argumentos. Se o
        // launcher reabrir sozinho depois de "Sair" (ex: Modo Xbox/Legion
        // Space reabrindo o app principal), o debug.log mostra o culpado.
        HandheldLauncher.App.Diagnostics.DebugLog.Write(
            $"START v{System.Diagnostics.FileVersionInfo.GetVersionInfo(Environment.ProcessPath ?? string.Empty).ProductVersion} pai={WatchdogGate.DescribeParent()} args=[{string.Join(' ', args)}]");

        // Launcher da instalação ANTIGA rodando? Fecha ele antes de tudo —
        // senão o mutex dele faz este (novo) só trazer a janela velha pra
        // frente. Ver LegacyInstallCleanup.
        LegacyInstallCleanup.KillLegacyProcesses();

        // Abertura automática (watchdog/login) depois de um "Sair", ou com
        // "abrir sozinho" desligado nas Configurações → sai sem abrir nada.
        // Ver WatchdogGate.
        if (WatchdogGate.ShouldExitImmediately(args))
        {
            HandheldLauncher.App.Diagnostics.DebugLog.Write("START ignorado (abertura automática bloqueada: Sair recente ou modo Desktop).");
            return;
        }

        if (!SingleInstance.TryAcquire())
        {
            SingleInstance.NotifyExistingInstance();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
