using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using HandheldLauncher.App.Platform;
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
            var appSettings = AppSettingsStore.Load();

            if (string.IsNullOrWhiteSpace(appSettings.SteamId64)
                && SteamAccountDetector.FindMostRecent() is { } steamAccount)
            {
                appSettings.SteamId64 = steamAccount.SteamId64;
                try { AppSettingsStore.Save(appSettings); } catch { /* melhor esforço */ }
            }

            if (string.IsNullOrWhiteSpace(appSettings.EdenExecutablePath)
                && EdenInstallationDetector.FindExecutable() is { } edenExe)
            {
                appSettings.EdenExecutablePath = edenExe;
                try { AppSettingsStore.Save(appSettings); } catch { /* melhor esforço */ }
            }

            var steamScanner = new SteamLibraryScanner();
            var xboxFolderScanner = appSettings.XboxScanFolders.Count > 0
                ? new XboxFolderScanner(appSettings.XboxScanFolders)
                : null;
            var steamOwnedGamesCandidate = new SteamOwnedGamesClient(appSettings.SteamWebApiKey, appSettings.SteamId64);
            var steamOwnedGamesClient = steamOwnedGamesCandidate.HasCredentials ? steamOwnedGamesCandidate : null;
            var steamAchievementsCandidate = new SteamAchievementsClient(appSettings.SteamWebApiKey, appSettings.SteamId64);
            var steamAchievementsClient = steamAchievementsCandidate.HasCredentials ? steamAchievementsCandidate : null;
            var libraryStore = new GameLibraryStore();

            var libraryService = new LibraryService(steamScanner, libraryStore, xboxFolderScanner, steamOwnedGamesClient,
                [new EpicLibraryScanner(), new GogLibraryScanner(), new UbisoftLibraryScanner(), new EaLibraryScanner(),
                 new EdenLibraryScanner(appSettings.EdenExecutablePath, appSettings.EdenRomFolders)]);
            var gameLauncher = new GameLauncher();

            var gridDbClient = new SteamGridDbClient(appSettings.SteamGridDbApiKey);
            var artworkService = new ArtworkService(gridDbClient, libraryStore);

            var performanceOverlay = new PerformanceOverlayService();
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    performanceOverlay.EnsureRunning();
                    var rtssExePath = performanceOverlay.FindInstalledExecutable();
                    if (rtssExePath is not null && Path.GetDirectoryName(rtssExePath) is { } rtssDir)
                    {
                        RtssProfileConfig.TryConfigureGlobalOsd(rtssDir);
                    }
                }
                catch { }
            }

            var updateService = new UpdateService();

            PerformanceHud? performanceHud = null;
            if (OperatingSystem.IsWindows())
            {
                performanceHud = new PerformanceHud(appSettings.OverlayPreset);
                desktop.Exit += (_, _) => performanceHud?.Dispose();
            }

            LegacyInstallCleanup.RunInBackground();

            ThemeManager.Apply(appSettings.Theme);
            ThemeManager.ApplyFontSize(appSettings.UiFontSize);
            GameTileViewModel.SizeScale = GameTileViewModel.ScaleForSetting(appSettings.TileSize);

            if (OperatingSystem.IsWindows())
            {
                // Captura a curva do Windows antes de aplicar o modo salvo.
                // Os próximos presets sempre serão derivados desta mesma base.
                DisplayColorManager.CaptureOriginal();
                DisplayColorManager.Apply(appSettings.ColorMode);
                desktop.Exit += (_, _) => DisplayColorManager.RestoreOriginal();
            }

            var mainViewModel = new MainViewModel(libraryService, gameLauncher, artworkService, performanceOverlay, updateService, performanceHud,
                steamOwnedGamesClient, steamAchievementsClient);
            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };

            if (OperatingSystem.IsWindows())
            {
                var overlayHotkeys = new HandheldLauncher.App.Input.OverlayHotkeyWatcher(() =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => mainWindow.ToggleOverlay());
                });
                desktop.Exit += (_, _) => overlayHotkeys.Dispose();

                HandheldLauncher.App.Input.GameInputHelper.ListenInBackground(() =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => mainWindow.ToggleOverlay());
                });
                HandheldLauncher.App.Input.GameInputHelper.StartHelperProcess();
                desktop.Exit += (_, _) => HandheldLauncher.App.Input.GameInputHelper.StopHelperProcess();
            }

            if (appSettings.KioskMode)
            {
                mainWindow.WindowState = Avalonia.Controls.WindowState.FullScreen;
            }

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
