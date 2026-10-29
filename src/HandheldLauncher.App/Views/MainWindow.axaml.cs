using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Input;
using HandheldLauncher.App.Platform;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Views;

public partial class MainWindow : Window
{
    // --- Win32 interop: global restore hotkey + forced screen-off. See
    // RegisterRestoreHotkey/TurnOffScreen below for why each one exists. ---
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int RestoreHotkeyId = 0xB00; // arbitrary, only needs to be unique within our own process
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint ModNoRepeat = 0x4000; // don't refire while the keys are held down
    private const uint VkH = 0x48; // 'H' - as in "Handheld"/"Home"
    private const uint WmHotkey = 0x0312;
    private const uint WmSysCommand = 0x0112;
    private const int ScMonitorPower = 0xF170;
    private static readonly IntPtr HwndBroadcast = new(0xffff);

    private readonly GamepadPoller _gamepad = new();
    private readonly DispatcherTimer _gamepadTimer;
    private DispatcherTimer? _topmostEnforcerTimer;
    private DispatcherTimer? _batteryTimer;
    private bool _hotkeyRegistered;

    public MainWindow()
    {
        InitializeComponent();

        // 20Hz is plenty for menu navigation and cheap enough to just leave
        // running the whole time the launcher is open.
        _gamepadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _gamepadTimer.Tick += OnGamepadTick;
        _gamepadTimer.Start();

        Closed += (_, _) => _gamepadTimer.Stop();

        // DataContext is assigned by the composition root (App.axaml.cs)
        // AFTER this constructor runs, so the ViewModel isn't available yet
        // here — wire its view-level requests (opening a dialog, closing
        // the window) once it actually shows up.
        DataContextChanged += OnDataContextChanged;

        // App.axaml.cs sets Topmost=true BEFORE Show() happens (it's a
        // property set right after `new MainWindow { ... }`, before
        // desktop.MainWindow assignment triggers the actual window show),
        // so by the time Opened fires here, Topmost already reflects
        // whether this is a kiosk install. See StartTopmostEnforcer below
        // for why a one-time Topmost=true isn't enough on the real
        // hardware.
        Opened += (_, _) =>
        {
            if (Topmost) StartTopmostEnforcer();
            RegisterRestoreHotkey();
        };
        Closed += (_, _) => UnregisterRestoreHotkeyIfNeeded();
    }

    /// <summary>
    /// Reported on the Legion Go 2, specifically in Windows "Modo Xbox" /
    /// full-screen-experience boot (not in a normal desktop boot, where
    /// the one-time Topmost=true from App.axaml.cs is enough on its own):
    /// Legion Space still ends up in front of us even though our window is
    /// Topmost. Likely explanation: Windows only guarantees "most recently
    /// asserted" ordering among several Topmost windows, not a fixed
    /// winner — and Legion Space, being the FSE "home app" being replaced,
    /// apparently keeps re-asserting its own foreground/topmost status
    /// after boot settles, which pushes us back regardless of our own
    /// one-time Topmost flag.
    ///
    /// This toggles Topmost off/on (which re-inserts the window at the
    /// front of that band) every couple seconds, so even if Legion Space
    /// "wins" a round, we win the next one shortly after. It's a
    /// mitigation, not a guaranteed fix — if Modo Xbox's shell runs its
    /// own composited layer above ordinary Win32 topmost windows entirely,
    /// no amount of toggling from our side will beat it, and the real fix
    /// would have to come from OmniConsole's side instead. Only starts
    /// when Topmost is already on (kiosk installs) — never runs on a
    /// plain `dotnet run` dev window.
    ///
    /// Deliberately does NOT call Activate() anymore (it used to, every
    /// 2 seconds). Reported bug: with Activate() in the loop, this ended
    /// up stealing keyboard focus back from whatever the user had just
    /// switched to on purpose — Alt+Tab to another app, or the Snipping
    /// Tool's capture overlay — since Activate() forces OUR window to the
    /// foreground/focused window, not just to the top of the Topmost
    /// z-order band. Toggling Topmost off/on alone still re-asserts our
    /// place in that band (which is what actually beats Legion Space
    /// re-covering us) without also grabbing input focus away from
    /// whatever the user is doing. If this turns out not to be enough to
    /// beat Legion Space in Modo Xbox on its own, the next step would be
    /// re-adding Activate() but ONLY right after boot (a short window),
    /// not forever.
    ///
    /// REGRESSÃO REPORTADA PELO USUÁRIO nesta leva ("modo desktop ele
    /// fica abrindo toda vez o app"): mesmo sem Activate() (então sem
    /// roubar o FOCO), o toggle off/on ainda empurra a janela pro topo do
    /// Z-ORDER a cada 2s — em "Modo Xbox"/kiosk isso nunca incomoda (não
    /// tem outra janela competindo pra começo), mas num desktop normal do
    /// Windows (com o usuário navegando em outros apps) isso aparece como
    /// a janela do launcher pulando pra cima de tudo repetidamente, pra
    /// sempre, mesmo muito depois do boot. O cenário que isso resolve
    /// (Legion Space brigando pelo topmost) só acontece nos primeiros
    /// segundos depois do boot — então agora isso só roda por um período
    /// curto (TopmostEnforcerGracePeriod) a partir da abertura da janela,
    /// em vez de pra sempre enquanto o app estiver aberto.
    /// </summary>
    private static readonly TimeSpan TopmostEnforcerGracePeriod = TimeSpan.FromSeconds(120);

