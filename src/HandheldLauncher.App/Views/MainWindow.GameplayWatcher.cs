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
using HandheldLauncher.Core.Services.Power;
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

    private int _procDeadTicks;
    private int _gameWatcherTicks = 0;

    private bool _gameWatcherDiagnosticLogged = false;

    // v0.27: o PID também fica visível para o TDP inteligente (GameSessionInfo), que só
    // pode medir o FPS do jogo — nunca do lançador da loja que estiver na frente.
    private int _detectedGamePidValue;
    private int _detectedGamePid
    {
        get => _detectedGamePidValue;
        set
        {
            _detectedGamePidValue = value;
            HandheldLauncher.App.Platform.GameSessionInfo.DetectedGamePid = value;
        }
    }

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

            // 24/11: 90 → 45 s — se a janela do jogo não for reconhecida, a tela de abertura não fica
            // parada por cima do jogo por um minuto e meio
            _launchPopupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
            _launchPopupTimer.Tick += (_, _) =>
            {
                _launchPopupTimer?.Stop();

                bool isSame = ReferenceEquals(_launchPopup, thisPopup);
                DebugLog.Write($"[POPUP] Timeout 45s disparado. isSame={isSame}, visible={_launchPopup?.IsVisible}, loaded={_launchPopup?.IsLoaded}");

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

            DebugLog.Write($"[POPUP] Aberto: '{title}' (watchdog 45s iniciado)");
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
        // 24/11 (relato: janela de abertura não fechava e o launcher não voltava com um port): quando o
        // launcher abriu o próprio .exe do jogo (port, atalho), é por ele que se acha a janela — o nome
        // do jogo ("Ocarina of Time") nem sempre está no título ("Ship of Harkinian") e o "maior .exe da
        // pasta" pode ser outro programa. Emulador fica de fora: a janela dele existe sem o jogo.
        var ownExe = IsDirectlyLaunchedGame(game)
            ? Path.GetFileNameWithoutExtension(game.ExecutablePath)!.ToLowerInvariant()
            : null;
        if (ownExe is not null && new[] { "unins", "crash", "launcher", "setup", "installer", "prereq" }.Any(b => ownExe.Contains(b)))
            ownExe = null;   // .exe com cara de lançador: fica o jeito antigo (maior .exe da pasta)
        try
        {
            if (ownExe is not null) exeHint = ownExe;
            else if (!string.IsNullOrEmpty(game.InstallDirectory) && Directory.Exists(game.InstallDirectory))
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

        // 24/11: janela do próprio processo que o launcher abriu (ports e atalhos diretos)
        if (!found && ownExe is not null && DataContext is MainViewModel pidVm && pidVm.LaunchedProcessId is var launchedPid && launchedPid > 0)
        {
            foreach (var w in windows)
            {
                if (w.Pid != (uint)launchedPid) continue;
                found = true;
                foundHwnd = w.Hwnd;
                foundPid = w.Pid;
                break;
            }
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

    /// <summary>
    /// 24/11: jogo que o launcher abre pelo próprio .exe e cujo processo é o jogo — ports e atalhos
    /// manuais. Lojas (Epic, GOG, EA, Xbox) e emuladores ficam de fora: o .exe de lá pode ser só um
    /// lançador, e a janela do emulador existe sem o jogo.
    /// </summary>
    internal static bool IsDirectlyLaunchedGame(GameEntry game) =>
        !string.IsNullOrWhiteSpace(game.ExecutablePath)
        && string.IsNullOrWhiteSpace(game.LaunchUri)
        && (game.Source == GameSource.Manual || game.Id.StartsWith(HandheldLauncher.Core.Services.PortsLibraryScanner.IdPrefix, StringComparison.Ordinal))
        && !HandheldLauncher.Core.Services.ManualLaunchTarget.IsSwitchEmulator(game.ExecutablePath);

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
            using var __meter = HandheldLauncher.Core.Services.Power.ActivityMeter.Measure("vigia do jogo");
            // 18/11: sem jogo, o PID detectado volta a 0 (antes ficava o do último jogo, e o pedido
            // HID ao controle achava que ainda havia jogo aberto).
            if (vm.RunningGame is null) { _gameWindowSeen = false; _gameWindowMissingSeconds = 0; _gameWatcherTicks = 0; if (_detectedGamePid != 0) _detectedGamePid = 0; return; }
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

                    // 24/11 (log: EA FC 27 abriu uma janela, o processo dela saiu e o jogo de verdade
                    // continuou em outro processo — o launcher voltou por cima do jogo). Se outra janela
                    // do jogo já está na tela, passa a acompanhar essa; senão espera 5 s antes de voltar.
                    if (procDead && found && foundPid != 0 && foundPid != (uint)_detectedGamePid)
                    {
                        DebugLog.Write($"[JOGO-WATCHER] Processo {_detectedGamePid} saiu, mas o jogo continua no processo {foundPid} — acompanhando esse.");
                        _detectedGamePid = (int)foundPid;
                        if (foundHwnd != IntPtr.Zero) LosslessScalingService.SetGameHwnd(foundHwnd);
                        _procDeadTicks = 0;
                        procDead = false;
                    }
                    else if (procDead && ++_procDeadTicks < 5)
                    {
                        if (_procDeadTicks == 1) DebugLog.Write("[JOGO-WATCHER] Processo do jogo saiu — esperando 5 s por outra janela do jogo antes de voltar.");
                        return;
                    }
                    if (!procDead) _procDeadTicks = 0;

                    if (procDead)
                    {
                        _procDeadTicks = 0;
                        DebugLog.Write($"[JOGO-WATCHER] Processo do jogo morreu e nenhuma janela do jogo voltou — restaurando launcher.");
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
            // v0.28 (bateria): sem download em andamento, confere a cada 30 s (antes: 6 s para sempre)
            if (ticksSinceBackgroundRefresh >= (vm.ActiveDownloads.Count > 0 ? 3 : 15)) { ticksSinceBackgroundRefresh = 0; vm.RefreshActiveDownloads(); }
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
        _batteryTimer.Tick += (_, _) =>
        {
            vm.BatteryStatusText = BatteryStatus.GetDisplayText();
            vm.ClockText = DateTime.Now.ToString("HH:mm");
            NoteBatteryForAnimations(BatteryStatus.TryRead() is { } bs ? !bs.OnAc : null);
        };
        _batteryTimer.Start();
        Closed += (_, _) => _batteryTimer?.Stop();

        vm.SensorsFastCheck = () => _overlayWindow is { IsVisible: true } || _activeDialog is not null
            || (WindowState != WindowState.Minimized && IsVisible && IsOurWindowInForeground());

        // v0.28: diagnóstico de bateria (amostras em segundo plano; ver BatteryDiagnostics)
        BatteryDiagnostics.Start(() =>
        {
            var game = vm.RunningGame;
            var qs = _overlayWindow is { IsVisible: true };
            if (game is not null) return new LauncherActivityState(PowerState.Game, game.TitleOverride ?? game.Title, qs);
            var front = _activeDialog is not null
                || (WindowState != WindowState.Minimized && IsVisible && IsOurWindowInForeground());
            return new LauncherActivityState(front || qs ? PowerState.Launcher : PowerState.Background, null, qs);
        });
        Closed += (_, _) => BatteryDiagnostics.Stop();
    }

}