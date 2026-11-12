using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Input;
using HandheldLauncher.App.Platform;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.App.Views.Layouts;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;
using Avalonia.VisualTree; 
using Avalonia.LogicalTree;

namespace HandheldLauncher.App.Views;

// Parte da MainWindow: polling do gamepad, atalhos de teclado, navegação da
// biblioteca e o watcher que detecta a janela do jogo em execução.
// Extraído de MainWindow.axaml.cs (v0.16) — nenhuma mudança de comportamento.
public partial class MainWindow : Window
{
    private static System.Collections.Generic.List<(IntPtr Hwnd, string Title, uint Pid)> GetAllVisibleWindows()
    {
        var results = new System.Collections.Generic.List<(IntPtr, string, uint)>();
        try
        {
            EnumWindows((hWnd, lParam) =>
            {
                try
                {
                    if (!IsWindowVisible(hWnd)) return true;
                    var sb = new System.Text.StringBuilder(512);
                    var len = GetWindowText(hWnd, sb, sb.Capacity);
                    if (len <= 0) return true;
                    var title = sb.ToString();
                    if (string.IsNullOrWhiteSpace(title)) return true;
                    GetWindowThreadProcessId(hWnd, out var pid);
                    results.Add((hWnd, title, pid));
                }
                catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
        return results;
    }

    private DispatcherTimer? _downloadsRefreshTimer;

    private bool _gameWindowSeen = false;

    private int _gameWindowMissingSeconds = 0;

    private int _gameWatcherTicks = 0;

    private bool _gameWatcherDiagnosticLogged = false;

    private int _detectedGamePid = 0;

    private string _lastLoggedExeHint = string.Empty;
   private bool _addButtonFocused;

private Button? GetAddProgramButton()
{
    if (_libraryLayout is not Control control) return null;
    return FindButtonByName(control, "AddProgramButton");
}

private static Button? FindButtonByName(Visual root, string name)
{
    if (root is Button btn && btn.Name == name) return btn;
    foreach (var child in root.GetVisualChildren())
    {
        var found = FindButtonByName(child, name);
        if (found is not null) return found;
    }
    return null;
}

private void SetAddButtonFocused(bool focused)
{
    _addButtonFocused = focused;
    var addBtn = GetAddProgramButton();
    if (addBtn is null) return;
    
    if (focused) 
    {
        addBtn.Classes.Add("addFocused");
    }
    else
    {
        addBtn.Classes.Remove("addFocused");
    }
}

    /// <summary>
    /// v0.25.4: leva o foco do controle ao botão "+" (Adicionar programa).
    /// Antes só funcionava com MoveRight e com pelo menos um programa na aba;
    /// com a lista vazia (ou numa grade vertical) era impossível chegar lá.
    /// </summary>
    private bool TryFocusAddProgramButton(MainViewModel vm)
    {
        if (!vm.IsFilterApplicationsActive) return false;
        var addBtn = GetAddProgramButton();
        if (addBtn is null || !addBtn.IsEffectivelyVisible) return false;

        SetAddButtonFocused(true);
        addBtn.BringIntoView();
        return true;
    }

    /// <summary>Devolve o foco ao último programa da lista (ou só limpa o "+").</summary>
    private void ReturnFromAddProgramButton(MainViewModel vm)
    {
        if (!_addButtonFocused) return;
        SetAddButtonFocused(false);
        if (vm.Games.Count > 0)
        {
            vm.SelectedIndex = vm.Games.Count - 1;
            ScrollGameRowSelectionIntoView(vm);
        }
    }

    private void ShowLaunchPopup(string title, string? iconPath)
    {
        try
        {
            try { _launchPopup?.Close(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
            _launchPopup = null;
            _launchPopupTimer?.Stop();
            _launchPopupTimer = null;

            _launchPopup = new GameLaunchPopupWindow(title, iconPath);
            _launchPopup.Show();

            var thisPopup = _launchPopup;

            _launchPopupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
            _launchPopupTimer.Tick += (_, _) =>
            {
                _launchPopupTimer?.Stop();

                bool isSame = ReferenceEquals(_launchPopup, thisPopup);
                DebugLog.Write($"[POPUP] Timeout 90s disparado. isSame={isSame}, visible={_launchPopup?.IsVisible}, loaded={_launchPopup?.IsLoaded}");

                if (isSame && _launchPopup is not null)
                {
                    try
                    {
                        _launchPopup.Close();
                        DebugLog.Write("[POPUP] Close() executado.");
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Write($"[POPUP] Close() EXCEPTION: {ex.Message}");
                    }
                    _launchPopup = null;
                }
                else
                {
                    DebugLog.Write("[POPUP] Timer disparou, mas janela já não é a mesma (já foi fechada).");
                }
            };
            _launchPopupTimer.Start();

            DebugLog.Write($"[POPUP] Aberto: '{title}' (watchdog 90s iniciado)");
        }
        catch (Exception ex) { DebugLog.Write($"[POPUP] EXCEPTION: {ex.Message}"); }
    }  

    private void CloseLaunchPopupWithGrace()
    {
        DebugLog.Write($"[POPUP] CloseLaunchPopupWithGrace chamado. popup_null={_launchPopup is null}");
        if (_launchPopup is null) return;

        _launchPopupTimer?.Stop();
        _launchPopupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _launchPopupTimer.Tick += (_, _) =>
        {
            _launchPopupTimer?.Stop();
            try { _launchPopup?.Close(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
            _launchPopup = null;
            DebugLog.Write("[POPUP] Fechado após 3s de detecção da janela.");
        };
        _launchPopupTimer.Start();
        DebugLog.Write("[POPUP] Janela detectada — fechando em 3s.");
    }

    // v0.18: extraído do corpo do timer do watcher pra poder ser chamado
    // também fora do polling de 1s — o Guardião de suspensão usa isto pra
    // achar a janela do jogo de novo assim que o aparelho acorda, sem
    // esperar o próximo tick. Mesma lógica de antes (nome normalizado da
    // janela, com fallback pro nome do .exe), nenhuma mudança de comportamento.
    private (bool Found, IntPtr Hwnd, uint Pid, string ExeHint) FindGameWindow(GameEntry game)
    {
        var gameTitle = game.Title;
        if (string.IsNullOrEmpty(gameTitle)) return (false, IntPtr.Zero, 0, string.Empty);

        static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s)
                if (char.IsLetterOrDigit(c) || c == ' ')
                    sb.Append(char.ToLowerInvariant(c));
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        var normalizedTarget = Normalize(gameTitle);

        var exeHint = string.Empty;
        try
        {
            if (!string.IsNullOrEmpty(game.InstallDirectory) && Directory.Exists(game.InstallDirectory))
            {
                var badNames = new[] { "unins", "crash", "report", "installer", "setup", "ubisoft", "upc", "connect", "launcher", "prereq", "vcredist", "directx", "vulkan", "dxsetup", "dotnet" };

                bool IsGameExe(string path)
                {
                    var name = Path.GetFileName(path).ToLowerInvariant();
                    return !badNames.Any(b => name.Contains(b));
                }

                var candidates = Directory.EnumerateFiles(game.InstallDirectory, "*.exe", SearchOption.AllDirectories)
                    .Where(IsGameExe)
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .ToList();

                if (candidates.Count > 0)
                    exeHint = Path.GetFileNameWithoutExtension(candidates[0]).ToLowerInvariant();

                if (!string.Equals(_lastLoggedExeHint, exeHint, StringComparison.OrdinalIgnoreCase))
                {
                    _lastLoggedExeHint = exeHint;
                    DebugLog.Write($"[JOGO-WATCHER] exeHint detectado: '{exeHint}' (de {candidates.Count} candidatos)");
                }
            }
        }
        catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }

        var windows = GetAllVisibleWindows();
        bool found = false;
        IntPtr foundHwnd = IntPtr.Zero;
        uint foundPid = 0;

        foreach (var w in windows)
        {
            try
            {
                var norm = Normalize(w.Title);
                if (!string.IsNullOrEmpty(normalizedTarget) && norm.Contains(normalizedTarget))
                {
                    found = true;
                    foundHwnd = w.Hwnd;
                    foundPid = w.Pid;
                    break;
                }
            }
            catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
        }

        if (!found && !string.IsNullOrEmpty(exeHint))
        {
            foreach (var w in windows)
            {
                try
                {
                    var proc = Process.GetProcessById((int)w.Pid);
                    var procName = proc.ProcessName.ToLowerInvariant();
                    if (procName == exeHint || procName.Contains(exeHint) || exeHint.Contains(procName))
                    {
                        found = true;
                        foundHwnd = w.Hwnd;
                        foundPid = w.Pid;
                        break;
                    }
                }
                catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
            }
        }

        return (found, foundHwnd, foundPid, exeHint);
    }

    // v0.18 "Guardião de suspensão": chamado pelo SuspendGuardianService
    // quando o Windows acorda da suspensão. Se tinha jogo rodando, acha a
    // janela dele de novo (pode ter perdido o handle — alguns jogos recriam
    // a janela ao voltar da suspensão) e devolve o foco, com o mesmo truque
    // de AttachThreadInput já usado em ForceForeground (MainWindow.WindowChrome.cs)
    // pra isso funcionar mesmo sem a nossa janela estar em primeiro plano.
    public void RestoreGameFocusAfterResume()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (DataContext is not MainViewModel vm) return;
        if (vm.RunningGame is null)
        {
            DebugLog.Write("[SUSPENSAO] Nenhum jogo em execução — nada de foco a devolver.");
            return;
        }

        try
        {
            var (found, hwnd, _, _) = FindGameWindow(vm.RunningGame);
            if (!found || hwnd == IntPtr.Zero)
            {
                DebugLog.Write($"[SUSPENSAO] Janela de '{vm.RunningGame.Title}' não encontrada pra devolver o foco.");
                return;
            }

            var foregroundHandle = GetForegroundWindow();
            if (foregroundHandle == hwnd)
            {
                DebugLog.Write("[SUSPENSAO] Jogo já está em primeiro plano — nada a fazer.");
                return;
            }

            DebugLog.Write($"[SUSPENSAO] Devolvendo foco pro jogo '{vm.RunningGame.Title}' (hwnd=0x{hwnd:X}).");

            if (foregroundHandle != IntPtr.Zero)
            {
                var fgThread = GetWindowThreadProcessId(foregroundHandle, out _);
                var myThread = GetCurrentThreadId();
                if (fgThread != 0 && fgThread != myThread)
                {
                    AttachThreadInput(fgThread, myThread, true);
                    try
                    {
                        BringWindowToTop(hwnd);
                        SetForegroundWindow(hwnd);
                    }
                    finally
                    {
                        AttachThreadInput(fgThread, myThread, false);
                    }
                    return;
                }
            }

            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[SUSPENSAO] EXCEPTION em RestoreGameFocusAfterResume: {ex.Message}");
        }
    }

    private void StartGameWatcher(MainViewModel vm)
    {
        _gameWatcherTimer?.Stop();
        _gameWindowSeen = false;
        _gameWindowMissingSeconds = 0;
        _gameWatcherTicks = 0;
        _gameWatcherDiagnosticLogged = false;
        _detectedGamePid = 0;
        _lastLoggedExeHint = string.Empty;

        DebugLog.Write("[JOGO-WATCHER] Iniciado.");

        _gameWatcherTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _gameWatcherTimer.Tick += (_, _) =>
        {
            if (vm.RunningGame is null) { _gameWindowSeen = false; _gameWindowMissingSeconds = 0; _gameWatcherTicks = 0; return; }
            if (!OperatingSystem.IsWindows()) return;
            if (_activeDialog != null) { _gameWindowMissingSeconds = 0; return; }

            _gameWatcherTicks++;

            try
            {
                var game = vm.RunningGame;
                var gameTitle = game.Title;
                if (string.IsNullOrEmpty(gameTitle)) return;

                var (found, foundHwnd, foundPid, exeHint) = FindGameWindow(game);

                // Verificação rápida de processo morto — restaura em ~1s em vez de esperar 8s
                if (_gameWindowSeen && _detectedGamePid != 0)
                {
                    bool procDead = false;
                    try
                    {
                        using var proc = Process.GetProcessById(_detectedGamePid);
                        procDead = proc.HasExited;
                    }
                    catch (ArgumentException) { procDead = true; }
                    catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }

                    if (procDead)
                    {
                        DebugLog.Write($"[JOGO-WATCHER] Processo do jogo morreu — restaurando launcher imediatamente.");
                        LosslessScalingService.SetGameHwnd(IntPtr.Zero);
                        vm.NotifyGameExitedExternally();
                        _gameWindowSeen = false;
                        _gameWindowMissingSeconds = 0;
                        _detectedGamePid = 0;
                        return;
                    }
                }

                // v0.18.4: o processo lançado era só um lançador intermediário e
                // saiu, e a janela do jogo nunca apareceu em 45s — o jogo não
                // abriu (ou abriu com um nome de janela que não reconhecemos).
                // Libera o launcher em vez de ficar escondido pra sempre.
                if (!found && !_gameWindowSeen && vm.LaunchedProcessExitedEarly && _gameWatcherTicks >= 45)
                {
                    DebugLog.Write($"[JOGO-WATCHER] '{gameTitle}': lançador intermediário saiu e a janela do jogo não apareceu em {_gameWatcherTicks}s — liberando launcher.");
                    vm.NotifyGameExitedExternally();
                    return;
                }

                if (!found && !_gameWindowSeen && !_gameWatcherDiagnosticLogged && _gameWatcherTicks >= 10)
                {
                    _gameWatcherDiagnosticLogged = true;
                    DebugLog.Write($"[JOGO-WATCHER] DIAGNÓSTICO (tick={_gameWatcherTicks}): '{gameTitle}' não detectado. exeHint='{exeHint}', installDir='{game.InstallDirectory}'.");
                    // v0.18: a lista de janelas agora vive dentro de FindGameWindow
                    // (GetAllVisibleWindows); pega de novo aqui só pro log de
                    // diagnóstico, que é raro (só depois de 10 ticks sem achar).
                    var diagnosticWindows = GetAllVisibleWindows();
                    DebugLog.Write($"[JOGO-WATCHER] Janelas visíveis no sistema ({diagnosticWindows.Count}):");
                    foreach (var w in diagnosticWindows)
                        DebugLog.Write($"[JOGO-WATCHER]   - '{w.Title}' (pid={w.Pid}, hwnd=0x{w.Hwnd:X})");
                }

                if (found)
                {
                    if (!_gameWindowSeen)
                    {
                        DebugLog.Write($"[JOGO-WATCHER] Janela de '{gameTitle}' detectada pela primeira vez (hwnd=0x{foundHwnd:X}, pid={foundPid}).");
                        LosslessScalingService.SetGameHwnd(foundHwnd);
                        _detectedGamePid = (int)foundPid;
                        CloseLaunchPopupWithGrace();
                        _gameWindowSeen = true;
                    }
                    _gameWindowMissingSeconds = 0;
                }
                else if (_gameWindowSeen)
                {
                    _gameWindowMissingSeconds += 1;
                    DebugLog.Write($"[JOGO-WATCHER] Janela de '{gameTitle}' ausente há {_gameWindowMissingSeconds}s.");
                    if (_gameWindowMissingSeconds >= 8)
                    {
                        DebugLog.Write($"[JOGO-WATCHER] Janela de '{gameTitle}' sumiu há {_gameWindowMissingSeconds}s — liberando launcher.");
                        LosslessScalingService.SetGameHwnd(IntPtr.Zero);
                        vm.NotifyGameExitedExternally();
                        _gameWindowSeen = false;
                        _gameWindowMissingSeconds = 0;
                        _detectedGamePid = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[JOGO-WATCHER] EXCEPTION: {ex.Message}");
            }
        };
        _gameWatcherTimer.Start();
        Closed += (_, _) => _gameWatcherTimer?.Stop();
    }

    private void StartDownloadsRefreshTimer(MainViewModel vm)
    {
        var ticksSinceBackgroundRefresh = 0;
        _downloadsRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _downloadsRefreshTimer.Tick += (_, _) =>
        {
            if (vm.IsDownloadsOpen) { vm.RefreshActiveDownloads(); ticksSinceBackgroundRefresh = 0; return; }
            if (vm.RunningGame is not null) return;
            ticksSinceBackgroundRefresh++;
            if (ticksSinceBackgroundRefresh >= 3) { ticksSinceBackgroundRefresh = 0; vm.RefreshActiveDownloads(); }
        };
        _downloadsRefreshTimer.Start();
        Closed += (_, _) => _downloadsRefreshTimer?.Stop();
        vm.RefreshActiveDownloads();
    }

    private void StartBatteryMonitor(MainViewModel vm)
    {
        vm.BatteryStatusText = BatteryStatus.GetDisplayText();
        vm.ClockText = DateTime.Now.ToString("HH:mm");
        _batteryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _batteryTimer.Tick += (_, _) => { vm.BatteryStatusText = BatteryStatus.GetDisplayText(); vm.ClockText = DateTime.Now.ToString("HH:mm"); };
        _batteryTimer.Start();
        Closed += (_, _) => _batteryTimer?.Stop();
    }

    private bool _inputIgnoredWhileBackground;
    private DateTime _lastBackgroundInputAt = DateTime.MinValue;
    private DateTime _foregroundRegainedAt = DateTime.MinValue;
    private bool _wasOurWindowInForeground = true;

    private static bool DialogOpenedExternalApp(Window dialog) => dialog switch
    {
        GameDetailsWindow { OpenedExternalApp: true } => true,
        StorageManagerWindow { OpenedExternalApp: true } => true,
        HelpCenterWindow { OpenedExternalApp: true } => true,
        _ => false,
    };

    private bool IsDialogOrLauncherInForeground(Window dialog)
    {
        if (!OperatingSystem.IsWindows()) return true;
        try
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return true;
            var mine = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            var dlg = dialog.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            return fg == mine || fg == dlg;
        }
        catch { return true; }
    }

    // v0.25.9: ponte do controle para o QS com o jogo em foco (ver Input/HidPadBridge).
    private HidPadReader? _hidPad;
    private bool _hidPadArmed;
    private bool _hidPadLoggedDuplicate;

    /// <summary>
    /// v0.25.9 (revisão): chamado a cada abertura/fechamento do QS. O tique do
    /// controle PARA quando o QS fecha com o jogo na frente, então nenhum tique
    /// "vê" o painel fechado — sem isto, a 2ª abertura reaproveitava a
    /// referência antiga e os botões apertados jogando viravam ações no QS.
    /// </summary>
    private void ResetOverlayInputState()
    {
        _hidPadArmed = false;
        _overlayTraceWasVisible = false;
    }

    private IReadOnlyList<GamepadAction> ApplyHidBridge(IReadOnlyList<GamepadAction> xinputActions)
    {
        var qsInGame = OperatingSystem.IsWindows()
                       && _gamepadComboModeIsGame == true
                       && _overlayWindow is { IsVisible: true };
        if (!qsInGame)
        {
            _hidPadArmed = false;
            return xinputActions;
        }

        if (_hidPad is null)
        {
            _hidPad = new HidPadReader();
            Closed += (_, _) => { _hidPad?.Dispose(); _hidPad = null; };
        }

        if (!_hidPadArmed)
        {
            // Abertura do QS: apertos feitos jogando não viram ações no painel.
            _hidPad.Resync();
            _hidPadArmed = true;
            _hidPadLoggedDuplicate = false;
        }

        var hidActions = _hidPad.Poll();
        // Sem ponte (ou ajudante morto, com dado velho): fica como antes (XInput).
        if (!_hidPad.HasData || !GameInputHelper.IsHelperRunning) return xinputActions;

        // Com a ponte ativa, ela é a única fonte — se o XInput também entregar
        // (launcher em foco), o mesmo aperto não pode valer duas vezes.
        if (xinputActions.Count > 0 && !_hidPadLoggedDuplicate)
        {
            _hidPadLoggedDuplicate = true;
            DebugLog.Write("[QS-INPUT] XInput também entregou botões; usando só a ponte HID para não duplicar.");
        }
        return hidActions;
    }

    // v0.25.9: diagnóstico de entrada com o Quick Settings aberto.
    private bool _loggedOverlayOverDialog;
    private bool _overlayTraceWasVisible;
    private DateTime _overlayTraceOpenedAt;
    private DateTime _overlayTraceLastInputAt;
    private DateTime _overlayTraceLastReportAt;
    private int _overlayTraceReports;
    private bool _overlayTraceFirstInputLogged;

    /// <summary>
    /// v0.25.9: com o QS visível, anota no log o primeiro botão recebido e,
    /// se nada chegar por 5 s, uma foto do estado (slots do XInput, slot
    /// ativo, quem está na frente, diálogo aberto, ajudante do L3+R3). Até 6
    /// fotos por abertura, pra não inundar o log. Só lê estado — não muda nada.
    /// </summary>
    private void TraceOverlayInput(int actionCount)
    {
        var visible = _overlayWindow is { IsVisible: true };
        var now = DateTime.UtcNow;

        if (visible && !_overlayTraceWasVisible)
        {
            _overlayTraceOpenedAt = now;
            _overlayTraceLastInputAt = now;
            _overlayTraceLastReportAt = now;
            _overlayTraceReports = 0;
            _overlayTraceFirstInputLogged = false;
            _loggedOverlayOverDialog = false;
        }
        _overlayTraceWasVisible = visible;
        if (!visible) return;

        if (actionCount > 0)
        {
            _overlayTraceLastInputAt = now;
            if (!_overlayTraceFirstInputLogged)
            {
                _overlayTraceFirstInputLogged = true;
                DebugLog.Write($"[QS-INPUT] Primeiro botão recebido {(now - _overlayTraceOpenedAt).TotalMilliseconds:F0} ms após abrir (slot={_gamepad.ActiveSlot}).");
            }
            return;
        }

        if (_overlayTraceReports >= 6) return;
        if (now - _overlayTraceLastInputAt < TimeSpan.FromSeconds(5)) return;
        if (now - _overlayTraceLastReportAt < TimeSpan.FromSeconds(5)) return;

        _overlayTraceLastReportAt = now;
        _overlayTraceReports++;
        DebugLog.Write($"[QS-INPUT] {(now - _overlayTraceLastInputAt).TotalSeconds:F0} s sem botão com o QS aberto · {DescribeInputState()}");
    }

    /// <summary>v0.25.9: foto do estado de entrada para o log (só leitura).</summary>
    private string DescribeInputState()
    {
        try
        {
            var connected = "";
            if (OperatingSystem.IsWindows())
                for (uint slot = 0; slot < 4; slot++)
                    if (XInput.TryGetState(slot, out _)) connected += slot;

            var foreground = OperatingSystem.IsWindows() ? HidComboReader.ForegroundDescription() : "n/d";
            var restart = OverlayWindow.LastControllerRestartUtc is { } r
                ? $"{(DateTime.UtcNow - r).TotalSeconds:F0} s atrás"
                : "nenhum";
            return $"xinput=[{connected}], slot ativo={_gamepad.ActiveSlot}, conectado={_gamepad.IsConnected}, " +
                   $"timer={_gamepadTimer.IsEnabled}, diálogo={_activeDialog?.GetType().Name ?? "nenhum"}, " +
                   $"ajudante L3+R3={(GameInputHelper.IsHelperRunning ? "vivo" : "parado")}, " +
                   $"reinício do controle: {restart} (pendente={_overlayWindow?.HasPendingControllerRestart ?? false}), " +
                   $"elevado={ElevationService.IsElevated}, em foco: {foreground}";
        }
        catch (Exception ex)
        {
            return $"(falha ao descrever: {ex.GetType().Name})";
        }
    }

    private void OnGamepadTick(object? sender, EventArgs e)
    {
        try
        {
            if (DataContext is not MainViewModel vm) return;
            var actions = _gamepad.Poll();

            // v0.25.9: QS aberto num jogo → botões vêm do ajudante (HID em
            // segundo plano), porque o XInput não chega a quem está sem foco.
            actions = ApplyHidBridge(actions);

            // Linux: o evdev é global — o jogo e o launcher recebem os MESMOS botões. Com um
            // jogo aberto e o launcher sem foco, só o atalho do QS vale; senão "B" no jogo fechava
            // o jogo pelo launcher, "A" relançava e a janela voltava a brigar pela frente.
            if (OperatingSystem.IsLinux() && actions.Count > 0
                && (vm.IsLaunching || vm.RunningGame is not null)
                && _overlayWindow is not { IsVisible: true }
                && !IsActive && _activeDialog?.IsActive != true)
            {
                var onlyQs = actions.Where(a => a == GamepadAction.ToggleQuickSettings).ToList();
                actions = onlyQs;
            }

            // v0.25.9 (diagnóstico do "QS não responde ao controle"): enquanto
            // o painel está aberto, registra se o controle está chegando.
            TraceOverlayInput(actions.Count);

            // v0.25.9: o Quick Settings fica POR CIMA de tudo, inclusive de um
            // diálogo modal (Detalhes, Configurações, Central de Ajuda…). Antes,
            // com um diálogo registrado, o lote inteiro ia pro diálogo escondido
            // atrás e o QS aberto não recebia nenhum botão. Só muda o caso em
            // que os dois existem ao mesmo tempo; sem QS visível, nada muda.
            if (_activeDialog is not null && _overlayWindow is { IsVisible: true } overlayOverDialog)
            {
                if (actions.Count > 0 && !_loggedOverlayOverDialog)
                {
                    _loggedOverlayOverDialog = true;
                    DebugLog.Write($"[OVERLAY] Controle enviado ao QS (havia diálogo aberto por baixo: {_activeDialog.GetType().Name}).");
                }

                foreach (var overlayAction in actions)
                {
                    UiSounds.PlayForAction(overlayAction);
                    if (overlayAction == GamepadAction.ToggleQuickSettings)
                    {
                        ToggleOverlay();
                        return;
                    }

                    overlayOverDialog.HandleGamepadAction(overlayAction);
                    if (!overlayOverDialog.IsVisible)
                        return;
                }
                return;
            }

            if (_activeDialog is { } dialog)
            {
                // v0.18.3 (revisão): se o diálogo abriu um app externo (navegador
                // da loja, Explorer, Steam, Configurações do Windows) e esse app
                // está NA FRENTE, os botões são do app — não do diálogo escondido
                // atrás. Antes, A na página do navegador fechava os Detalhes e
                // abria o jogo por trás; B/X idem, sem o usuário ver.
                if (actions.Count > 0 && DialogOpenedExternalApp(dialog) && !IsDialogOrLauncherInForeground(dialog))
                    return;

                foreach (var dialogAction in actions)
                {
                    UiSounds.PlayForAction(dialogAction);
                    if (OperatingSystem.IsLinux())
                        DebugLog.Write($"[DIALOG] {dialog.GetType().Name} ação={dialogAction} foco={dialog.FocusManager?.GetFocusedElement()?.GetType().Name ?? "nenhum"}");
                    // v0.24.2: teclado na tela — com ele aberto, D-Pad/A/B são
                    // dele (antes iam para o navegador genérico e o B fechava a
                    // janela em vez de esconder o teclado).
                    if (dialog is IGamepadKeyboardHost keyboardHost && keyboardHost.HandleKeyboardGamepadAction(dialogAction))
                        continue;
                    if (dialog is SettingsWindow sw && sw.HandleSettingsGamepadAction(dialogAction))
                        continue;
                    // v0.16.2: Central de Ajuda — LB/RB trocam de aba e B dentro de
                    // um artigo volta pra lista (em vez de fechar a janela).
                    if (dialog is HelpCenterWindow hc && hc.HandleGamepadAction(dialogAction))
                        continue;
                    // v0.18 (relato do usuário: barra de rolagem do Gerenciador de
                    // Armazenamento e da Saúde do Aparelho não respondia ao controle)
                    // — mesmo padrão acima: essas janelas só têm botão(ões) de rodapé
                    // focáveis, então MoveUp/MoveDown precisam rolar o ScrollViewer
                    // direto em vez de tentar mover o foco (que não tem pra onde ir).
                    if (dialog is StorageManagerWindow sm && sm.HandleGamepadAction(dialogAction))
                        continue;
                    if (dialog is DeviceHealthWindow dh && dh.HandleGamepadAction(dialogAction))
                        continue;
                    // v0.18 Detalhes 2.0: a página do jogo tem navegação própria
                    // (A Jogar, X Favoritar, Y ação da seção, LB/RB seções...).
                    if (dialog is GameDetailsWindow gd && gd.HandleGamepadAction(dialogAction))
                        continue;
                    DialogGamepadNavigator.Handle(dialog, dialogAction);
                }
                return;
            }

            // v0.16.3: quando o launcher volta pra frente, anota a hora — o "A"
            // que fez a troca de janela não pode abrir o jogo selecionado.
            var ourWindowInForeground = IsOurWindowInForeground();
            if (ourWindowInForeground && !_wasOurWindowInForeground) _foregroundRegainedAt = DateTime.UtcNow;
            _wasOurWindowInForeground = ourWindowInForeground;

            // O destino das ações é decidido uma única vez por ciclo de polling.
            // Antes, se B fechasse o overlay no meio do foreach, ações adicionais
            // do mesmo relatório podiam cair no launcher atrás dele. Isso também
            // permitia que um ToggleQuickSettings do mesmo lote reabrisse a janela
            // durante o fechamento. Enquanto o QS estava visível no início do tick,
            // todo o lote pertence a ele e nunca vaza para a janela principal.
            var overlayForThisTick = _overlayWindow;
            if (overlayForThisTick is { IsVisible: true })
            {
                foreach (var overlayAction in actions)
                {
                    UiSounds.PlayForAction(overlayAction);
                    if (overlayAction == GamepadAction.ToggleQuickSettings)
                    {
                        ToggleOverlay();
                        return;
                    }

                    overlayForThisTick.HandleGamepadAction(overlayAction);
                    if (!overlayForThisTick.IsVisible)
                        return; // descarta o restante do lote após fechar o QS
                }
                return;
            }

            foreach (var action in actions)
            {
                if (action == GamepadAction.ToggleQuickSettings)
                {
                    UiSounds.PlayForAction(action);
                    ToggleOverlay();
                    return; // o restante deste lote não pertence ao launcher
                }

                // v0.24.2: teclado na tela aberto (busca da Nuvem) — o controle é
                // dele: D-Pad anda nas teclas, A escreve, B esconde o teclado.
                if (Keyboard.IsOpen && Keyboard.HandleGamepadAction(action))
                    continue;

                if (vm.IsAboutOpen)
                {
                    if (action is GamepadAction.Back or GamepadAction.Confirm or GamepadAction.GlobalMenu or GamepadAction.Menu) { UiSounds.PlayBack(); vm.CloseAbout(); }
                    continue;
                }

                if (vm.IsShortcutsHelpOpen)
                {
                    if (action is GamepadAction.Back or GamepadAction.Confirm or GamepadAction.GlobalMenu or GamepadAction.Menu) { UiSounds.PlayBack(); vm.CloseShortcutsHelp(); }
                    continue;
                }

                // Central de Controle aberta: o D-Pad/teclado pertencem a ela. Isto
                // fica ANTES da checagem de "launcher na frente" — senão o B não
                // fecha o painel quando outra janela rouba o foco. Com um diálogo
                // aberto por cima (senha do Wi-Fi), ele é quem manda.
                if (_isControlCenterOpen && _activeDialog is null)
                {
                    UiSounds.PlayForAction(action);
                    HandleControlCenterGamepad(action);
                    continue;
                }

                // v0.16.3: botões só valem com o launcher NA FRENTE — antes isso só
                // era checado com jogo rodando. Relato de 05/11: com o app do
                // Xbox na frente o controle mexia na biblioteca atrás, e o "A"
                // usado pra trazer o launcher pra frente (botão de alternar do
                // Legion) já abria o jogo selecionado.
                if (!ourWindowInForeground)
                {
                    if (!_inputIgnoredWhileBackground)
                    {
                        _inputIgnoredWhileBackground = true;
                        DebugLog.Write("[INPUT] Launcher não está na frente — botões ignorados até ele voltar ao foco.");
                    }
                    _lastBackgroundInputAt = DateTime.UtcNow;
                    continue;
                }
                if (_inputIgnoredWhileBackground)
                {
                    _inputIgnoredWhileBackground = false;
                    DebugLog.Write("[INPUT] Launcher voltou ao foco — botões liberados.");
                }
                // O mesmo aperto que trouxe o launcher pra frente não pode abrir jogo.
                if (action == GamepadAction.Confirm
                    && (DateTime.UtcNow - _lastBackgroundInputAt < TimeSpan.FromMilliseconds(600)
                        || DateTime.UtcNow - _foregroundRegainedAt < TimeSpan.FromMilliseconds(600)))
                {
                    DebugLog.Write("[INPUT] 'A' ignorado: foi o aperto que trouxe o launcher pra frente.");
                    continue;
                }
                UiSounds.PlayForAction(action);

                // v0.25.2: ecrã de lançamento do Cartoon aberto — o controle
                // pertence-lhe: A joga, X abre os detalhes, B fecha.
                if (_libraryLayout is ILaunchScreenHost launchScreen && launchScreen.IsLaunchScreenOpen)
                {
                    switch (action)
                    {
                        case GamepadAction.Confirm:
                            _ = vm.LaunchSelected();
                            launchScreen.CloseLaunchScreen();
                            break;
                        case GamepadAction.GameDetails:
                            vm.OpenGameDetailsForSelected();
                            launchScreen.CloseLaunchScreen();
                            break;
                        case GamepadAction.Back:
                        case GamepadAction.Menu:
                        case GamepadAction.GlobalMenu:
                            launchScreen.CloseLaunchScreen();
                            break;
                    }
                    continue;
                }

                if (vm.IsCoverPickerOpen)
                {

                    switch (action)
                    {
                        case GamepadAction.MoveLeft: vm.MoveCoverPickerSelection(-1); ScrollCoverPickerSelectionIntoView(vm); break;
                        case GamepadAction.MoveRight: vm.MoveCoverPickerSelection(1); ScrollCoverPickerSelectionIntoView(vm); break;
                        case GamepadAction.MoveUp: vm.MoveCoverPickerSelectionVertical(-1); ScrollCoverPickerSelectionIntoView(vm); break;
                        case GamepadAction.MoveDown: vm.MoveCoverPickerSelectionVertical(1); ScrollCoverPickerSelectionIntoView(vm); break;
                        case GamepadAction.Confirm: _ = vm.ConfirmCoverPickerSelection(); break;
                        case GamepadAction.Back: case GamepadAction.Menu: case GamepadAction.GlobalMenu: vm.CloseCoverPicker(); break;
                    }
                    continue;
                }

                if (vm.IsContextMenuOpen)
                {
                    switch (action)
                    {
                        case GamepadAction.MoveUp: vm.MoveContextMenuSelection(-1); break;
                        case GamepadAction.MoveDown: vm.MoveContextMenuSelection(1); break;
                        case GamepadAction.Confirm: vm.ConfirmContextMenuSelection(); break;
                        case GamepadAction.Back: case GamepadAction.Menu: vm.CloseContextMenu(); break;
                    }
                    continue;
                }

                if (vm.IsGlobalMenuOpen)
                {
                    switch (action)
                    {
                        case GamepadAction.MoveUp: vm.MoveGlobalMenuSelection(-1); break;
                        case GamepadAction.MoveDown: vm.MoveGlobalMenuSelection(1); break;
                        case GamepadAction.Confirm: vm.ConfirmGlobalMenuSelection(); break;
                        case GamepadAction.Back: vm.GlobalMenuBack(); break;
                        case GamepadAction.GlobalMenu: 
                            vm.CloseGlobalMenu(); 
                            // v0.18.6: Recupera o foco
                            ForceForeground(allowRestore: true, forceIfGameRunning: true);
                            break;
                    }
                    continue;
                }

                if (vm.IsDownloadsOpen)
                {
                    switch (action)
                    {
                        case GamepadAction.Back: case GamepadAction.Menu: case GamepadAction.GlobalMenu: case GamepadAction.Downloads: vm.CloseDownloads(); break;
                        case GamepadAction.Confirm: vm.OpenSteamDownloads(); break;
                    }
                    continue;
                }

                // v0.18.3: aba Nuvem — busca (Y), faixa de letras (X), B limpa o filtro.
                if (_cloudGridActive && vm.IsFilterCloudActive)
                {
                    if (IsCloudSearchFocused()) { EndCloudSearch(); continue; } // qualquer botão encerra a digitação

                    if (vm.IsCloudLetterBarFocused)
                    {
                        var leaveToTabs = false;
                        switch (action)
                        {
                            case GamepadAction.MoveLeft: vm.MoveCloudLetterCursor(-1); continue;
                            case GamepadAction.MoveRight: vm.MoveCloudLetterCursor(1); continue;
                            case GamepadAction.Confirm: vm.ConfirmCloudLetterCursor(); ScrollGameRowSelectionIntoView(vm); continue;
                            case GamepadAction.Back: case GamepadAction.GameDetails: case GamepadAction.MoveDown: vm.LeaveCloudLetterBar(); continue;
                            case GamepadAction.PreviousFilter: case GamepadAction.NextFilter: vm.LeaveCloudLetterBar(); leaveToTabs = true; break;
                            default: continue;
                        }
                        if (!leaveToTabs) continue;
                    }
                    else
                    {
                        switch (action)
                        {
                            // Na aba Nuvem o Y continua sendo a busca por texto (é o
                            // único jeito de filtrar ali); a Central de Controle abre
                            // pelo ícone 📶 da barra de cima.
                            case GamepadAction.Connectivity:
                            case GamepadAction.Downloads: BeginCloudSearch(); continue;
                            case GamepadAction.GameDetails: vm.ToggleCloudLetterBar(); continue;
                            case GamepadAction.Back when vm.HasCloudFilter: vm.ClearCloudFilters(); ScrollGameRowSelectionIntoView(vm); continue;
                            case GamepadAction.MoveUp: if (!MoveSelectionVertical(vm, -1)) vm.ToggleCloudLetterBar(); continue;
                        }
                    }
                }

                if (vm.IsSubFilterRowFocused)
                {
                    switch (action)
                    {
                        case GamepadAction.MoveLeft: vm.MoveSubFilterCursor(-1); continue;
                        case GamepadAction.MoveRight: vm.MoveSubFilterCursor(1); continue;
                        case GamepadAction.Confirm: vm.ToggleFocusedSubFilter(); continue;
                        // v0.25.2: no Cartoon os subfiltros ficam no rodapé — sai-se para cima OU para baixo.
                        case GamepadAction.MoveUp when _filtersBelowGrid:
                        case GamepadAction.MoveDown when _filtersBelowGrid:
                        case GamepadAction.Back:
                            vm.LeaveSubFilterRow(); continue;
                        case GamepadAction.MoveDown: vm.LeaveSubFilterRow(); continue;
                        case GamepadAction.MoveUp: continue;
                        case GamepadAction.PreviousFilter: case GamepadAction.NextFilter: break;
                        default: continue;
                    }
                }

                // =========================================================
// v0.21.0 — Foco do controle no botão "+" (Adicionar programa)
// =========================================================
// =========================================================
// v0.21.0 — Foco do controle no botão "+" (Adicionar programa)
// =========================================================
if (_addButtonFocused)
{
    switch (action)
    {
        case GamepadAction.Confirm:
            SetAddButtonFocused(false);
            HandleAddProgramClick();
            continue;
        case GamepadAction.Back:
            SetAddButtonFocused(false);
            continue;
        case GamepadAction.MoveUp:
        case GamepadAction.MoveLeft:
            ReturnFromAddProgramButton(vm);
            continue;
        case GamepadAction.MoveDown:
            SetAddButtonFocused(false);
            if (_filtersBelowGrid) vm.FocusSubFilterRow();
            continue;
        case GamepadAction.MoveRight:
            SetAddButtonFocused(false);
            continue;
        default:
            SetAddButtonFocused(false);
            break;
    }
}

                switch (action)
                {
                    case GamepadAction.Downloads: vm.ToggleDownloads(); break;
                    case GamepadAction.Connectivity: ToggleControlCenter(); break;
                    case GamepadAction.MoveLeft: vm.MovePrevious(); ScrollGameRowSelectionIntoView(vm); break;
                   case GamepadAction.MoveRight:
    // v0.21.0/v0.25.4: no fim da aba Programas (ou sem programas), MoveRight
    // horizontal leva ao botão "+". Na grade vertical o caminho é MoveDown.
    if (!_addButtonFocused && vm.IsFilterApplicationsActive)
    {
        var games = vm.Games;
        var noGames = games.Count == 0;
        var atLast = games.Count > 0 && vm.SelectedTile == games[^1];
        if ((noGames || (!IsGridNav && atLast)) && TryFocusAddProgramButton(vm))
            break;
    }
    vm.MoveNext();
    ScrollGameRowSelectionIntoView(vm);
    break;
                    case GamepadAction.MoveUp:
                        if (_filtersBelowGrid) MoveSelectionVertical(vm, -1);
                        else if (!MoveSelectionVertical(vm, -1)) vm.FocusSubFilterRow();
                        break;
                    case GamepadAction.MoveDown:
                        if (_filtersBelowGrid)
                        {
                            if (!_addButtonFocused && vm.Games.Count == 0 && TryFocusAddProgramButton(vm))
                                break;
                            if (!MoveSelectionVertical(vm, 1))
                            {
                                if (!_addButtonFocused && TryFocusAddProgramButton(vm))
                                    break;
                                vm.FocusSubFilterRow();
                            }
                        }
                        else if (IsGridNav)
                        {
                            if (!MoveSelectionVertical(vm, 1) && TryFocusAddProgramButton(vm))
                                break;
                        }
                        else
                        {
                            MoveSelectionVertical(vm, 1);
                        }
                        break;
                    case GamepadAction.Confirm:
                        // v0.25.2: no Cartoon o A abre o ecrã de lançamento; lá dentro é que joga.
                        if (_libraryLayout is ILaunchScreenHost launchHost) launchHost.OpenLaunchScreen();
                        else { _ = vm.LaunchSelected(); ScrollGameRowSelectionIntoView(vm); }
                        break;
                    case GamepadAction.Back: vm.CloseRunningGame(); break;
                    case GamepadAction.Menu: vm.ToggleContextMenu(); break;
                    case GamepadAction.GlobalMenu:
                        vm.ToggleGlobalMenu();
                        // v0.18.6: Força o launcher a voltar pra frente caso o Windows abra o Menu Iniciar/Task View
                        ForceForeground(allowRestore: true, forceIfGameRunning: true);
                        break;
                    case GamepadAction.PreviousFilter: vm.CyclePreviousFilter(); break;
                    case GamepadAction.NextFilter: vm.CycleNextFilter(); break;
                    case GamepadAction.GameDetails: vm.OpenGameDetailsForSelected(); break;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Tick] EXCEPTION no handler do gamepad: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // v0.18.3: digitando na busca da aba Nuvem — as teclas são do campo, não
        // atalhos do launcher (senão "D" abriria Downloads, setas mudariam o jogo...).
        // Enter/Esc/↓ terminam a busca e voltam pra grade.
        if (_cloudGridActive && IsCloudSearchFocused())
        {
            if (e.Key is Key.Enter or Key.Escape or Key.Down) { EndCloudSearch(); e.Handled = true; }
            return;
        }

        if (e.Key == Key.F1) { vm.ToggleShortcutsHelp(); e.Handled = true; return; }
        if (vm.IsShortcutsHelpOpen) { vm.CloseShortcutsHelp(); e.Handled = true; return; }

        if (vm.IsCoverPickerOpen)
        {
            switch (e.Key)
            {
                case Key.Left: vm.MoveCoverPickerSelection(-1); ScrollCoverPickerSelectionIntoView(vm); e.Handled = true; break;
                case Key.Right: vm.MoveCoverPickerSelection(1); ScrollCoverPickerSelectionIntoView(vm); e.Handled = true; break;
                case Key.Up: vm.MoveCoverPickerSelectionVertical(-1); ScrollCoverPickerSelectionIntoView(vm); e.Handled = true; break;
                case Key.Down: vm.MoveCoverPickerSelectionVertical(1); ScrollCoverPickerSelectionIntoView(vm); e.Handled = true; break;
                case Key.Enter: _ = vm.ConfirmCoverPickerSelection(); e.Handled = true; break;
                case Key.Escape: vm.CloseCoverPicker(); e.Handled = true; break;
            }
            return;
        }

        if (vm.IsContextMenuOpen)
        {
            switch (e.Key)
            {
                case Key.Up: vm.MoveContextMenuSelection(-1); e.Handled = true; break;
                case Key.Down: vm.MoveContextMenuSelection(1); e.Handled = true; break;
                case Key.Enter: vm.ConfirmContextMenuSelection(); e.Handled = true; break;
                case Key.Escape: case Key.Space: vm.CloseContextMenu(); e.Handled = true; break;
            }
            return;
        }

        if (vm.IsAboutOpen) { if (e.Key is Key.Escape or Key.Enter or Key.Tab) { vm.CloseAbout(); e.Handled = true; } return; }

        if (vm.IsGlobalMenuOpen)
        {
            switch (e.Key)
            {
                case Key.Up: vm.MoveGlobalMenuSelection(-1); e.Handled = true; break;
                case Key.Down: vm.MoveGlobalMenuSelection(1); e.Handled = true; break;
                case Key.Enter: vm.ConfirmGlobalMenuSelection(); e.Handled = true; break;
                case Key.Escape: vm.GlobalMenuBack(); e.Handled = true; break;
                case Key.Tab: vm.CloseGlobalMenu(); e.Handled = true; break;
            }
            return;
        }

        if (vm.IsDownloadsOpen)
        {
            switch (e.Key)
            {
                case Key.Escape: case Key.Tab: case Key.D: vm.CloseDownloads(); e.Handled = true; break;
                case Key.Enter: vm.OpenSteamDownloads(); e.Handled = true; break;
            }
            return;
        }

        if (vm.IsSubFilterRowFocused)
        {
            switch (e.Key)
            {
                case Key.Left: vm.MoveSubFilterCursor(-1); e.Handled = true; return;
                case Key.Right: vm.MoveSubFilterCursor(1); e.Handled = true; return;
                case Key.Enter: case Key.Space: vm.ToggleFocusedSubFilter(); e.Handled = true; return;
                // v0.25.2: no Cartoon os subfiltros ficam no rodapé (sai-se por cima ou por baixo).
                case Key.Up when _filtersBelowGrid:
                case Key.Down when _filtersBelowGrid:
                case Key.Down: case Key.Escape: vm.LeaveSubFilterRow(); e.Handled = true; return;
                case Key.Up: e.Handled = true; return;
            }
        }

        var settings = AppSettingsStore.Load();

        // v0.25.4: paridade de teclado com o controle no botão "+".
        if (_addButtonFocused)
        {
            if (e.Key == Key.Escape) { SetAddButtonFocused(false); e.Handled = true; return; }
            if (e.Key == Key.Enter || TryMatch(settings.KeyLaunchGame, e))
            {
                SetAddButtonFocused(false);
                HandleAddProgramClick();
                e.Handled = true;
                return;
            }
            if (e.Key is Key.Up or Key.Left)
            {
                ReturnFromAddProgramButton(vm);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Down)
            {
                SetAddButtonFocused(false);
                if (_filtersBelowGrid) vm.FocusSubFilterRow();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Right)
            {
                SetAddButtonFocused(false);
                e.Handled = true;
                return;
            }
        }

        // v0.25.2: ecrã de lançamento (Cartoon). Com ele aberto é modal: Enter
        // joga, X/Pespaço abrem os detalhes e Esc/B fecham — o resto é ignorado.
        if (_libraryLayout is ILaunchScreenHost launchHost && launchHost.IsLaunchScreenOpen)
        {
            if (e.Key == Key.Escape || TryMatch(settings.KeyGlobalMenu, e)) launchHost.CloseLaunchScreen();
            else if (TryMatch(settings.KeyLaunchGame, e) || e.Key == Key.Enter) { _ = vm.LaunchSelected(); launchHost.CloseLaunchScreen(); }
            else if (e.Key == Key.Space || TryMatch(settings.KeyContextMenu, e)) { vm.OpenGameDetailsForSelected(); launchHost.CloseLaunchScreen(); }
            e.Handled = true;
            return;
        }

        if (TryMatch(settings.KeyLaunchGame, e))
        {
            if (_libraryLayout is ILaunchScreenHost host) host.OpenLaunchScreen();
            else { _ = vm.LaunchSelected(); ScrollGameRowSelectionIntoView(vm); }
            e.Handled = true; return;
        }
        if (TryMatch(settings.KeyGlobalMenu, e)) { vm.ToggleGlobalMenu(); e.Handled = true; return; }
        if (TryMatch(settings.KeyCloseGame, e)) { vm.CloseRunningGame(); e.Handled = true; return; }
        if (TryMatch(settings.KeyContextMenu, e)) { vm.ToggleContextMenu(); e.Handled = true; return; }
        if (TryMatch(settings.KeyPrevFilter, e)) { vm.CyclePreviousFilter(); e.Handled = true; return; }
        if (TryMatch(settings.KeyNextFilter, e)) { vm.CycleNextFilter(); e.Handled = true; return; }
        if (TryMatch(settings.KeyDownloads, e)) { vm.ToggleDownloads(); e.Handled = true; return; }

        switch (e.Key)
        {
            case Key.Left: vm.MovePrevious(); ScrollGameRowSelectionIntoView(vm); e.Handled = true; break;
            case Key.Right:
                if (!_addButtonFocused && vm.IsFilterApplicationsActive)
                {
                    var games = vm.Games;
                    var noGames = games.Count == 0;
                    var atLast = games.Count > 0 && vm.SelectedTile == games[^1];
                    if ((noGames || (!IsGridNav && atLast)) && TryFocusAddProgramButton(vm))
                    { e.Handled = true; break; }
                }
                vm.MoveNext(); ScrollGameRowSelectionIntoView(vm); e.Handled = true; break;
            case Key.Up:
                if (_filtersBelowGrid) MoveSelectionVertical(vm, -1);
                else if (!MoveSelectionVertical(vm, -1)) vm.FocusSubFilterRow();
                e.Handled = true; break;
            case Key.Down:
                if (_filtersBelowGrid)
                {
                    if (!_addButtonFocused && vm.Games.Count == 0 && TryFocusAddProgramButton(vm))
                    { e.Handled = true; break; }
                    if (!MoveSelectionVertical(vm, 1))
                    {
                        if (!_addButtonFocused && TryFocusAddProgramButton(vm))
                        { e.Handled = true; break; }
                        vm.FocusSubFilterRow();
                    }
                }
                else if (IsGridNav)
                {
                    if (!MoveSelectionVertical(vm, 1) && TryFocusAddProgramButton(vm))
                    { e.Handled = true; break; }
                }
                else
                {
                    MoveSelectionVertical(vm, 1);
                }
                e.Handled = true; break;
        }
    }

    private bool TryMatch(string gestureString, KeyEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(gestureString)) return false;
        try { var gesture = KeyGesture.Parse(gestureString); return gesture.Matches(e); }
        catch { return false; }
    }

    private void ScrollCoverPickerSelectionIntoView(MainViewModel vm)
    {
        if (CoverPickerScrollViewer is null) return;
        if (!vm.IsCoverPickerOpen || vm.CoverPickerCandidates.Count == 0) return;
        const double itemHeight = 170 + 12;
        var row = vm.CoverPickerIndex / MainViewModel.CoverPickerColumns;
        var itemTop = row * itemHeight;
        var itemBottom = itemTop + itemHeight;
        var viewTop = CoverPickerScrollViewer.Offset.Y;
        var viewBottom = viewTop + CoverPickerScrollViewer.Viewport.Height;
        if (itemTop < viewTop) CoverPickerScrollViewer.Offset = new Vector(CoverPickerScrollViewer.Offset.X, itemTop);
        else if (itemBottom > viewBottom) CoverPickerScrollViewer.Offset = new Vector(CoverPickerScrollViewer.Offset.X, itemBottom - CoverPickerScrollViewer.Viewport.Height);
    }

    private void ScrollGameRowSelectionIntoView(MainViewModel vm) => Dispatcher.UIThread.Post(() => ScrollSelectionIntoViewNow(vm), DispatcherPriority.Background);

    private void ScrollSelectionIntoViewNow(MainViewModel vm)
    {
        if (GameRowScrollViewer is null || GamesItemsControl is null) return;
        if (vm.Games.Count == 0 || vm.SelectedIndex < 0 || vm.SelectedIndex >= vm.Games.Count) return;

        var container = GamesItemsControl.ContainerFromIndex(vm.SelectedIndex);
        // Medir a tile em relação à janela visível do ScrollViewer (não ao ItemsControl).
        // Assim a posição já considera o deslocamento atual e funciona mesmo quando
        // o ItemsControl ocupa toda a largura do layout.
        var position = container?.TranslatePoint(new Point(0, 0), GameRowScrollViewer);
        if (container is not null && position is { } p)
        {
            var offset = GameRowScrollViewer.Offset;
            var viewport = GameRowScrollViewer.Viewport;
            var maxX = Math.Max(0, GameRowScrollViewer.Extent.Width - viewport.Width);
            var maxY = Math.Max(0, GameRowScrollViewer.Extent.Height - viewport.Height);

            if (IsGridNav)
            {
                var top = p.Y - 12;
                var bottom = p.Y + container.Bounds.Height + 12;
                var nextY = offset.Y;
                if (top < 0) nextY += top;
                else if (bottom > viewport.Height) nextY += bottom - viewport.Height;
                GameRowScrollViewer.Offset = new Vector(offset.X, Math.Clamp(nextY, 0, maxY));
            }
            else if (_libraryLayout.CentersSelection)
            {
                var target = offset.X + p.X + container.Bounds.Width / 2 - viewport.Width / 2;
                GameRowScrollViewer.Offset = new Vector(Math.Clamp(target, 0, maxX), offset.Y);
            }
            else
            {
                // Clássico: deslocamento mínimo, somente quando a capa sair da área visível.
                var left = p.X;
                var right = p.X + container.Bounds.Width;
                var nextX = offset.X;
                if (left < 0) nextX += left;
                else if (right > viewport.Width) nextX += right - viewport.Width;
                GameRowScrollViewer.Offset = new Vector(Math.Clamp(nextX, 0, maxX), offset.Y);
            }
            return;
        }

        // Fallback para o caso em que o container ainda não foi realizado pelo ItemsControl.
        if (IsGridNav) return;
        const double spacing = 28;
        double itemLeft = 0;
        for (var i = 0; i < vm.SelectedIndex; i++)
            itemLeft += vm.Games[i].TileWidth + spacing;

        var itemRight = itemLeft + vm.Games[vm.SelectedIndex].TileWidth;
        var viewLeft = GameRowScrollViewer.Offset.X;
        var viewRight = viewLeft + GameRowScrollViewer.Viewport.Width;
        var fallbackMaxOffset = Math.Max(0, GameRowScrollViewer.Extent.Width - GameRowScrollViewer.Viewport.Width);
        if (itemLeft < viewLeft)
            GameRowScrollViewer.Offset = new Vector(Math.Clamp(itemLeft, 0, fallbackMaxOffset), GameRowScrollViewer.Offset.Y);
        else if (itemRight > viewRight)
            GameRowScrollViewer.Offset = new Vector(Math.Clamp(itemRight - GameRowScrollViewer.Viewport.Width, 0, fallbackMaxOffset), GameRowScrollViewer.Offset.Y);
    }

    private bool MoveSelectionVertical(MainViewModel vm, int direction)
    {
        if (!IsGridNav || vm.SelectedIndex < 0) return false;
        var current = GamesItemsControl.ContainerFromIndex(vm.SelectedIndex);
        if (current is null) return false;
        var currentBounds = current.Bounds;
        var currentCenterX = currentBounds.X + currentBounds.Width / 2;
        var best = -1; var bestRowY = direction > 0 ? double.MaxValue : double.MinValue; var bestDx = double.MaxValue;
        for (var i = 0; i < vm.Games.Count; i++)
        {
            var container = GamesItemsControl.ContainerFromIndex(i);
            if (container is null) continue;
            var b = container.Bounds;
            var isInDirection = direction > 0 ? b.Y > currentBounds.Y + 1 : b.Y < currentBounds.Y - 1;
            if (!isInDirection) continue;
            var isCloserRow = direction > 0 ? b.Y < bestRowY - 1 : b.Y > bestRowY + 1;
            var isSameRow = Math.Abs(b.Y - bestRowY) <= 1;
            var dx = Math.Abs(b.X + b.Width / 2 - currentCenterX);
            if (isCloserRow || (isSameRow && dx < bestDx)) { best = i; bestRowY = b.Y; bestDx = dx; }
        }
        if (best < 0) return false;
        vm.SelectedIndex = best;
        ScrollGameRowSelectionIntoView(vm);
        return true;
    }

    public void HandleSubFilterClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is Button { DataContext: SubFilterViewModel subFilter }) vm.ToggleSubFilter(subFilter);
    }

    public void HandleTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not Control { DataContext: GameTileViewModel tile }) return;
        var index = vm.Games.IndexOf(tile);
        if (index >= 0) vm.SelectedIndex = index;
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed && !vm.IsContextMenuOpen) { vm.ToggleContextMenu(); e.Handled = true; }
    }

    public void HandleTileDoubleTapped(object? sender, TappedEventArgs e) { if (DataContext is not MainViewModel vm) return; _ = vm.LaunchSelected(); ScrollGameRowSelectionIntoView(vm); }

}