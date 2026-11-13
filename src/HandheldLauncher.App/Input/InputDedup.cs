using System;
using Avalonia.Input;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Input;

/// <summary>
/// v0.25.10 ("ghost" nos toques): o mesmo aperto do controle pode chegar duas
/// vezes — pelo XInput (GamepadPoller) e como tecla (setas/Enter/Esc), quando
/// algo converte o controle em teclado (ex.: a configuração de área de trabalho
/// do Steam Input). Os dois caminhos movem a seleção e o item pula dois.
///
/// Regra: um movimento do controle e uma tecla do MESMO tipo com menos de
/// <see cref="WindowMs"/> de diferença contam uma vez só — vale o que chegou
/// primeiro. Teclado usado sozinho (PC) e controle sozinho não mudam nada.
/// </summary>
public static class InputDedup
{
    private const int WindowMs = 150;
    private const int MaxLogs = 5;

    private enum Kind { None, Up, Down, Left, Right, Confirm, Back }

    private static readonly DateTime[] LastPad = new DateTime[7];
    private static readonly DateTime[] LastKey = new DateTime[7];
    private static int _logged;

    /// <summary>Devolve true se este movimento do controle já veio como tecla agora há pouco.</summary>
    public static bool DropGamepad(GamepadAction action)
    {
        var kind = FromAction(action);
        if (kind == Kind.None) return false;
        var now = DateTime.UtcNow;
        if ((now - LastKey[(int)kind]).TotalMilliseconds < WindowMs)
        {
            LastKey[(int)kind] = DateTime.MinValue; // um par só
            Log($"controle {action} ignorado: a mesma ação já chegou como tecla");
            return true;
        }
        LastPad[(int)kind] = now;
        return false;
    }

    /// <summary>
    /// Fase de entrada (Tunnel): devolve true se esta tecla repete um movimento do
    /// controle de agora há pouco. Não registra a tecla — isso fica para
    /// <see cref="NoteKeyHandled"/>, depois de alguém de fato tratá-la.
    /// </summary>
    public static bool DropKey(Key key)
    {
        var kind = FromKey(key);
        if (kind == Kind.None) return false;
        if ((DateTime.UtcNow - LastPad[(int)kind]).TotalMilliseconds < WindowMs)
        {
            LastPad[(int)kind] = DateTime.MinValue;
            Log($"tecla {key} ignorada: a mesma ação já chegou pelo controle");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Fase de saída (Bubble, inclusive já tratados): só uma tecla que FEZ algo
    /// (Handled) pode anular o aperto do controle que vem logo depois. Tecla sem
    /// dono (ex.: o QS não trata teclado) não apaga o controle (revisão 12/11).
    /// </summary>
    public static void NoteKeyHandled(Key key, bool handled, bool droppedByUs)
    {
        if (!handled || droppedByUs) return;
        var kind = FromKey(key);
        if (kind == Kind.None) return;
        LastKey[(int)kind] = DateTime.UtcNow;
    }

    private static void Log(string what)
    {
        if (_logged >= MaxLogs) return;
        _logged++;
        DebugLog.Write($"[INPUT] Duplicata: {what} (controle também virando teclado — Steam Input em modo área de trabalho?). [{_logged}/{MaxLogs}]");
    }

    private static Kind FromAction(GamepadAction a) => a switch
    {
        GamepadAction.MoveUp => Kind.Up,
        GamepadAction.MoveDown => Kind.Down,
        GamepadAction.MoveLeft => Kind.Left,
        GamepadAction.MoveRight => Kind.Right,
        GamepadAction.Confirm => Kind.Confirm,
        GamepadAction.Back => Kind.Back,
        _ => Kind.None,
    };

    private static Kind FromKey(Key k) => k switch
    {
        Key.Up => Kind.Up,
        Key.Down => Kind.Down,
        Key.Left => Kind.Left,
        Key.Right => Kind.Right,
        Key.Enter => Kind.Confirm,
        Key.Escape => Kind.Back,
        _ => Kind.None,
    };
}
