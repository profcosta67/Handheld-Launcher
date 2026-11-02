using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Input;

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
    GameDetails,
    ToggleQuickSettings,
}

public sealed class GamepadPoller
{
    private const short StickDeadzone = 12000;
    private const int RepeatInitialDelayTicks = 7;
    private const int RepeatIntervalTicks = 2;

    private readonly XInput.Buttons[] _previousButtons = new XInput.Buttons[4];
    private readonly int[] _leftHeldTicks = new int[4];
    private readonly int[] _rightHeldTicks = new int[4];
    private readonly int[] _upHeldTicks = new int[4];
    private readonly int[] _downHeldTicks = new int[4];

    private int _activeSlot = -1;
    private DateTime _lastDiag = DateTime.MinValue;
    private DateTime _slotLockedUntil = DateTime.MinValue;

    public bool IsConnected { get; private set; }
    public int ActiveSlot => _activeSlot;

    public IReadOnlyList<GamepadAction> Poll()
    {
        try
        {
            var actions = new List<GamepadAction>();

            UpdateActiveSlot();

            if ((DateTime.Now - _lastDiag).TotalSeconds >= 3)
            {
                _lastDiag = DateTime.Now;
                var slotInfo = _activeSlot >= 0 ? _activeSlot.ToString() : "none";
                var connected = "";
                for (uint s = 0; s < 4; s++)
                    if (XInput.TryGetState(s, out _)) connected += s;
                DebugLog.Write($"[GamepadPoller] Estado: slot={slotInfo}, conectados=[{connected}], lock={(_slotLockedUntil > DateTime.Now ? "sim" : "não")}");
            }

            if (_activeSlot < 0 || !XInput.TryGetState((uint)_activeSlot, out var state))
            {
                IsConnected = false;
                ResetAllSlots();
                return actions;
            }

            IsConnected = true;
            var buttons = (XInput.Buttons)state.Gamepad.wButtons;
            var prevButtons = _previousButtons[_activeSlot];

            // 🟢 NOVA LÓGICA: Start = Menu Global (sistema), Back/Select = Opções do Jogo
            if (WasJustPressed(buttons, prevButtons, XInput.Buttons.Start)) actions.Add(GamepadAction.GlobalMenu);
            if (WasJustPressed(buttons, prevButtons, XInput.Buttons.Back)) actions.Add(GamepadAction.Menu);
            if (WasJustPressed(buttons, prevButtons, XInput.Buttons.A)) actions.Add(GamepadAction.Confirm);
            if (WasJustPressed(buttons, prevButtons, XInput.Buttons.B)) actions.Add(GamepadAction.Back);
            if (WasJustPressed(buttons, prevButtons, XInput.Buttons.LeftShoulder)) actions.Add(GamepadAction.PreviousFilter);
            if (WasJustPressed(buttons, prevButtons, XInput.Buttons.RightShoulder)) actions.Add(GamepadAction.NextFilter);
            if (WasJustPressed(buttons, prevButtons, XInput.Buttons.Y)) actions.Add(GamepadAction.Downloads);
            if (WasJustPressed(buttons, prevButtons, XInput.Buttons.X)) actions.Add(GamepadAction.GameDetails);

            // 🟢 Direções com deadzone (sem Math.Abs — usa comparação dupla)
            var lx = state.Gamepad.sThumbLX;
            var ly = state.Gamepad.sThumbLY;
            var isLeftHeld = buttons.HasFlag(XInput.Buttons.DPadLeft) || lx < -StickDeadzone;
            var isRightHeld = buttons.HasFlag(XInput.Buttons.DPadRight) || lx > StickDeadzone;
            var isUpHeld = buttons.HasFlag(XInput.Buttons.DPadUp) || ly > StickDeadzone;
            var isDownHeld = buttons.HasFlag(XInput.Buttons.DPadDown) || ly < -StickDeadzone;

            if (UpdateHeldDirection(isLeftHeld, ref _leftHeldTicks[_activeSlot])) actions.Add(GamepadAction.MoveLeft);
            if (UpdateHeldDirection(isRightHeld, ref _rightHeldTicks[_activeSlot])) actions.Add(GamepadAction.MoveRight);
            if (UpdateHeldDirection(isUpHeld, ref _upHeldTicks[_activeSlot])) actions.Add(GamepadAction.MoveUp);
            if (UpdateHeldDirection(isDownHeld, ref _downHeldTicks[_activeSlot])) actions.Add(GamepadAction.MoveDown);

            if (actions.Count > 0)
                DebugLog.Write($"[GamepadPoller] Ações slot {_activeSlot}: {string.Join(", ", actions)}");

            return actions;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[GamepadPoller] EXCEPTION em Poll: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            return Array.Empty<GamepadAction>();
        }
        finally
        {
            for (uint slot = 0; slot < 4; slot++)
            {
                if (XInput.TryGetState(slot, out var s))
                    _previousButtons[slot] = (XInput.Buttons)s.Gamepad.wButtons;
                else
                    _previousButtons[slot] = default;
            }
        }
    }

    private void UpdateActiveSlot()
    {
        for (uint slot = 0; slot < 4; slot++)
        {
            if (!XInput.TryGetState(slot, out var s))
            {
                _previousButtons[slot] = default;
                continue;
            }

            var cur = (XInput.Buttons)s.Gamepad.wButtons;
            var prev = _previousButtons[slot];

            if ((cur & ~prev) != 0)
            {
                if ((int)slot != _activeSlot)
                {
                    DebugLog.Write($"[GamepadPoller] Slot → {slot} (botão novo)");
                    _activeSlot = (int)slot;
                }
                _slotLockedUntil = DateTime.Now.AddSeconds(3);
                return;
            }
        }

        if (_activeSlot >= 0 && XInput.TryGetState((uint)_activeSlot, out _))
            return;

        for (uint slot = 0; slot < 4; slot++)
        {
            if (XInput.TryGetState(slot, out _))
            {
                if ((int)slot != _activeSlot)
                    DebugLog.Write($"[GamepadPoller] Slot → {slot} (fallback)");
                _activeSlot = (int)slot;
                return;
            }
        }
        _activeSlot = -1;
    }

    private void ResetAllSlots()
    {
        for (var i = 0; i < 4; i++)
        {
            _previousButtons[i] = default;
            _leftHeldTicks[i] = 0;
            _rightHeldTicks[i] = 0;
            _upHeldTicks[i] = 0;
            _downHeldTicks[i] = 0;
        }
    }

    private static bool UpdateHeldDirection(bool isHeld, ref int heldTicks)
    {
        if (!isHeld) { heldTicks = 0; return false; }
        heldTicks++;
        if (heldTicks == 1) return true;
        if (heldTicks <= RepeatInitialDelayTicks) return false;
        return (heldTicks - RepeatInitialDelayTicks) % RepeatIntervalTicks == 0;
    }

    private static bool WasJustPressed(XInput.Buttons current, XInput.Buttons previous, XInput.Buttons button)
        => current.HasFlag(button) && !previous.HasFlag(button);
}