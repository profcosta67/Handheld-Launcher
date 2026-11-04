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

namespace HandheldLauncher.App.Views;

public partial class OverlayWindow : Window
{
    private bool _lsfgIsActive = false;
    private int _gamePid = 0;

    private readonly IHardwareService _hardwareService;
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

    // v0.16.2: no boot, o firmware/serviço da Lenovo às vezes reaplica os
    // próprios limites logo depois do launcher (log real: 1ª leitura 20W,
    // seguintes 45W/37W) e a restauração desistia. Agora tenta de novo
    // algumas vezes, com intervalo. Estático porque a OverlayWindow é
    // recriada na primeira abertura do Quick Settings — só uma sequência de
    // novas tentativas por sessão.
    private static readonly TimeSpan[] StartupTdpRetryDelays =
        { TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30) };
    private static int _startupTdpRetryRunning;
    private static volatile bool _startupTdpConfirmed;
    private readonly bool _freezeGameOnOverlay;

    // 🟢 ENFORCER: mantém o overlay sempre no topo enquanto visível
    private DispatcherTimer? _topmostEnforcer;

    private enum SubMenuMode { None, Resolution, RefreshRate, Tdp, Fan, Color, Hud, Volume, Brightness, Capture, Lossless }
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

    // 🟢 Lossless: índice 0 = botão toggle, índice 1 = multiplicador
    private int _tempLosslessIndex = 0;
    private int _currentMultiplierIndex = 0; // 0 = 2x, 1 = 3x, 2 = 4x
    private int _tempMultiplierIndex = 0;

    private readonly MainViewModel _viewModel;
    public bool IsAppClosing { get; set; } = false;

    private Button[] _resolutionButtons = null!;
    private Button[] _refreshButtons = null!;
    private Button[] _multiplierButtons = null!;

    // 🟢 FIX: Controla se um scroll está pendente para evitar fila dupla
    private bool _scrollPending = false;

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

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

    public OverlayWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        ShowActivated = false;

        _tdpHardwareSupported = OperatingSystem.IsWindows()
            && RyzenSmuService.IsSupportedPlatform()
            && RyzenSmuService.IsSupportedCpu();
        if (!_tdpHardwareSupported)
        {
            BtnUltraEco.IsEnabled = false;
            BtnEco.IsEnabled = false;
            BtnNormal.IsEnabled = false;
            BtnDesempenho.IsEnabled = false;
            BtnBoost.IsEnabled = false;
            DebugLog.Write("[OVERLAY] Controles TDP desabilitados: este equipamento não está na matriz de hardware validado.");
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
            _freezeGameOnOverlay = settings.FreezeGameOnOverlay;
            SetTdpButtonVisual(string.IsNullOrEmpty(settings.TdpMode) ? "Normal" : settings.TdpMode);
            if (OperatingSystem.IsWindows())
            {
                _fanCurveService = new LenovoFanCurveService();
                if (_tdpHardwareSupported && !_fanCurveService.IsSupportedModel)
                {
                    // TDP continua liberado em qualquer Ryzen Zen 4 Mobile
                    // (Phoenix/Hawk Point) — não só no Legion Go 2 — pra não
                    // travar o recurso em aparelhos como ROG Ally/Ally X, que
                    // usam o mesmo silício. Só avisamos que este modelo
                    // específico ainda não foi validado, na mesma faixa
                    // (5W-30W) que ferramentas como RyzenAdj já usam nele.
                    TxtTdpStatus.Text = "Ainda não validado neste modelo específico; comandos ficam entre 5W e 30W.";
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
            // v0.16: re-aplica o TDP salvo já na construção da janela (não só
            // quando o usuário abre o Quick Settings pela primeira vez), pro
            // aparelho voltar com o último perfil mesmo que ele nunca abra o
            // overlay depois de reiniciar/desligar o console. Mesma lógica que
            // já existia em OnOpened, só que agora roda em paralelo com a
            // restauração da curva de ventoinha acima, no boot do launcher.
            if (!_tdpAppliedOnOpen && _tdpHardwareSupported)
            {
                _tdpAppliedOnOpen = true;
                var savedTdp = string.IsNullOrWhiteSpace(settings.TdpMode) ? "Normal" : settings.TdpMode;
                _ = Task.Run(async () =>
                {
                    DebugLog.Write($"[OVERLAY] Re-aplicando TDP salvo na inicialização: {savedTdp}");
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
                4 => "Confortável", // v0.16.2: modo que só existe nas Configurações
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
        _topmostEnforcer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _topmostEnforcer.Tick += (_, _) => ForceTopmost();
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

                // Nunca passar por cima de uma escolha feita no Quick Settings
                // enquanto esperávamos.
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
        UpdateLsfgStatus();

        Dispatcher.UIThread.Post(() =>
        {
            PositionOnRightEdge();
            ForceTopmost();
        }, DispatcherPriority.Loaded);

        var retryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        retryTimer.Tick += (_, _) =>
        {
            retryTimer.Stop();
            PositionOnRightEdge();
            ForceTopmost();
            UpdateLsfgStatus();
        };
        retryTimer.Start();

        StartTopmostEnforcer();

        if (_freezeGameOnOverlay) InputBlockService.BlockGamepadForGame(_gamePid);

        if (_hardwareService is WindowsHardwareService winSvcForSync)
        {
            _viewModel.UpdateHudRecordingState(winSvcForSync.IsRecording);
        }
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
            if (screen == null) { DebugLog.Write("[OVERLAY] PositionOnRightEdge: screen null"); return; }

            var wa = screen.WorkingArea;
            var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;

            double actualWidth = 0;
            double actualHeight = 0;

            if (Bounds.Width > 0 && !double.IsNaN(Bounds.Width)) { actualWidth = Bounds.Width; actualHeight = Bounds.Height; }
            else if (ClientSize.Width > 0) { actualWidth = ClientSize.Width; actualHeight = ClientSize.Height; }
            else if (Width > 0 && !double.IsNaN(Width)) { actualWidth = Width; actualHeight = Height; }
            else { actualWidth = 480; actualHeight = 600; }

            if (double.IsNaN(Width) || Width <= 0) Width = actualWidth;
            if (double.IsNaN(Height) || Height <= 0) Height = actualHeight;

            var widthPx = (int)(actualWidth * scaling);
            var heightPx = (int)(actualHeight * scaling);

            if (widthPx <= 0 || heightPx <= 0) return;

            var x = wa.X + wa.Width - widthPx - 20;
            var y = wa.Y + (wa.Height - heightPx) / 2;

            Position = new PixelPoint(x, y);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[OVERLAY] PositionOnRightEdge EXCEPTION: {ex.Message}");
        }
    }

    public void HideWithUnblock()
    {
        StopTopmostEnforcer();
        InputBlockService.UnblockGamepadForGame();
        Hide();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!IsAppClosing) { e.Cancel = true; HideWithUnblock(); }
        base.OnClosing(e);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => HideWithUnblock();

    public void HandleGamepadAction(GamepadAction action)
    {
        if (action == GamepadAction.ToggleQuickSettings) { HideWithUnblock(); return; }

        if (_activeSubMenu != SubMenuMode.None)
        {
            // 🟢 Tratamento especial para o Lossless (navegação interna toggle ⇄ multiplicador)
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
            case GamepadAction.GlobalMenu: HideWithUnblock(); break;
            case GamepadAction.MoveUp: _focusIndex = Math.Max(0, _focusIndex - 1); UpdateFocusVisual(); break;
            case GamepadAction.MoveDown: _focusIndex = Math.Min(_sections.Length - 1, _focusIndex + 1); UpdateFocusVisual(); break;
            case GamepadAction.Confirm: EnterSubMenu(); break;
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
                string[] tdps = { "UltraEco", "Eco", "Normal", "Desempenho", "Boost" };
                _tempTdpIndex = Array.IndexOf(tdps, _currentTdp);
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
            case SubMenuMode.Tdp: _tempTdpIndex = Math.Clamp(_tempTdpIndex + direction, 0, 4); UpdateTempVisuals(); break;
            case SubMenuMode.Fan: _tempFanIndex = Math.Clamp(_tempFanIndex + direction, 0, 4); UpdateTempVisuals(); break;
            case SubMenuMode.Color: _tempColorIndex = Math.Clamp(_tempColorIndex + direction, 0, 3); UpdateTempVisuals(); break;
            case SubMenuMode.Hud: _tempHudIndex = Math.Clamp(_tempHudIndex + direction, 0, 3); UpdateTempVisuals(); break;
            case SubMenuMode.Volume: if (VolumeSlider != null) VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + (direction * 5), 0, 100); break;
            case SubMenuMode.Brightness: if (BrightnessSlider != null) BrightnessSlider.Value = Math.Clamp(BrightnessSlider.Value + (direction * 5), 0, 100); break;
            case SubMenuMode.Capture: _tempCaptureIndex = Math.Clamp(_tempCaptureIndex + direction, 0, 1); UpdateTempVisuals(); break;
            case SubMenuMode.Lossless: break; // tratado em HandleGamepadAction
        }
    }

    private void ApplySubMenuChange()
    {
        switch (_activeSubMenu)
        {
            case SubMenuMode.Resolution: _currentResIndex = _tempResIndex; ApplyDisplayMode(); break;
            case SubMenuMode.RefreshRate: _currentHzIndex = _tempHzIndex; ApplyDisplayMode(); break;
            case SubMenuMode.Tdp:
                string[] tdpModes = { "UltraEco", "Eco", "Normal", "Desempenho", "Boost" };
                string newTdp = tdpModes[_tempTdpIndex];
                if (_hardwareService.SetTdpMode(newTdp))
                {
                    SetTdpButtonVisual(newTdp);
                    SaveSetting("TdpMode", newTdp);
                    _viewModel.UpdateHudTdpStatus(newTdp);
                    var linkedFanMode = newTdp switch { "UltraEco" or "Eco" => "Silencioso", "Normal" => "Normal", "Desempenho" => "Desempenho", "Boost" => "Boost", _ => "Automático" };
                    ApplyFanMode(linkedFanMode);
                }
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

    // 🟢 Atualiza visual dos 3 botões de multiplicador
    private void UpdateMultiplierVisual()
    {
        if (_multiplierButtons == null) return;
        for (int i = 0; i < _multiplierButtons.Length; i++)
        {
            if (_multiplierButtons[i] != null)
                _multiplierButtons[i].Background = (i == _currentMultiplierIndex) ? _activeColor : _inactiveColor;
        }
    }

    // 🟢 Handler de clique do mouse nos botões de multiplicador
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
        for (int i = 0; i < _sections.Length; i++) _sections[i].BorderBrush = (i == _focusIndex) ? _focusBorderColor : Brushes.Transparent;
        ScrollToFocusedSection();
    }

    private void ScrollToFocusedSection()
    {
        if (MainScrollViewer == null || _sections.Length == 0) return;
        if (_focusIndex < 0 || _focusIndex >= _sections.Length) return;

        if (_scrollPending) return;
        _scrollPending = true;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _scrollPending = false;
            try
            {
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
            catch { }
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

            // Atualiza os 3 botões de multiplicador
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
        if (_hardwareService.SetTdpMode(mode))
        {
            SetTdpButtonVisual(mode);
            SaveSetting("TdpMode", mode);
            _viewModel.UpdateHudTdpStatus(mode);
            var linkedFanMode = mode switch { "UltraEco" or "Eco" => "Silencioso", "Normal" => "Normal", "Desempenho" => "Desempenho", "Boost" => "Boost", _ => "Automático" };
            ApplyFanMode(linkedFanMode);
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
                if (_freezeGameOnOverlay) InputBlockService.BlockGamepadForGame(_gamePid);
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
            bool isRecording = winService.IsRecording;

            _viewModel.UpdateHudRecordingState(isRecording);

            if (BtnRecord != null)
            {
                BtnRecord.Content = isRecording ? "🎥 Parar" : "🎥 Gravar";
                BtnRecord.Background = isRecording ? new SolidColorBrush(Color.FromRgb(255, 64, 64)) : _inactiveColor;
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
                    if (wasVisible) { Show(); if (_freezeGameOnOverlay) InputBlockService.BlockGamepadForGame(_gamePid); ForceTopmost(); }
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
                    if (_freezeGameOnOverlay) InputBlockService.BlockGamepadForGame(_gamePid);
                    ForceTopmost();
                    await Task.Delay(300);
                }
            }

            if (_gameHwnd != IntPtr.Zero)
            {
                LosslessScalingService.SetGameHwnd(_gameHwnd);
                try { SetForegroundWindow(_gameHwnd); } catch { }
                await Task.Delay(300);
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
                if (_freezeGameOnOverlay) InputBlockService.BlockGamepadForGame(_gamePid);
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