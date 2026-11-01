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

    private enum SubMenuMode { None, Resolution, RefreshRate }
    private SubMenuMode _activeSubMenu = SubMenuMode.None;
    private int _currentResIndex = 0;
    private int _currentHzIndex = 0;
    private int _tempResIndex = 0;
    private int _tempHzIndex = 0;

    private readonly MainViewModel _viewModel;
    public bool IsAppClosing { get; set; } = false; 

    private Button[] _resolutionButtons = null!;
    private Button[] _refreshButtons = null!;

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public HARDWAREINPUT hi; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_SNAPSHOT = 0x2C;

    public OverlayWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent(); 

        _sections = new[] { 
            ResolutionSection, RefreshSection, 
            TdpSection, ColorSection, HudSection, 
            VolumeSection, BrightnessSection, 
            CaptureSection 
        };

        _resolutionButtons = new[] { BtnRes720, BtnRes900, BtnRes1080, BtnRes1200, BtnRes2K, BtnResGo };
        _refreshButtons = new[] { BtnHz60, BtnHz120, BtnHz144, BtnHz240 };

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

            string displayInfo = _hardwareService.GetDisplayInfo();
            if (displayInfo.Contains("x") && displayInfo.Contains("Hz"))
            {
                var parts = displayInfo.Split(' ');
                var resParts = parts[0].Split('x');
                if (resParts.Length == 2 && int.TryParse(resParts[0], out var w) && int.TryParse(resParts[1], out var h))
                {
                    for (int i = 0; i < _resolutionButtons.Length; i++)
                        if (_resolutionButtons[i].Tag?.ToString() == $"{w}x{h}") { _currentResIndex = i; break; }
                }
                if (parts.Length > 1 && int.TryParse(parts[1].Replace("Hz", ""), out var hz))
                {
                    for (int i = 0; i < _refreshButtons.Length; i++)
                        if (_refreshButtons[i].Tag?.ToString() == hz.ToString()) { _currentHzIndex = i; break; }
                }
            }
            UpdatePersistentVisuals();
            UpdateScreenInfoText();
        }
        catch (Exception ex) { DebugLog.Write($"[OVERLAY] Aviso: {ex.Message}"); }

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
        if (_activeSubMenu != SubMenuMode.None)
        {
            switch (action)
            {
                case GamepadAction.MoveLeft:
                case GamepadAction.MoveUp:
                    if (_activeSubMenu == SubMenuMode.Resolution) _tempResIndex = Math.Max(0, _tempResIndex - 1);
                    else _tempHzIndex = Math.Max(0, _tempHzIndex - 1);
                    UpdateTempVisuals();
                    break;
                case GamepadAction.MoveRight:
                case GamepadAction.MoveDown:
                    if (_activeSubMenu == SubMenuMode.Resolution) _tempResIndex = Math.Min(_resolutionButtons.Length - 1, _tempResIndex + 1);
                    else _tempHzIndex = Math.Min(_refreshButtons.Length - 1, _tempHzIndex + 1);
                    UpdateTempVisuals();
                    break;
                case GamepadAction.Confirm:
                    if (_activeSubMenu == SubMenuMode.Resolution) _currentResIndex = _tempResIndex;
                    else _currentHzIndex = _tempHzIndex;
                    ApplyDisplayMode();
                    _activeSubMenu = SubMenuMode.None;
                    UpdateFocusVisual();
                    UpdatePersistentVisuals();
                    break;
                case GamepadAction.Back:
                    _activeSubMenu = SubMenuMode.None;
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
                if (_focusIndex == 0) 
                {
                    _tempResIndex = _currentResIndex;
                    _activeSubMenu = SubMenuMode.Resolution;
                    UpdateTempVisuals();
                }
                else if (_focusIndex == 1)
                {
                    _tempHzIndex = _currentHzIndex;
                    _activeSubMenu = SubMenuMode.RefreshRate;
                    UpdateTempVisuals();
                }
                else
                {
                    HandleConfirm();
                }
                break;
        }
    }

    private void UpdateFocusVisual()
    {
        for (int i = 0; i < _sections.Length; i++)
            _sections[i].BorderBrush = (i == _focusIndex) ? _focusBorderColor : Brushes.Transparent;

        // 🟢 Rola o ScrollViewer para mostrar a seção focada
        ScrollToFocusedSection();
    }

    // 🟢 NOVO: Scroll automático para a seção focada
    private void ScrollToFocusedSection()
    {
        if (MainScrollViewer == null || _sections.Length == 0) return;
        if (_focusIndex < 0 || _focusIndex >= _sections.Length) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var focused = _sections[_focusIndex];
                var position = focused.TranslatePoint(new Point(0, 0), MainScrollViewer);
                if (position is null) return;

                var viewTop = MainScrollViewer.Offset.Y;
                var viewBottom = viewTop + MainScrollViewer.Viewport.Height;
                var itemTop = position.Value.Y;
                var itemBottom = itemTop + focused.Bounds.Height;

                if (itemTop < viewTop)
                    MainScrollViewer.Offset = new Vector(MainScrollViewer.Offset.X, itemTop);
                else if (itemBottom > viewBottom)
                    MainScrollViewer.Offset = new Vector(MainScrollViewer.Offset.X, itemBottom - MainScrollViewer.Viewport.Height);
            }
            catch { }
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void HandleHorizontalInput(int direction)
    {
        switch (_focusIndex)
        {
            case 2: // TDP
                string[] tdpModes = { "UltraEco", "Eco", "Normal", "Desempenho", "Boost" };
                int currentIndex = Array.IndexOf(tdpModes, _currentTdp);
                if (currentIndex < 0) currentIndex = 2;
                int nextIndex = Math.Clamp(currentIndex + Math.Sign(direction), 0, tdpModes.Length - 1);
                string newTdp = tdpModes[nextIndex];
                if (newTdp != _currentTdp) {
                    if (_hardwareService.SetTdpMode(newTdp)) {
                        SetTdpButtonVisual(newTdp);
                        SaveSetting("TdpMode", newTdp);
                    } else {
                        DebugLog.Write($"[OVERLAY] O perfil TDP {newTdp} não foi aplicado; seleção anterior mantida.");
                    }
                }
                break;
            case 3: // Cor
                var newColorIndex = Math.Clamp(_currentColor + direction, 0, 3);
                string mode = newColorIndex switch { 1 => "Vívido", 2 => "OLED (Alto Contraste)", 3 => "Noturno (Luz Quente)", _ => "Padrão" };
                if (_hardwareService.SetColorMode(mode)) {
                    SetColorButtonVisual(newColorIndex);
                    SaveSetting("ColorMode", newColorIndex.ToString());
                } else {
                    DebugLog.Write($"[OVERLAY] O modo de cor {mode} não foi aplicado; seleção anterior mantida.");
                }
                break;
            case 4: // HUD
                if (_viewModel.IsPerformanceHudEnabled) {
                    var newHudIndex = Math.Clamp(_viewModel.CurrentHudPreset + direction, 0, 3);
                    _viewModel.SetOverlayPreset(newHudIndex);
                    SetHudButtonVisual(newHudIndex);
                    SaveSetting("OverlayPreset", newHudIndex.ToString());
                }
                break;
            case 5: // Volume
                if (VolumeSlider != null) VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + (direction * 5), 0, 100);
                break;
            case 6: // Brilho
                if (BrightnessSlider != null) BrightnessSlider.Value = Math.Clamp(BrightnessSlider.Value + (direction * 5), 0, 100);
                break;
            case 7: // Captura
                if (direction < 0) OnScreenshotClick(null, new RoutedEventArgs());
                else if (direction > 0) OnRecordClick(null, new RoutedEventArgs());
                break;
        }
    }

    private void HandleConfirm()
    {
        if (_focusIndex == 4) {
            var nextIndex = (_viewModel.CurrentHudPreset + 1) % 4;
            _viewModel.SetOverlayPreset(nextIndex);
            SetHudButtonVisual(nextIndex);
            SaveSetting("OverlayPreset", nextIndex.ToString());
        }
        else if (_focusIndex == 7)
            OnScreenshotClick(null, new RoutedEventArgs());
    }

    private void UpdatePersistentVisuals()
    {
        for (int i = 0; i < _resolutionButtons.Length; i++)
            _resolutionButtons[i].Background = (i == _currentResIndex) ? _activeColor : _inactiveColor;
        for (int i = 0; i < _refreshButtons.Length; i++)
            _refreshButtons[i].Background = (i == _currentHzIndex) ? _activeColor : _inactiveColor;
    }

    private void UpdateTempVisuals()
    {
        if (_activeSubMenu == SubMenuMode.Resolution)
        {
            for (int i = 0; i < _resolutionButtons.Length; i++)
                _resolutionButtons[i].Background = (i == _tempResIndex) ? _activeColor : _inactiveColor;
        }
        else if (_activeSubMenu == SubMenuMode.RefreshRate)
        {
            for (int i = 0; i < _refreshButtons.Length; i++)
                _refreshButtons[i].Background = (i == _tempHzIndex) ? _activeColor : _inactiveColor;
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
        }
    }

    private void UpdateScreenInfoText()
    {
        if (TxtScreenInfo != null) TxtScreenInfo.Text = _hardwareService.GetDisplayInfo();
    }

    private void OnResolutionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            for (int i = 0; i < _resolutionButtons.Length; i++)
                if (_resolutionButtons[i] == btn) { _currentResIndex = i; break; }
            ApplyDisplayMode();
            UpdatePersistentVisuals();
        }
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            for (int i = 0; i < _refreshButtons.Length; i++)
                if (_refreshButtons[i] == btn) { _currentHzIndex = i; break; }
            ApplyDisplayMode();
            UpdatePersistentVisuals();
        }
    }

    private void OnTdpClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedButton) return;
        var mode = clickedButton.Tag?.ToString() ?? "Normal";
        if (_hardwareService.SetTdpMode(mode))
        {
            SetTdpButtonVisual(mode);
            SaveSetting("TdpMode", mode);
        }
        else
        {
            DebugLog.Write($"[OVERLAY] Falha ao aplicar o perfil TDP {mode}; estado visual não foi alterado.");
        }
    }

    private void SetTdpButtonVisual(string activeMode)
    {
        if (BtnUltraEco != null) BtnUltraEco.Background = activeMode == "UltraEco" ? _activeColor : _inactiveColor;
        if (BtnEco != null) BtnEco.Background = activeMode == "Eco" ? _activeColor : _inactiveColor;
        if (BtnNormal != null) BtnNormal.Background = activeMode == "Normal" ? _activeColor : _inactiveColor;
        if (BtnDesempenho != null) BtnDesempenho.Background = activeMode == "Desempenho" ? _activeColor : _inactiveColor;
        if (BtnBoost != null) BtnBoost.Background = activeMode == "Boost" ? _activeColor : _inactiveColor;
        _currentTdp = activeMode;
    }

    private void OnColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedButton) return;
        if (int.TryParse(clickedButton.Tag?.ToString(), out int index)) {
            string mode = index switch { 1 => "Vívido", 2 => "OLED (Alto Contraste)", 3 => "Noturno (Luz Quente)", _ => "Padrão" };
            if (_hardwareService.SetColorMode(mode))
            {
                SetColorButtonVisual(index);
                SaveSetting("ColorMode", index.ToString());
            }
            else
            {
                DebugLog.Write($"[OVERLAY] Falha ao aplicar modo de cor {mode}; estado visual não foi alterado.");
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
        DebugLog.Write($"[OVERLAY] Evento do slider de volume: {e.NewValue:F0}%.");
        _hardwareService?.SetVolume(Math.Round(e.NewValue));
        SaveSetting("Volume", e.NewValue.ToString("F0"));
    }

    private void OnBrightnessChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializing) return;
        DebugLog.Write($"[OVERLAY] Evento do slider de brilho: {e.NewValue:F0}%.");
        _hardwareService?.SetBrightness(Math.Round(e.NewValue));
        SaveSetting("Brightness", e.NewValue.ToString("F0"));
    }

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
            timer.Tick += (_, _) => { if (BtnScreenshot != null) BtnScreenshot.Content = "📷 Tirar Print"; timer.Stop(); };
            timer.Start();
        }
        catch (Exception ex) { DebugLog.Write($"[CAPTURA] Erro: {ex.Message}"); }
    }

    private void OnRecordClick(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (_hardwareService is WindowsHardwareService winService)
        {
            winService.ToggleScreenRecording();
            if (BtnRecord != null) 
            {
                bool isRecording = winService.IsRecording;
                BtnRecord.Content = isRecording ? "🎥 Parar" : "🎥 Gravar Tela";
                BtnRecord.Background = isRecording ? new SolidColorBrush(Color.FromRgb(255, 64, 64)) : _inactiveColor;
            }
        }
    }

    private void SendKey(ushort key, bool keyUp)
    {
        var input = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = key, dwFlags = keyUp ? KEYEVENTF_KEYUP : 0 } } };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private void SaveSetting(string key, string value)
    {
        try
        {
            var settings = AppSettingsStore.Load();
            switch (key) {
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