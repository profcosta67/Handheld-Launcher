namespace HandheldLauncher.Core.Services.Keyboard;

/// <summary>Tipo de tecla — decide o que o teclado faz ao ser premida.</summary>
public enum KeyboardKeyKind
{
    /// <summary>Escreve <see cref="KeyboardKey.Text"/> no campo.</summary>
    Text,
    Backspace,
    Space,
    Enter,
    Shift,
    Page,
    Close,
    Clear,
}

/// <summary>Direção do D-Pad dentro do teclado.</summary>
public enum KeyboardDirection
{
    Left,
    Right,
    Up,
    Down,
}

/// <summary>
/// Uma tecla do teclado na tela. <paramref name="Units"/> é a largura em
/// "quadrados" (1 = tecla normal), para o XAML montar a grelha sem medidas
/// mágicas espalhadas pelo código.
/// </summary>
public sealed record KeyboardKey(string Id, string Label, KeyboardKeyKind Kind, double Units = 1, string? Text = null);

/// <summary>
/// Layout e navegação do teclado do launcher (v0.24.2), em código puro para
/// poder ser testado sem Avalonia.
///
/// <para>Existe porque o TabTip do Windows é uma janela FORA da árvore do
/// Avalonia: o leitor de XInput do launcher não a alcança, então o D-Pad/A/B
/// não funcionam nos campos de texto (senha do Wi‑Fi, busca da Nuvem, campos
/// das Configurações). Um teclado nosso, dentro da janela, resolve isso sem
/// depender do Windows.</para>
/// </summary>
public static class OnScreenKeyboardLayout
{
    private const string Row1 = "qwertyuiop";
    private const string Row2 = "asdfghjkl";
    private const string Row3 = "zxcvbnm";

    /// <summary>Teclas da página de letras. <paramref name="shifted"/> = maiúsculas.</summary>
    public static IReadOnlyList<IReadOnlyList<KeyboardKey>> Letters(bool shifted)
    {
        var row1 = Row1.Select(c => CharKey(c, shifted)).ToList();
        var row2 = Row2.Select(c => CharKey(c, shifted)).ToList();
        var row3 = Row3.Select(c => CharKey(c, shifted)).ToList();

        row1.Add(new KeyboardKey("backspace", "⌫", KeyboardKeyKind.Backspace, 1.5));
        row2.Add(new KeyboardKey("enter", "⏎", KeyboardKeyKind.Enter, 1.5));
        row3.Add(new KeyboardKey("shift", shifted ? "⇧ fixo" : "⇧", KeyboardKeyKind.Shift, 1.5));

        return
        [
            row1,
            row2,
            row3,
            [
                new KeyboardKey("page", "123", KeyboardKeyKind.Page, 1.5),
                new KeyboardKey("space", "espaço", KeyboardKeyKind.Space, 4),
                new KeyboardKey("clear", "limpar", KeyboardKeyKind.Clear, 1.5),
                new KeyboardKey("close", "fechar", KeyboardKeyKind.Close, 1.5),
            ],
        ];
    }

    /// <summary>Página de números e símbolos (o que não existe na página de letras).</summary>
    public static IReadOnlyList<IReadOnlyList<KeyboardKey>> Symbols()
    {
        return
        [
            [
                CharKey('1'), CharKey('2'), CharKey('3'), CharKey('4'), CharKey('5'),
                CharKey('6'), CharKey('7'), CharKey('8'), CharKey('9'), CharKey('0'),
            ],
            [
                CharKey('!'), CharKey('@'), CharKey('#'), CharKey('$'), CharKey('%'),
                CharKey('&'), CharKey('*'), CharKey('('), CharKey(')'), CharKey('-'),
                new KeyboardKey("backspace", "⌫", KeyboardKeyKind.Backspace, 1.5),
            ],
            [
                CharKey('_'), CharKey('='), CharKey('+'), CharKey('['), CharKey(']'),
                CharKey('{'), CharKey('}'), CharKey(';'), CharKey(':'), CharKey('\''),
                new KeyboardKey("enter", "⏎", KeyboardKeyKind.Enter, 1.5),
            ],
            [
                CharKey('"'), CharKey(','), CharKey('.'), CharKey('?'), CharKey('/'),
                CharKey('\\'), CharKey('|'), CharKey('~'), CharKey('^'), CharKey('`'),
            ],
            [
                new KeyboardKey("page", "abc", KeyboardKeyKind.Page, 1.5),
                new KeyboardKey("space", "espaço", KeyboardKeyKind.Space, 4),
                new KeyboardKey("clear", "limpar", KeyboardKeyKind.Clear, 1.5),
                new KeyboardKey("close", "fechar", KeyboardKeyKind.Close, 1.5),
            ],
        ];
    }

    private static KeyboardKey CharKey(char value, bool shifted = false)
    {
        var text = shifted ? char.ToUpperInvariant(value).ToString() : value.ToString();
        return new KeyboardKey($"char-{text}", text, KeyboardKeyKind.Text, 1, text);
    }

    /// <summary>
    /// Posição (linha, coluna) depois de uma direção do D-Pad. As linhas têm
    /// larguras diferentes (⇧, espaço, limpar, fechar), então ←/→ andam dentro
    /// da linha (sem sair) e ↑/↓ escolhem a tecla da linha vizinha com o centro
    /// mais próximo — é o que o utilizador espera, em vez de "salta para a
    /// coluna 0".
    /// </summary>
    public static (int Row, int Col) Move(
        IReadOnlyList<IReadOnlyList<KeyboardKey>> rows,
        int row,
        int col,
        KeyboardDirection direction)
    {
        if (rows.Count == 0) return (0, 0);

        row = Math.Clamp(row, 0, rows.Count - 1);
        var current = rows[row];
        if (current.Count == 0) return (row, 0);

        col = Math.Clamp(col, 0, current.Count - 1);

        switch (direction)
        {
            case KeyboardDirection.Left:
                return (row, Math.Max(0, col - 1));

            case KeyboardDirection.Right:
                return (row, Math.Min(current.Count - 1, col + 1));

            case KeyboardDirection.Up:
                return row == 0 ? (row, col) : (row - 1, NearestColumn(rows, row, col, -1));

            case KeyboardDirection.Down:
                return row == rows.Count - 1 ? (row, col) : (row + 1, NearestColumn(rows, row, col, +1));

            default:
                return (row, col);
        }
    }

    /// <summary>
    /// Índice da tecla da linha vizinha cujo centro está mais perto do centro
    /// da tecla atual. Usa as larguras reais (<see cref="KeyboardKey.Units"/>),
    /// por isso ↑ do "espaço" (largo) cai no meio da linha de cima.
    /// </summary>
    private static int NearestColumn(
        IReadOnlyList<IReadOnlyList<KeyboardKey>> rows,
        int row,
        int col,
        int rowStep)
    {
        var target = rows[row + rowStep];
        if (target.Count == 0) return 0;

        var currentCenter = Center(rows[row], col);
        var best = 0;
        var bestDistance = double.MaxValue;

        for (var i = 0; i < target.Count; i++)
        {
            var distance = Math.Abs(Center(target, i) - currentCenter);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private static double Center(IReadOnlyList<KeyboardKey> row, int col)
    {
        var x = 0.0;
        for (var i = 0; i < row.Count; i++)
        {
            var width = row[i].Units;
            if (i == col) return x + width / 2;
            x += width;
        }

        return x;
    }

}
