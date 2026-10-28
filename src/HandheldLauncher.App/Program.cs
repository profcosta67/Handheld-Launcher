using Avalonia;

namespace HandheldLauncher.App;

internal static class Program
{
    // Avalonia needs a static Main and this exact AppBuilder shape so the
    // designer/previewer can find it via reflection — don't inline this
    // into App.axaml.cs.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
