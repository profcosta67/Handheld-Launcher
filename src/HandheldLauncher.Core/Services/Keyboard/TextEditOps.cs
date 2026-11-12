namespace HandheldLauncher.Core.Services.Keyboard;

/// <summary>
/// Edição do texto do campo focado, sem depender do Avalonia: o
/// <c>OnScreenKeyboard</c> só aplica o resultado em <c>TextBox.Text</c> +
/// <c>CaretIndex</c>. Assim a aritmética do cursor (inserir no meio,
/// substituir a seleção, apagar no início, apagar o último caractere) fica
/// coberta por testes em vez de ser testada de olho no aparelho.
/// </summary>
public static class TextEditOps
{
    /// <summary>
    /// Insere <paramref name="insert"/> na posição do cursor. Se houver seleção
    /// (típico depois de "limpar" ou de um Tab), a seleção é substituída — o
    /// mesmo que o Windows faz.
    /// </summary>
    public static (string Text, int Caret) Insert(string? text, int selectionStart, int selectionLength, string insert)
    {
        var current = text ?? string.Empty;
        var (start, length) = Normalize(current, selectionStart, selectionLength);
        var result = current.Remove(start, length).Insert(start, insert);
        return (result, start + insert.Length);
    }

    /// <summary>
    /// Apaga o caractere antes do cursor (ou a seleção, se houver). Devolve o
    /// texto e a posição do cursor — que NÃO pode ficar negativa quando o
    /// cursor já está no início.
    /// </summary>
    public static (string Text, int Caret) Backspace(string? text, int selectionStart, int selectionLength)
    {
        var current = text ?? string.Empty;
        var (start, length) = Normalize(current, selectionStart, selectionLength);

        if (length > 0)
            return (current.Remove(start, length), start);

        if (start == 0) return (current, 0);

        return (current.Remove(start - 1, 1), start - 1);
    }

    /// <summary>
    /// Move o cursor <paramref name="delta"/> posições (−1 = esquerda, +1 = direita).
    /// Com seleção, a primeira seta só a desfaz: ← vai ao início dela, → ao fim —
    /// como num campo de texto normal. Nunca sai de [0, tamanho do texto].
    /// </summary>
    public static int MoveCaret(string? text, int selectionStart, int selectionLength, int delta)
    {
        var current = text ?? string.Empty;
        var (start, length) = Normalize(current, selectionStart, selectionLength);

        if (length > 0) return delta < 0 ? start : start + length;

        return Math.Clamp(start + delta, 0, current.Length);
    }

    private static (int Start, int Length) Normalize(string text, int selectionStart, int selectionLength)
    {
        var start = Math.Clamp(selectionStart, 0, text.Length);
        var length = Math.Clamp(selectionLength, 0, text.Length - start);
        return (start, length);
    }
}
