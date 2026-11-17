using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using HandheldLauncher.Core.Services;
using HandheldLauncher.Core.Models;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Platform;
using HandheldLauncher.App.Input;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.App.Platform.Hardware; 
using HandheldLauncher.Services.HidHide;

namespace HandheldLauncher.App.Views;

// Parte do OverlayWindow (0.28): posição, abertura, fechamento e painel de Configurações rápidas.
// Movido sem alterar nenhuma linha de código; ver OverlayWindow.axaml.cs.
public partial class OverlayWindow
{
    public void ReapplyAfterSuspendResume() =>
        HandheldLauncher.App.Diagnostics.UiSafe.Run(() => ReapplyAfterSuspendResumeCore(), "ReapplyAfterSuspendResume");

    public async Task ReapplyAfterSuspendResumeCore()
    {
        try
        {
            var settings = AppSettingsStore.Load();

            if (_tdpHardwareSupported)
            {
                var savedTdp = string.IsNullOrWhiteSpace(settings.TdpMode) ? "Normal" : settings.TdpMode;
                DebugLog.Write($"[SUSPENSAO] Reaplicando TDP salvo: {savedTdp}.");
                var ok = _hardwareService.SetTdpMode(savedTdp);
                if (ok)
                {
                    DebugLog.Write($"[SUSPENSAO] TDP {savedTdp} reaplicado com sucesso.");
                    Dispatcher.UIThread.Post(() => _viewModel.UpdateHudTdpStatus(savedTdp));
                }
                else
                {
                    DebugLog.Write($"[SUSPENSAO] Falha ao reaplicar TDP {savedTdp} — nova tentativa em 5s.");
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    var retryOk = _hardwareService.SetTdpMode(savedTdp);
                    DebugLog.Write(retryOk
                        ? $"[SUSPENSAO] TDP {savedTdp} reaplicado na 2ª tentativa."
                        : $"[SUSPENSAO] TDP {savedTdp} NÃO reaplicou após a suspensão (2 tentativas).");
                    if (retryOk) Dispatcher.UIThread.Post(() => _viewModel.UpdateHudTdpStatus(savedTdp));
                }
            }

            if (OperatingSystem.IsWindows() && _fanCurveService != null && _currentFanMode != "Automático")
            {
                DebugLog.Write($"[SUSPENSAO] Reaplicando curva de ventoinha: {_currentFanMode}.");
                if (_fanCurveService.TryApplyPreset(_currentFanMode, out var fanMsg))
                    DebugLog.Write($"[SUSPENSAO] Ventoinha reaplicada: {fanMsg}.");
                else
                    DebugLog.Write($"[SUSPENSAO] Falha ao reaplicar ventoinha após a suspensão: {fanMsg}.");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[SUSPENSAO] EXCEPTION em ReapplyAfterSuspendResume: {ex.Message}");
        }
    }

    private async Task RetryStartupTdpAsync(string savedTdp)
    {
        if (Interlocked.Exchange(ref _startupTdpRetryRunning, 1) == 1) return;
        try
        {
            for (var attempt = 0; attempt < StartupTdpRetryDelays.Length; attempt++)
            {
                await Task.Delay(StartupTdpRetryDelays[attempt]);

                if (_startupTdpConfirmed)
                {
                    DebugLog.Write("[OVERLAY] TDP da inicialização já foi confirmado por outra aplicação — novas tentativas encerradas.");
                    return;
                }

                var current = AppSettingsStore.Load().TdpMode;
                if (string.IsNullOrWhiteSpace(current)) current = "Normal";
                if (!string.Equals(current, savedTdp, StringComparison.Ordinal))
                {
                    DebugLog.Write($"[OVERLAY] TDP salvo mudou para {current} durante a espera — tentativa de restaurar {savedTdp} cancelada.");
                    StartupRestoreStatus.Report("TDP", $"restauração de {savedTdp} cancelada (usuário escolheu {current})", ok: true);
                    return;
                }

                DebugLog.Write($"[OVERLAY] Nova tentativa {attempt + 2}/{StartupTdpRetryDelays.Length + 1} de restaurar o TDP {savedTdp}.");
                if (_hardwareService.SetTdpMode(savedTdp))
                {
                    _startupTdpConfirmed = true;
                    DebugLog.Write($"[OVERLAY] TDP {savedTdp} confirmado na tentativa {attempt + 2}.");
                    StartupRestoreStatus.Report("TDP", $"{savedTdp} confirmado na {attempt + 2}ª tentativa", ok: true);
                    Dispatcher.UIThread.Post(() => _viewModel.UpdateHudTdpStatus(savedTdp));
                    return;
                }
            }

            DebugLog.Write($"[OVERLAY] TDP {savedTdp} não confirmou em {StartupTdpRetryDelays.Length + 1} tentativas — o firmware pode estar sobrescrevendo os limites.");
            StartupRestoreStatus.Report("TDP", $"{savedTdp} NÃO confirmou em {StartupTdpRetryDelays.Length + 1} tentativas", ok: false);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[OVERLAY] Erro nas novas tentativas de TDP: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _startupTdpRetryRunning, 0);
        }
    }