    private void StartTopmostEnforcer()
    {
        _topmostEnforcerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _topmostEnforcerTimer.Tick += (_, _) =>
        {
            // Two reasons to bail without touching anything:
            // - Topmost off on purpose: something else (not us) turned it
            //   off, so stop fighting it.
            // - !IsVisible: the window is hidden via "Minimizar"
            //   (MinimizeToTray/Hide()). Toggling Topmost on a hidden
            //   window shouldn't bring it back on screen the way Activate()
            //   did, but there's nothing to "reassert in the z-order"
            //   while hidden anyway — skip it either way.
            if (!Topmost || !IsVisible) return;
            Topmost = false;
            Topmost = true;
        };
        _topmostEnforcerTimer.Start();
        Closed += (_, _) => _topmostEnforcerTimer?.Stop();

        // Timer separado, de disparo único, só pra desligar o de cima
        // depois do período de graça — mantém o Tick acima simples (sem
        // precisar contar ticks) e deixa a decisão "até quando lutar"
        // isolada num lugar só.
        var graceTimer = new DispatcherTimer { Interval = TopmostEnforcerGracePeriod };
        graceTimer.Tick += (_, _) =>
        {
            _topmostEnforcerTimer?.Stop();
            graceTimer.Stop();
        };
        graceTimer.Start();
        Closed += (_, _) => graceTimer.Stop();
    }

    /// <summary>
    /// Registers Ctrl+Alt+H as a global hotkey — unlike a normal KeyDown
    /// handler, RegisterHotKey keeps working even while this window is
    /// hidden (via "Minimizar") or not focused, which is the whole point:
    /// it's how you get the launcher back after minimizing it. Needs the
    /// real native HWND, which only exists once the window has actually
    /// been shown once — hooked off Opened like StartTopmostEnforcer
    /// above, same reason. MOD_NOREPEAT so holding the keys down doesn't
    /// fire RestoreFromTray() over and over. Always on (not gated to
    /// kiosk/Topmost like the enforcer) since "Minimizar" itself is a
    /// normal bottom-bar button in every mode, not just kiosk.
    /// </summary>
    private void RegisterRestoreHotkey()
    {
        if (TryGetPlatformHandle() is not { } handle) return;

        Win32Properties.AddWndProcHookCallback(this, OnNativeWndProcHook);
        _hotkeyRegistered = RegisterHotKey(handle.Handle, RestoreHotkeyId, ModControl | ModAlt | ModNoRepeat, VkH);
    }

    private void UnregisterRestoreHotkeyIfNeeded()
    {
        if (_hotkeyRegistered && TryGetPlatformHandle() is { } handle)
        {
            UnregisterHotKey(handle.Handle, RestoreHotkeyId);
        }
        Win32Properties.RemoveWndProcHookCallback(this, OnNativeWndProcHook);
        _hotkeyRegistered = false;
    }

