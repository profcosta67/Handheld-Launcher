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
/// v1 deliberately does NOT auto-repeat on held direction — only distinct
/// presses navigate. Feels a bit stiff for fast scrolling through a long
/// library; worth revisiting once this is confirmed working on real
/// hardware (easier to tune repeat timing with an actual controller in
/// hand than to guess it blind).
/// </summary>
public sealed class GamepadPoller
{
    // Standard-ish deadzone for the left stick's X/Y axes (range is -32768..32767).
    private const short StickDeadzone = 12000;

    private XInput.Buttons _previousButtons;
    private bool _previousStickLeft;
    private bool _previousStickRight;
    private bool _previousStickUp;
    private bool _previousStickDown;

    public bool IsConnected { get; private set; }

    public IReadOnlyList<GamepadAction> Poll()
    {
        var actions = new List<GamepadAction>();

        if (!XInput.TryGetState(out var state))
        {
            IsConnected = false;
            _previousButtons = default;
            _previousStickLeft = false;
            _previousStickRight = false;
            _previousStickUp = false;
            _previousStickDown = false;
            return actions;
        }

        IsConnected = true;
        var buttons = (XInput.Buttons)state.Gamepad.wButtons;

        if (WasJustPressed(buttons, XInput.Buttons.DPadLeft)) actions.Add(GamepadAction.MoveLeft);
        if (WasJustPressed(buttons, XInput.Buttons.DPadRight)) actions.Add(GamepadAction.MoveRight);
        if (WasJustPressed(buttons, XInput.Buttons.DPadUp)) actions.Add(GamepadAction.MoveUp);
        if (WasJustPressed(buttons, XInput.Buttons.DPadDown)) actions.Add(GamepadAction.MoveDown);
        if (WasJustPressed(buttons, XInput.Buttons.A)) actions.Add(GamepadAction.Confirm);
        if (WasJustPressed(buttons, XInput.Buttons.B)) actions.Add(GamepadAction.Back);
        if (WasJustPressed(buttons, XInput.Buttons.Start)) actions.Add(GamepadAction.Menu);
        if (WasJustPressed(buttons, XInput.Buttons.Back)) actions.Add(GamepadAction.GlobalMenu);

        // Left stick as a D-pad alternative — same edge-detection idea, just
        // on a deadzone crossing instead of a button bit. Bug fix: this used
        // to only read sThumbLX (left/right) — pushing the stick up/down
        // never produced MoveUp/MoveDown at all, so the two menus (which
        // need vertical navigation) only responded to the D-pad, not the
        // stick. XInput's Y axis is positive = up, negative = down.
        var stickLeft = state.Gamepad.sThumbLX < -StickDeadzone;
        var stickRight = state.Gamepad.sThumbLX > StickDeadzone;
        var stickUp = state.Gamepad.sThumbLY > StickDeadzone;
        var stickDown = state.Gamepad.sThumbLY < -StickDeadzone;

        if (stickLeft && !_previousStickLeft) actions.Add(GamepadAction.MoveLeft);
        if (stickRight && !_previousStickRight) actions.Add(GamepadAction.MoveRight);
        if (stickUp && !_previousStickUp) actions.Add(GamepadAction.MoveUp);
        if (stickDown && !_previousStickDown) actions.Add(GamepadAction.MoveDown);

        _previousStickLeft = stickLeft;
        _previousStickRight = stickRight;
        _previousStickUp = stickUp;
        _previousStickDown = stickDown;
        _previousButtons = buttons;

        return actions;
    }

    private bool WasJustPressed(XInput.Buttons current, XInput.Buttons button)
        => current.HasFlag(button) && !_previousButtons.HasFlag(button);
}
