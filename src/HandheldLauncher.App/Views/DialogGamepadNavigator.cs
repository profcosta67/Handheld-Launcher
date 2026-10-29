using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using HandheldLauncher.App.Input;
using HandheldLauncher.App.Platform;

namespace HandheldLauncher.App.Views;

/// <summary>
/// Controle dentro das janelas de diálogo (Configurações, Adicionar jogo) —
/// bug reportado: "o controle não funciona nas configurações para acessar o
/// botão salvar/fechar". O MainWindow continua lendo o controle (o diálogo
/// não tem leitor próprio) e, com um diálogo aberto, manda as ações pra cá
/// em vez de mexer na biblioteca por trás:
/// <list type="bullet">
/// <item>↑/↓: próximo/anterior item focável (com destaque de foco), rolando
/// a tela até ele. ←/→ fazem a MESMA coisa (v0.9.7 — bug reportado: "quando
/// chego lá na aba de salvar, se passo pro lado não vai, só obedece indo
/// pra baixo"; antes ←/→ não tinham NENHUM efeito fora de um ComboBox).</item>
/// <item>Numa caixa de seleção (Tema, Tamanho...) fechada: ←/→ ainda giram
/// a opção sem abrir nada (atalho rápido que já existia). A agora ABRE a
/// lista de verdade (v0.9.7 — bug reportado: "quando clico o A pra
/// selecionar um tema, ele vai mudando, ideal seria abrir a lista"; antes A
/// girava a opção igual ←/→, nunca mostrava a lista).</item>
/// <item>Com a lista de uma caixa de seleção ABERTA: ↑/↓/←/→ trocam o item
/// destacado dentro dela (sem sair da caixa); A confirma e fecha a lista; B
/// só fecha a lista (não fecha o diálogo inteiro).</item>
/// <item>A (fora de uma lista aberta): aperta botão, marca caixinha/opção,
/// ou entra na caixa de texto abrindo o teclado virtual.</item>
/// <item>B (fora de uma lista aberta): fecha o diálogo.</item>
/// </list>
/// </summary>
internal static class DialogGamepadNavigator
{
    public static void Handle(Window dialog, GamepadAction action)
    {
        var focused = dialog.FocusManager?.GetFocusedElement() as Control;

        // Lista de uma caixa de seleção aberta (ver Activate abaixo): o
        // Avalonia só navega essa lista sozinho quando a tecla chega como
        // evento de teclado de verdade — aqui são ações sintéticas do
        // controle (GamepadPoller), então ↑/↓ precisam mexer no
        // SelectedIndex na mão, e A/B precisam fechar a lista em vez de
        // ativar outro controle/fechar o diálogo.
        if (focused is ComboBox { IsDropDownOpen: true } openCombo)
        {
            switch (action)
            {
                case GamepadAction.MoveUp:
                    CycleCombo(openCombo, -1);
                    return;
                case GamepadAction.MoveDown:
                    CycleCombo(openCombo, 1);
                    return;
                case GamepadAction.Confirm:
                case GamepadAction.Back:
                    openCombo.IsDropDownOpen = false;
                    return;
            }
        }

        switch (action)
        {
            case GamepadAction.MoveDown:
                MoveFocus(dialog, focused, NavigationDirection.Next);
                break;

            case GamepadAction.MoveUp:
                MoveFocus(dialog, focused, NavigationDirection.Previous);
                break;

            case GamepadAction.MoveRight:
                if (focused is ComboBox comboRight && comboRight.ItemCount > 0)
                {
                    CycleCombo(comboRight, 1);
                }
                else
                {
                    MoveFocus(dialog, focused, NavigationDirection.Next);
                }
                break;

            case GamepadAction.MoveLeft:
                if (focused is ComboBox comboLeft && comboLeft.ItemCount > 0)
                {
                    CycleCombo(comboLeft, -1);
                }
                else
                {
                    MoveFocus(dialog, focused, NavigationDirection.Previous);
                }
                break;

            case GamepadAction.Confirm:
                Activate(focused);
                break;

            case GamepadAction.Back:
                dialog.Close();
                break;
        }
    }

    private static void CycleCombo(ComboBox combo, int delta)
    {
        if (combo.ItemCount == 0) return;
        combo.SelectedIndex = Math.Clamp(combo.SelectedIndex + delta, 0, combo.ItemCount - 1);
    }

    private static void MoveFocus(Window dialog, Control? focused, NavigationDirection direction)
    {
        IInputElement? next = focused is null
            ? FirstFocusable(dialog)
            : KeyboardNavigationHandler.GetNext(focused, direction);

        if (next is Control control)
        {
            control.Focus(NavigationMethod.Directional);
            control.BringIntoView();
        }
    }

    private static Control? FirstFocusable(Window dialog)
    {
        foreach (var descendant in dialog.GetLogicalDescendants())
        {
            if (descendant is Control { Focusable: true, IsEffectivelyVisible: true, IsEffectivelyEnabled: true } control)
            {
                return control;
            }
        }
        return null;
    }

    private static void Activate(Control? focused)
    {
        switch (focused)
        {
            case RadioButton radio:
                radio.IsChecked = true;
                break;
            case ToggleButton toggle: // CheckBox herda de ToggleButton
                toggle.IsChecked = toggle.IsChecked != true;
                break;
            case Button button:
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                break;
            case ComboBox combo when combo.ItemCount > 0:
                // v0.9.7, pedido do usuário ("ideal seria abrir a lista"):
                // A abre o dropdown de verdade em vez de girar pra frente
                // (girar sem abrir continua disponível em ←/→, ver Handle).
                combo.IsDropDownOpen = true;
                break;
            case TextBox:
                TouchKeyboard.Show();
                break;
        }
    }
}