    private bool _snapBackWired;

    private void LogGameStateAfterOpen(IntPtr gameHwnd, string when)
    {
        try
        {
            if (!IsVisible) return;
            var fg = GetForegroundWindow();
            var mine = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            var who = fg == gameHwnd ? "jogo" : fg == mine ? "QS" : fg == IntPtr.Zero ? "ninguém" : $"outra (0x{fg:X})";
            DebugLog.Write($"[QS] Jogo {when} após abrir: minimizado={IsIconic(gameHwnd)}, visível={IsWindowVisible(gameHwnd)}, em foco={who}, posição do QS={Position.X},{Position.Y}.");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[QS] Estado do jogo após abrir: falhou ({ex.GetType().Name}).");
        }
    }

    public void SetGamePid(int pid)
    {
        _gamePid = pid;
        DebugLog.Write($"[OVERLAY] GamePid registrado: {pid}");
    }

     protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        DebugLog.Write($"[OVERLAY] OnOpened chamado. Width={Width}, Height={Height}");

        // v0.25.9: o QS reabriu antes do reinício do controle do fechamento
        // anterior rodar — não re-enumerar o controle com o painel aberto.
        CancelPendingControllerRestart("Quick Settings reaberto");

        // 🟢 INTEGRAÇÃO HIDHIDE (B7): Esconde o controle apenas se o toggle estiver ligado
        var overlaySettings = AppSettingsStore.Load();
        if (_gamePid > 0 && overlaySettings.IsolateControllerOnOverlay)
        {
            _hidHideService.HideControllerForProcess(_gamePid);
        }

        // v0.24.1: congelamento opt-in (aviso de risco aceito nas Configurações).
        // É o único mecanismo que tira o input a um jogo JÁ ABERTO — o HidHide
        // decide quem vê o dispositivo no momento em que a app o abre.
        if (_gamePid > 0 && AppSettingsStore.Load().FreezeGameOnOverlay)
        {
            HandheldLauncher.App.Platform.InputBlockService.BlockGamepadForGame(_gamePid);
            // v0.25.1: prova no log de que o caminho do congelamento foi tomado
            // (o teste de 09/11 nunca chegou a mostrar isto com o jogo aberto).
            DebugLog.Write($"[INPUT] Congelamento pedido para o pid {_gamePid} (ver [INPUT] Bloqueio…).");
        }

        // 0.24.1: limitação conhecida — em ecrã inteiro o painel pode deixar o
        // jogo preto. Deteta (só leitura) e avisa no painel.
        var (windowMode, fullscreenDetail) = HandheldLauncher.App.Platform.FullscreenDetector.Detect(_gameHwnd);
        DebugLog.Write($"[QS] janela do jogo: {fullscreenDetail}");
        if (HandheldLauncher.Core.Services.FullscreenHeuristic.WarningFor(windowMode) is { } fullscreenWarning)
        {
            TxtFullscreenWarning.Text = fullscreenWarning;
            FullscreenWarning.IsVisible = true;
        }
        else
        {
            FullscreenWarning.IsVisible = false;
        }

        // QS 2.0: mostrar o painel novo (por cima do antigo) e ligar os eventos.
        WireNewQsPanel();
        RefreshNewQsPanel();

        // Cabe no ecrã atual (altura e posição) — inclui o caso de ter mudado
        // de resolução desde a última abertura.
        FitToScreen();

