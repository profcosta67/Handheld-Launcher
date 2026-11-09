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

            _currentHudIndex = settings.OverlayPreset;
            SetHudButtonVisual(_currentHudIndex);

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
        _topmostEnforcer.Tick += (_, _) => { ForceTopmost(); GuardPosition(); };
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

    public async void ReapplyAfterSuspendResume()
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

    public void SetGamePid(int pid)
    {
        _gamePid = pid;
        DebugLog.Write($"[OVERLAY] GamePid registrado: {pid}");
    }

     protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        DebugLog.Write($"[OVERLAY] OnOpened chamado. Width={Width}, Height={Height}");

        // 🟢 INTEGRAÇÃO HIDHIDE (B7): Esconde o controle apenas se o toggle estiver ligado
        if (_gamePid > 0 && AppSettingsStore.Load().IsolateControllerOnOverlay)
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

        if (OperatingSystem.IsWindows())
        {
            var h = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (h != IntPtr.Zero)
            {
                var ex = GetWindowLong(h, GWL_EXSTYLE);
                SetWindowLong(h, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
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

            var wa = screen.WorkingArea;
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

            var wa = screen.WorkingArea;
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

    public void SetInitialPosition()
    {
        try
        {
            var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
            if (screen == null) return;

            var wa = screen.WorkingArea;
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
        DebugLog.Write($"[OVERLAY-CLOSE] origem=HideWithUnblock fullscreen-contexto; overlay={TryGetPlatformHandle()?.Handle}, game={_gameHwnd}, foreground={GetForegroundWindow()}, visible={IsVisible}, submenu={_activeSubMenu}");
        
        if (_activeSubMenu == SubMenuMode.TdpCustom) CancelTdpCustomEdit();
        FlushPendingCustomApply();
        
        StopTopmostEnforcer();
             
        Hide();

        // 🟢 INTEGRAÇÃO HIDHIDE (B7): Devolve o controle para o jogo
        if (_gamePid > 0 && AppSettingsStore.Load().IsolateControllerOnOverlay)
        {
            _hidHideService.RestoreControllerForProcess(_gamePid);
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
        NewQsPanel.ResolutionRequested += (_, button) => ApplyResolutionFromQs(button.Tag?.ToString() ?? string.Empty);
        NewQsPanel.FanRequested += (_, button) => OnFanClick(button, new RoutedEventArgs());
        NewQsPanel.ColorRequested += (_, button) => OnColorClick(button, new RoutedEventArgs());
        NewQsPanel.MultiplierRequested += (_, button) => OnMultiplierClick(button, new RoutedEventArgs());
        NewQsPanel.PrintRequested += (_, _) => OnScreenshotClick(this, new RoutedEventArgs());
        NewQsPanel.RecordRequested += (_, _) => OnRecordClick(this, new RoutedEventArgs());
        NewQsPanel.LosslessToggled += (_, _) => OnLsfgToggleClick(this, new RoutedEventArgs());
        NewQsPanel.CloseRequested += (_, _) =>
        {
            DebugLog.Write("[QS] Fecho pedido pelo painel 2.0 (✕ / B / Fechar Menu).");
            HideWithUnblock();
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

        // Só avisa quando o TDP não vai mesmo aplicar.
        if (!HandheldLauncher.App.Platform.ElevationService.IsElevated)
            NewQsPanel.ShowElevationWarning("Modo padrão: o TDP não aplica. Reinicia como administrador.");
    }

    /// <summary>
    private void RestoreForegroundAfterClose()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var target = _gameHwnd;

            // Nalguns fechos o handle do jogo vem a 0x0 (a janela dele foi
            // recriada ou ainda não estava pronta) — procurar pelo PID.
            if (target == IntPtr.Zero && _gamePid > 0)
            {
                try
                {
                    using var process = System.Diagnostics.Process.GetProcessById(_gamePid);
                    target = process.MainWindowHandle;
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

        _ = Task.Run(() =>
        {
            bool success = false;
            lock (WindowsHardwareService.TdpApplyLock)
            {
                success = _hardwareService.SetTdpMode(mode);
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
        }, TimeSpan.FromMilliseconds(1200));

        DebugLog.Write($"[TELA] QS 2.0: {w}x{h}@{hz}Hz aplicado.");
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

    private void OnFanClick(object? sender, RoutedEventArgs e) { if (sender is Button button) ApplyFanMode(button.Tag?.ToString() ?? "Automático"); }

    private void ApplyFanMode(string mode)
    {
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
        else TxtFanStatus.Text = message + " Curva anterior preservada.";
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

    private void OnVolumeChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializing) return;
        _hardwareService?.SetVolume(Math.Round(e.NewValue)); SaveSetting("Volume", e.NewValue.ToString("F0"));
    }

    private void OnBrightnessChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializing) return;
        _hardwareService?.SetBrightness(Math.Round(e.NewValue)); SaveSetting("Brightness", e.NewValue.ToString("F0"));
    }

    private async void OnScreenshotClick(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows()) return;

        var wasVisible = IsVisible;
        try
        {
            if (wasVisible) HideWithUnblock();
            await Task.Delay(250);

            SendKey(VK_LWIN, false);
            await Task.Delay(80);
            SendKey(VK_SNAPSHOT, false);
            await Task.Delay(80);
            SendKey(VK_SNAPSHOT, true);
            await Task.Delay(80);
            SendKey(VK_LWIN, true);

            DebugLog.Write("[CAPTURA] Atalho Win+PrintScreen enviado após ocultar o overlay.");
            if (BtnScreenshot != null) BtnScreenshot.Content = "Captura enviada";
            await Task.Delay(1500);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[CAPTURA] Erro: {ex.GetType().Name}: {ex.Message}");
            if (BtnScreenshot != null) BtnScreenshot.Content = "Falha";
            await Task.Delay(1200);
        }
        finally
        {
            if (BtnScreenshot != null) BtnScreenshot.Content = "📷 Print";
            if (wasVisible && !IsAppClosing)
            {
                Show();
                ForceTopmost();
            }
        }
    }

    private void OnRecordClick(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (_hardwareService is WindowsHardwareService winService)
        {
            winService.ToggleScreenRecording();
            _viewModel.UpdateHudRecordingState(winService.IsRecording);
            UpdateRecordButtonState();
        }
    }
    
    private void UpdateRecordButtonState()
    {
        if (_hardwareService is WindowsHardwareService winService)
        {
            bool isRecording = winService.IsRecording;

            if (BtnRecord != null)
            {
                if (isRecording)
                {
                    BtnRecord.Content = "⏹ Parar";
                    BtnRecord.Background = new SolidColorBrush(Color.FromRgb(255, 59, 48));
                    BtnRecord.Foreground = Brushes.White;
                }
                else
                {
                    BtnRecord.Content = "🎥 Gravar";
                    BtnRecord.Background = _inactiveColor;
                    BtnRecord.Foreground = Brushes.White;
                }
            }

            if (RecordingBadge != null)
            {
                RecordingBadge.IsVisible = isRecording;
            }
        }
    }

    private async void OnLsfgToggleClick(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows()) return;

        if (BtnLsfgToggle != null) BtnLsfgToggle.IsEnabled = false;

        try
        {
            if (!LosslessScalingService.IsLosslessRunning())
            {
                DebugLog.Write("[PATINHO] Lossless não está rodando — iniciando em segundo plano.");

                var wasVisible = IsVisible;
                if (wasVisible) HideWithUnblock();

                if (LsfgStatusText != null) LsfgStatusText.Text = "Iniciando Lossless...";

                if (!LosslessScalingService.TryLaunchLossless())
                {
                    if (LsfgStatusText != null) LsfgStatusText.Text = "❌ Falha ao iniciar Lossless";
                    if (wasVisible) { Show(); ForceTopmost(); }
                    return;
                }

                for (int i = 0; i < 20; i++)
                {
                    await Task.Delay(500);
                    if (LosslessScalingService.IsLosslessRunning()) break;
                }
                await Task.Delay(1500);

                if (wasVisible && !IsAppClosing)
                {
                    Show();
                    ForceTopmost();
                    await Task.Delay(300);
                }
            }

            if (LsfgStatusText != null) LsfgStatusText.Text = "Enviando atalho...";

            bool success = LosslessScalingService.ToggleFrameGen();

            if (success)
            {
                _lsfgIsActive = !_lsfgIsActive;
                UpdateLsfgToggleVisual();
                DebugLog.Write($"[PATINHO] Toggle OK — estado: {(_lsfgIsActive ? "ativado" : "desativado")}");
            }
            else
            {
                if (LsfgStatusText != null) LsfgStatusText.Text = "❌ Falha ao enviar atalho";
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[PATINHO] Toggle EXCEPTION: {ex.Message}");
            if (LsfgStatusText != null) LsfgStatusText.Text = $"❌ Erro: {ex.Message}";
        }
        finally
        {
            if (BtnLsfgToggle != null) BtnLsfgToggle.IsEnabled = true;
            if (!IsVisible && !IsAppClosing)
            {
                Show();
                ForceTopmost();
            }
        }
    }

    private void UpdateLsfgToggleVisual()
    {
        if (BtnLsfgToggle == null) return;

        if (_lsfgIsActive)
        {
            BtnLsfgToggle.Content = "🟢 Ativado";
            BtnLsfgToggle.Background = new SolidColorBrush(Color.FromRgb(46, 151, 98));
        }
        else
        {
            BtnLsfgToggle.Content = "⚪ Desativado";
            BtnLsfgToggle.Background = new SolidColorBrush(Color.FromRgb(42, 42, 58));
        }

        if (LsfgStatusText != null)
        {
            LsfgStatusText.Text = _lsfgIsActive ? "Frame Generation ativo" : "Frame Generation inativo";
        }
    }

    private void UpdateLsfgButtonState()
    {
        if (BtnLsfgToggle == null) return;

        bool running = LosslessScalingService.IsLosslessRunning();
        bool activeContext = running && _gameHwnd != IntPtr.Zero && _lsfgIsActive;

        if (!running)
        {
            BtnLsfgToggle.Content = "Abrir Lossless";
            BtnLsfgToggle.Background = _inactiveColor;
            BtnLsfgToggle.IsEnabled = true;
        }
        else if (activeContext)
        {
            BtnLsfgToggle.Content = "🟢 Desativar";
            BtnLsfgToggle.Background = new SolidColorBrush(Color.FromRgb(46, 151, 98));
            BtnLsfgToggle.IsEnabled = true;
        }
        else
        {
            BtnLsfgToggle.Content = "Aplicar";
            BtnLsfgToggle.Background = _inactiveColor;
            BtnLsfgToggle.IsEnabled = true;
        }
    }

    private void OnLsfgOpenClick(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var path = LosslessScalingService.GetExecutablePath();
            if (!string.IsNullOrEmpty(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            else
            {
                DebugLog.Write("[PATINHO] Caminho do Lossless Scaling não encontrado.");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[PATINHO] Erro ao abrir Lossless Scaling: {ex.Message}");
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
                case "TdpCustomWatts": settings.TdpCustomWatts = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "FanMode": settings.FanMode = value; break;
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