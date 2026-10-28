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
    // Standard-ish deadzone for the left stick's X axis (range is -32768..32767).
    private const short StickDeadzone = 12000;

    private XInput.Buttons _previousButtons;
    private bool _previousStickLeft;
    private bool _previousStickRight;

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

        // Left stick as a D-pad alternative — same edge-detection idea, just
        // on a deadzone crossing instead of a button bit.
        var stickLeft = state.Gamepad.sThumbLX < -StickDeadzone;
        var stickRight = state.Gamepad.sThumbLX > StickDeadzone;

        if (stickLeft && !_previousStickLeft) actions.Add(GamepadAction.MoveLeft);
        if (stickRight && !_previousStickRight) actions.Add(GamepadAction.MoveRight);

        _previousStickLeft = stickLeft;
        _previousStickRight = stickRight;
        _previousButtons = buttons;

        return actions;
    }

    private bool WasJustPressed(XInput.Buttons current, XInput.Buttons button)
        => current.HasFlag(button) && !_previousButtons.HasFlag(button);
}
