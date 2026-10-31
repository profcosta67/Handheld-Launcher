using System;
using System.Runtime.InteropServices;
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
    private int _currentColor = 0;
    
    private readonly MainViewModel _viewModel;
    public bool IsAppClosing { get; set; } = false; 

    // 🟢 IMPORTAÇÕES DO WINDOWS PARA SIMULAR TECLAS
    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_R = 0x52;
    private const ushort VK_SNAPSHOT = 0x2C;

    public OverlayWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent(); 

        _sections = new[] { TdpSection, ColorSection, HudSection, VolumeSection, CaptureSection };

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
            SetColorButtonVisual(settings.ColorMode);
            if (VolumeSlider != null) VolumeSlider.Value = settings.Volume;
            if (BrightnessSlider != null) BrightnessSlider.Value = settings.Brightness;
            SetHudButtonVisual(settings.OverlayPreset);
        }
        catch (Exception ex) { DebugLog.Write($"[OVERLAY] Aviso ao carregar settings: {ex.Message}"); }

        _isInitializing = false;
        UpdateFocusVisual();
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
        if (!IsAppClosing) { e.Cancel = true; Hide(); }
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
                    _hardwareService?.SetTdpMode(newTdp);
                    SetTdpButtonVisual(newTdp);
                    SaveSetting("TdpMode", newTdp);
                }
                break;

            case 1: // Cor
                var newColorIndex = Math.Clamp(_currentColor + direction, 0, 3);
                string mode = newColorIndex switch
                {
                    1 => "Vívido",
                    2 => "OLED (Alto Contraste)",
                    3 => "Noturno (Luz Quente)",
                    _ => "Padrão"
                };
                _hardwareService?.SetColorMode(mode);
                SetColorButtonVisual(newColorIndex);
                SaveSetting("ColorMode", newColorIndex.ToString());
                break;

            case 2: // HUD
                if (_viewModel.IsPerformanceHudEnabled)
                {
                    var newHudIndex = Math.Clamp(_viewModel.CurrentHudPreset + direction, 0, 3);
                    _viewModel.SetOverlayPreset(newHudIndex);
                    SetHudButtonVisual(newHudIndex);
                    SaveSetting("OverlayPreset", newHudIndex.ToString());
                }
                break;

            case 3: // Volume
                if (VolumeSlider != null) VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + (direction * 5), 0, 100);
                break;

            case 4: // Captura
                if (direction < 0) OnScreenshotClick(null, new RoutedEventArgs());
                else if (direction > 0) OnRecordClick(null, new RoutedEventArgs());
                break;
        }
    }

    private void HandleConfirm()
    {
        if (_focusIndex == 2) // HUD
        {
            var nextIndex = (_viewModel.CurrentHudPreset + 1) % 4;
            _viewModel.SetOverlayPreset(nextIndex);
            SetHudButtonVisual(nextIndex);
            SaveSetting("OverlayPreset", nextIndex.ToString());
        }
    }

    private void OnTdpClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedButton) return;
        var mode = clickedButton.Tag?.ToString() ?? "Normal";
        
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

    // 🟢 MÉTODOS DO MODO DE COR (BOTÕES)
    private void OnColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedButton) return;
        if (int.TryParse(clickedButton.Tag?.ToString(), out int index))
        {
            string mode = index switch
            {
                1 => "Vívido",
                2 => "OLED (Alto Contraste)",
                3 => "Noturno (Luz Quente)",
                _ => "Padrão"
            };
            _hardwareService?.SetColorMode(mode);
            SetColorButtonVisual(index);
            SaveSetting("ColorMode", index.ToString());
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
        if (int.TryParse(clickedButton.Tag?.ToString(), out int preset))
        {
            _viewModel.SetOverlayPreset(preset);
            SetHudButtonVisual(preset);
            SaveSetting("OverlayPreset", preset.ToString());
        }
    }

    private void SetHudButtonVisual(int preset)
    {
        if (BtnHudOff != null) BtnHudOff.Background = preset == 0 ? _activeColor : _inactiveColor;
        if (BtnHudMin != null) BtnHudMin.Background = preset == 1 ? _activeColor : _inactiveColor;
        if (BtnHudMed != null) BtnHudMed.Background = preset == 2 ? _activeColor : _inactiveColor;
        if (BtnHudFull != null) BtnHudFull.Background = preset == 3 ? _activeColor : _inactiveColor;
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

    // 🟢 PRINT (Win+PrintScreen)
    private void OnScreenshotClick(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            SendKey(VK_LWIN, false);
            SendKey(VK_SNAPSHOT, false);
            SendKey(VK_SNAPSHOT, true);
            SendKey(VK_LWIN, true);

            DebugLog.Write("[CAPTURA] Atalho Win+PrintScreen simulado.");
            
            if (BtnScreenshot != null) BtnScreenshot.Content = "Salvo!";
            var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) => { if (BtnScreenshot != null) BtnScreenshot.Content = "Tirar Print"; timer.Stop(); };
            timer.Start();
        }
        catch (Exception ex) { DebugLog.Write($"[CAPTURA] Erro: {ex.Message}"); }
    }

    // 🟢 GRAVAR (Win+Alt+R)
    private void OnRecordClick(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            SendKey(VK_LWIN, false);
            SendKey(VK_MENU, false);
            SendKey(VK_R, false);
            SendKey(VK_R, true);
            SendKey(VK_MENU, true);
            SendKey(VK_LWIN, true);

            DebugLog.Write("[CAPTURA] Atalho Win+Alt+R simulado.");
            
            if (BtnRecord != null) BtnRecord.Content = "Gravando...";
            var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) => { if (BtnRecord != null) BtnRecord.Content = "Gravar Tela"; timer.Stop(); };
            timer.Start();
        }
        catch (Exception ex) { DebugLog.Write($"[CAPTURA] Erro: {ex.Message}"); }
    }

    private void SendKey(ushort key, bool keyUp)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = key,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
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
        catch (Exception ex) { DebugLog.Write($"[OVERLAY] Erro ao salvar setting {key}: {ex.Message}"); }
    }
}