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

        // Processo ajudante do GameInput (L3+R3 com o jogo na frente) — o
        // mesmo exe, aberto pelo launcher com "--gameinput-helper <pid>".
        // Sai daqui antes de QUALQUER outra coisa (log START, mutex, trava
        // do watchdog): não é uma abertura do launcher. Ver Input/GameInputHelper.
        if (args.Length > 0 && args[0] == HandheldLauncher.App.Input.GameInputHelper.Argument)
        {
            HandheldLauncher.App.Input.GameInputHelper.RunHelper(args);
            return;
        }

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

        // v0.9.21: aberto por um "reiniciar o launcher" (seletor de layout)
        // — espera o processo anterior morrer ANTES de disputar o mutex.
        // Sem isto, o reinício perderia a disputa, cairia no
        // NotifyExistingInstance abaixo e não reiniciaria nada. Ver
        // LauncherRestart.
        LauncherRestart.WaitForPreviousInstance(args);

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
