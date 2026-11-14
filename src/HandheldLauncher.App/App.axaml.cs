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
using HandheldLauncher.Services.HidHide;
using HandheldLauncher.Services.Syncthing;
#if WINDOWS
using Nefarius.Drivers.HidHide;
#endif

namespace HandheldLauncher.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // v0.25.10: tecla que repete um movimento do controle (controle virando teclado)
        // não move a seleção de novo — ver Input/InputDedup. Vale para todas as janelas.
        Avalonia.Input.KeyEventArgs? droppedKey = null;
        Avalonia.Input.InputElement.KeyDownEvent.AddClassHandler<Avalonia.Controls.Window>(
            (_, e) =>
            {
                if (!HandheldLauncher.App.Input.InputDedup.DropKey(e.Key)) return;
                e.Handled = true;
                droppedKey = e;
            },
            Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Avalonia.Input.InputElement.KeyDownEvent.AddClassHandler<Avalonia.Controls.Window>(
            (_, e) => HandheldLauncher.App.Input.InputDedup.NoteKeyHandled(e.Key, e.Handled, ReferenceEquals(droppedKey, e)),
            Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // O manifesto exige elevação para que os controles de hardware
            // funcionem sem pedir UAC a cada ação.
            DebugLog.Write($"[ELEVACAO] Launcher a correr {(ElevationService.IsElevated ? "COMO ADMINISTRADOR" : "sem administrador (modo padrão)")}.");

            // Aparece na "Saúde do aparelho": sem administrador, mascarar o
            // controle (HidHide) e escrever TDP não têm efeito.
            StartupRestoreStatus.Report(
                "Modo",
                ElevationService.IsElevated ? "administrador" : "padrão (sem admin)",
                ElevationService.IsElevated);

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

            SteamGridDbClient.Log = HandheldLauncher.App.Diagnostics.DebugLog.Write;
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

            // =====================================================================
            // v0.23.2: HidHide  |  v0.24: tolerante a correr sem administrador
            // =====================================================================
            // O driver é acedido via Nefarius e sem elevação o acesso pode falhar.
            // Antes (manifest requireAdministrator) o processo era sempre admin;
            // agora é um recurso que simplesmente fica indisponível em vez de
            // derrubar o arranque.
            IHidHideService hidHideService;
#if WINDOWS
            try
            {
                var realHidHide = new HidHideService(new HidHideControlService());
                realHidHide.Initialize();
                desktop.Exit += (_, _) => realHidHide.Dispose();
                hidHideService = realHidHide;

                DebugLog.Write($"[HidHide] Disponível={realHidHide.IsAvailable}, pronto={realHidHide.IsReady} (elevado={ElevationService.IsElevated}) — {realHidHide.StatusDetail}.");
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[HidHide] Indisponível sem elevação: {ex.GetType().Name}: {ex.Message}. Ocultação de controle desativada.");
                hidHideService = new NullHidHideService();
            }
#else
            // Linux: o HidHide (firewall de dispositivos HID) é um driver do
            // Windows; não existe equivalente. O isolamento de controle será
            // resolvido por uma camada virtual (feeder) — backlog.
            hidHideService = new NullHidHideService();
            DebugLog.Write("[HidHide] Linux: mascaramento de controle indisponível — NullHidHideService.");
#endif

            // =====================================================================
            // v0.23.2: Syncthing
            // =====================================================================
            var syncthingService = new SyncthingService();
            syncthingService.Initialize();
            if (appSettings.SyncthingEnabled)
            {
                syncthingService.Start();
            }
            desktop.Exit += (_, _) => syncthingService.Dispose();

            PerformanceHud? performanceHud = null;
            if (OperatingSystem.IsWindows())
            {
                // v0.25.3: HUD restaurado do que o usuário deixou. Sem histórico
                // (instalação nova) começa no preset "Completo".
                var hudPreset = appSettings.OverlayPresetChosen
                    ? Math.Clamp(appSettings.OverlayPreset, 0, PerformanceHud.PresetNames.Length - 1)
                    : PerformanceHud.PresetNames.Length - 1;
                if (!appSettings.OverlayPresetChosen)
                {
                    appSettings.OverlayPreset = hudPreset;
                    appSettings.OverlayPresetChosen = true;
                    try { AppSettingsStore.Save(appSettings); }
                    catch (Exception ex) { DebugLog.Write($"[HUD] Falha ao salvar preset inicial: {ex.Message}"); }
                    DebugLog.Write($"[HUD] Sem histórico — iniciando no preset '{PerformanceHud.PresetNames[hudPreset]}'.");
                }

                performanceHud = new PerformanceHud(hudPreset);
                desktop.Exit += (_, _) => performanceHud?.Dispose();
            }

            LegacyInstallCleanup.RunInBackground();

            ThemeManager.Apply(appSettings.Theme);
            ThemeManager.ApplyFontSize(appSettings.UiFontSize);
            GameTileViewModel.SizeScale = GameTileViewModel.ScaleForSetting(appSettings.TileSize);

            if (OperatingSystem.IsWindows())
            {
                DisplayColorManager.CaptureOriginal();
                var colorIndex = Math.Clamp(appSettings.ColorMode, 0, DisplayColorManager.ModeNames.Length - 1);
                var colorOk = DisplayColorManager.Apply(colorIndex, force: true);
                HandheldLauncher.App.Diagnostics.StartupRestoreStatus.Report(
                    "Cor", $"{DisplayColorManager.ModeNames[colorIndex]}{(colorOk ? " aplicada" : " não aplicada (veja [DisplayColor])")}", colorOk);
                desktop.Exit += (_, _) => DisplayColorManager.RestoreOriginal();
            }

            var mainViewModel = new MainViewModel(libraryService, gameLauncher, artworkService, performanceOverlay, updateService, performanceHud, steamOwnedGamesClient, steamAchievementsClient, syncthingService);
            desktop.Exit += (_, _) => mainViewModel.Dispose();

            // v0.27: TDP inteligente (só age com a opção ligada e um jogo aberto).
            if (OperatingSystem.IsWindows())
            {
                var smartTdp = new SmartTdpService(mainViewModel);
                desktop.Exit += (_, _) => smartTdp.Dispose();
            }

            var mainWindow = new MainWindow(mainViewModel, hidHideService, syncthingService)
            {
                DataContext = mainViewModel,
            };

            // Lossless Scaling
            if (OperatingSystem.IsWindows())
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
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
                HandheldLauncher.App.Input.GameInputHelper.ListenInBackground(() =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => mainWindow.ToggleOverlay());
                });
                mainWindow.InitializeGamepadComboDetection();

                HandheldLauncher.App.Diagnostics.ControllerDiagnostics.StartMonitoring();
                desktop.Exit += (_, _) => HandheldLauncher.App.Input.GameInputHelper.StopHelperProcess();
            }

            if (appSettings.KioskMode)
            {
                mainWindow.WindowState = Avalonia.Controls.WindowState.FullScreen;
            }

            desktop.MainWindow = mainWindow;

            // v0.25.2: a paleta Cartoon pode vir do tema (16 escura / 17 clara) ou
            // do layout (3) — o layout força a paleta em ApplyAppearance, mas a
            // classe precisa de existir já na primeira pintura.
            if (appSettings.Theme is 16 or 17 || appSettings.LayoutTheme == 3)
            {
                mainWindow.Classes.Add("cartoon");
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}