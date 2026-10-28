namespace HandheldLauncher.App.Input;

public enum GamepadAction
{
    MoveLeft,
    MoveRight,
    MoveUp,
    MoveDown,
    Confirm,
    Back,

    /// <summary>Start button — opens/closes the per-tile context menu.</summary>
    Menu,

    /// <summary>
    /// View/Back button (XInput still calls it "Back", same physical key
    /// since the Xbox 360 era) — opens/closes the global actions menu
    /// (Buscar capas, Adicionar jogo, Atualizar, Sair, Fechar jogo). This is
    /// what makes the bottom bar's buttons reachable without a mouse.
    /// </summary>
    GlobalMenu,
}

/// <summary>
/// Turns raw XInput state into high-level navigation actions, with edge
/// detection so a held button fires once, not every poll. Call Poll() from
/// a UI-thread timer; it's cheap enough to run at 20-30Hz.
///
/// Directions (D-pad and stick) now auto-repeat while held — bug reported
/// by the user on the real Legion Go 2: "quando segurar o direcional pra
/// os lados ele não vai até o final, ele fica parado. Só passa dando
/// toque" (holding a direction didn't keep moving, only a fresh tap did —
/// exactly what the old "v1 deliberately does NOT auto-repeat" doc comment
/// here used to warn about). A/B/Start/Back stay single-press-only on
/// purpose (repeating "Confirm" while A is held would re-launch the game
/// over and over).
/// </summary>
public sealed class GamepadPoller
{
    // Standard-ish deadzone for the left stick's X/Y axes (range is -32768..32767).
    private const short StickDeadzone = 12000;

    // Tuned assuming Poll() is called every 50ms (MainWindow's
    // _gamepadTimer interval) — NOT real elapsed time, just a tick count,
    // same "hand-rolled, no API I can't verify" approach as the rest of
    // this project's input/scroll code. ~350ms before the first repeat
    // (feels like a deliberate hold, not an accidental extra step), then
    // one extra move every ~120ms (fast enough to cross a big library
    // quickly, slow enough to still land on the tile you wanted).
    private const int RepeatInitialDelayTicks = 7;
    private const int RepeatIntervalTicks = 2;

    private XInput.Buttons _previousButtons;
    private int _leftHeldTicks;
    private int _rightHeldTicks;
    private int _upHeldTicks;
    private int _downHeldTicks;

    public bool IsConnected { get; private set; }

    public IReadOnlyList<GamepadAction> Poll()
    {
        var actions = new List<GamepadAction>();

        if (!XInput.TryGetState(out var state))
        {
            IsConnected = false;
            _previousButtons = default;
            _leftHeldTicks = 0;
            _rightHeldTicks = 0;
            _upHeldTicks = 0;
            _downHeldTicks = 0;
            return actions;
        }

        IsConnected = true;
        var buttons = (XInput.Buttons)state.Gamepad.wButtons;

        if (WasJustPressed(buttons, XInput.Buttons.A)) actions.Add(GamepadAction.Confirm);
        if (WasJustPressed(buttons, XInput.Buttons.B)) actions.Add(GamepadAction.Back);
        if (WasJustPressed(buttons, XInput.Buttons.Start)) actions.Add(GamepadAction.Menu);
        if (WasJustPressed(buttons, XInput.Buttons.Back)) actions.Add(GamepadAction.GlobalMenu);

        // D-pad OR left stick — either one holding a direction counts the
        // same way, so you never get a double-fire from both at once.
        // Bug fix kept from before: XInput's Y axis is positive = up,
        // negative = down.
        var isLeftHeld = buttons.HasFlag(XInput.Buttons.DPadLeft) || state.Gamepad.sThumbLX < -StickDeadzone;
        var isRightHeld = buttons.HasFlag(XInput.Buttons.DPadRight) || state.Gamepad.sThumbLX > StickDeadzone;
        var isUpHeld = buttons.HasFlag(XInput.Buttons.DPadUp) || state.Gamepad.sThumbLY > StickDeadzone;
        var isDownHeld = buttons.HasFlag(XInput.Buttons.DPadDown) || state.Gamepad.sThumbLY < -StickDeadzone;

        if (UpdateHeldDirection(isLeftHeld, ref _leftHeldTicks)) actions.Add(GamepadAction.MoveLeft);
        if (UpdateHeldDirection(isRightHeld, ref _rightHeldTicks)) actions.Add(GamepadAction.MoveRight);
        if (UpdateHeldDirection(isUpHeld, ref _upHeldTicks)) actions.Add(GamepadAction.MoveUp);
        if (UpdateHeldDirection(isDownHeld, ref _downHeldTicks)) actions.Add(GamepadAction.MoveDown);

        _previousButtons = buttons;

        return actions;
    }

    /// <summary>
    /// One direction's hold/repeat state machine. Not held → resets the
    /// counter and never fires. Held → counts ticks; fires on the very
    /// first tick (tick 1, the initial tap-like press), stays quiet through
    /// the initial delay, then fires every RepeatIntervalTicks after that.
    /// </summary>
    private static bool UpdateHeldDirection(bool isHeld, ref int heldTicks)
    {
        if (!isHeld)
        {
            heldTicks = 0;
            return false;
        }

        heldTicks++;
        if (heldTicks == 1) return true;
        if (heldTicks <= RepeatInitialDelayTicks) return false;
        return (heldTicks - RepeatInitialDelayTicks) % RepeatIntervalTicks == 0;
    }

    private bool WasJustPressed(XInput.Buttons current, XInput.Buttons button)
        => current.HasFlag(button) && !_previousButtons.HasFlag(button);
}
