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

// Parte do OverlayWindow (0.28): ventoinha, cor e HUD.
// Movido sem alterar nenhuma linha de código; ver OverlayWindow.axaml.cs.
public partial class OverlayWindow
{
    private void OnFanClick(object? sender, RoutedEventArgs e) { if (sender is Button button) ApplyFanMode(button.Tag?.ToString() ?? "Automático"); }

    private void ApplyFanMode(string mode)
    {
        GameSessionInfo.NoteFanTouch(); // o TDP inteligente deixa a ventoinha com o usuário quando ele mexe nela
        if (mode == "Automático")
        {
            _fanCurveService?.RestoreOriginal(); _currentFanMode = mode; SetFanButtonVisual(mode);
            TxtFanStatus.Text = "Curva original restaurada; firmware voltou ao controle automático.";
            SaveSetting("FanMode", mode); return;
        }
        if (_fanCurveService is null) { TxtFanStatus.Text = "Controle manual indisponível nesta plataforma."; return; }
        if (_fanCurveService.TryApplyPreset(mode, out var message))
        {
            _currentFanMode = mode; SetFanButtonVisual(mode); TxtFanStatus.Text = message; SaveSetting("FanMode", mode);
        }
        else if (!ElevationService.IsElevated && OperatingSystem.IsWindows())
        {
            // v0.25.5 (Camada 2): sem admin a escrita WMI falha — delega ao processo elevado (UAC uma vez).
            DebugLog.Write($"[FAN] Escrita direta falhou (sem admin) — delegando ao processo elevado: {mode}.");
            _ = Task.Run(() =>
            {
                if (ElevationService.RunElevated("fan-apply", "--mode", mode))
                    Dispatcher.UIThread.Post(() =>
                    {
                        _currentFanMode = mode;
                        SetFanButtonVisual(mode);
                        TxtFanStatus.Text = $"Curva {mode} aplicada (elevado).";
                        SaveSetting("FanMode", mode);
                    });
            });
        }
        else TxtFanStatus.Text = message + " Curva anterior preservada.";
    }

    /// <summary>
    /// v0.27 — ventoinha comandada pelo TDP inteligente. Aplica a curva sem gravar nas
    /// configurações (a escolha do usuário fica guardada); com null devolve a curva gravada.
    /// </summary>
    public void ApplySmartFan(string? mode)
    {
        if (mode is null)
        {
            string saved;
            try { saved = AppSettingsStore.Load().FanMode; } catch { saved = ""; }
            if (string.IsNullOrWhiteSpace(saved)) saved = "Automático";
            DebugLog.Write($"[FAN] Modo inteligente saiu: ventoinha de volta a {saved}.");
            ApplyFanMode(saved);
            return;
        }

        if (_fanCurveService is null || mode == _currentFanMode) return;
        if (_fanCurveService.TryApplyPreset(mode, out var message))
        {
            _currentFanMode = mode;
            SetFanButtonVisual(mode);
            TxtFanStatus.Text = message + " (modo inteligente)";
        }
        else DebugLog.Write($"[FAN] Modo inteligente não conseguiu aplicar {mode}: {message}");
    }

    private void SetFanButtonVisual(string mode)
    {
        if (BtnFanAuto == null) return;
        BtnFanAuto.Background = mode == "Automático" ? _activeColor : _inactiveColor;
        BtnFanSilent.Background = mode == "Silencioso" ? _activeColor : _inactiveColor;
        BtnFanNormal.Background = mode == "Normal" ? _activeColor : _inactiveColor;
        BtnFanPerformance.Background = mode == "Desempenho" ? _activeColor : _inactiveColor;
        BtnFanBoost.Background = mode == "Boost" ? _activeColor : _inactiveColor;
        _currentFanMode = mode;
    }

    private void OnColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedButton) return;
        if (int.TryParse(clickedButton.Tag?.ToString(), out int index))
        {
            string mode = index switch { 1 => "Vívido", 2 => "OLED", 3 => "Noturno", _ => "Padrão" };
            if (_hardwareService.SetColorMode(mode))
            {
                SetColorButtonVisual(index);
                SaveSetting("ColorMode", index.ToString());
                _viewModel.UpdateHudColorMode(mode);
            }
        }
    }

    private void SetColorButtonVisual(int index)
    {
        if (BtnColorDefault != null) BtnColorDefault.Background = index == 0 ? _activeColor : _inactiveColor;
        if (BtnColorVivid != null) BtnColorVivid.Background = index == 1 ? _activeColor : _inactiveColor;
        if (BtnColorOled != null) BtnColorOled.Background = index == 2 ? _activeColor : _inactiveColor;
        if (BtnColorNight != null) BtnColorNight.Background = index == 3 ? _activeColor : _inactiveColor;
        _currentColor = index;
    }

    private void OnHudClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedButton) return;
        if (int.TryParse(clickedButton.Tag?.ToString(), out int preset)) {
            _currentHudIndex = preset; _viewModel.SetOverlayPreset(preset); SetHudButtonVisual(preset); SaveSetting("OverlayPreset", preset.ToString());
        }
    }

    private void SetHudButtonVisual(int preset)
    {
        if (BtnHudOff != null) BtnHudOff.Background = preset == 0 ? _activeColor : _inactiveColor;
        if (BtnHudMin != null) BtnHudMin.Background = preset == 1 ? _activeColor : _inactiveColor;
        if (BtnHudMed != null) BtnHudMed.Background = preset == 2 ? _activeColor : _inactiveColor;
        if (BtnHudFull != null) BtnHudFull.Background = preset == 3 ? _activeColor : _inactiveColor;
        _currentHudIndex = preset;
    }
}
