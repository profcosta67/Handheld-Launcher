namespace HandheldLauncher.App.Input;

// O Enum fica aqui no topo
public enum GamepadAction
{
    MoveLeft,
    MoveRight,
    MoveUp,
    MoveDown,
    Confirm,
    Back,
    Menu,
    GlobalMenu,
    PreviousFilter,
    NextFilter,
    Downloads,
    CycleOverlay,        // L3 + R3
    GameDetails,
    ToggleQuickSettings, // Back + Start (Select + Start)
}

public sealed class GamepadPoller
{
    private const short StickDeadzone = 12000;
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

        // 🟢 LÓGICA DO CHORD: Back + Start (Select + Start)
        var isBackStartPressed = buttons.HasFlag(XInput.Buttons.Back) && buttons.HasFlag(XInput.Buttons.Start);
        var wasBackStartPressed = _previousButtons.HasFlag(XInput.Buttons.Back) && _previousButtons.HasFlag(XInput.Buttons.Start);

        if (isBackStartPressed && !wasBackStartPressed)
        {
            actions.Add(GamepadAction.ToggleQuickSettings);
        }
        else
        {
            // Se não for o chord, processa os botões normalmente
            if (WasJustPressed(buttons, XInput.Buttons.Start)) actions.Add(GamepadAction.Menu);
            if (WasJustPressed(buttons, XInput.Buttons.Back)) actions.Add(GamepadAction.GlobalMenu);
        }

        if (WasJustPressed(buttons, XInput.Buttons.A)) actions.Add(GamepadAction.Confirm);
        if (WasJustPressed(buttons, XInput.Buttons.B)) actions.Add(GamepadAction.Back);
        if (WasJustPressed(buttons, XInput.Buttons.LeftShoulder)) actions.Add(GamepadAction.PreviousFilter);
        if (WasJustPressed(buttons, XInput.Buttons.RightShoulder)) actions.Add(GamepadAction.NextFilter);
        if (WasJustPressed(buttons, XInput.Buttons.Y)) actions.Add(GamepadAction.Downloads);
        if (WasJustPressed(buttons, XInput.Buttons.X)) actions.Add(GamepadAction.GameDetails);

        // L3+R3 (abrir/fechar o HUD no jogo)
        var isL3R3Pressed = buttons.HasFlag(XInput.Buttons.LeftThumb) && buttons.HasFlag(XInput.Buttons.RightThumb);
        var wasL3R3Pressed = _previousButtons.HasFlag(XInput.Buttons.LeftThumb) && _previousButtons.HasFlag(XInput.Buttons.RightThumb);
        if (isL3R3Pressed && !wasL3R3Pressed)
        {
            actions.Add(GamepadAction.CycleOverlay);
        }

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

    private static bool UpdateHeldDirection(bool isHeld, ref int heldTicks)
    {
        if (!isHeld) { heldTicks = 0; return false; }
        heldTicks++;
        if (heldTicks == 1) return true;
        if (heldTicks <= RepeatInitialDelayTicks) return false;
        return (heldTicks - RepeatInitialDelayTicks) % RepeatIntervalTicks == 0;
    }

    private bool WasJustPressed(XInput.Buttons current, XInput.Buttons button)
        => current.HasFlag(button) && !_previousButtons.HasFlag(button);
}