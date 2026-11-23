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

public partial class OverlayWindow : Window
{
    public static event Action<bool>? OnOverlayVisibilityChanged;

    private bool _lsfgIsActive = false;
    private int _gamePid = 0;

    private readonly IHardwareService _hardwareService;
    private readonly IHidHideService _hidHideService;

    /// <summary>QS 2.0: a ponte só é ligada uma vez.</summary>
    private bool _qsWired;
    private readonly IBrush _activeColor = new SolidColorBrush(Color.FromRgb(124, 92, 252));
    private readonly IBrush _inactiveColor = new SolidColorBrush(Color.FromRgb(37, 42, 54));
    private readonly IBrush _focusBorderColor = new SolidColorBrush(Color.FromRgb(124, 92, 252));

    private bool _isInitializing = true;
    private int _focusIndex = 0;
    private readonly Border[] _sections;
    private string _currentTdp = "Normal";
    private bool _tdpHardwareSupported;
    private readonly LenovoFanCurveService? _fanCurveService;
    private string _currentFanMode = "Automático";
    private int _currentColor = 0;
    private bool _tdpAppliedOnOpen = false;

    private static readonly TimeSpan[] StartupTdpRetryDelays =
        { TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30) };
    private static int _startupTdpRetryRunning;
    private static volatile bool _startupTdpConfirmed;

    private DispatcherTimer? _topmostEnforcer;

    // v0.25.9: reinício do controle agendado pelo fechamento do QS. Fica num
    // campo para poder ser CANCELADO se o painel reabrir antes de ele rodar —
    // antes era um RunOnce solto: reabrir o QS logo depois de fechar fazia o
    // controle ser re-enumerado com o painel aberto, e o painel ficava sem
    // responder até o Windows devolver o dispositivo.
    private IDisposable? _pendingControllerRestart;

    /// <summary>Há reinício de controle agendado e ainda não executado?</summary>
    public bool HasPendingControllerRestart => _pendingControllerRestart is not null;

    /// <summary>Quando o último reinício de controle foi disparado (UTC).</summary>
    public static DateTime? LastControllerRestartUtc { get; private set; }

    private void CancelPendingControllerRestart(string reason)
    {
        if (_pendingControllerRestart is null) return;
        _pendingControllerRestart.Dispose();
        _pendingControllerRestart = null;
        DebugLog.Write($"[HIDHIDE] Reinício do controle agendado CANCELADO ({reason}).");
    }

    private enum SubMenuMode { None, Resolution, RefreshRate, Tdp, TdpCustom, Fan, Color, Hud, Volume, Brightness, Capture, Lossless }

    private const int CustomTdpIndex = 5;
    private int _customWatts = TdpProfiles.DefaultCustomWatts;
    private int _tempCustomWatts = TdpProfiles.DefaultCustomWatts;
    private bool _suppressCustomSlider;
    private DispatcherTimer? _customApplyTimer;
    private int _pendingCustomWatts;
    private TdpLimits _tdpLimits = TdpLimits.Fallback(onAc: true);
    private bool? _lastOnAc;
    private bool _tdpCustomEdited;
    private bool _showUnvalidatedNotice;
    private SubMenuMode _activeSubMenu = SubMenuMode.None;

    private int _currentResIndex = 0;
    private int _currentHzIndex = 0;
    private int _currentHudIndex = 0;

    private int _tempResIndex = 0;
    private int _tempHzIndex = 0;
    private int _tempTdpIndex = 2;
    private int _tempFanIndex = 0;
    private int _tempColorIndex = 0;
    private int _tempHudIndex = 0;
    private int _tempCaptureIndex = 0;

    private int _tempLosslessIndex = 0;
    private int _currentMultiplierIndex = 0;
    private int _tempMultiplierIndex = 0;

    private readonly MainViewModel _viewModel;
    public bool IsAppClosing { get; set; } = false;

    private Button[] _resolutionButtons = null!;
    private Button[] _refreshButtons = null!;
    private Button[] _multiplierButtons = null!;

    private bool _scrollPending = false;

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll", EntryPoint = "IsIconic")] private static extern bool IsMinimizedWindow(IntPtr hWnd);
    private const int SwRestore = 9;

    // v0.25.2 (QS 2.0): o Windows recusa trocar o foreground vindo de um
    // processo que não está em primeiro plano (provado no log de 09/11:
    // "foco devolvido … foreground agora=0x0"). O truque do ALT — manter o
    // VK_MENU "premido" durante a chamada — é o que os overlays usam para
    // contornar essa restrição.
    private const byte VK_MENU = 0x12;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_HIDEWINDOW = 0x0080;
    
    private IntPtr _gameHwnd = IntPtr.Zero;

    [DllImport("user32.dll")] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public HARDWAREINPUT hi; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_SNAPSHOT = 0x2C;

    public OverlayWindow(MainViewModel viewModel, IHidHideService hidHideService)
    {
        _viewModel = viewModel;
        _hidHideService = hidHideService;
        InitializeComponent();

        // Vidro real do Windows (Acrylic) quando o compositor suporta; se não,
        // cai para Blur e, em último caso, para alpha simples. Sem esta lista o
        // Acrylic podia falhar e a janela ficar opaca/preta.
        TransparencyLevelHint = new[]
        {
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Blur,
            WindowTransparencyLevel.Transparent,
        };

        ShowActivated = false;

        _tdpHardwareSupported = OperatingSystem.IsWindows()
            && RyzenSmuService.IsSupportedPlatform()
            && RyzenSmuService.IsSupportedCpu();

        var profile = DeviceDetector.Detect();
        DebugLog.Write($"[OVERLAY] Perfil detectado: {profile.Name} | Validado: {profile.IsValidated}");

        if (!_tdpHardwareSupported)
        {
            BtnUltraEco.IsEnabled = false;
            BtnEco.IsEnabled = false;
            BtnNormal.IsEnabled = false;
            BtnDesempenho.IsEnabled = false;
            BtnBoost.IsEnabled = false;
            BtnTdpCustom.IsEnabled = false;
            TdpCustomPanel.IsEnabled = false;
            DebugLog.Write("[OVERLAY] Controles TDP desabilitados: este equipamento não está na matriz de hardware validado.");
        }
        else if (!profile.IsValidated && profile.Id != "generic")
        {
            _showUnvalidatedNotice = true;
            TxtTdpStatus.Text = profile.ValidationMessage;
            TxtTdpStatus.IsVisible = true;

            if (!profile.SupportsCustomTdp)
            {
                BtnTdpCustom.IsEnabled = false;
                TdpCustomPanel.IsEnabled = false;
            }
        }

        _sections = new[] {
            ResolutionSection, RefreshSection,
            TdpSection, FanSection, ColorSection, HudSection,
            VolumeSection, BrightnessSection, LosslessSection, CaptureSection
        };

        _resolutionButtons = new[] { BtnRes720, BtnRes900, BtnRes1080, BtnRes1200, BtnRes2K, BtnResGo };
        _refreshButtons = new[] { BtnHz60, BtnHz120, BtnHz144, BtnHz240 };
        _multiplierButtons = new[] { BtnMult2x, BtnMult3x, BtnMult4x };

        if (OperatingSystem.IsWindows()) _hardwareService = new WindowsHardwareService();
        else if (OperatingSystem.IsLinux()) _hardwareService = new LinuxHardwareService();
        else _hardwareService = new MockHardwareService();

        try
        {
            var settings = AppSettingsStore.Load();
            
            if (_tdpHardwareSupported)
            {
                var onAc = DeviceTdpLimits.IsOnAcPower();
                _lastOnAc = onAc;
                StartPowerSourceWatch();
                _ = Task.Run(() =>
                {
                    var limits = DeviceTdpLimits.For(onAc);
                    Dispatcher.UIThread.Post(() => OnTdpLimitsChanged(limits));
                });
            }
            UpdateTdpRangeUi();
            _customWatts = TdpProfiles.IsCustom(settings.TdpMode)
                ? TdpProfiles.WattsFor(settings.TdpMode) ?? TdpProfiles.DefaultCustomWatts
                : TdpProfiles.ClampWatts(settings.TdpCustomWatts);
            SetTdpButtonVisual(string.IsNullOrEmpty(settings.TdpMode) ? "Normal" : settings.TdpMode);
            if (OperatingSystem.IsWindows())
            {
                _fanCurveService = new LenovoFanCurveService();
                SmartTdpService.FanApplier = ApplySmartFan; // v0.27: a ventoinha acompanha o TDP inteligente
                if (_tdpHardwareSupported && !_fanCurveService.IsSupportedModel)
                {
                    _showUnvalidatedNotice = true;
                    TxtTdpStatus.Text = UnvalidatedNoticeText();
                    TxtTdpStatus.IsVisible = true;
                }
                if (_fanCurveService.TryRead(out _, out var fanReadMessage))
                {
                    _currentFanMode = string.IsNullOrWhiteSpace(settings.FanMode) ? "Automático" : settings.FanMode;
                    if (_currentFanMode != "Automático")
                    {
                        if (_fanCurveService.TryApplyPreset(_currentFanMode, out var startupFanMessage))
                        {
                            TxtFanStatus.Text = startupFanMessage;
                            StartupRestoreStatus.Report("Ventoinha", $"{_currentFanMode} aplicada", ok: true);
                        }
                        else
                        {
                            StartupRestoreStatus.Report("Ventoinha", $"{_currentFanMode} não aplicou ({startupFanMessage}) — automático mantido", ok: false);
                            _currentFanMode = "Automático";
                            TxtFanStatus.Text = startupFanMessage + " Controle automático mantido.";
                        }
                    }
                    else
                    {
                        TxtFanStatus.Text = fanReadMessage + " Controle automático do firmware ativo.";
                        StartupRestoreStatus.Report("Ventoinha", "Automático (firmware)", ok: true);
                    }
                }
                else
                {
                    _currentFanMode = "Automático";
                    TxtFanStatus.Text = fanReadMessage + " Controle original mantido.";
                    StartupRestoreStatus.Report("Ventoinha", $"curva não lida ({fanReadMessage}) — controle original mantido", ok: !_fanCurveService.IsSupportedModel);
                }
                SetFanButtonVisual(_currentFanMode);
            }
            else
            {
                TxtFanStatus.Text = "Controle de curva disponível no Windows para o Legion Go 2 (8AHP2).";
                SetFanButtonVisual("Automático");
            }
            
            if (!_tdpAppliedOnOpen && _tdpHardwareSupported)
            {
                _tdpAppliedOnOpen = true;
                var savedTdp = string.IsNullOrWhiteSpace(settings.TdpMode) ? "Normal" : settings.TdpMode;
                _ = Task.Run(async () =>
                {
                    DebugLog.Write($"[OVERLAY] Re-aplicando TDP salvo na inicialização: {savedTdp}");
                    _startupTdpConfirmed = false;
                    var ok = _hardwareService.SetTdpMode(savedTdp);
                    DebugLog.Write(ok
                        ? $"[OVERLAY] TDP {savedTdp} re-aplicado com sucesso."
                        : $"[OVERLAY] Falha ao re-aplicar TDP {savedTdp}; veja os logs [PawnIO].");

                    if (ok)
                    {
                        _startupTdpConfirmed = true;
                        StartupRestoreStatus.Report("TDP", $"{savedTdp} confirmado na 1ª tentativa", ok: true);
                        Dispatcher.UIThread.Post(() => _viewModel.UpdateHudTdpStatus(savedTdp));
                        return;
                    }

                    await RetryStartupTdpAsync(savedTdp);
                });
            }

            SetColorButtonVisual(settings.ColorMode);
            if (VolumeSlider != null) VolumeSlider.Value = settings.Volume;
            if (BrightnessSlider != null) BrightnessSlider.Value = settings.Brightness;

            // v0.26: HUD restaurado do que o usuário deixou; sem histórico
            // (instalação nova) começa no preset "Completo".
            _currentHudIndex = settings.OverlayPresetChosen
                ? Math.Clamp(settings.OverlayPreset, 0, PerformanceHud.PresetNames.Length - 1)
                : PerformanceHud.PresetNames.Length - 1;
            _viewModel.SetOverlayPreset(_currentHudIndex);
            SetHudButtonVisual(_currentHudIndex);
            DebugLog.Write($"[HUD] Iniciando no preset '{PerformanceHud.PresetNames[_currentHudIndex]}' (histórico={settings.OverlayPresetChosen}).");

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
            UpdateMultiplierVisual();

            var initialColorName = settings.ColorMode switch
            {
                1 => "Vívido",
                2 => "OLED",
                3 => "Noturno",
                4 => "Confortável",
                _ => "Padrão"
            };
            _viewModel.UpdateHudColorMode(initialColorName);
        }
        catch (Exception ex) { DebugLog.Write($"[OVERLAY] Aviso: {ex.Message}"); }

        Closed += (_, _) => _fanCurveService?.Dispose();
        _isInitializing = false;
        UpdateFocusVisual();
    }

    public void ForceTopmost()
    {
        if (!OperatingSystem.IsWindows() || !IsVisible) return;
        try
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero) return;
            SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch (Exception ex) { DebugLog.Write($"[OVERLAY] ForceTopmost EXCEPTION: {ex.Message}"); }
    }

    private void StartTopmostEnforcer()
    {
        if (_topmostEnforcer != null) return;
        // v0.25.1: 120 ms → 250 ms. Cada tick chamava SetWindowPos e o
        // GuardPosition; a essa frequência o painel repintava a cada instante
        // ("piscadas") sem ganho nenhum de robustez.
        _topmostEnforcer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _topmostEnforcer.Tick += (_, _) =>
        {
            using var __meter = HandheldLauncher.Core.Services.Power.ActivityMeter.Measure("QS por cima");
            ForceTopmost(); GuardPosition();
        };
        _topmostEnforcer.Start();
    }

    private void StopTopmostEnforcer()
    {
        _topmostEnforcer?.Stop();
        _topmostEnforcer = null;
    }

    public void SetGameHwnd(IntPtr hwnd)
    {
        bool wasGame = _gameHwnd != IntPtr.Zero;
        _gameHwnd = hwnd;

        if (wasGame && hwnd == IntPtr.Zero)
        {
            _lsfgIsActive = false;
            UpdateLsfgButtonState();
        }
    }
}
