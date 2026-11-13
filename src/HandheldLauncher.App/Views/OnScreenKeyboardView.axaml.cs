using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Input;
using HandheldLauncher.Core.Services.Keyboard;

namespace HandheldLauncher.App.Views;

/// <summary>
/// Teclado na tela do launcher (v0.24.2).
///
/// <para>Motivo: o TabTip do Windows é uma janela FORA da árvore do Avalonia,
/// então o leitor de XInput do launcher não lhe chega e o D-Pad/A/B não fazem
/// nada nos campos de texto — a senha do Wi‑Fi só era utilizável com teclado
/// físico. Este teclado é um controlo normal: o gamepad chega-lhe pelo
/// <see cref="HandleGamepadAction"/>, chamado pelo MainWindow antes do
/// navegador genérico dos diálogos.</para>
///
/// <para>O layout e a aritmética do cursor vivem no Core
/// (<see cref="OnScreenKeyboardLayout"/>, <see cref="TextEditOps"/>) e estão
/// cobertos por testes.</para>
/// </summary>
public partial class OnScreenKeyboardView : UserControl
{
    /// <summary>Largura, em píxeis, de uma tecla de 1 unidade.</summary>
    private const double KeyUnit = 42;

    private readonly List<(int Row, int Col, Button Button)> _buttons = new();

    private TextBox? _target;
    private bool _symbols;
    private bool _shift;
    private int _row = 1;
    private int _col;
    private IReadOnlyList<IReadOnlyList<KeyboardKey>> _rows = OnScreenKeyboardLayout.Letters(shifted: false);

    /// <summary>O utilizador premiu ⏎ no teclado (a janela decide o que fazer).</summary>
    public event EventHandler? SubmitRequested;

    /// <summary>O utilizador premiu "fechar" (ou B) — a janela esconde/limpa.</summary>
    public event EventHandler? CloseRequested;

    public OnScreenKeyboardView()
    {
        InitializeComponent();
    }

    public bool IsOpen { get; private set; }

    public TextBox? Target => _target;

    /// <summary>Abre o teclado a escrever em <paramref name="target"/>.</summary>
    public void Show(TextBox target)
    {
        _target = target;
        _symbols = false;
        _shift = false;
        // Começa no "a" (linha do meio) — a primeira tecla que um polegar procura.
        _row = 1;
        _col = 0;
        Rebuild();
        IsOpen = true;
        IsVisible = true;
        DebugLog.Write($"[TECLADO] Aberto para '{target.Name}'.");
    }

    public void Hide()
    {
        if (!IsOpen) return;
        IsOpen = false;
        IsVisible = false;
        _target = null;
        DebugLog.Write("[TECLADO] Fechado.");
    }

    /// <summary>
    /// Trata uma ação do controle. Devolve true quando o teclado está aberto —
    /// nesse caso a ação NÃO pode seguir para o navegador genérico (senão o B
    /// fechava o diálogo inteiro em vez de fechar o teclado).
    /// </summary>
    public bool HandleGamepadAction(GamepadAction action)
    {
        if (!IsOpen) return false;

        switch (action)
        {
            case GamepadAction.MoveLeft:
                Move(KeyboardDirection.Left);
                break;
            case GamepadAction.MoveRight:
                Move(KeyboardDirection.Right);
                break;
            case GamepadAction.MoveUp:
                Move(KeyboardDirection.Up);
                break;
            case GamepadAction.MoveDown:
                Move(KeyboardDirection.Down);
                break;
            case GamepadAction.Confirm:
                ActivateFocusedKey();
                break;
            case GamepadAction.Back:
                RaiseClose();
                break;
            case GamepadAction.PreviousFilter: // LB: cursor para a esquerda
                MoveCaret(-1);
                break;
            case GamepadAction.NextFilter: // RB: cursor para a direita
                MoveCaret(+1);
                break;
            case GamepadAction.GameDetails: // X: apaga (padrão de console)
                Apply(TextEditOps.Backspace(TargetText(), SelectionStart(), SelectionLength()));
                break;
            case GamepadAction.Connectivity: // Y: espaço
                Apply(TextEditOps.Insert(TargetText(), SelectionStart(), SelectionLength(), " "));
                break;
            default:
                // Qualquer outra ação com o teclado aberto é consumida de propósito.
                break;
        }

        return true;
    }

    // ---------------------------------------------------------------- grelha

