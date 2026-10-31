using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using HandheldLauncher.Core.Services;
using HandheldLauncher.Core.Models;
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
    private string _currentTdp = "Normal";
    
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
            var settings = AppSettingsStore.Load();

            SetTdpButtonVisual(string.IsNullOrEmpty(settings.TdpMode) ? "Normal" : settings.TdpMode);
            
            if (ColorModeCombo != null) 
                ColorModeCombo.SelectedIndex = Math.Clamp(settings.ColorMode, 0, 3);
            
            if (VolumeSlider != null) 
                VolumeSlider.Value = settings.Volume;
            
            if (BrightnessSlider != null) 
                BrightnessSlider.Value = settings.Brightness;
            
            SetHudButtonVisual(settings.OverlayPreset);
        }
        catch (Exception ex) { DebugLog.Write($"[OVERLAY] Aviso ao carregar settings: {ex.Message}"); }

        Deactivated += (_, _) => DebugLog.Write("[OVERLAY] Janela perdeu o foco (Deactivated).");
        
        _isInitializing = false;
        UpdateFocusVisual();
        DebugLog.Write("[OVERLAY] Construtor concluído.");
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        
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
        if (!IsAppClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Hide(); 

    public void HandleGamepadAction(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.Back:
            case GamepadAction.GlobalMenu:
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
                    if (_currentTdp == "Boost") newTdp = "Desempenho";
                    else if (_currentTdp == "Desempenho") newTdp = "Normal";
                    else if (_currentTdp == "Normal") newTdp = "Eco";
                }
                else if (direction > 0)
                {
                    if (_currentTdp == "Eco") newTdp = "Normal";
                    else if (_currentTdp == "Normal") newTdp = "Desempenho";
                    else if (_currentTdp == "Desempenho") newTdp = "Boost";
                }

                if (newTdp != _currentTdp)
                {
                    SetTdpButtonVisual(newTdp);
                    _hardwareService?.SetTdpMode(newTdp);
                    SaveSetting("TdpMode", newTdp);
                }
                break;

            case 1: // Cor
                if (ColorModeCombo != null)
                {
                    var newIndex = Math.Clamp(ColorModeCombo.SelectedIndex + direction, 0, ColorModeCombo.ItemCount - 1);
                    ColorModeCombo.SelectedIndex = newIndex;
                }
                break;

            case 2: // HUD
                if (_viewModel.IsPerformanceHudEnabled)
                {
                    var newHudIndex = Math.Clamp(_viewModel.CurrentHudPreset + direction, 0, 3);
                    _viewModel.SetOverlayPreset(newHudIndex);
                    SaveSetting("OverlayPreset", newHudIndex.ToString());
                    SetHudButtonVisual(newHudIndex);
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
        if (_focusIndex == 2 && _viewModel.IsPerformanceHudEnabled) // HUD
        {
            var nextIndex = (_viewModel.CurrentHudPreset + 1) % 4;
            _viewModel.SetOverlayPreset(nextIndex);
            SaveSetting("OverlayPreset", nextIndex.ToString());
            SetHudButtonVisual(nextIndex);
        }
    }

    private void OnTdpClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedButton) return;
        var mode = clickedButton.Content?.ToString() ?? "Desconhecido";
        
        if (mode == "Norm") mode = "Normal";
        if (mode == "Desemp") mode = "Desempenho";
        
        _hardwareService?.SetTdpMode(mode);
        SetTdpButtonVisual(mode);
        SaveSetting("TdpMode", mode);
    }

    private void SetTdpButtonVisual(string activeMode)
    {
        if (BtnEco != null) BtnEco.Background = activeMode == "Eco" ? _activeColor : _inactiveColor;
        if (BtnNormal != null) BtnNormal.Background = activeMode == "Normal" ? _activeColor : _inactiveColor;
        if (BtnDesempenho != null) BtnDesempenho.Background = activeMode == "Desempenho" ? _activeColor : _inactiveColor;
        if (BtnBoost != null) BtnBoost.Background = activeMode == "Boost" ? _activeColor : _inactiveColor;
        
        _currentTdp = activeMode;
    }

    private void OnHudClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedButton) return;
        var mode = clickedButton.Content?.ToString() ?? "Off";
        
        int preset = mode switch
        {
            "Off" => 0,
            "Min" => 1,
            "Méd" => 2,
            "Full" => 3,
            _ => 0
        };

        _viewModel.SetOverlayPreset(preset);
        SaveSetting("OverlayPreset", preset.ToString());
        SetHudButtonVisual(preset);
    }

    private void SetHudButtonVisual(int preset)
    {
        if (BtnHudOff != null) BtnHudOff.Background = preset == 0 ? _activeColor : _inactiveColor;
        if (BtnHudMin != null) BtnHudMin.Background = preset == 1 ? _activeColor : _inactiveColor;
        if (BtnHudMed != null) BtnHudMed.Background = preset == 2 ? _activeColor : _inactiveColor;
        if (BtnHudFull != null) BtnHudFull.Background = preset == 3 ? _activeColor : _inactiveColor;
    }

    private void OnColorModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return; 
        if (ColorModeCombo?.SelectedIndex is int index)
        {
            _hardwareService?.SetColorMode(ColorModeCombo.SelectedItem?.ToString() ?? "Padrão");
            SaveSetting("ColorMode", index.ToString());
        }
    }

    private void OnVolumeChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializing) return;
        _hardwareService?.SetVolume(Math.Round(e.NewValue));
        SaveSetting("Volume", e.NewValue.ToString("F0"));
    }

    private void OnBrightnessChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializing) return;
        _hardwareService?.SetBrightness(Math.Round(e.NewValue));
        SaveSetting("Brightness", e.NewValue.ToString("F0"));
    }

    private void SaveSetting(string key, string value)
    {
        try
        {
            var settings = AppSettingsStore.Load();
            switch (key)
            {
                case "TdpMode": settings.TdpMode = value; break;
                case "ColorMode": settings.ColorMode = int.Parse(value); break;
                case "OverlayPreset": settings.OverlayPreset = int.Parse(value); break;
                case "Volume": settings.Volume = (int)double.Parse(value); break;
                case "Brightness": settings.Brightness = (int)double.Parse(value); break;
            }
            AppSettingsStore.Save(settings);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[OVERLAY] Erro ao salvar setting {key}: {ex.Message}");
        }
    }
}