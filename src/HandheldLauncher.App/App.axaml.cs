using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HandheldLauncher.App.Diagnostics;
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
                try { AppSettingsStore.Save(appSettings); }
                catch (Exception ex) { DebugLog.Write($"[Startup] Falha ao salvar SteamId64 detectado: {ex.Message}"); }
            }

            if (string.IsNullOrWhiteSpace(appSettings.EdenExecutablePath)
                && EdenInstallationDetector.FindExecutable() is { } edenExe)
            {
                appSettings.EdenExecutablePath = edenExe;
                try { AppSettingsStore.Save(appSettings); }
                catch (Exception ex) { DebugLog.Write($"[Startup] Falha ao salvar caminho do Eden detectado: {ex.Message}"); }
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
                catch (Exception ex) { DebugLog.Write($"[Startup] Falha ao configurar RTSS/overlay de desempenho: {ex.Message}"); }
            }

            var updateService = new UpdateService(appSettings.BetaChannelOptIn);

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
                DisplayColorManager.CaptureOriginal();
                DisplayColorManager.Apply(appSettings.ColorMode);
                desktop.Exit += (_, _) => DisplayColorManager.RestoreOriginal();
            }

            var mainViewModel = new MainViewModel(libraryService, gameLauncher, artworkService, performanceOverlay, updateService, performanceHud,
                steamOwnedGamesClient, steamAchievementsClient);
            desktop.Exit += (_, _) => mainViewModel.Dispose();
            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };

            // 🟢 v0.15.4: Inicia o Lossless Scaling em segundo plano (minimizado)
            // pra ficar pronto pra receber o atalho Ctrl+Alt+S quando o usuário clicar no toggle do Overlay.
            if (OperatingSystem.IsWindows())
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Pequeno delay pra não competir com o boot do launcher
                        await Task.Delay(5000);
                        if (!LosslessScalingService.IsLosslessRunning())
                        {
                            var launched = LosslessScalingService.TryLaunchLossless();
                            System.Diagnostics.Debug.WriteLine($"[PATINHO] Boot launch: {(launched ? "iniciado" : "falhou")}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[PATINHO] Boot launch EXCEPTION: {ex.Message}");
                    }
                });
            }

            if (OperatingSystem.IsWindows())
            {
                // v0.16: os dois detectores de L3+R3 (OverlayHotkeyWatcher via
                // XInput comum, e GameInputHelper via GameInput com entrega em
                // segundo plano) rodavam juntos o tempo todo e podiam disparar
                // o mesmo ToggleOverlay() em corrida — causa provável do bug
                // intermitente do Quick Settings relatado pelo usuário. Agora
                // o MainWindow alterna entre os dois conforme um jogo está
                // rodando ou não (só um fica ativo por vez), o que também
                // reduz o tempo em que o launcher força entrega de input em
                // segundo plano — suspeito de travar a suspensão do aparelho.
                HandheldLauncher.App.Input.GameInputHelper.ListenInBackground(() =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => mainWindow.ToggleOverlay());
                });
                mainWindow.InitializeGamepadComboDetection();
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