    private void Rebuild()
    {
        _rows = _symbols ? OnScreenKeyboardLayout.Symbols() : OnScreenKeyboardLayout.Letters(_shift);
        RowsHost.Children.Clear();
        _buttons.Clear();

        for (var row = 0; row < _rows.Count; row++)
        {
            var line = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };

            for (var col = 0; col < _rows[row].Count; col++)
            {
                var key = _rows[row][col];
                var button = new Button
                {
                    Content = key.Label,
                    Width = KeyUnit * key.Units,
                    // Focusable=false: o foco fica no TextBox (o cursor continua
                    // visível) e os botões não roubam o D-Pad à navegação de foco
                    // do próprio Avalonia.
                    Focusable = false,
                };

                button.Classes.Add("kbKey");

                var captured = key;
                button.Click += (_, _) => Activate(captured);
                line.Children.Add(button);
                _buttons.Add((row, col, button));
            }

            RowsHost.Children.Add(line);
        }

        _row = Math.Clamp(_row, 0, _rows.Count - 1);
        _col = Math.Clamp(_col, 0, _rows[_row].Count - 1);
        Highlight();
    }

    private void Move(KeyboardDirection direction)
    {
        (_row, _col) = OnScreenKeyboardLayout.Move(_rows, _row, _col, direction);
        Highlight();
    }

    private void Highlight()
    {
        foreach (var (row, col, button) in _buttons)
        {
            if (row == _row && col == _col) button.Classes.Add("kbSelected");
            else button.Classes.Remove("kbSelected");
        }
    }

    private void ActivateFocusedKey()
    {
        if (_row < 0 || _row >= _rows.Count) return;
        if (_col < 0 || _col >= _rows[_row].Count) return;
        Activate(_rows[_row][_col]);
    }

    private void Activate(KeyboardKey key)
    {
        switch (key.Kind)
        {
            case KeyboardKeyKind.Text:
                Apply(TextEditOps.Insert(TargetText(), SelectionStart(), SelectionLength(), key.Text ?? string.Empty));
                if (_shift)
                {
                    // Como num teclado a sério: o ⇧ vale só para a próxima letra.
                    _shift = false;
                    Rebuild();
                }
                break;

            case KeyboardKeyKind.Space:
                Apply(TextEditOps.Insert(TargetText(), SelectionStart(), SelectionLength(), " "));
                break;

            case KeyboardKeyKind.Backspace:
                Apply(TextEditOps.Backspace(TargetText(), SelectionStart(), SelectionLength()));
                break;

            case KeyboardKeyKind.Clear:
                if (_target is not null)
                {
                    _target.Text = string.Empty;
                    _target.CaretIndex = 0;
                }
                break;

            case KeyboardKeyKind.CaretLeft:
                MoveCaret(-1);
                break;

            case KeyboardKeyKind.CaretRight:
                MoveCaret(+1);
                break;

            case KeyboardKeyKind.Enter:
                SubmitRequested?.Invoke(this, EventArgs.Empty);
                break;

            case KeyboardKeyKind.Shift:
                _shift = !_shift;
                Rebuild();
                break;

            case KeyboardKeyKind.Page:
                _symbols = !_symbols;
                Rebuild();
                break;

            case KeyboardKeyKind.Close:
                RaiseClose();
                break;
        }
    }

    private void MoveCaret(int delta)
    {
        if (_target is null) return;
        var caret = TextEditOps.MoveCaret(TargetText(), SelectionStart(), SelectionLength(), delta);
        _target.SelectionStart = caret;
        _target.SelectionEnd = caret;
        _target.CaretIndex = caret;
    }

    private void RaiseClose()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    // ---------------------------------------------------------------- texto

    private string TargetText() => _target?.Text ?? string.Empty;

    private int SelectionLength()
        => _target is null ? 0 : Math.Abs(_target.SelectionEnd - _target.SelectionStart);

    private int SelectionStart()
    {
        if (_target is null) return 0;
        var length = SelectionLength();
        return length == 0
            ? _target.CaretIndex
            : Math.Min(_target.SelectionStart, _target.SelectionEnd);
    }

    private void Apply((string Text, int Caret) edit)
    {
        if (_target is null) return;
        _target.Text = edit.Text;
        _target.CaretIndex = Math.Clamp(edit.Caret, 0, edit.Text.Length);
    }

}