    /// <summary>
    /// Raw Win32 message hook (see RegisterRestoreHotkey) — handles our own
    /// WM_HOTKEY, plus SingleInstance's broadcast restore message: a second
    /// launch attempt that lost the Mutex check in Program.Main sends this
    /// instead of opening its own window, so this is where THIS (the real)
    /// instance reacts to "someone just tried to open me again" the same
    /// way it already reacts to Ctrl+Alt+H or the tray icon.
    /// </summary>
    private IntPtr OnNativeWndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == RestoreHotkeyId)
        {
            RestoreFromTray();
            handled = true;
        }
        else if (msg == SingleInstance.RestoreMessageId)
        {
            RestoreFromTray();
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Hides the window instead of closing it — the process keeps
    /// running, so the watchdog's "is it running?" check (see
    /// installer/Install-Launcher.ps1) never has a reason to relaunch it.
    /// This is the fix for "fica abrindo toda hora, principalmente no
    /// modo desktop": Alt+F4/crash is for when you want the watchdog to
    /// kick back in; this is for when you just want it out of the way for
    /// a while without fighting the watchdog over it. Bound to the
    /// bottom-bar "Minimizar" button (OnMinimizeClick).
    /// </summary>
    private void MinimizeToTray()
    {
        Hide();
    }

    /// <summary>Called by the tray icon's "Sair" menu item (App.axaml.cs) — same deliberate-quit behavior as the gear/global menu's own "Sair".</summary>
    public void QuitFromTray()
    {
        WatchdogGate.MarkDeliberateQuit();
        Close();
    }

    /// <summary>Called by the tray icon's click/"Abrir" menu item (App.axaml.cs) and by the Ctrl+Alt+H hotkey above.</summary>
    public void RestoreFromTray()
    {
        Show();
        if (Topmost)
        {
            // Hide()/Show() shouldn't normally lose WindowState, but this
            // costs nothing and removes any doubt for the one case that
            // actually matters (kiosk installs).
            WindowState = WindowState.FullScreen;
        }
        Activate();
    }

    /// <summary>
    /// SC_MONITORPOWER via a broadcast WM_SYSCOMMAND — the standard Win32
    /// trick to force the display off without touching sleep/hibernate.
    /// lParam=2 means "off" (1=low power, -1=on). A real key press, mouse
    /// move, or controller button on the Legion Go wakes it back up
    /// through Windows' own normal input handling — nothing else needed
    /// on our side for that.
    /// </summary>
    private static void TurnOffScreen()
    {
        try
        {
            PostMessage(HwndBroadcast, WmSysCommand, (IntPtr)ScMonitorPower, (IntPtr)2);
        }
        catch
        {
            // Best-effort — never let this crash the app.
        }
    }

    /// <summary>
    /// `shutdown /r /t 0` — immediate restart. No in-app confirmation step
    /// before this fires (see chat/README for that open question — a
    /// stray tap on "Power" then this item currently restarts right
    /// away, which could lose game progress).
    /// </summary>
    private static void RestartDevice() => RunShutdownExe("/r /t 0");

    /// <summary>`shutdown /s /t 0` — immediate shutdown, same no-confirmation caveat as RestartDevice.</summary>
    private static void ShutdownDevice() => RunShutdownExe("/s /t 0");

    private static void RunShutdownExe(string arguments)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("shutdown.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
            // Best-effort — never let this crash the app.
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        vm.AddGameRequested += (_, _) => _ = OpenAddGameDialogAsync(vm);
        vm.SettingsRequested += (_, _) => _ = OpenSettingsDialogAsync(vm);
        vm.QuitRequested += (_, _) =>
        {
            WatchdogGate.MarkDeliberateQuit();
            vm.ClearOverlayNow();
            Close();
        };
        vm.MinimizeRequested += (_, _) => MinimizeToTray();
        vm.TurnOffScreenRequested += (_, _) => TurnOffScreen();
        vm.RestartDeviceRequested += (_, _) => RestartDevice();
        vm.ShutdownDeviceRequested += (_, _) => ShutdownDevice();

        StartBatteryMonitor(vm);
        StartDownloadsRefreshTimer(vm);

        // Verificação de atualização em segundo plano (espera ~20s e
        // repete a cada 6h; no-op fora de uma instalação via Setup.exe).
        _ = vm.RunUpdateLoopAsync();

        // Primeira vez no launcher instalado: pergunta o modo (Desktop ou
        // Handheld) abrindo as Configurações. Quem já está em modo
        // Handheld (KioskMode ligado) não é incomodado.
        try
        {
            var settings = AppSettingsStore.Load();
            if (vm.UpdatesInstalled && !settings.ModeChosen && !settings.KioskMode)
            {
                Dispatcher.UIThread.Post(() => _ = OpenSettingsDialogAsync(vm), DispatcherPriority.Background);
            }
        }
        catch
        {
            // Melhor esforço.
        }
    }

    /// <summary>Toque no aviso "⬆ vX pronta" da barra de cima.</summary>
    private void OnUpdateBadgeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.ApplyUpdate();
    }

    /// <summary>
    /// Enquanto o painel de Downloads estiver aberto, reconsulta "baixando
    /// agora" periodicamente — diferente de IsCoverPickerOpen/etc, essa
    /// tela mostra progresso que muda sozinho (a Steam continua baixando
    /// com o painel aberto na tela), então um refresh só no momento de
    /// abrir ficaria parado/desatualizado. 2s é frequente o bastante pra
    /// parecer "ao vivo" sem reler o .acf de cada jogo toda hora; não
    /// reconsulta "na conta, não instalado" aqui (RefreshOwnedGames é só
    /// na abertura) — isso é uma chamada de rede de verdade (Steam Web
    /// API), não um arquivo local, então refazer a cada 2s seria
    /// desperdício sem benefício real (a lista de jogos não instalados não
    /// muda a cada segundo).
    /// </summary>
    private DispatcherTimer? _downloadsRefreshTimer;

    private void StartDownloadsRefreshTimer(MainViewModel vm)
    {
        // Ampliado numa leva posterior: o indicador de % na barra de cima
        // (pedido do usuário: "incluir algo que indica visual o percentual
        // do download") precisa de "baixando agora" atualizado MESMO com o
        // painel fechado. Então: painel aberto → a cada 2s (igual antes);
        // painel fechado → a cada 6s (3 ticks), só pra barra de cima — e
        // PAUSADO enquanto um jogo estiver rodando (RunningGame != null),
        // pra não ficar relendo .acf no disco no meio da jogatina num
        // handheld (é pouca coisa, mas zero é melhor que pouco). Volta
        // sozinho quando o jogo fecha.
        var ticksSinceBackgroundRefresh = 0;

        _downloadsRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _downloadsRefreshTimer.Tick += (_, _) =>
        {
            if (vm.IsDownloadsOpen)
            {
                vm.RefreshActiveDownloads();
                ticksSinceBackgroundRefresh = 0;
                return;
            }

            if (vm.RunningGame is not null) return;

            ticksSinceBackgroundRefresh++;
            if (ticksSinceBackgroundRefresh >= 3)
            {
                ticksSinceBackgroundRefresh = 0;
                vm.RefreshActiveDownloads();
            }
        };
        _downloadsRefreshTimer.Start();
        Closed += (_, _) => _downloadsRefreshTimer?.Stop();

        // Primeira leitura já na abertura, pra barra de cima não ficar sem
        // indicador nos primeiros segundos se algo já estiver baixando.
        vm.RefreshActiveDownloads();
    }

    /// <summary>
    /// Pushes the real battery % (Platform.BatteryStatus, a Win32
    /// GetSystemPowerStatus call) into the ViewModel's BatteryStatusText —
    /// fixes the reported "a bateria não está acompanhando o real, ela está
    /// cravada a 100%" bug (that text used to be a literal hardcoded string
    /// in MainWindow.axaml, never wired to anything real). Refreshes every
    /// 30s — battery % doesn't change fast enough to need anything quicker,
    /// and this is a plain OS call on every tick, cheap either way. Reads
    /// once immediately too, so the top bar doesn't sit blank until the
    /// first tick 30s later.
    /// </summary>
    private void StartBatteryMonitor(MainViewModel vm)
    {
        vm.BatteryStatusText = BatteryStatus.GetDisplayText();

        _batteryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _batteryTimer.Tick += (_, _) => vm.BatteryStatusText = BatteryStatus.GetDisplayText();
        _batteryTimer.Start();
        Closed += (_, _) => _batteryTimer?.Stop();
    }

    private void OnGamepadTick(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        foreach (var action in _gamepad.Poll())
        {
            // L3+R3 troca o HUD de desempenho — de propósito ANTES da trava
            // abaixo, pra funcionar com o jogo em primeiro plano.
            if (action == GamepadAction.CycleOverlay)
            {
                vm.CycleOverlayPreset();
                continue;
            }

            // Jogo aberto e o launcher NÃO está em primeiro plano: o
            // controle é do jogo. Sem isto, o XInput (lido mesmo em segundo
            // plano) fazia B "fechar jogo" e A "abrir jogo" no launcher
            // escondido enquanto você jogava.
            if (vm.RunningGame is not null && !IsOurWindowInForeground())
            {
                continue;
            }

            // Temporary diagnostic (see Diagnostics/DebugLog.cs) — logs
            // every action AND the exact overlay state at the moment it's
            // dispatched, so a "nothing happens" report can be checked
            // against what actually fired instead of guessed at blind.
            DebugLog.Write($"action={action} pickerOpen={vm.IsCoverPickerOpen} ctxOpen={vm.IsContextMenuOpen} globalOpen={vm.IsGlobalMenuOpen} powerOpen={vm.IsPowerMenuOpen} downloadsOpen={vm.IsDownloadsOpen}");

            // Cover/banner picker takes over input first — it can be opened
            // from inside the per-tile context menu (which closes itself
            // before opening the picker), so checking it ahead of
            // IsContextMenuOpen below is just belt-and-suspenders; only one
            // of the three overlay bools is ever true at once in practice.
            if (vm.IsCoverPickerOpen)
            {
                switch (action)
                {
                    case GamepadAction.MoveLeft:
                        vm.MoveCoverPickerSelection(-1);
                        ScrollCoverPickerSelectionIntoView(vm);
                        break;
                    case GamepadAction.MoveRight:
                        vm.MoveCoverPickerSelection(1);
                        ScrollCoverPickerSelectionIntoView(vm);
                        break;
                    case GamepadAction.MoveUp:
                        vm.MoveCoverPickerSelectionVertical(-1);
                        ScrollCoverPickerSelectionIntoView(vm);
                        break;
                    case GamepadAction.MoveDown:
                        vm.MoveCoverPickerSelectionVertical(1);
                        ScrollCoverPickerSelectionIntoView(vm);
                        break;
                    case GamepadAction.Confirm:
                        vm.ConfirmCoverPickerSelection();
                        break;
                    case GamepadAction.Back:
                    case GamepadAction.Menu:
                    case GamepadAction.GlobalMenu:
                        vm.CloseCoverPicker();
                        break;
                }
                continue;
            }

            // While the Start-button context menu is open, every action
            // means something different (navigate/confirm/close THAT menu)
            // instead of the normal tile navigation — swallow it here so
            // e.g. Confirm doesn't also launch a game behind the menu.
            if (vm.IsContextMenuOpen)
            {
                switch (action)
                {
                    case GamepadAction.MoveUp:
                        vm.MoveContextMenuSelection(-1);
                        break;
                    case GamepadAction.MoveDown:
                        vm.MoveContextMenuSelection(1);
                        break;
                    case GamepadAction.Confirm:
                        vm.ConfirmContextMenuSelection();
                        break;
                    case GamepadAction.Back:
                    case GamepadAction.Menu:
                        vm.CloseContextMenu();
                        break;
                }
                continue;
            }

            // Same idea for the global menu (View/Back button, or the
            // gear icon in the top bar for mouse/touch — see
            // OnGearMenuClick) — this is what reaches "Atualizar
            // Biblioteca"/"Atualizar Capas Automático"/"Adicionar
            // jogo"/"Sair" without a mouse. Only reachable from the
            // plain-navigation branch below, so the menus below can never
            // both be open from gamepad input alone.
            if (vm.IsGlobalMenuOpen)
            {
                switch (action)
                {
                    case GamepadAction.MoveUp:
                        vm.MoveGlobalMenuSelection(-1);
                        break;
                    case GamepadAction.MoveDown:
                        vm.MoveGlobalMenuSelection(1);
                        break;
                    case GamepadAction.Confirm:
                        vm.ConfirmGlobalMenuSelection();
                        break;
                    case GamepadAction.Back:
                    case GamepadAction.GlobalMenu:
                        vm.CloseGlobalMenu();
                        break;
                }
                continue;
            }

            // The "Power" menu (bottom-bar button, mouse/touch-only for
            // now — no gamepad button opens it yet) gets the same
            // "swallow input while open" treatment as the other overlays,
            // for when it's navigated by keyboard/gamepad once already
            // open.
            if (vm.IsPowerMenuOpen)
            {
                switch (action)
                {
                    case GamepadAction.MoveUp:
                        vm.MovePowerMenuSelection(-1);
                        break;
                    case GamepadAction.MoveDown:
                        vm.MovePowerMenuSelection(1);
                        break;
                    case GamepadAction.Confirm:
                        vm.ConfirmPowerMenuSelection();
                        break;
                    case GamepadAction.Back:
                    case GamepadAction.Menu:
                    case GamepadAction.GlobalMenu:
                        vm.ClosePowerMenu();
                        break;
                }
                continue;
            }

            // Painel de Downloads — DECISÃO DE ESCOPO (v1): a lista em si
            // (downloads em andamento, jogos "na conta não instalado" e o
            // botão "Instalar" de cada um) é mouse/touch-only, diferente do
            // resto do app. Motivo: não é uma tela de jogo com grade fixa
            // como o CoverPicker, é uma lista de tamanho variável com
            // controles por item (botão "Instalar"), e isso pediria um
            // modelo de foco novo só pra essa tela. Por enquanto o gamepad
            // só fecha o painel (Back/Menu/GlobalMenu) — igual os outros
            // overlays — pra nunca deixar quem só tem controle PRESO aqui
            // sem conseguir sair. Revisitar se o recurso pegar.
            if (vm.IsDownloadsOpen)
            {
                switch (action)
                {
                    case GamepadAction.Back:
                    case GamepadAction.Menu:
                    case GamepadAction.GlobalMenu:
                    case GamepadAction.Downloads: // Y abre E fecha (toggle)
                        vm.CloseDownloads();
                        break;
                }
                continue;
            }

            switch (action)
            {
                case GamepadAction.Downloads:
                    vm.ToggleDownloads();
                    break;
                case GamepadAction.MoveLeft:
                    vm.MovePrevious();
                    ScrollGameRowSelectionIntoView(vm);
                    break;
                case GamepadAction.MoveRight:
                    vm.MoveNext();
                    ScrollGameRowSelectionIntoView(vm);
                    break;
                case GamepadAction.Confirm:
                    vm.LaunchSelected();
                    // LaunchSelected reorders the row (the game just
                    // launched becomes the wide tile 0) and resets
                    // SelectedIndex to 0 — if the row was scrolled right
                    // before this, keep that new tile actually on screen.
                    ScrollGameRowSelectionIntoView(vm);
                    break;
                case GamepadAction.Back:
                    vm.CloseRunningGame();
                    break;
                case GamepadAction.Menu:
                    vm.ToggleContextMenu();
                    break;
                case GamepadAction.GlobalMenu:
                    vm.ToggleGlobalMenu();
                    break;
                case GamepadAction.PreviousFilter:
                    vm.CyclePreviousFilter();
                    break;
                case GamepadAction.NextFilter:
                    vm.CycleNextFilter();
                    break;
            }
        }
    }

    // Keyboard nav stays as a fallback for dev-time testing on desktop —
    // the gamepad poller above is what actually matters on the handheld.
    // Space stands in for Start (per-tile menu), Tab stands in for
    // View/Back (global menu) — neither has a literal keyboard equivalent.
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (vm.IsCoverPickerOpen)
        {
            switch (e.Key)
            {
                case Key.Left:
                    vm.MoveCoverPickerSelection(-1);
                    ScrollCoverPickerSelectionIntoView(vm);
                    e.Handled = true;
                    break;
                case Key.Right:
                    vm.MoveCoverPickerSelection(1);
                    ScrollCoverPickerSelectionIntoView(vm);
                    e.Handled = true;
                    break;
                case Key.Up:
                    vm.MoveCoverPickerSelectionVertical(-1);
                    ScrollCoverPickerSelectionIntoView(vm);
                    e.Handled = true;
                    break;
                case Key.Down:
                    vm.MoveCoverPickerSelectionVertical(1);
                    ScrollCoverPickerSelectionIntoView(vm);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    vm.ConfirmCoverPickerSelection();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    vm.CloseCoverPicker();
                    e.Handled = true;
                    break;
            }
            return;
        }

        if (vm.IsContextMenuOpen)
        {
            switch (e.Key)
            {
                case Key.Up:
                    vm.MoveContextMenuSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Down:
                    vm.MoveContextMenuSelection(1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    vm.ConfirmContextMenuSelection();
                    e.Handled = true;
                    break;
                case Key.Escape:
                case Key.Space:
                    vm.CloseContextMenu();
                    e.Handled = true;
                    break;
            }
            return;
        }

        if (vm.IsGlobalMenuOpen)
        {
            switch (e.Key)
            {
                case Key.Up:
                    vm.MoveGlobalMenuSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Down:
                    vm.MoveGlobalMenuSelection(1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    vm.ConfirmGlobalMenuSelection();
                    e.Handled = true;
                    break;
                case Key.Escape:
                case Key.Tab:
                    vm.CloseGlobalMenu();
                    e.Handled = true;
                    break;
            }
            return;
        }

        if (vm.IsPowerMenuOpen)
        {
            switch (e.Key)
            {
                case Key.Up:
                    vm.MovePowerMenuSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Down:
                    vm.MovePowerMenuSelection(1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    vm.ConfirmPowerMenuSelection();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    vm.ClosePowerMenu();
                    e.Handled = true;
                    break;
            }
            return;
        }

        // Mesma decisão de escopo do OnGamepadTick: painel de Downloads é
        // mouse/touch-only para a lista em si, teclado/gamepad só fecha.
        if (vm.IsDownloadsOpen)
        {
            switch (e.Key)
            {
                case Key.Escape:
                case Key.Tab:
                case Key.D: // fallback de teclado do Y (toggle)
                    vm.CloseDownloads();
                    e.Handled = true;
                    break;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                vm.MovePrevious();
                ScrollGameRowSelectionIntoView(vm);
                e.Handled = true;
                break;
            case Key.Right:
                vm.MoveNext();
                ScrollGameRowSelectionIntoView(vm);
                e.Handled = true;
                break;
            case Key.Enter:
                vm.LaunchSelected();
                ScrollGameRowSelectionIntoView(vm);
                e.Handled = true;
                break;
            case Key.Escape:
                vm.CloseRunningGame();
                e.Handled = true;
                break;
            case Key.Space:
                vm.ToggleContextMenu();
                e.Handled = true;
                break;
            case Key.Tab:
                vm.ToggleGlobalMenu();
                e.Handled = true;
                break;
            // Q/E = fallback de teclado pra LB/RB (ciclar abas de filtro) —
            // dev-time, já que teclado não tem botões de ombro de verdade.
            case Key.Q:
                vm.CyclePreviousFilter();
                e.Handled = true;
                break;
            case Key.E:
                vm.CycleNextFilter();
                e.Handled = true;
                break;
            // D = fallback de teclado pro Y do controle (abrir Downloads).
            case Key.D:
                vm.ToggleDownloads();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// The cover/banner picker is a plain ItemsControl with hand-rolled
    /// IsSelected flags (not a ListBox), so there's no built-in "scroll
    /// selected item into view" tied to selection changes — has to be done
    /// by hand here after every move. Deliberately uses the well-known
    /// ScrollViewer.Offset/.Viewport properties rather than
    /// ItemsControl.ContainerFromIndex/Control.BringIntoView(), since this
    /// project can't be compiled here to verify a less-certain API choice.
    /// </summary>
    private void ScrollCoverPickerSelectionIntoView(MainViewModel vm)
    {
        if (CoverPickerScrollViewer is null) return;
        if (!vm.IsCoverPickerOpen || vm.CoverPickerCandidates.Count == 0) return;

        // Must match MainWindow.axaml's coverCandidate cell: Border
        // Height="170" plus Margin="6" on each side (top+bottom = 12).
        const double itemHeight = 170 + 12;
        var row = vm.CoverPickerIndex / MainViewModel.CoverPickerColumns;
        var itemTop = row * itemHeight;
        var itemBottom = itemTop + itemHeight;

        var viewTop = CoverPickerScrollViewer.Offset.Y;
        var viewBottom = viewTop + CoverPickerScrollViewer.Viewport.Height;

        if (itemTop < viewTop)
        {
            CoverPickerScrollViewer.Offset = new Vector(CoverPickerScrollViewer.Offset.X, itemTop);
        }
        else if (itemBottom > viewBottom)
        {
            CoverPickerScrollViewer.Offset = new Vector(CoverPickerScrollViewer.Offset.X, itemBottom - CoverPickerScrollViewer.Viewport.Height);
        }
    }

    /// <summary>
    /// Same bug/fix shape as ScrollCoverPickerSelectionIntoView above, for
    /// the main horizontal game row: a plain ItemsControl/StackPanel has no
    /// built-in "scroll the selected item into view" either, so moving
    /// left/right past whatever's currently visible did nothing (the
    /// reported "o scroll da tela de jogos está parado" bug). Horizontal
    /// instead of vertical, and the per-item width isn't fixed like the
    /// picker's grid cells — the "último jogo aberto" tile is wider
    /// (Border.gameTile.lastPlayed in Theme.axaml) and it's always
    /// whichever tile has IsLastPlayed=true, not necessarily index 0 (it
    /// usually is, since ReorderLastPlayedFirst moves it there, but this
    /// walks the actual flags rather than assuming that to stay correct
    /// either way). Tile widths/spacing below must match
    /// Theme.axaml's Border.gameTile (220) / .lastPlayed (580) and
    /// MainWindow.axaml's StackPanel Spacing="18" exactly.
    /// </summary>
    private void ScrollGameRowSelectionIntoView(MainViewModel vm)
    {
        if (GameRowScrollViewer is null) return;
        if (vm.Games.Count == 0 || vm.SelectedIndex < 0) return;

        const double normalWidth = 220;
        const double lastPlayedWidth = 580; // 620 → 580 nesta leva — ver Theme.axaml
        const double spacing = 18; // 14 → 18 nesta leva — junto com a largura menor acima, resolve a WIDE CAPSULE sobrepondo a tile vizinha

        double itemLeft = 0;
        for (var i = 0; i < vm.SelectedIndex; i++)
        {
            itemLeft += (vm.Games[i].IsLastPlayed ? lastPlayedWidth : normalWidth) + spacing;
        }

        var itemWidth = vm.Games[vm.SelectedIndex].IsLastPlayed ? lastPlayedWidth : normalWidth;
        var itemRight = itemLeft + itemWidth;

        var viewLeft = GameRowScrollViewer.Offset.X;
        var viewRight = viewLeft + GameRowScrollViewer.Viewport.Width;

        if (itemLeft < viewLeft)
        {
            GameRowScrollViewer.Offset = new Vector(itemLeft, GameRowScrollViewer.Offset.Y);
        }
        else if (itemRight > viewRight)
        {
            GameRowScrollViewer.Offset = new Vector(itemRight - GameRowScrollViewer.Viewport.Width, GameRowScrollViewer.Offset.Y);
        }
    }

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not Control { DataContext: GameTileViewModel tile }) return;

        var index = vm.Games.IndexOf(tile);
        if (index >= 0) vm.SelectedIndex = index;
    }

    private void OnTileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.LaunchSelected();
        ScrollGameRowSelectionIntoView(vm);
    }

    /// <summary>
    /// Gear icon in the top bar — opens the exact same menu as View/Back
    /// on the controller (vm.ToggleGlobalMenu()), not a second separate
    /// menu, so mouse/touch and controller always see the same options.
    /// </summary>
    private void OnGearMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.ToggleGlobalMenu();
    }

    /// <summary>
    /// Atalho de Downloads na barra de cima (novo numa leva posterior,
    /// pedido do usuário: "colocar um atalho lá em cima igual a Steam") —
    /// mesmo painel/toggle do item "Downloads" do menu geral, só um
    /// caminho mais rápido pra mouse/touch chegar lá.
    /// </summary>
    private void OnDownloadsShortcutClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.ToggleDownloads();
    }

    private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        MinimizeToTray();
    }

    private void OnPowerMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.TogglePowerMenu();
    }

    private void OnCloseGameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.CloseRunningGame();
    }

    /// <summary>Botão ✕ do painel de Downloads (mouse/touch — ver MainViewModel.IsDownloadsOpen).</summary>
    private void OnCloseDownloadsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.CloseDownloads();
    }

    /// <summary>
    /// Botão "Instalar" de um item de OwnedNotInstalledGames — o item
    /// dentro do ItemsControl não tem Command parametrizável (projeto não
    /// usa RelayCommand&lt;T&gt;, só o parameterless), então pega o
    /// OwnedSteamGame direto do DataContext do Button (que, dentro do
    /// DataTemplate, é o próprio item da lista) em vez disso.
    /// </summary>
    private void OnInstallOwnedGameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OwnedSteamGame game } && DataContext is MainViewModel vm)
        {
            vm.InstallOwnedGame(game);
        }
    }

    /// <summary>Called by the global menu's "Adicionar jogo" entry (vm.AddGameRequested, wired in OnDataContextChanged).</summary>
    private async Task OpenAddGameDialogAsync(MainViewModel vm)
    {
        var dialog = new AddGameWindow();
        var result = await dialog.ShowDialog<GameEntry?>(this);
        if (result is null) return;

        vm.AddManualGame(result);
    }

    /// <summary>
    /// Called by the global menu's "Configurações" entry
    /// (vm.SettingsRequested, wired in OnDataContextChanged). Mesmo padrão
    /// do OpenAddGameDialogAsync acima — diálogo modal separado em vez de
    /// overlay da MainWindow (ver AddGameWindow.axaml, que já deixava essa
    /// intenção registrada em comentário: "eventual tela de
    /// configurações territory"). O próprio SettingsWindow lê/grava
    /// settings.json via AppSettingsStore — a ViewModel fica burra, só
    /// com os campos de texto.
    /// </summary>
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>A janela em primeiro plano é a nossa? (trava do controle com jogo aberto)</summary>
    private bool IsOurWindowInForeground()
    {
        try
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            return handle != IntPtr.Zero && GetForegroundWindow() == handle;
        }
        catch
        {
            // Na dúvida, não bloqueia o controle.
            return true;
        }
    }

    private async Task OpenSettingsDialogAsync(MainViewModel vm)
    {
        var dialog = new SettingsWindow();
        dialog.ViewModel.VersionText = vm.AppVersionText;
        dialog.ViewModel.UpdatesAvailable = vm.UpdatesInstalled;
        dialog.CheckForUpdates = vm.CheckForUpdatesWithResultAsync;
        dialog.ViewModel.OverlayStatusText = vm.OverlayStatusText;
        await dialog.ShowDialog(this);

        // Preset do HUD vale na hora (o resto das Configurações ainda pede reinício).
        vm.ReloadOverlayPresetFromSettings();
    }
}
