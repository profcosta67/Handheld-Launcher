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

/// <summary>v0.28: leitura do controle na tela principal (tick de 50 ms, ponte HID, foco em diálogos). Separado do GameplayWatcher.</summary>
public partial class MainWindow
{
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
            Closed += (_, _) => { var pad = _hidPad; _hidPad = null; if (pad is not null) ShutdownStepBounded("leitor HID do controle", pad.Dispose); };
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

        // v0.25.10 ("ghost" nos toques): com o QS EM FOCO (modo padrão desde 11/11) o
        // XInput chega ao painel normalmente, com debounce e repeat. A ponte conta cada
        // borda crua do HID (sem debounce) e entrava ~60 ms antes do XInput — no log de
        // 12/11 13:15 apareciam movimentos da ponte sem aperto correspondente no XInput.
        // Com foco, a fonte é o XInput; a ponte só vale quando o painel está SEM foco.
        // Revisão 12/11: só se o XInput estiver vivo — slot morto (reinício do controle,
        // HidHide) volta para a ponte, senão o QS ficaria sem input nenhum.
        if (_overlayWindow is { IsActive: true } && _gamepad.IsConnected)
        {
            if (!_hidPadLoggedDuplicate)
            {
                _hidPadLoggedDuplicate = true;
                DebugLog.Write("[QS-INPUT] QS em foco: usando o XInput (a ponte HID fica calada para não duplicar).");
            }
            return xinputActions;
        }

        if (xinputActions.Count > 0 && !_hidPadLoggedDuplicate)
        {
            _hidPadLoggedDuplicate = true;
            DebugLog.Write("[QS-INPUT] XInput também entregou botões com o QS sem foco; usando só a ponte HID.");
        }
        if (hidActions.Count > 0)
            DebugLog.Write($"[QS-INPUT] Ponte HID → {string.Join(", ", hidActions)}");
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
        using var __meter = HandheldLauncher.Core.Services.Power.ActivityMeter.Measure("controle (leitura a cada 50 ms)");
        try
        {
            if (DataContext is not MainViewModel vm) return;
            var actions = _gamepad.Poll();

            // v0.25.9: QS aberto num jogo → botões vêm do ajudante (HID em
            // segundo plano), porque o XInput não chega a quem está sem foco.
            actions = ApplyHidBridge(actions);

            // v0.25.10: o mesmo aperto que já chegou como tecla (controle virando teclado) não vale duas vezes.
            if (actions.Count > 0)
                actions = actions.Where(a => !HandheldLauncher.App.Input.InputDedup.DropGamepad(a)).ToList();

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
                // 18/11: com o seletor de arquivos do Windows aberto por cima do "Adicionar jogo",
                // os botões são dele — B não pode fechar a janela por baixo.
                if (dialog is AddGameWindow { IsNativePickerOpen: true })
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
}
