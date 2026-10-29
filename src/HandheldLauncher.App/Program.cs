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
