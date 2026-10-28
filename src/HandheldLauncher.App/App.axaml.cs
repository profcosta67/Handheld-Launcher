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

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(libraryService, gameLauncher, artworkService),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