        if (OperatingSystem.IsLinux() && !LinuxSemFoco)
        {
            // Linux/GNOME: sem isto o QS abre ATRÁS do jogo (só aparece a notificação "está pronto").
            HandheldLauncher.App.Platform.LinuxWindowActivator.RaiseWithRetry(
                () => TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, () => IsVisible);
        }

        // v0.25.10: posiciona à direita ANTES de pegar o foco, e volta na hora se algo
        // de fora mover a janela (o vigia de 1 s deixava o QS "segundos" no centro).
        PositionOnRightEdge();
        ResetPositionGuardForShow();
        if (OperatingSystem.IsWindows() && !_snapBackWired)
        {
            _snapBackWired = true;
            PositionChanged += (_, _) => Dispatcher.UIThread.Post(GuardPosition, DispatcherPriority.Background);
        }

        if (OperatingSystem.IsWindows())
        {
            var h = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (h != IntPtr.Zero)
            {
                var ex = GetWindowLong(h, GWL_EXSTYLE);
                SetWindowLong(h, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

                // v0.25.9: tirar o foco do jogo para ele deixar de receber XInput
                // (padrão desde 11/11). O FullscreenDetector não distingue
                // borderless "popup" de exclusivo (Days Gone aparece como
                // FullscreenLikely), por isso vale para todos os modos — o fecho
                // já restaura o jogo se ele tiver minimizado. Em tela cheia o
                // Windows mostra um fundo preto atrás do painel (aceito).
                if (_gamePid > 0 && overlaySettings.OverlayTakesFocus)
                {
                    SetWindowLong(h, GWL_EXSTYLE, (ex | WS_EX_TOOLWINDOW) & ~WS_EX_NOACTIVATE);
                    // ALT descido e solto ANTES da troca: a liberação não pode ir
                    // para o painel, senão o jogo fica a ver o ALT premido.
                    keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
                    keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    SetForegroundWindow(h);
                    DebugLog.Write($"[QS] Foco pego pelo Quick Settings (modo={windowMode}, foreground agora=0x{GetForegroundWindow():X}, painel=0x{h:X}).");
                }
                else if (_gamePid > 0)
                {
                    DebugLog.Write($"[QS] Modo sem foco: o jogo continua em foco (modo={windowMode}).");
                }

                // v0.25.10: diagnóstico — o que o jogo fez depois que o QS abriu
                // (minimizou? sumiu? perdeu o foco?), sem adivinhar pelo relato.
                if (_gamePid > 0 && _gameHwnd != IntPtr.Zero)
                {
                    var gameHwnd = _gameHwnd;
                    DispatcherTimer.RunOnce(() => LogGameStateAfterOpen(gameHwnd, "0,5 s"), TimeSpan.FromMilliseconds(500));
                    DispatcherTimer.RunOnce(() => LogGameStateAfterOpen(gameHwnd, "2 s"), TimeSpan.FromSeconds(2));
                }
            }
        }

        OnOverlayVisibilityChanged?.Invoke(true);

        PositionOnRightEdge();
        ResetPositionGuardForShow();
        StartTopmostEnforcer();
        UpdateLsfgStatus();

        Dispatcher.UIThread.Post(() =>
        {
            ForceTopmost();
        }, DispatcherPriority.Loaded);
    }
    private void UpdateLsfgStatus()
    {
        if (LsfgStatusText == null) return;

        if (OperatingSystem.IsLinux())
        {
            // Linux: o frame-gen é o LSFG-VK (camada Vulkan), não um app a correr.
            LsfgStatusText.Text = LinuxLosslessService.GetStatus();
            return;
        }

        LsfgStatusText.Text = LosslessScalingService.IsLosslessRunning()
            ? "Lossless pronto"
            : "Lossless não está rodando";
    }

