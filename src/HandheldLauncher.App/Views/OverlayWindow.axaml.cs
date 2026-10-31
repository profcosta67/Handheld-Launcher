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
using HandheldLauncher.App.ViewModels;

namespace HandheldLauncher.App.Views;

public partial class OverlayWindow : Window
{
    private readonly IHardwareService _hardwareService;
    private readonly IBrush _activeColor = new SolidColorBrush(Color.FromRgb(124, 92, 252));
    private readonly IBrush _inactiveColor = new SolidColorBrush(Color.FromRgb(37, 42, 54));
    private readonly IBrush _focusBorderColor = new SolidColorBrush(Color.FromRgb(124, 92, 252));
    
    private bool _isInitializing = true;
    private int _focusIndex = 0;
    private readonly Border[] _sections;
    private string _currentTdp = "Equilibrado";
    
    private readonly MainViewModel _viewModel;
    public bool IsAppClosing { get; set; } = false; 

    public OverlayWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        
        DebugLog.Write("[OVERLAY] Construtor iniciado.");
        InitializeComponent(); 

        _sections = new[] { TdpSection, ColorSection, HudSection, VolumeSection, BrightnessSection };

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
        catch (Exception ex) { DebugLog.Write($"[OVERLAY] Aviso: {ex.Message}"); }

        // 🟢 O LOG DE FOCO AGORA É UM EVENTO, COLOCADO AQUI NO CONSTRUTOR 🟢
        Deactivated += (_, _) => DebugLog.Write("[OVERLAY] Janela perdeu o foco (Deactivated).");
        
        _isInitializing = false;
        UpdateFocusVisual();
        DebugLog.Write("[OVERLAY] Construtor concluído.");
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        DebugLog.Write("[OVERLAY] OnOpened disparado.");
        var screen = Screens.Primary;
        if (screen != null)
        {
            var workingArea = screen.WorkingArea;
            var rightEdge = workingArea.X + workingArea.Width;
            var centerY = workingArea.Y + (workingArea.Height - (int)Height) / 2;
            Position = new PixelPoint(rightEdge - (int)Width - 20, centerY);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        DebugLog.Write($"[OVERLAY] OnClosing disparado. IsAppClosing={IsAppClosing}");
        if (!IsAppClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        DebugLog.Write("[OVERLAY] Botão Fechar clicado.");
        Hide(); 
    }

    public void HandleGamepadAction(GamepadAction action)
    {
        DebugLog.Write($"[OVERLAY] Controle: {action} | Foco atual: {_focusIndex}");

        switch (action)
        {
            case GamepadAction.Back:
            case GamepadAction.GlobalMenu:
                DebugLog.Write("[OVERLAY] Fechando pelo controle (Back/Menu).");
                Hide();
                break;
            case GamepadAction.MoveUp:
                _focusIndex = Math.Max(0, _focusIndex - 1);
                UpdateFocusVisual();
                break;
            case GamepadAction.MoveDown:
                _focusIndex = Math.Min(_sections.Length - 1, _focusIndex + 1);
                UpdateFocusVisual();
                break;
            case GamepadAction.MoveLeft:
                HandleHorizontalInput(-1);
                break;
            case GamepadAction.MoveRight:
                HandleHorizontalInput(1);
                break;
            case GamepadAction.Confirm:
                HandleConfirm();
                break;
        }
    }

    private void UpdateFocusVisual()
    {
        for (int i = 0; i < _sections.Length; i++)
        {
            _sections[i].BorderBrush = (i == _focusIndex) ? _focusBorderColor : Brushes.Transparent;
        }
    }

    private void HandleHorizontalInput(int direction)
    {
        switch (_focusIndex)
        {
            case 0: // TDP
                string newTdp = _currentTdp;
                if (direction < 0)
                {
                    if (_currentTdp == "Turbo") newTdp = "Equilibrado";
                    else if (_currentTdp == "Equilibrado") newTdp = "Eco";
                }
                else if (direction > 0)
                {
                    if (_currentTdp == "Eco") newTdp = "Equilibrado";
                    else if (_currentTdp == "Equilibrado") newTdp = "Turbo";
                }

                if (newTdp != _currentTdp)
                {
                    SetTdpButtonVisual(newTdp);
                    _hardwareService?.SetTdpMode(newTdp);
                }
                break;
            case 1: // Cor
                if (ColorModeCombo != null)
                {
                    var newIndex = Math.Clamp(ColorModeCombo.SelectedIndex + direction, 0, ColorModeCombo.ItemCount - 1);
                    ColorModeCombo.SelectedIndex = newIndex;
                }
                break;
            case 3: // Volume
                if (VolumeSlider != null)
                    VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + (direction * 5), 0, 100);
                break;
            case 4: // Brilho
                if (BrightnessSlider != null)
                    BrightnessSlider.Value = Math.Clamp(BrightnessSlider.Value + (direction * 5), 0, 100);
                break;
        }
    }

    private void HandleConfirm()
    {
        if (_focusIndex == 2 && HudToggle != null)
        {
            HudToggle.IsChecked = !HudToggle.IsChecked;
        }
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
        _currentTdp = activeMode;
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
        {
            _hardwareService?.SetPerformanceHudEnabled(enabled);
        }
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