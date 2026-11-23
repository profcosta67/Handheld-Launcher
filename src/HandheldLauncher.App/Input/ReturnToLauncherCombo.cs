using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Input;

/// <summary>
/// 0.27 beta.71 (pedido do usuário): segurar View + Menu por 1 s traz o launcher de volta para a
/// frente — para quando o jogo fecha e o launcher não volta sozinho. Lê o XInput numa thread
/// própria (funciona com o launcher atrás). Quem decide se age (jogo na frente, QS aberto) é o
/// callback; aqui só se detecta o gesto, uma vez por aperto.
/// </summary>
internal sealed class ReturnToLauncherCombo : IDisposable
{
    private const ushort Menu = 0x0010;   // XINPUT_GAMEPAD_START
    private const ushort View = 0x0020;   // XINPUT_GAMEPAD_BACK
    public static readonly TimeSpan HoldTime = TimeSpan.FromSeconds(1);

    private readonly Action _onCombo;
    private readonly Thread _thread;
    private volatile bool _stop;
    private readonly DateTime?[] _heldSince = new DateTime?[4];
    private readonly bool[] _fired = new bool[4];

    public ReturnToLauncherCombo(Action onCombo)
    {
        _onCombo = onCombo;
        _thread = new Thread(Run) { IsBackground = true, Name = "ReturnToLauncherCombo" };
        _thread.Start();
    }

    private void Run()
    {
        while (!_stop)
        {
            try
            {
                using var __meter = HandheldLauncher.Core.Services.Power.ActivityMeter.Measure("atalho View+Menu");
                var now = DateTime.UtcNow;
                for (uint slot = 0; slot < 4; slot++)
                {
                    var both = XInput.TryGetState(slot, out var state)
                        && (state.Gamepad.wButtons & (Menu | View)) == (Menu | View);
                    if (!both)
                    {
                        _heldSince[slot] = null;
                        _fired[slot] = false;
                        continue;
                    }

                    _heldSince[slot] ??= now;
                    if (_fired[slot] || now - _heldSince[slot]!.Value < HoldTime) continue;
                    _fired[slot] = true; // só uma vez até soltar
                    DebugLog.Write($"[VOLTAR] View + Menu segurados por 1 s no controle {slot}.");
                    _onCombo();
                }
            }
            catch
            {
                // Nunca derrubar a thread por um erro de leitura.
            }

            Thread.Sleep(100); // v0.28: 50 → 100 ms; o gesto exige 1 s segurado
        }
    }

    public void Dispose() => _stop = true;
}