    public void PositionOnRightEdge()
    {
        try
        {
            var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
            if (screen == null) return;

            var wa = LinuxRealWorkArea(screen.WorkingArea);
            var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;

            double actualWidth = (Width > 0 && !double.IsNaN(Width)) ? Width : (Bounds.Width > 0 ? Bounds.Width : 440);
            double actualHeight = (Height > 0 && !double.IsNaN(Height)) ? Height : (Bounds.Height > 0 ? Bounds.Height : 850);

            var widthPx = (int)(actualWidth * scaling);
            var heightPx = (int)(actualHeight * scaling);

            if (widthPx <= 0 || heightPx <= 0) return;

            var x = wa.X + wa.Width - widthPx - 20;
            var y = wa.Y + (wa.Height - heightPx) / 2;

            if (OperatingSystem.IsWindows())
            {
                var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (handle != IntPtr.Zero)
                {
                    Position = new PixelPoint(x, y);
                    SetWindowPos(handle, HWND_TOPMOST, x, y, widthPx, heightPx, SWP_NOACTIVATE);
                    DebugLog.Write($"[OVERLAY] Reposicionado sem ocultar: {x},{y}");
                }
            }
            else
            {
                Position = new PixelPoint(x, y);
            }
            
            RememberExpectedPosition(new PixelPoint(x, y));
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[OVERLAY] PositionOnRightEdge EXCEPTION: {ex.Message}");
        }
    }

