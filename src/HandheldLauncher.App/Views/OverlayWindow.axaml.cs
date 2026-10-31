using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using HandheldLauncher.Core.Services;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Platform;
using HandheldLauncher.App.Input;

namespace HandheldLauncher.App.Views;

public partial class OverlayWindow : Window
{
    private readonly IHardwareService _hardwareService;
    private readonly IBrush _activeColor = new SolidColorBrush(Color.FromRgb(124, 92, 252));
    private readonly IBrush _inactiveColor = new SolidColorBrush(Color.FromRgb(37, 42, 54));
    private bool _isInitializing = true;

    public OverlayWindow()
    {
        DebugLog.Write("[OVERLAY] Construtor iniciado.");
        InitializeComponent(); 

        if (OperatingSystem.IsWindows())
            _hardwareService = new WindowsHardwareService();
        else if (OperatingSystem.IsLinux())
            _hardwareService = new LinuxHardwareService();
        else
            _hardwareService = new MockHardwareService();
        
        try 
        {
            SetTdpButtonVisual("Equilibrado");
            if (ColorModeCombo != null) ColorModeCombo.SelectedIndex = 0;
            if (VolumeSlider != null) VolumeSlider.Value = 70;
            if (BrightnessSlider != null) BrightnessSlider.Value = 80;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[OVERLAY] Aviso: {ex.Message}");
        }
        
        _isInitializing = false;
        DebugLog.Write("[OVERLAY] Construtor concluído.");
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var screen = Screens.Primary;
        if (screen != null)
        {
            var workingArea = screen.WorkingArea;
            var rightEdge = workingArea.X + workingArea.Width;
            var centerY = workingArea.Y + (workingArea.Height - (int)Height) / 2;
            Position = new PixelPoint(rightEdge - (int)Width - 20, centerY);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Hide(); 

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }

    public void HandleGamepadAction(GamepadAction action)
    {
        if (action == GamepadAction.Back || action == GamepadAction.GlobalMenu)
            Hide();
    }

    private void OnTdpClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedButton) return;
        var mode = clickedButton.Content?.ToString() ?? "Desconhecido";
        _hardwareService?.SetTdpMode(mode);
        SetTdpButtonVisual(mode);
    }

    private void SetTdpButtonVisual(string activeMode)
    {
        if (BtnEco != null) BtnEco.Background = activeMode == "Eco" ? _activeColor : _inactiveColor;
        if (BtnEquilibrado != null) BtnEquilibrado.Background = activeMode == "Equilibrado" ? _activeColor : _inactiveColor;
        if (BtnTurbo != null) BtnTurbo.Background = activeMode == "Turbo" ? _activeColor : _inactiveColor;
    }

    private void OnColorModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return; 
        if (ColorModeCombo?.SelectedItem is ComboBoxItem selectedItem)
            _hardwareService?.SetColorMode(selectedItem.Content?.ToString() ?? "Padrão");
    }

    private void OnHudToggled(object? sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        if (HudToggle?.IsChecked is bool enabled)
            _hardwareService?.SetPerformanceHudEnabled(enabled);
    }

    private void OnVolumeChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializing) return;
        _hardwareService?.SetVolume(Math.Round(e.NewValue));
    }

    private void OnBrightnessChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializing) return;
        _hardwareService?.SetBrightness(Math.Round(e.NewValue));
    }
}
