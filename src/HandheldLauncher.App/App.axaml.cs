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
                desktop.Exit += (_, _) => performanceHud.Dispose();
            }

            LegacyInstallCleanup.RunInBackground();

            ThemeManager.Apply(appSettings.Theme);
            ThemeManager.ApplyFontSize(appSettings.UiFontSize);
            GameTileViewModel.SizeScale = GameTileViewModel.ScaleForSetting(appSettings.TileSize);

            if (OperatingSystem.IsWindows())
            {
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
                PowerThrottling.DisableForThisProcess();
                var overlayHotkeys = new HandheldLauncher.App.Input.OverlayHotkeyWatcher(mainViewModel.CycleOverlayPreset);
                desktop.Exit += (_, _) => overlayHotkeys.Dispose();

                HandheldLauncher.App.Input.GameInputHelper.ListenInBackground(mainViewModel.CycleOverlayPreset);
                HandheldLauncher.App.Input.GameInputHelper.StartHelperProcess();
                desktop.Exit += (_, _) => HandheldLauncher.App.Input.GameInputHelper.StopHelperProcess();
            }

            if (appSettings.KioskMode)
            {
                mainWindow.WindowState = Avalonia.Controls.WindowState.FullScreen;
                mainWindow.SystemDecorations = Avalonia.Controls.SystemDecorations.None;
                mainWindow.Topmost = true;
            }

            desktop.MainWindow = mainWindow;

            SetupTrayIcon(mainWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void SetupTrayIcon(MainWindow mainWindow)
    {
        var trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://HandheldLauncher/Assets/app-icon.ico"))),
            ToolTipText = "Handheld Launcher",
        };
        trayIcon.Clicked += (_, _) => mainWindow.RestoreFromTray();

        var openItem = new NativeMenuItem("Abrir");
        openItem.Click += (_, _) => mainWindow.RestoreFromTray();

        var quitItem = new NativeMenuItem("Sair");
        quitItem.Click += (_, _) => mainWindow.QuitFromTray();

        var menu = new NativeMenu();
        menu.Items.Add(openItem);
        menu.Items.Add(quitItem);
        trayIcon.Menu = menu;

        var icons = new TrayIcons();
        icons.Add(trayIcon);
        TrayIcon.SetIcons(Current!, icons);
    }
}