    /// <summary>
    /// QS 2.0 — garante que o painel cabe no ecrã atual. Chamado ao abrir e
    /// depois de mudar de resolução: antes ficava ancorado às coordenadas da
    /// resolução anterior e aparecia cortado fora da tela (bug de 09/11).
    /// </summary>
    public void FitToScreen()
    {
        try
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary ?? Screens.All.FirstOrDefault();
            if (screen == null) return;

            var wa = LinuxRealWorkArea(screen.WorkingArea);
            var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;

            // 850 é a altura de desenho; nunca pode exceder o ecrã disponível,
            // senão o rodapé e o fim do scroll ficam fora de alcance.
            var maxLogicalHeight = (wa.Height / scaling) - 20;
            Height = Math.Max(480, Math.Min(850, maxLogicalHeight));

            PositionOnRightEdge();
            DebugLog.Write($"[QS] Ajustado ao ecrã: {wa.Width}x{wa.Height} (escala {scaling:0.##}) → altura {Height:0}.");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[QS] FitToScreen EXCEPTION: {ex.Message}");
        }
    }

    /// <summary>
    /// Linux/X11: depois de um xrandr o Avalonia continua a reportar a área de trabalho antiga
    /// (o QS ficava fora da tela). Usa o tamanho real do X quando difere.
    /// </summary>
    private static PixelRect LinuxRealWorkArea(PixelRect avaloniaArea)
    {
        if (!OperatingSystem.IsLinux()) return avaloniaArea;
        var real = HandheldLauncher.App.Platform.LinuxWindowActivator.GetScreenSize();
        if (real is null || real.Value.W <= 0 || real.Value.H <= 0) return avaloniaArea;
        if (real.Value.W == avaloniaArea.X + avaloniaArea.Width && real.Value.H >= avaloniaArea.Y + avaloniaArea.Height) return avaloniaArea;
        const int topBar = 40; // barra do GNOME
        DebugLog.Write($"[TELA] Linux: tamanho real {real.Value.W}x{real.Value.H} difere do Avalonia ({avaloniaArea.Width}x{avaloniaArea.Height}) — usando o real.");
        return new PixelRect(0, topBar, real.Value.W, real.Value.H - topBar);
    }

    public void SetInitialPosition()
    {
        try
        {
            var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
            if (screen == null) return;

            var wa = LinuxRealWorkArea(screen.WorkingArea);
            var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
            
            double w = (Width > 0 && !double.IsNaN(Width)) ? Width : 440;
            double h = (Height > 0 && !double.IsNaN(Height)) ? Height : 850;

            var widthPx = (int)(w * scaling);
            var heightPx = (int)(h * scaling);

            var x = wa.X + wa.Width - widthPx - 20;
            var y = wa.Y + (wa.Height - heightPx) / 2;

            Position = new PixelPoint(x, y);
            DebugLog.Write($"[OVERLAY] SetInitialPosition: {x},{y} (tamanho {widthPx}x{heightPx})");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[OVERLAY] SetInitialPosition EXCEPTION: {ex.Message}");
        }
    }

     public void HideWithUnblock()
    {
        DebugLog.Write($"[OVERLAY-CLOSE] origem=HideWithUnblock fullscreen-contexto; overlay={TryGetPlatformHandle()?.Handle}, game={_gameHwnd}, foreground={(OperatingSystem.IsWindows() ? GetForegroundWindow() : IntPtr.Zero)}, visible={IsVisible}, submenu={_activeSubMenu}");
        
        if (_activeSubMenu == SubMenuMode.TdpCustom) CancelTdpCustomEdit();
        try { NewQsPanel.CancelEdits(); } catch (Exception ex) { DebugLog.Swallowed(ex); }
        FlushPendingCustomApply();
        
        StopTopmostEnforcer();
             
        Hide();

        // 🟢 INTEGRAÇÃO HIDHIDE (B7): Devolve o controle para o jogo
        if (_gamePid > 0 && AppSettingsStore.Load().IsolateControllerOnOverlay)
        {
            _hidHideService.RestoreControllerForProcess(_gamePid);

            // v0.25.3: o HidHide devolve o dispositivo à lista, mas alguns jogos
            // (Days Gone) não voltam a abri-lo. Reiniciar o gamepad no Windows
            // força a re-detecção; é o mesmo controle, só re-enumerado.
            // v0.25.9: pode ser desligado nas Configurações, é cancelado se o
            // QS reabrir antes de rodar e nunca roda com o painel visível.
            CancelPendingControllerRestart("novo fechamento");
            if (AppSettingsStore.Load().RestartControllerAfterOverlay)
            {
                _pendingControllerRestart = DispatcherTimer.RunOnce(() =>
                {
                    _pendingControllerRestart = null;
                    if (IsVisible)
                    {
                        DebugLog.Write("[HIDHIDE] Reinício do controle PULADO: o Quick Settings está aberto.");
                        return;
                    }
                    if (ElevationService.IsElevated) LastControllerRestartUtc = DateTime.UtcNow;
                    DebugLog.Write("[HIDHIDE] Reiniciando o controle (fechamento do Quick Settings).");
                    _hidHideService.ForceRestartController();
                }, TimeSpan.FromMilliseconds(800));
            }
            else
            {
                DebugLog.Write("[HIDHIDE] Reinício do controle ao fechar o QS desligado nas Configurações.");
            }
        }

        // v0.24.1: retomar o jogo se ele chegou a ser congelado (é idempotente:
        // sem nada suspenso só garante que o marcador de recuperação sumiu).
        if (AppSettingsStore.Load().FreezeGameOnOverlay)
        {
            HandheldLauncher.App.Platform.InputBlockService.UnblockGamepadForGame();
        }

        OnOverlayVisibilityChanged?.Invoke(false);

        // v0.25.1: devolver o FOCO. O Quick Settings é NOACTIVATE; ao esconder-se
        // o Windows fica sem janela em foreground (log de 09/11: foreground=0) e
        // nem o jogo nem o launcher voltam a receber input — daí "o QS fecha e
        // os controles não voltam".
        RestoreForegroundAfterClose();
    }

    /// <summary>
    /// QS 2.0 — liga o painel novo aos handlers que JÁ existem. Nada é
    /// reescrito: cada evento leva o Button com o Tag no formato antigo
    /// ("Eco", "1600x900", "Automático", "1") e o handler antigo faz o resto.
    /// </summary>
    private void WireNewQsPanel()
    {
        if (_qsWired) return;
        _qsWired = true;

        NewQsPanel.TdpWattsRequested += (_, watts) => ApplyTdpWattsFromQs(watts);

        // v0.25.3: HUD escolhido no QS 2.0 — mesma rota do painel antigo.
        NewQsPanel.HudPresetRequested += (_, preset) =>
        {
            _currentHudIndex = Math.Clamp(preset, 0, PerformanceHud.PresetNames.Length - 1);
            _viewModel.SetOverlayPreset(_currentHudIndex);
            SetHudButtonVisual(_currentHudIndex);
            NewQsPanel.SetHud(_currentHudIndex);
            DebugLog.Write($"[HUD] Preset escolhido no QS: '{PerformanceHud.PresetNames[_currentHudIndex]}'.");
        };
        NewQsPanel.ResolutionRequested += (_, button) => ApplyResolutionFromQs(button.Tag?.ToString() ?? string.Empty);
        NewQsPanel.FanRequested += (_, button) => OnFanClick(button, new RoutedEventArgs());
        NewQsPanel.ColorRequested += (_, button) => OnColorClick(button, new RoutedEventArgs());
        NewQsPanel.MultiplierRequested += (_, button) => OnMultiplierClick(button, new RoutedEventArgs());
        NewQsPanel.PrintRequested += (_, _) => OnScreenshotClick(this, new RoutedEventArgs());
        NewQsPanel.RecordRequested += (_, _) => OnRecordClick(this, new RoutedEventArgs());
        NewQsPanel.LosslessToggled += (_, _) => OnLsfgToggleClick(this, new RoutedEventArgs());
        NewQsPanel.PowerPlanRequested += (_, guid) =>
        {
            if (PowerPlanService.SetActivePlan(guid))
            {
                NewQsPanel.SelectPowerPlan(guid);
                NewQsPanel.SetPowerPlanStatus(string.Empty);
                DebugLog.Write($"[PLANO] Seleção do Quick Settings aplicada: {guid}.");
            }
            else
                NewQsPanel.SetPowerPlanStatus("Não foi possível aplicar esse plano de energia.");
        };
        NewQsPanel.CloseRequested += (_, _) =>
        {
            DebugLog.Write("[QS] Fecho pedido pelo painel 2.0 (✕ / B / Fechar Menu).");
            HideWithUnblock();
        };

        // Engrenagem do rodapé: fecha o painel e abre as Configurações.
        NewQsPanel.SettingsRequested += (_, _) =>
        {
            DebugLog.Write("[QS] Configurações pedidas pelo painel 2.0.");
            HideWithUnblock();
            _viewModel.RequestSettings();
        };

        NewQsPanel.BrightnessChanged += (_, value) =>
        {
            _hardwareService?.SetBrightness(Math.Round((double)value));
            SaveSetting("Brightness", value.ToString());
        };

        // Perfil real do aparelho: é isto que faz o Legion mostrar 1200p e o ROG
        // Ally ficar nos 1080p (ScreenPresets, com testes no Core).
        var profile = HandheldLauncher.App.Platform.Hardware.DeviceDetector.Detect();
        var currentPreset = _currentTdp switch
        {
            "Eco" => PowerPreset.Eco,
            "Desempenho" => PowerPreset.Desempenho,
            _ when _currentTdp.Contains("Custom", StringComparison.OrdinalIgnoreCase) => PowerPreset.Custom,
            _ => PowerPreset.Normal,
        };

        // Rodapé do painel mostra a bateria (o QsPanel não usa DataContext).
        NewQsPanel.DataContext = _viewModel;

        NewQsPanel.Initialize(
            deviceId: profile.Id,
            minWatts: profile.TdpMin,
            maxWatts: Math.Max(profile.TdpMax, profile.TdpMaxAc),
            currentWatts: _customWatts,
            fanModes: ["Automático", "Silencioso", "Normal", "Desempenho", "Boost"],
            colorModes: ["Padrão", "Vívido", "OLED", "Noturno"],
            currentFan: _currentFanMode,
            currentColor: _currentColor,
            currentMultiplier: _currentMultiplierIndex,
            currentPreset: currentPreset);

        NewQsPanel.IsVisible = true;
        RefreshNewQsPanel();

        DebugLog.Write("[QS] Painel 2.0 ligado ao painel antigo (ponte).");
    }

    /// <summary>Refresca o estado visível do painel novo a cada abertura.</summary>
    private void RefreshNewQsPanel()
    {
        NewQsPanel.SetBrightness((int)Math.Round(BrightnessSlider?.Value ?? 70));
        NewQsPanel.SetScreenInfo(TxtScreenInfo?.Text ?? "—");
        NewQsPanel.SetBattery(_viewModel.BatteryStatusText);
        NewQsPanel.SetHud(_currentHudIndex);
        NewQsPanel.RunningGameProvider ??= () => (_viewModel.RunningGame?.Id, _viewModel.RunningGame?.Title);
        NewQsPanel.SetRunningGame(_viewModel.RunningGame?.Id, _viewModel.RunningGame?.Title); // meta de FPS por jogo
        NewQsPanel.RefreshSmartTdp(); // a opção pode ter mudado nas Configurações
        NewQsPanel.InitializePowerPlans(PowerPlanService.GetAvailablePlans());
        // v0.25.3: o aviso "o TDP não aplica" era mostrado SEMPRE que o launcher
        // não estava elevado — mesmo com o PawnIO a aplicar TDP sem admin. Agora
        // só aparece quando uma tentativa real falhar (ver ApplyTdpWattsFromQs).
        NewQsPanel.HideElevationWarning();
    }

    /// <summary>
    /// <summary>Linux: janela X11 que estava ativa (jogo ou launcher) antes do QS abrir.</summary>
    private IntPtr _linuxPrevActive;
    /// <summary>Linux experimental: QS não pede foco (ver AppSettings.LinuxQsSemFoco).</summary>
    public bool LinuxSemFoco { get; set; }
    public void SetLinuxPreviousActive(IntPtr xid) => _linuxPrevActive = xid;

    private void RestoreForegroundAfterClose()
    {
        if (OperatingSystem.IsLinux())
        {
            var prev = _linuxPrevActive;
            _linuxPrevActive = IntPtr.Zero;
            // A janela do próprio launcher não precisa de "devolução": a Home se reativa sozinha, e
            // um pedido atrasado desminimizava o launcher DEPOIS de abrir um jogo.
            var mainXid = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (prev == mainXid) prev = IntPtr.Zero;
            if (prev == IntPtr.Zero) HandheldLauncher.App.Platform.LinuxWindowActivator.CancelPending();
            if (prev != IntPtr.Zero)
            {
                DebugLog.Write($"[X11] Devolvendo o foco à janela 0x{prev.ToInt64():x} (ativa antes do QS).");
                HandheldLauncher.App.Platform.LinuxWindowActivator.RaiseWithRetry(() => prev, () => !IsVisible, above: false, delaysMs: new[] { 250, 600, 1100, 1800 }, logState: true);
            }
            return;
        }
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var target = _gameHwnd;
            var targetIsGame = target != IntPtr.Zero;

            // Nalguns fechos o handle do jogo vem a 0x0 (a janela dele foi
            // recriada ou ainda não estava pronta) — procurar pelo PID.
            if (target == IntPtr.Zero && _gamePid > 0)
            {
                try
                {
                    using var process = System.Diagnostics.Process.GetProcessById(_gamePid);
                    target = process.MainWindowHandle;
                    targetIsGame = target != IntPtr.Zero;
                }
                catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); }
            }

            // Sem jogo (ex.: QS aberto na tela inicial): devolver ao launcher.
            if (target == IntPtr.Zero && Owner is Window owner)
            {
                owner.Activate();
                target = owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            }

            if (target != IntPtr.Zero)
            {
                // v0.25.9: jogo em fullscreen exclusivo se minimiza quando perde o
                // foco com o QS por cima (log de 11/11: janela 160x28, foreground=0).
                // Sem restaurar, o SetForegroundWindow não tem efeito e fica a tela cinza.
                // Só a janela do jogo: o launcher minimizado não pode saltar
                // por cima do jogo (revisão 11/11).
                if (targetIsGame && IsMinimizedWindow(target))
                {
                    DebugLog.Write("[OVERLAY-CLOSE] Janela do jogo minimizada — restaurando.");
                    ShowWindow(target, SwRestore);
                }

                // v0.25.2: ALT "premido" durante a troca — sem isto o Windows
                // ignorava o SetForegroundWindow (foreground agora=0x0).
                keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
                SetForegroundWindow(target);
                keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }

            DebugLog.Write(
                $"[OVERLAY-CLOSE] foco devolvido para 0x{target:X} (jogo={_gameHwnd != IntPtr.Zero}, foreground agora=0x{GetForegroundWindow():X}).");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[OVERLAY-CLOSE] Falha ao devolver o foco: {ex.GetType().Name}: {ex.Message}");
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!IsAppClosing) { e.Cancel = true; HideWithUnblock(); }
        base.OnClosing(e);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => HideWithUnblock();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible && BtnCloseXCloud is not null)
            BtnCloseXCloud.IsVisible = XCloudLauncher.IsRunning;
    }

    private void OnCloseXCloudClick(object? sender, RoutedEventArgs e) => CloseXCloudFromOverlay();

    private void CloseXCloudFromOverlay()
    {
        DebugLog.Write("[XCLOUD] Fechar pedido pelo Quick Settings.");
        HideWithUnblock();
        _ = Task.Run(XCloudLauncher.CloseAll);
    }
}
