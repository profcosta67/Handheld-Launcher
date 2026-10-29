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
/// a tela até ele.</item>
/// <item>←/→: troca a opção de uma caixa de seleção (Tema, Tamanho...).</item>
/// <item>A: aperta botão, marca caixinha/opção, avança a caixa de seleção,
/// ou entra na caixa de texto abrindo o teclado virtual.</item>
/// <item>B: fecha o diálogo.</item>
/// </list>
/// </summary>
internal static class DialogGamepadNavigator
{
    public static void Handle(Window dialog, GamepadAction action)
    {
        var focused = dialog.FocusManager?.GetFocusedElement() as Control;

        switch (action)
        {
            case GamepadAction.MoveDown:
                MoveFocus(dialog, focused, NavigationDirection.Next);
                break;

            case GamepadAction.MoveUp:
                MoveFocus(dialog, focused, NavigationDirection.Previous);
                break;

            case GamepadAction.MoveLeft:
            case GamepadAction.MoveRight:
                if (focused is ComboBox combo && combo.ItemCount > 0)
                {
                    var delta = action == GamepadAction.MoveRight ? 1 : -1;
                    combo.SelectedIndex = Math.Clamp(combo.SelectedIndex + delta, 0, combo.ItemCount - 1);
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
                combo.SelectedIndex = (combo.SelectedIndex + 1) % combo.ItemCount;
                break;
            case TextBox:
                TouchKeyboard.Show();
                break;
        }
    }
}
