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

// Parte do OverlayWindow (0.28): controle (gamepad), submenus e destaques.
// Movido sem alterar nenhuma linha de código; ver OverlayWindow.axaml.cs.
public partial class OverlayWindow
{
    public void HandleGamepadAction(GamepadAction action)
    {
        // O toggle é tratado no GameplayWatcher (ToggleOverlay). Se chegar aqui é
        // um eco do mesmo lote de input e NÃO deve fechar o painel que acabou de
        // abrir (bug reportado: "no home chama o QS, ele aparece e fecha").
        if (action == GamepadAction.ToggleQuickSettings) return;

        // QS 2.0: o painel novo tem a sua própria navegação por D-pad.
        if (NewQsPanel is { IsVisible: true }) { NewQsPanel.HandleGamepadAction(action); return; }

        if (_activeSubMenu == SubMenuMode.TdpCustom)
        {
            HandleTdpCustomAction(action);
            return;
        }

        if (_activeSubMenu == SubMenuMode.Tdp && action == GamepadAction.Confirm && _tempTdpIndex == CustomTdpIndex)
        {
            EnterTdpCustomEdit();
            return;
        }

        if (_activeSubMenu != SubMenuMode.None)
        {
            if (_activeSubMenu == SubMenuMode.Lossless)
            {
                switch (action)
                {
                    case GamepadAction.MoveUp:
                        _tempLosslessIndex = Math.Max(0, _tempLosslessIndex - 1);
                        UpdateTempVisuals();
                        break;
                    case GamepadAction.MoveDown:
                        _tempLosslessIndex = Math.Min(1, _tempLosslessIndex + 1);
                        UpdateTempVisuals();
                        break;
                    case GamepadAction.MoveLeft:
                        if (_tempLosslessIndex == 1)
                        {
                            _tempMultiplierIndex = Math.Max(0, _tempMultiplierIndex - 1);
                            UpdateMultiplierVisual();
                            UpdateTempVisuals();
                        }
                        break;
                    case GamepadAction.MoveRight:
                        if (_tempLosslessIndex == 1)
                        {
                            _tempMultiplierIndex = Math.Min(2, _tempMultiplierIndex + 1);
                            UpdateMultiplierVisual();
                            UpdateTempVisuals();
                        }
                        break;
                    case GamepadAction.Confirm:
                        ApplySubMenuChange();
                        _activeSubMenu = SubMenuMode.None;
                        ClearTempHighlights();
                        UpdateFocusVisual();
                        UpdatePersistentVisuals();
                        break;
                    case GamepadAction.Back:
                        _activeSubMenu = SubMenuMode.None;
                        ClearTempHighlights();
                        UpdateFocusVisual();
                        UpdatePersistentVisuals();
                        break;
                }
                return;
            }

            switch (action)
            {
                case GamepadAction.MoveLeft:
                case GamepadAction.MoveUp: HandleSubMenuMove(-1); break;
                case GamepadAction.MoveRight:
                case GamepadAction.MoveDown: HandleSubMenuMove(1); break;
                case GamepadAction.Confirm:
                    ApplySubMenuChange();
                    _activeSubMenu = SubMenuMode.None;
                    ClearTempHighlights();
                    UpdateFocusVisual();
                    UpdatePersistentVisuals();
                    break;
                case GamepadAction.Back:
                    _activeSubMenu = SubMenuMode.None;
                    ClearTempHighlights();
                    UpdateFocusVisual();
                    UpdatePersistentVisuals();
                    break;
            }
            return;
        }

        switch (action)
        {
            case GamepadAction.Back:
            case GamepadAction.GlobalMenu:
                HideWithUnblock();
                break;
            case GamepadAction.Downloads when BtnCloseXCloud is { IsVisible: true }:
                CloseXCloudFromOverlay();
                break;
            case GamepadAction.MoveUp:
                _focusIndex = Math.Max(0, _focusIndex - 1);
                UpdateFocusVisual();
                break;
            case GamepadAction.MoveDown:
                _focusIndex = Math.Min(_sections.Length, _focusIndex + 1);
                UpdateFocusVisual();
                break;
            case GamepadAction.Confirm:
                if (_focusIndex == _sections.Length) HideWithUnblock();
                else EnterSubMenu();
                break;
        }
    }

