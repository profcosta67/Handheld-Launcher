using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Input;

/// <summary>
/// L3+R3 (os dois analógicos juntos) troca o preset do HUD — numa THREAD
/// PRÓPRIA, não no timer da interface.
///
/// Bug reportado na v0.6.0: "o botão pra mudar entre as opções de HUD
/// parou de funcionar, funcionou de primeira mas agora não vai mais". O
/// combo era lido pelo mesmo timer de 50 ms da janela (DispatcherTimer). Com
/// o jogo na frente e o launcher em segundo plano/escondido na bandeja, o
/// Windows 11 pode desacelerar os timers de interface de um app em segundo
/// plano (modo de eficiência / "EcoQoS") — um toque rápido de L3+R3 cai
/// entre duas leituras e se perde. Aqui: thread dedicada lendo os 4 slots do
/// XInput a cada ~40 ms (o controle pode não estar no slot 0 depois de
/// reconectar), e o launcher pede ao Windows pra não ser desacelerado (ver
/// Platform/PowerThrottling).
/// </summary>
internal sealed class OverlayHotkeyWatcher : IDisposable
{
    private const ushort LeftThumb = 0x0040;
    private const ushort RightThumb = 0x0080;

    private readonly Action _onCombo;
    private readonly Thread _thread;
    private volatile bool _stop;
    private readonly bool[] _wasDown = new bool[4];

    public OverlayHotkeyWatcher(Action onCombo)
    {
        _onCombo = onCombo;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "OverlayHotkeyWatcher",
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
                        _wasDown[slot] = false;
                        continue;
                    }

                    var buttons = state.Gamepad.wButtons;
                    var isDown = (buttons & LeftThumb) != 0 && (buttons & RightThumb) != 0;

                    // Dispara uma vez por aperto (borda de subida), não a
                    // cada leitura enquanto os dois continuam afundados.
                    if (isDown && !_wasDown[slot])
                    {
                        DebugLog.Write($"OverlayHotkeyWatcher: L3+R3 no controle {slot}");
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
