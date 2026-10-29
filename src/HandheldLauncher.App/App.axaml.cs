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
            // Composition-root: plain manual DI on purpose. The whole app is
            // a handful of services — a container would be ceremony, not
            // help — see docs/architecture.md if that changes later.
            var appSettings = AppSettingsStore.Load();

            // Integração Steam, etapa 1 (v0.9.0): sem SteamID configurado,
            // usa a conta logada no cliente Steam deste PC (sem senha, só o
            // número público do perfil — ver SteamAccountDetector).
            if (string.IsNullOrWhiteSpace(appSettings.SteamId64)
                && SteamAccountDetector.FindMostRecent() is { } steamAccount)
            {
                appSettings.SteamId64 = steamAccount.SteamId64;
                try { AppSettingsStore.Save(appSettings); } catch { /* melhor esforço */ }
            }

            var steamScanner = new SteamLibraryScanner();
            // Null quando AppSettings.XboxScanFolders está vazio (padrão) —
            // "editor de onde procurar" jogos Xbox pedido pelo usuário,
            // hand-edit só em settings.json por enquanto (ver XboxFolderScanner).
            var xboxFolderScanner = appSettings.XboxScanFolders.Count > 0
                ? new XboxFolderScanner(appSettings.XboxScanFolders)
                : null;
            // Null quando SteamWebApiKey/SteamId64 não estão os dois
            // preenchidos (padrão) — painel de Downloads mostra a mensagem
            // explicando o que falta em vez da lista "na conta, não
            // instalado" (ver ILibraryService.HasSteamWebApiCredentials).
            var steamOwnedGamesCandidate = new SteamOwnedGamesClient(appSettings.SteamWebApiKey, appSettings.SteamId64);
            var steamOwnedGamesClient = steamOwnedGamesCandidate.HasCredentials ? steamOwnedGamesCandidate : null;
            var libraryStore = new GameLibraryStore();
            var libraryService = new LibraryService(steamScanner, libraryStore, xboxFolderScanner, steamOwnedGamesClient, new EpicLibraryScanner());
            var gameLauncher = new GameLauncher();

            var gridDbClient = new SteamGridDbClient(appSettings.SteamGridDbApiKey);
            var artworkService = new ArtworkService(gridDbClient, libraryStore);

            // RTSS (overlay de FPS) — "deixa rodando" é melhor esforço puro:
            // nunca lança nem bloqueia a abertura do launcher se não
            // estiver instalado ou der qualquer erro (ver PerformanceOverlayService).
            var performanceOverlay = new PerformanceOverlayService();
            try
            {
                performanceOverlay.EnsureRunning();
            }
            catch
            {
                // Melhor esforço.
            }

            // Atualização automática (Velopack/GitHub Releases). Fora de uma
            // instalação via Setup.exe vira no-op (ver UpdateService).
            var updateService = new UpdateService();

            // HUD de desempenho estilo MangoHud (texto colorido desenhado
            // pelo RTSS por cima do jogo) — ver Platform/PerformanceHud.
            var performanceHud = new PerformanceHud(appSettings.OverlayPreset);
            desktop.Exit += (_, _) => performanceHud.Dispose();

            // Instalação antiga (Install-Launcher.ps1) ainda no aparelho?
            // Redireciona a tarefa, migra o backup e apaga a pasta velha.
            LegacyInstallCleanup.RunInBackground();

            // Aparência (v0.6.0) — antes de criar a janela/tiles: cores do
            // tema e escala das capas já valem desde o primeiro desenho.
            ThemeManager.Apply(appSettings.Theme);
            ThemeManager.ApplyFontSize(appSettings.UiFontSize);
            GameTileViewModel.SizeScale = GameTileViewModel.ScaleForSetting(appSettings.TileSize);

            var mainViewModel = new MainViewModel(libraryService, gameLauncher, artworkService, performanceOverlay, updateService, performanceHud);
            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };

            // L3+R3 troca o HUD numa thread própria (funciona com o jogo na
            // frente e o launcher escondido) + pede pro Windows não
            // desacelerar o launcher em segundo plano. Ver
            // Input/OverlayHotkeyWatcher e Platform/PowerThrottling.
            PowerThrottling.DisableForThisProcess();
            var overlayHotkeys = new HandheldLauncher.App.Input.OverlayHotkeyWatcher(mainViewModel.CycleOverlayPreset);
            desktop.Exit += (_, _) => overlayHotkeys.Dispose();

            // ...e com o JOGO na frente, via GameInput num processo ajudante
            // (o Windows não entrega o controle pra apps em segundo plano
            // sem pedir). Os dois caminhos chamam o mesmo
            // CycleOverlayPreset, que ignora disparos repetidos em < 0,5 s.
            HandheldLauncher.App.Input.GameInputHelper.ListenInBackground(mainViewModel.CycleOverlayPreset);
            HandheldLauncher.App.Input.GameInputHelper.StartHelperProcess();
            desktop.Exit += (_, _) => HandheldLauncher.App.Input.GameInputHelper.StopHelperProcess();

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

                // Reported on the Legion Go 2: Legion Space/Xbox/Steam all
                // open around the same time at boot and our window ends up
                // buried behind them. Topmost keeps us on top of the Z-order
                // regardless of what else opens afterward — it does NOT
                // block Alt+Tab (the user can still switch away on purpose),
                // it only stops OTHER windows from covering ours by default.
                mainWindow.Topmost = true;
            }

            desktop.MainWindow = mainWindow;

            SetupTrayIcon(mainWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Tray icon for the "Minimizar" button (MainWindow.MinimizeToTray) —
    /// always set up, not just in kiosk mode, since minimizing is a
    /// normal bottom-bar action in every mode. Left-click/"Abrir"
    /// restores the window (same as the Ctrl+Alt+H hotkey registered in
    /// MainWindow); "Sair" closes it for good (watchdog disabled first,
    /// same as the gear/global menu's own "Sair"). No need to keep a
    /// reference to the TrayIcon alive in a field here — TrayIcon.SetIcons
    /// attaches it to this Application instance itself via an attached
    /// property, and the Application lives for the whole process.
    /// </summary>
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