    private void EnterSubMenu()
    {
        ClearTempHighlights();

        switch (_focusIndex)
        {
            case 0: _tempResIndex = _currentResIndex; _activeSubMenu = SubMenuMode.Resolution; UpdateTempVisuals(); break;
            case 1: _tempHzIndex = _currentHzIndex; _activeSubMenu = SubMenuMode.RefreshRate; UpdateTempVisuals(); break;
            case 2:
                if (!_tdpHardwareSupported) return;
                FlushPendingCustomApply();
                _tempTdpIndex = TdpProfiles.IsCustom(_currentTdp) ? CustomTdpIndex : Array.IndexOf(TdpProfiles.Presets, _currentTdp);
                if (_tempTdpIndex < 0) _tempTdpIndex = 2;
                _activeSubMenu = SubMenuMode.Tdp; UpdateTempVisuals(); break;
            case 3:
                string[] fans = { "Automático", "Silencioso", "Normal", "Desempenho", "Boost" };
                _tempFanIndex = Array.IndexOf(fans, _currentFanMode);
                if (_tempFanIndex < 0) _tempFanIndex = 0;
                _activeSubMenu = SubMenuMode.Fan; UpdateTempVisuals(); break;
            case 4: _tempColorIndex = _currentColor; _activeSubMenu = SubMenuMode.Color; UpdateTempVisuals(); break;
            case 5: _tempHudIndex = _currentHudIndex; _activeSubMenu = SubMenuMode.Hud; UpdateTempVisuals(); break;
            case 6: _activeSubMenu = SubMenuMode.Volume; break;
            case 7: _activeSubMenu = SubMenuMode.Brightness; break;
            case 8:
                _tempLosslessIndex = 0;
                _tempMultiplierIndex = _currentMultiplierIndex;
                _activeSubMenu = SubMenuMode.Lossless;
                UpdateMultiplierVisual();
                UpdateTempVisuals();
                UpdateLsfgButtonState();
                UpdateLsfgStatus();
                break;
            case 9: _tempCaptureIndex = 0; _activeSubMenu = SubMenuMode.Capture; UpdateTempVisuals(); break;
        }
    }

