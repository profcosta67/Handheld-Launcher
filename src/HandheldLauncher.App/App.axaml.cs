using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.App.Views;
using HandheldLauncher.Core.Services;
using HandheldLauncher.Core.Services.Artwork;

namespace HandheldLauncher.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Composition-root: plain manual DI on purpose. The whole app is
            // a handful of services — a container would be ceremony, not
            // help — see docs/architecture.md if that changes later.
            var appSettings = AppSettingsStore.Load();

            var steamScanner = new SteamLibraryScanner();
            var libraryStore = new GameLibraryStore();
            var libraryService = new LibraryService(steamScanner, libraryStore);
            var gameLauncher = new GameLauncher();

            var gridDbClient = new SteamGridDbClient(appSettings.SteamGridDbApiKey);
            var artworkService = new ArtworkService(gridDbClient, libraryStore);

            var mainWindow = new MainWindow
            {
                DataContext = new MainViewModel(libraryService, gameLauncher, artworkService),
            };

            // Modo kiosk: fullscreen/borderless instead of the normal dev
            // window, driven entirely by settings.json (set by the
            // installer) — never by a build-time switch, so the exact same
            // binary behaves one way under `dotnet run` and another once
            // installed. Deliberately just an Avalonia WindowState/
            // SystemDecorations change, nothing shell-level: Alt+Tab,
            // Alt+F4 and the taskbar underneath keep working untouched.
            if (appSettings.KioskMode)
            {
                mainWindow.WindowState = Avalonia.Controls.WindowState.FullScreen;
                mainWindow.SystemDecorations = Avalonia.Controls.SystemDecorations.None;
            }

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
