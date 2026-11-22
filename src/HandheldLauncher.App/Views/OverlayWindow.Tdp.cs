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

// Parte do OverlayWindow (0.28): resolução, taxa de atualização, TDP e energia.
// Movido sem alterar nenhuma linha de código; ver OverlayWindow.axaml.cs.
public partial class OverlayWindow
{
    private void OnResolutionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            for (int i = 0; i < _resolutionButtons.Length; i++) if (_resolutionButtons[i] == btn) { _currentResIndex = i; break; }
            ApplyDisplayMode(); UpdatePersistentVisuals();
        }
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            for (int i = 0; i < _refreshButtons.Length; i++) if (_refreshButtons[i] == btn) { _currentHzIndex = i; break; }
            ApplyDisplayMode(); UpdatePersistentVisuals();
        }
    }

    private void OnTdpClick(object? sender, RoutedEventArgs e)
    {
        if (!_tdpHardwareSupported) return;
        if (sender is not Button clickedButton) return;
        var mode = clickedButton.Tag?.ToString() ?? "Normal";
        if (_activeSubMenu == SubMenuMode.TdpCustom) { CancelTdpCustomEdit(); UpdateFocusVisual(); }
        if (mode == "Custom")
            mode = TdpProfiles.CustomMode(_customApplyTimer?.IsEnabled == true ? _pendingCustomWatts : _customWatts);
        ApplyTdp(mode);
    }

    private void FlushPendingCustomApply()
    {
        if (_customApplyTimer?.IsEnabled != true) return;
        OnCustomApplyTick(_customApplyTimer, EventArgs.Empty);
    }

    private bool ApplyTdp(string mode)
    {
        _customApplyTimer?.Stop();
        if (!_tdpHardwareSupported) return false;

        // O TDP inteligente, ao sair, não pode repor o perfil antigo por cima deste pedido
        // (o perfil novo só é gravado depois que o hardware confirma, segundos depois).
        GameSessionInfo.NoteUserTdpRequest();

        _ = Task.Run(() =>
        {
            bool success = false;
            lock (WindowsHardwareService.TdpApplyLock)
            {
                success = _hardwareService.SetTdpMode(mode);
            }

            if (!success && !ElevationService.IsElevated && OperatingSystem.IsWindows())
            {
                // v0.25.5 (Camada 2): sem admin a escrita do PawnIO falha — delega
                // ao processo elevado (UAC uma vez por alteração).
                DebugLog.Write($"[TDP] Escrita direta falhou (sem admin) — delegando ao processo elevado: {mode}.");
                success = ElevationService.RunElevated("tdp-apply", "--mode", mode);
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (!success)
                {
                    SetTdpButtonVisual(_currentTdp);
                    return;
                }

                SetTdpButtonVisual(mode);
                if (TdpProfiles.IsCustom(mode)) _customWatts = TdpProfiles.WattsFor(mode) ?? _customWatts;
                
                try
                {
                    var settings = AppSettingsStore.Load();
                    settings.TdpMode = mode;
                    if (TdpProfiles.IsCustom(mode)) settings.TdpCustomWatts = _customWatts;
                    AppSettingsStore.Save(settings);
                }
                catch (Exception ex) { DebugLog.Write($"[OVERLAY] Erro ao salvar TDP {mode}: {ex.Message}"); }
                
                _viewModel.UpdateHudTdpStatus(mode);
                ApplyFanMode(TdpProfiles.LinkedFanMode(mode));
            });
        });

        return true;
    }

    /// <summary>
    /// QS 2.0 — aplica watts vindos do painel (presets 8/15/20 W ou o slider
    /// Custom). O valor é **sempre** limitado à janela do firmware
    /// (`TdpLimits.ClampSpl`) antes de chegar ao EC: é esta linha que impede o
    /// Custom de pedir mais do que o aparelho aguenta.
    /// </summary>
    public void ApplyTdpWattsFromQs(int watts)
    {
        if (!_tdpHardwareSupported)
        {
            DebugLog.Write("[TDP] QS 2.0: hardware sem suporte a TDP — pedido ignorado.");
            return;
        }

        var clamped = _tdpLimits.ClampSpl(watts);
        _customWatts = clamped;
        ApplyTdp(TdpProfiles.CustomMode(clamped));

        DebugLog.Write($"[TDP] QS 2.0: pedido {watts} W → {clamped} W (janela {_tdpLimits.SplMin}–{_tdpLimits.SplMax} W).");
    }

    /// <summary>
    /// QS 2.0 — resolução pedida diretamente (Tag "WxH"), mantendo a taxa atual.
    /// O handler antigo só reconhecia os botões do painel antigo, por isso a
    /// mudança não surtia efeito (bug reportado a 09/11).
    /// </summary>
    public void ApplyResolutionFromQs(string wxh)
    {
        var parts = (wxh ?? string.Empty).Split('x');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var w) || !int.TryParse(parts[1], out var h))
        {
            DebugLog.Write($"[TELA] QS 2.0: resolução inválida '{wxh}'.");
            return;
        }

        if (!int.TryParse(_refreshButtons[_currentHzIndex].Tag?.ToString(), out var hz)) return;

        _hardwareService.SetDisplayMode(w, h, hz);
        UpdateScreenInfoText();

        // A resolução mudou: o Windows leva ~1 s a aplicar o modo. Só depois
        // faz sentido recalcular a posição/tamanho do painel.
        if (OperatingSystem.IsWindows())
            DispatcherTimer.RunOnce(() => DisplayColorManager.ReapplyCurrent(), TimeSpan.FromMilliseconds(1500));

        DispatcherTimer.RunOnce(() =>
        {
            UpdateScreenInfoText();
            FitToScreen();
            if (OperatingSystem.IsLinux()) RefitMainWindowLinux();
        }, TimeSpan.FromMilliseconds(1200));

        DebugLog.Write($"[TELA] QS 2.0: {w}x{h}@{hz}Hz aplicado.");
    }

    /// <summary>Linux: depois de trocar a resolução, a tela cheia da Home fica com o tamanho antigo — refaz.</summary>
    private static void RefitMainWindowLinux()
    {
        try
        {
            var main = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (main is null || main.WindowState != WindowState.FullScreen) return;
            main.WindowState = WindowState.Normal;
            DispatcherTimer.RunOnce(() => main.WindowState = WindowState.FullScreen, TimeSpan.FromMilliseconds(250));
            DebugLog.Write("[TELA] Linux: Home reajustada à nova resolução.");
        }
        catch (Exception ex) { DebugLog.Write($"[TELA] Linux: falha ao reajustar a Home: {ex.Message}"); }
    }

    private void SetTdpButtonVisual(string activeMode)
    {
        if (BtnUltraEco != null) BtnUltraEco.Background = activeMode == "UltraEco" ? _activeColor : _inactiveColor;
        if (BtnEco != null) BtnEco.Background = activeMode == "Eco" ? _activeColor : _inactiveColor;
        if (BtnNormal != null) BtnNormal.Background = activeMode == "Normal" ? _activeColor : _inactiveColor;
        if (BtnDesempenho != null) BtnDesempenho.Background = activeMode == "Desempenho" ? _activeColor : _inactiveColor;
        if (BtnBoost != null) BtnBoost.Background = activeMode == "Boost" ? _activeColor : _inactiveColor;
        var isCustom = TdpProfiles.IsCustom(activeMode);
        if (BtnTdpCustom != null) BtnTdpCustom.Background = isCustom ? _activeColor : _inactiveColor;
        if (TdpCustomButtonText != null) TdpCustomButtonText.Text = isCustom ? $"{TdpProfiles.WattsFor(activeMode)} W" : "Person.";
        if (TdpCustomPanel != null)
        {
            TdpCustomPanel.IsVisible = isCustom;
            TdpCustomPanel.BorderBrush = Brushes.Transparent;
        }
        if (TdpCustomHint != null) TdpCustomHint.Text = DefaultCustomHint();
        if (isCustom) ShowCustomWatts(TdpProfiles.WattsFor(activeMode) ?? _customWatts);
        _currentTdp = activeMode;
    }

    private void ShowCustomWatts(int watts)
    {
        watts = TdpProfiles.ClampWatts(watts);
        if (TdpCustomValueText != null) TdpCustomValueText.Text = $"{watts} W";
        var (spl, sppt, fppt) = _tdpLimits.PeaksFor(watts);
        if (TdpCustomPeaksText != null)
        {
            var source = _tdpLimits.OnAc ? "tomada" : "bateria";
            TdpCustomPeaksText.Text = spl > watts
                ? $"Mínimo do aparelho é {_tdpLimits.SplMin} W → aplicado {spl} / {sppt} / {fppt} W (SPL / SPPT / FPPT)"
                : spl < watts
                    ? $"Na {source} o limite é {_tdpLimits.SplMax} W → aplicado {spl} / {sppt} / {fppt} W (SPL / SPPT / FPPT)"
                    : $"SPL {spl} W · picos SPPT {sppt} W / FPPT {fppt} W";
        }
        var sliderValue = _tdpLimits.ClampSpl(watts);
        if (TdpCustomSlider != null && (int)Math.Round(TdpCustomSlider.Value) != sliderValue)
        {
            _suppressCustomSlider = true;
            try { TdpCustomSlider.Value = sliderValue; }
            finally { _suppressCustomSlider = false; }
        }
    }

    private void UpdateTdpRangeUi()
    {
        if (TdpCustomSlider != null)
        {
            _suppressCustomSlider = true;
            try
            {
                TdpCustomSlider.Minimum = _tdpLimits.SplMin;
                TdpCustomSlider.Maximum = _tdpLimits.SplMax;
            }
            finally { _suppressCustomSlider = false; }
        }
        if (_activeSubMenu != SubMenuMode.TdpCustom && TdpCustomHint != null)
            TdpCustomHint.Text = DefaultCustomHint();
        if (BtnTdpCustom != null)
            ToolTip.SetTip(BtnTdpCustom, $"Personalizado • {_tdpLimits.SplMin} a {_tdpLimits.SplMax} W ({(_tdpLimits.OnAc ? "tomada" : "bateria")})");
    }

    private string DefaultCustomHint()
        => $"{_tdpLimits.SplMin} W a {_tdpLimits.SplMax} W na {(_tdpLimits.OnAc ? "tomada" : "bateria")}"
           + (_tdpLimits.IsFallback ? " (limite do aparelho desconhecido)" : "");

    private string UnvalidatedNoticeText()
        => $"Ainda não validado neste modelo específico; perfis até 30 W, Personalizado entre {_tdpLimits.SplMin} W e {_tdpLimits.SplMax} W ({_tdpLimits.Source}).";

    private void OnTdpLimitsChanged(TdpLimits limits)
    {
        _tdpLimits = limits;
        UpdateTdpRangeUi();
        if (_showUnvalidatedNotice && TxtTdpStatus != null) TxtTdpStatus.Text = UnvalidatedNoticeText();
        if (_activeSubMenu == SubMenuMode.TdpCustom)
        {
            _tempCustomWatts = _tdpLimits.ClampSpl(_tempCustomWatts);
            ShowCustomWatts(_tempCustomWatts);
        }
        else if (TdpProfiles.IsCustom(_currentTdp))
        {
            ShowCustomWatts(TdpProfiles.WattsFor(_currentTdp) ?? _customWatts);
        }
    }

    private static OverlayWindow? s_powerWatchOwner;
    private static bool s_powerWatchSubscribed;

    private void StartPowerSourceWatch()
    {
        s_powerWatchOwner = this;
        if (!OperatingSystem.IsWindows() || s_powerWatchSubscribed) return;
        try
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
            {
                if (e.Mode == Microsoft.Win32.PowerModes.StatusChange) s_powerWatchOwner?.OnPowerSourceMaybeChanged();
            };
            s_powerWatchSubscribed = true;
        }
        catch (Exception ex) { DebugLog.Write($"[TDP-LIMITES] Não deu pra acompanhar tomada/bateria: {ex.Message}"); }
    }

    private void OnPowerSourceMaybeChanged()
    {
        try
        {
            var onAc = DeviceTdpLimits.IsOnAcPower();
            if (_lastOnAc == onAc) return;
            _lastOnAc = onAc;
            var previous = _tdpLimits;

            _ = Task.Run(async () =>
            {
                try
                {
                    var limits = DeviceTdpLimits.For(onAc);
                    Dispatcher.UIThread.Post(() => OnTdpLimitsChanged(limits));

                    await Task.Delay(TimeSpan.FromSeconds(2));

                    lock (WindowsHardwareService.TdpApplyLock)
                    {
                        var saved = AppSettingsStore.Load().TdpMode;
                        if (!TdpProfiles.IsCustom(saved)) return;
                        var asked = TdpProfiles.WattsFor(saved) ?? TdpProfiles.DefaultCustomWatts;
                        if (previous.PeaksFor(asked) == limits.PeaksFor(asked)) return;

                        DebugLog.Write($"[TDP-LIMITES] Agora na {(onAc ? "tomada" : "bateria")} — teto SPL {limits.SplMax} W. Reaplicando {saved}.");
                        var ok = _hardwareService.SetTdpMode(saved);
                        DebugLog.Write(ok
                            ? $"[TDP-LIMITES] {saved} reaplicado após troca de energia."
                            : $"[TDP-LIMITES] Falha ao reaplicar {saved} após troca de energia.");
                    }
                }
                catch (Exception ex) { DebugLog.Write($"[TDP-LIMITES] EXCEPTION ao reaplicar após troca de energia: {ex.Message}"); }
            });
        }
        catch (Exception ex) { DebugLog.Write($"[TDP-LIMITES] EXCEPTION em PowerModeChanged: {ex.Message}"); }
    }

    private void OnTdpCustomSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializing || _suppressCustomSlider || !_tdpHardwareSupported) return;
        var watts = _tdpLimits.ClampSpl((int)Math.Round(e.NewValue));
        if (_activeSubMenu == SubMenuMode.TdpCustom)
        {
            _tempCustomWatts = watts;
            if (TdpCustomValueText != null) TdpCustomValueText.Text = $"{watts} W";
            return;
        }
        if (TdpCustomValueText != null) TdpCustomValueText.Text = $"{watts} W";
        ScheduleCustomApply(watts);
    }

    private void OnTdpCustomStepClick(object? sender, RoutedEventArgs e)
    {
        if (!_tdpHardwareSupported || sender is not Button { Tag: string tag } || !int.TryParse(tag, out var step)) return;
        int current;
        if (_activeSubMenu == SubMenuMode.TdpCustom) current = _tempCustomWatts;
        else if (_customApplyTimer?.IsEnabled == true) current = _pendingCustomWatts;
        else current = TdpProfiles.IsCustom(_currentTdp) ? TdpProfiles.WattsFor(_currentTdp) ?? _customWatts : _customWatts;
        if (step > 0 && current >= _tdpLimits.SplMax) return;
        var watts = _tdpLimits.ClampSpl(_tdpLimits.ClampSpl(current) + step);
        ShowCustomWatts(watts);
        if (_activeSubMenu == SubMenuMode.TdpCustom) { _tempCustomWatts = watts; _tdpCustomEdited = true; return; }
        ScheduleCustomApply(watts);
    }

    private void ScheduleCustomApply(int watts)
    {
        if (_customApplyTimer is null)
        {
            _customApplyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _customApplyTimer.Tick += OnCustomApplyTick;
        }
        _customApplyTimer.Stop();
        _pendingCustomWatts = watts;
        _customApplyTimer.Start();
    }

    private void OnCustomApplyTick(object? sender, EventArgs e)
    {
        _customApplyTimer?.Stop();
        var watts = _pendingCustomWatts;
        var mode = TdpProfiles.CustomMode(watts);
        if (mode == _currentTdp) return;
        DebugLog.Write($"[OVERLAY] TDP personalizado pelo toque: {watts} W.");
        ApplyTdp(mode);
    }

    private void EnterTdpCustomEdit()
    {
        if (!_tdpHardwareSupported) return;
        _customApplyTimer?.Stop();
        _activeSubMenu = SubMenuMode.TdpCustom;
        _tempCustomWatts = _tdpLimits.ClampSpl(TdpProfiles.IsCustom(_currentTdp) ? TdpProfiles.WattsFor(_currentTdp) ?? _customWatts : _customWatts);
        _tdpCustomEdited = false;
        BtnTdpCustom.Background = _activeColor;
        TdpCustomPanel.IsVisible = true;
        TdpCustomPanel.BorderBrush = _focusBorderColor;
        TdpCustomHint.Text = "◀ ▶ 1 W · ▲ ▼ 5 W · A aplica · B cancela";
        ShowCustomWatts(_tempCustomWatts);
        Dispatcher.UIThread.Post(() => TdpCustomPanel.BringIntoView(), DispatcherPriority.Loaded);
    }

    private void HandleTdpCustomAction(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.MoveLeft: _tempCustomWatts = _tdpLimits.ClampSpl(_tempCustomWatts - 1); _tdpCustomEdited = true; ShowCustomWatts(_tempCustomWatts); break;
            case GamepadAction.MoveRight: _tempCustomWatts = _tdpLimits.ClampSpl(_tempCustomWatts + 1); _tdpCustomEdited = true; ShowCustomWatts(_tempCustomWatts); break;
            case GamepadAction.MoveUp: _tempCustomWatts = _tdpLimits.ClampSpl(_tempCustomWatts + 5); _tdpCustomEdited = true; ShowCustomWatts(_tempCustomWatts); break;
            case GamepadAction.MoveDown: _tempCustomWatts = _tdpLimits.ClampSpl(_tempCustomWatts - 5); _tdpCustomEdited = true; ShowCustomWatts(_tempCustomWatts); break;
            case GamepadAction.Confirm:
                _activeSubMenu = SubMenuMode.None;
                var chosen = _tdpCustomEdited
                    ? _tempCustomWatts
                    : (TdpProfiles.IsCustom(_currentTdp) ? TdpProfiles.WattsFor(_currentTdp) ?? _customWatts : _customWatts);
                DebugLog.Write($"[OVERLAY] TDP personalizado pelo controle: {chosen} W{(_tdpCustomEdited ? "" : " (sem alteração)")}.");
                ApplyTdp(TdpProfiles.CustomMode(chosen));
                ClearTempHighlights();
                UpdateFocusVisual();
                UpdatePersistentVisuals();
                break;
            case GamepadAction.Back:
                CancelTdpCustomEdit();
                UpdateFocusVisual();
                break;
        }
    }

    private void CancelTdpCustomEdit()
    {
        _activeSubMenu = SubMenuMode.None;
        ClearTempHighlights();
        UpdatePersistentVisuals();
    }
}
