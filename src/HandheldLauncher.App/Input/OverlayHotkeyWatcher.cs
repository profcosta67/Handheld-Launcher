using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Input;

/// <summary>
/// L3+R3 (os dois analógicos juntos) troca o preset do HUD — numa THREAD
/// PRÓPRIA, não no timer da interface. Lê os 4 slots do XInput a cada ~40 ms
/// (o controle pode não estar no slot 0 depois de reconectar).
/// </summary>
internal sealed class OverlayHotkeyWatcher : IDisposable
{
    private const ushort LeftThumb = 0x0040;
    private const ushort RightThumb = 0x0080;

    private readonly Action _onCombo;
    private readonly Thread _thread;
    private volatile bool _stop;
    private readonly bool[] _wasDown = new bool[4];
    private readonly bool[] _wasSeen = new bool[4];

    // v0.16.2 (diagnóstico): no modo observação o watcher também registra
    // mudanças de botões (com limite), pra saber se o XInput enxerga o
    // controle com o jogo em foco. Ver MainWindow.SetGamepadComboDetectionForGame.
    private readonly bool _observeOnly;
    private readonly string _tag;
    private readonly ushort[] _lastButtons = new ushort[4];
    private int _buttonChangesLogged;

    public OverlayHotkeyWatcher(Action onCombo, bool observeOnly = false)
    {
        _onCombo = onCombo;
        _observeOnly = observeOnly;
        _tag = observeOnly ? "[OverlayHotkeyWatcher/observação]" : "[OverlayHotkeyWatcher]";
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = observeOnly ? "OverlayHotkeyWatcherObserver" : "OverlayHotkeyWatcher",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    private void Run()
    {
        while (!_stop)
        {
            try
            {
                for (uint slot = 0; slot < 4; slot++)
                {
                    if (!XInput.TryGetState(slot, out var state))
                    {
                        if (_wasSeen[slot])
                        {
                            _wasSeen[slot] = false;
                            DebugLog.Write($"{_tag} Controle desconectado do slot {slot}");
                        }
                        _wasDown[slot] = false;
                        continue;
                    }

                    // 🟢 Log de descoberta: só uma vez por conexão, sem floodar.
                    if (!_wasSeen[slot])
                    {
                        _wasSeen[slot] = true;
                        DebugLog.Write($"{_tag} Controle detectado no slot {slot}");
                    }

                    var buttons = state.Gamepad.wButtons;

                    if (_observeOnly && buttons != _lastButtons[slot])
                    {
                        if (_buttonChangesLogged < 60)
                        {
                            DebugLog.Write($"{_tag} slot {slot} botões=0x{buttons:X4}");
                            _buttonChangesLogged++;
                        }
                        _lastButtons[slot] = buttons;
                    }
                    var isDown = (buttons & LeftThumb) != 0 && (buttons & RightThumb) != 0;

                    if (isDown && !_wasDown[slot])
                    {
                        DebugLog.Write($"{_tag} L3+R3 no controle {slot}");
                        _onCombo();
                    }

                    _wasDown[slot] = isDown;
                }
            }
            catch
            {
                // Nunca derrubar a thread por um erro de leitura.
            }

            Thread.Sleep(40);
        }
    }

    public void Dispose() => _stop = true;
}