    private void HandleSubMenuMove(int direction)
    {
        switch (_activeSubMenu)
        {
            case SubMenuMode.Resolution: _tempResIndex = Math.Clamp(_tempResIndex + direction, 0, _resolutionButtons.Length - 1); UpdateTempVisuals(); break;
            case SubMenuMode.RefreshRate: _tempHzIndex = Math.Clamp(_tempHzIndex + direction, 0, _refreshButtons.Length - 1); UpdateTempVisuals(); break;
            case SubMenuMode.Tdp: _tempTdpIndex = Math.Clamp(_tempTdpIndex + direction, 0, CustomTdpIndex); UpdateTempVisuals(); break;
            case SubMenuMode.Fan: _tempFanIndex = Math.Clamp(_tempFanIndex + direction, 0, 4); UpdateTempVisuals(); break;
            case SubMenuMode.Color: _tempColorIndex = Math.Clamp(_tempColorIndex + direction, 0, 3); UpdateTempVisuals(); break;
            case SubMenuMode.Hud: _tempHudIndex = Math.Clamp(_tempHudIndex + direction, 0, 3); UpdateTempVisuals(); break;
            case SubMenuMode.Volume: if (VolumeSlider != null) VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + (direction * 5), 0, 100); break;
            case SubMenuMode.Brightness: if (BrightnessSlider != null) BrightnessSlider.Value = Math.Clamp(BrightnessSlider.Value + (direction * 5), 0, 100); break;
            case SubMenuMode.Capture: _tempCaptureIndex = Math.Clamp(_tempCaptureIndex + direction, 0, 1); UpdateTempVisuals(); break;
            case SubMenuMode.Lossless: break;
        }
    }

    private void ApplySubMenuChange()
    {
        switch (_activeSubMenu)
        {
            case SubMenuMode.Resolution: _currentResIndex = _tempResIndex; ApplyDisplayMode(); break;
            case SubMenuMode.RefreshRate: _currentHzIndex = _tempHzIndex; ApplyDisplayMode(); break;
            case SubMenuMode.Tdp:
                if (_tempTdpIndex >= 0 && _tempTdpIndex < TdpProfiles.Presets.Length)
                    ApplyTdp(TdpProfiles.Presets[_tempTdpIndex]);
                break;
            case SubMenuMode.Fan:
                string[] fanModes = { "Automático", "Silencioso", "Normal", "Desempenho", "Boost" };
                ApplyFanMode(fanModes[_tempFanIndex]);
                break;
            case SubMenuMode.Color:
                string mode = _tempColorIndex switch { 1 => "Vívido", 2 => "OLED", 3 => "Noturno", _ => "Padrão" };
                if (_hardwareService.SetColorMode(mode))
                {
                    SetColorButtonVisual(_tempColorIndex);
                    SaveSetting("ColorMode", _tempColorIndex.ToString());
                    _viewModel.UpdateHudColorMode(mode);
                }
                break;
            case SubMenuMode.Hud:
                _currentHudIndex = _tempHudIndex;
                _viewModel.SetOverlayPreset(_currentHudIndex);
                SetHudButtonVisual(_currentHudIndex);
                SaveSetting("OverlayPreset", _currentHudIndex.ToString());
                break;
            case SubMenuMode.Lossless:
                if (_tempLosslessIndex == 0)
                {
                    OnLsfgToggleClick(BtnLsfgToggle, new RoutedEventArgs());
                }
                else
                {
                    _currentMultiplierIndex = _tempMultiplierIndex;
                    if (LsfgStatusText != null)
                        LsfgStatusText.Text = $"Multiplicador salvo: {(_currentMultiplierIndex + 2)}x (configure no Lossless)";
                }
                break;
            case SubMenuMode.Capture:
                if (_tempCaptureIndex == 0) OnScreenshotClick(null, new RoutedEventArgs());
                else OnRecordClick(null, new RoutedEventArgs());
                break;
        }
    }

    private void UpdateMultiplierVisual()
    {
        if (_multiplierButtons == null) return;
        for (int i = 0; i < _multiplierButtons.Length; i++)
        {
            if (_multiplierButtons[i] != null)
                _multiplierButtons[i].Background = (i == _currentMultiplierIndex) ? _activeColor : _inactiveColor;
        }
    }

    private void OnMultiplierClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedButton) return;
        if (int.TryParse(clickedButton.Tag?.ToString(), out int index))
        {
            _currentMultiplierIndex = index;
            _tempMultiplierIndex = index;
            UpdateMultiplierVisual();
            // Linux: escreve o multiplicador no conf.toml do LSFG-VK.
            if (OperatingSystem.IsLinux()) LinuxLosslessService.SetMultiplier(index + 2);
            if (LsfgStatusText != null)
                LsfgStatusText.Text = $"Multiplicador salvo: {(index + 2)}x (configure no Lossless)";
        }
    }

    private void ClearTempHighlights()
    {
        if (BtnScreenshot != null) BtnScreenshot.Background = _inactiveColor;
        if (BtnRecord != null) BtnRecord.Background = _inactiveColor;
        if (BtnLsfgToggle != null) BtnLsfgToggle.Background = _inactiveColor;
        if (LsfgMultiplierBorder != null) LsfgMultiplierBorder.BorderBrush = Brushes.Transparent;
    }

    private void UpdateFocusVisual()
    {
        for (int i = 0; i < _sections.Length; i++)
            _sections[i].BorderBrush = (i == _focusIndex) ? _focusBorderColor : Brushes.Transparent;

        if (BtnCloseMenu != null)
            BtnCloseMenu.Background = _focusIndex == _sections.Length ? _activeColor : _inactiveColor;

        ScrollToFocusedSection();
    }

    private void ScrollToFocusedSection()
    {
        if (MainScrollViewer == null || _sections.Length == 0) return;
        if (_focusIndex < 0 || _focusIndex > _sections.Length) return;

        if (_scrollPending) return;
        _scrollPending = true;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _scrollPending = false;
            try
            {
                if (_focusIndex == _sections.Length)
                {
                    MainScrollViewer.Offset = new Vector(
                        MainScrollViewer.Offset.X,
                        Math.Max(0, MainScrollViewer.Extent.Height - MainScrollViewer.Viewport.Height));
                    return;
                }

                var focused = _sections[_focusIndex];

                double itemTop = focused.Bounds.Y;
                double itemBottom = itemTop + focused.Bounds.Height;

                double viewTop = MainScrollViewer.Offset.Y;
                double viewBottom = viewTop + MainScrollViewer.Viewport.Height;

                if (itemTop < viewTop)
                {
                    MainScrollViewer.Offset = new Vector(MainScrollViewer.Offset.X, Math.Max(0, itemTop - 20));
                }
                else if (itemBottom > viewBottom)
                {
                    double targetOffset = itemBottom - MainScrollViewer.Viewport.Height + 20;
                    double maxOffset = Math.Max(0, MainScrollViewer.Extent.Height - MainScrollViewer.Viewport.Height);
                    targetOffset = Math.Min(targetOffset, maxOffset);
                    MainScrollViewer.Offset = new Vector(MainScrollViewer.Offset.X, Math.Max(0, targetOffset));
                }
            }
            catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void UpdatePersistentVisuals()
    {
        for (int i = 0; i < _resolutionButtons.Length; i++) _resolutionButtons[i].Background = (i == _currentResIndex) ? _activeColor : _inactiveColor;
        for (int i = 0; i < _refreshButtons.Length; i++) _refreshButtons[i].Background = (i == _currentHzIndex) ? _activeColor : _inactiveColor;
        SetTdpButtonVisual(_currentTdp);
        SetFanButtonVisual(_currentFanMode);
        SetColorButtonVisual(_currentColor);
        SetHudButtonVisual(_currentHudIndex);
        UpdateMultiplierVisual();
    }

    private void UpdateTempVisuals()
    {
        if (_activeSubMenu == SubMenuMode.Resolution)
        {
            for (int i = 0; i < _resolutionButtons.Length; i++) _resolutionButtons[i].Background = (i == _tempResIndex) ? _activeColor : _inactiveColor;
        }
        else if (_activeSubMenu == SubMenuMode.RefreshRate)
        {
            for (int i = 0; i < _refreshButtons.Length; i++) _refreshButtons[i].Background = (i == _tempHzIndex) ? _activeColor : _inactiveColor;
        }
        else if (_activeSubMenu == SubMenuMode.Tdp)
        {
            BtnUltraEco.Background = _tempTdpIndex == 0 ? _activeColor : _inactiveColor;
            BtnEco.Background = _tempTdpIndex == 1 ? _activeColor : _inactiveColor;
            BtnNormal.Background = _tempTdpIndex == 2 ? _activeColor : _inactiveColor;
            BtnDesempenho.Background = _tempTdpIndex == 3 ? _activeColor : _inactiveColor;
            BtnBoost.Background = _tempTdpIndex == 4 ? _activeColor : _inactiveColor;
            BtnTdpCustom.Background = _tempTdpIndex == CustomTdpIndex ? _activeColor : _inactiveColor;
        }
        else if (_activeSubMenu == SubMenuMode.Fan)
        {
            BtnFanAuto.Background = _tempFanIndex == 0 ? _activeColor : _inactiveColor;
            BtnFanSilent.Background = _tempFanIndex == 1 ? _activeColor : _inactiveColor;
            BtnFanNormal.Background = _tempFanIndex == 2 ? _activeColor : _inactiveColor;
            BtnFanPerformance.Background = _tempFanIndex == 3 ? _activeColor : _inactiveColor;
            BtnFanBoost.Background = _tempFanIndex == 4 ? _activeColor : _inactiveColor;
        }
        else if (_activeSubMenu == SubMenuMode.Color)
        {
            BtnColorDefault.Background = _tempColorIndex == 0 ? _activeColor : _inactiveColor;
            BtnColorVivid.Background = _tempColorIndex == 1 ? _activeColor : _inactiveColor;
            BtnColorOled.Background = _tempColorIndex == 2 ? _activeColor : _inactiveColor;
            BtnColorNight.Background = _tempColorIndex == 3 ? _activeColor : _inactiveColor;
        }
        else if (_activeSubMenu == SubMenuMode.Hud)
        {
            BtnHudOff.Background = _tempHudIndex == 0 ? _activeColor : _inactiveColor;
            BtnHudMin.Background = _tempHudIndex == 1 ? _activeColor : _inactiveColor;
            BtnHudMed.Background = _tempHudIndex == 2 ? _activeColor : _inactiveColor;
            BtnHudFull.Background = _tempHudIndex == 3 ? _activeColor : _inactiveColor;
        }
        else if (_activeSubMenu == SubMenuMode.Lossless)
        {
            if (BtnLsfgToggle != null)
                BtnLsfgToggle.Background = _tempLosslessIndex == 0 ? _activeColor : _inactiveColor;

            if (LsfgMultiplierBorder != null)
                LsfgMultiplierBorder.BorderBrush = _tempLosslessIndex == 1 ? _activeColor : Brushes.Transparent;

            if (_multiplierButtons != null)
            {
                for (int i = 0; i < _multiplierButtons.Length; i++)
                {
                    if (_multiplierButtons[i] != null)
                        _multiplierButtons[i].Background = (i == _tempMultiplierIndex) ? _activeColor : _inactiveColor;
                }
            }
        }
        else if (_activeSubMenu == SubMenuMode.Capture)
        {
            BtnScreenshot.Background = _tempCaptureIndex == 0 ? _activeColor : _inactiveColor;
            BtnRecord.Background = _tempCaptureIndex == 1 ? _activeColor : _inactiveColor;
        }
    }

    private void ApplyDisplayMode()
    {
        var resTag = _resolutionButtons[_currentResIndex].Tag?.ToString() ?? "";
        var resParts = resTag.Split('x');
        if (resParts.Length != 2 || !int.TryParse(resParts[0], out var w) || !int.TryParse(resParts[1], out var h)) return;
        if (int.TryParse(_refreshButtons[_currentHzIndex].Tag?.ToString(), out var hz))
        {
            _hardwareService.SetDisplayMode(w, h, hz);
            UpdateScreenInfoText();

            if (OperatingSystem.IsWindows())
                DispatcherTimer.RunOnce(() => DisplayColorManager.ReapplyCurrent(), TimeSpan.FromMilliseconds(1500));
        }
    }

    private void UpdateScreenInfoText()
    {
        if (TxtScreenInfo != null) TxtScreenInfo.Text = _hardwareService.GetDisplayInfo();
    }
}
