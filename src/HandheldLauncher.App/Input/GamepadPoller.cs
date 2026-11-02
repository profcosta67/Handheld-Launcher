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
    // === Calibração do analógico ===
    // Precisa passar de PressDeadzone para começar um movimento.
    private const short StickPressDeadzone = 18000;
    // Precisa cair abaixo de ReleaseDeadzone para "soltar" (histerese — evita ruído do stick).
    private const short StickReleaseDeadzone = 8000;

    // === Debounce / cooldown ===
    // Um press só é confirmado depois de ficar pressionado por esse tempo.
    private const int PressDebounceMs = 35;
    // Depois de soltar, espera esse tempo antes de aceitar um novo press (anti-bounce).
    private const int ReleaseCooldownMs = 120;

    // === Repeat ===
    private const int RepeatInitialDelayMs = 500;
    private const int RepeatIntervalMs = 220;

    private readonly XInput.Buttons[] _previousButtons = new XInput.Buttons[4];

    private readonly DirectionState[] _leftState  = new DirectionState[4];
    private readonly DirectionState[] _rightState = new DirectionState[4];
    private readonly DirectionState[] _upState    = new DirectionState[4];
    private readonly DirectionState[] _downState  = new DirectionState[4];

    private int _activeSlot = -1;
    private DateTime _lastDiag = DateTime.MinValue;
    private DateTime _slotLockedUntil = DateTime.MinValue;

    public bool IsConnected { get; private set; }
    public int ActiveSlot => _activeSlot;

    private struct DirectionState
    {
        public bool IsActive;                    // "held" confirmado
        public DateTime FirstObservedPress;      // quando começou o press atual (para debounce)
        public DateTime HeldSince;               // quando foi confirmado como press
        public DateTime LastRepeat;              // último emit de repeat
        public DateTime ReleasedAt;              // último release confirmado (para cooldown)
    }

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

            // Botões de face
            if (WasJustPressed(buttons, prevButtons, XInput.Buttons.Start))          actions.Add(GamepadAction.GlobalMenu);
            if (WasJustPressed(buttons, prevButtons, XInput.Buttons.Back))           actions.Add(GamepadAction.Menu);
            if (WasJustPressed(buttons, prevButtons, XInput.Buttons.A))              actions.Add(GamepadAction.Confirm);
            if (WasJustPressed(buttons, prevButtons, XInput.Buttons.B))              actions.Add(GamepadAction.Back);
            if (WasJustPressed(buttons, prevButtons, XInput.Buttons.LeftShoulder))   actions.Add(GamepadAction.PreviousFilter);
            if (WasJustPressed(buttons, prevButtons, XInput.Buttons.RightShoulder))  actions.Add(GamepadAction.NextFilter);
            if (WasJustPressed(buttons, prevButtons, XInput.Buttons.Y))              actions.Add(GamepadAction.Downloads);
            if (WasJustPressed(buttons, prevButtons, XInput.Buttons.X))              actions.Add(GamepadAction.GameDetails);

            // === Direcionais ===
            var lx = state.Gamepad.sThumbLX;
            var ly = state.Gamepad.sThumbLY;

            bool dpadLeft  = buttons.HasFlag(XInput.Buttons.DPadLeft);
            bool dpadRight = buttons.HasFlag(XInput.Buttons.DPadRight);
            bool dpadUp    = buttons.HasFlag(XInput.Buttons.DPadUp);
            bool dpadDown  = buttons.HasFlag(XInput.Buttons.DPadDown);

            // PRIORIDADE: se qualquer D-Pad estiver pressionado, o analógico é ignorado em AMBOS os eixos.
            // Isso evita o bug "MoveLeft + MoveDown" causado por deriva do stick enquanto o D-Pad é usado.
            bool anyDpadPressed = dpadLeft || dpadRight || dpadUp || dpadDown;

            int slot = _activeSlot;
            var now = DateTime.UtcNow;

            // LEFT
            bool leftRaw = anyDpadPressed
                ? dpadLeft
                : (_leftState[slot].IsActive ? lx < -StickReleaseDeadzone : lx < -StickPressDeadzone);
            if (ProcessDirection(ref _leftState[slot], leftRaw, now, slot, "Left"))
                actions.Add(GamepadAction.MoveLeft);

            // RIGHT
            bool rightRaw = anyDpadPressed
                ? dpadRight
                : (_rightState[slot].IsActive ? lx > StickReleaseDeadzone : lx > StickPressDeadzone);
            if (ProcessDirection(ref _rightState[slot], rightRaw, now, slot, "Right"))
                actions.Add(GamepadAction.MoveRight);

            // UP
            bool upRaw = anyDpadPressed
                ? dpadUp
                : (_upState[slot].IsActive ? ly > StickReleaseDeadzone : ly > StickPressDeadzone);
            if (ProcessDirection(ref _upState[slot], upRaw, now, slot, "Up"))
                actions.Add(GamepadAction.MoveUp);

            // DOWN
            bool downRaw = anyDpadPressed
                ? dpadDown
                : (_downState[slot].IsActive ? ly < -StickReleaseDeadzone : ly < -StickPressDeadzone);
            if (ProcessDirection(ref _downState[slot], downRaw, now, slot, "Down"))
                actions.Add(GamepadAction.MoveDown);

            if (actions.Count > 0)
                DebugLog.Write($"[GamepadPoller] Ações slot {slot}: {string.Join(", ", actions)}");

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

    /// <summary>
    /// Aplica debounce + cooldown + repeat em uma direção.
    /// Retorna true quando o consumidor deve emitir a ação de movimento.
    /// </summary>
    private bool ProcessDirection(ref DirectionState state, bool rawPressed, DateTime now, int slot, string name)
    {
        if (!rawPressed)
        {
            state.FirstObservedPress = DateTime.MinValue;

            if (state.IsActive)
            {
                state.IsActive = false;
                state.HeldSince = DateTime.MinValue;
                state.LastRepeat = DateTime.MinValue;
                state.ReleasedAt = now;
                DebugLog.Write($"[GamepadPoller] RELEASE {name} slot={slot}");
            }
            return false;
        }

        // rawPressed = true
        if (state.IsActive)
        {
            // Já confirmado como "held" — só checa timing de repeat
            if ((now - state.HeldSince).TotalMilliseconds < RepeatInitialDelayMs)
                return false;

            if ((now - state.LastRepeat).TotalMilliseconds < RepeatIntervalMs)
                return false;

            state.LastRepeat = now;
            DebugLog.Write($"[GamepadPoller] REPEAT  {name} slot={slot}");
            return true;
        }

        // Ainda não é "held" — debounce
        if (state.FirstObservedPress == DateTime.MinValue)
            state.FirstObservedPress = now;

        if ((now - state.FirstObservedPress).TotalMilliseconds < PressDebounceMs)
            return false;

        // Cooldown pós-release (anti-bounce do D-Pad / glitch do stick)
        if (state.ReleasedAt != DateTime.MinValue &&
            (now - state.ReleasedAt).TotalMilliseconds < ReleaseCooldownMs)
        {
            return false;
        }

        // Press confirmado
        state.IsActive = true;
        state.HeldSince = now;
        state.LastRepeat = now;
        state.FirstObservedPress = DateTime.MinValue;
        state.ReleasedAt = DateTime.MinValue;
        DebugLog.Write($"[GamepadPoller] PRESS   {name} slot={slot}");
        return true;
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

            bool isButtonPressed = (cur & ~prev) != 0;
            bool isStickMoving =
                Math.Abs((int)s.Gamepad.sThumbLX) > StickPressDeadzone ||
                Math.Abs((int)s.Gamepad.sThumbLY) > StickPressDeadzone ||
                Math.Abs((int)s.Gamepad.sThumbRX) > StickPressDeadzone ||
                Math.Abs((int)s.Gamepad.sThumbRY) > StickPressDeadzone;

            if (isButtonPressed || isStickMoving)
            {
                if ((int)slot != _activeSlot)
                {
                    DebugLog.Write($"[GamepadPoller] Slot → {slot} (input detectado)");
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
            _leftState[i]  = default;
            _rightState[i] = default;
            _upState[i]    = default;
            _downState[i]  = default;
        }
    }

    private static bool WasJustPressed(XInput.Buttons current, XInput.Buttons previous, XInput.Buttons button)
        => current.HasFlag(button) && !previous.HasFlag(button);
}