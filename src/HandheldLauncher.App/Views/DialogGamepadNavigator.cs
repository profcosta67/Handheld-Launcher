using System.Linq;
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
/// só fecha a lista (não fecha o diálogo inteiro). v0.9.8, correção de um
/// bug do v0.9.7: isso aqui tinha virado dead code porque, assim que o
/// dropdown abre, o Avalonia muda o FOCO pro item dentro do popup — então
/// "o que está focado agora é um ComboBox com IsDropDownOpen=true" nunca
/// era verdade de verdade, e B caía direto no caso padrão (fechava o
/// diálogo inteiro). Corrigido procurando "existe algum ComboBox aberto
/// NESTA JANELA" na árvore, em vez de depender de qual controle específico
/// está focado.</item>
/// <item>A (fora de uma lista aberta): aperta botão, marca caixinha/opção,
/// ou entra na caixa de texto abrindo o teclado virtual.</item>
/// <item>B (fora de uma lista aberta): fecha o diálogo.</item>
/// </list>
/// </summary>
internal static class DialogGamepadNavigator
{
    public static void Handle(Window dialog, GamepadAction action)
    {
        // Ver FindOpenComboBox: não dá pra confiar em "o que está focado
        // agora é esse ComboBox" — o próprio Avalonia move o foco pro
        // item dentro do popup assim que ele abre.
        if (FindOpenComboBox(dialog) is { } openCombo)
        {
            switch (action)
            {
                case GamepadAction.MoveUp:
                case GamepadAction.MoveLeft:
                    CycleCombo(openCombo, -1);
                    return;
                case GamepadAction.MoveDown:
                case GamepadAction.MoveRight:
                    CycleCombo(openCombo, 1);
                    return;
                case GamepadAction.Confirm:
                case GamepadAction.Back:
                    openCombo.IsDropDownOpen = false;
                    return;
                default:
                    // Qualquer outra ação, com a lista aberta, não faz
                    // nada — nunca deixa cair no switch de baixo (que
                    // fecharia o diálogo inteiro no Back, por exemplo).
                    return;
            }
        }

        var focused = dialog.FocusManager?.GetFocusedElement() as Control;

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
                else if (focused is Slider sliderRight)
                {
                    AdjustSlider(sliderRight, 1);
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
                else if (focused is Slider sliderLeft)
                {
                    AdjustSlider(sliderLeft, -1);
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

            // v0.9.14: LB/RB cicla as abas de categoria da tela de
            // Configurações (ver CycleCategoryTab) — mesmo botão físico que
            // já cicla as abas de filtro da biblioteca por trás (pedido do
            // usuário na reorganização da tela: "separar em tópicos"). Sem
            // efeito num diálogo sem nenhum Button.categoryTab (ex:
            // AddGameWindow) — CycleCategoryTab simplesmente não acha nada
            // e não faz nada.
            case GamepadAction.PreviousFilter:
                CycleCategoryTab(dialog, -1);
                break;

            case GamepadAction.NextFilter:
                CycleCategoryTab(dialog, 1);
                break;
        }
    }

    /// <summary>
    /// Cicla as abas "Contas e Chaves/Diretórios/Modo de uso/Aparência" da
    /// tela de Configurações clicando o botão de verdade (mesmo truque que
    /// <see cref="Activate"/> já usa pra Confirm) — assim a ViewModel nem
    /// precisa expor nada específico de navegação por controle, só reage
    /// ao Click normal (ver SettingsWindow.axaml.cs.OnCategoryTabClick).
    /// Marca os candidatos pela classe de estilo "categoryTab" (não pelo
    /// tipo Button sozinho, que também cobriria "Salvar"/"Fechar" etc.) e
    /// pela classe "active" (ligada por Classes.active="{Binding ...}" no
    /// XAML) pra achar qual é a aba atual.
    /// </summary>
    private static void CycleCategoryTab(Window dialog, int delta)
    {
        var tabs = dialog.GetLogicalDescendants()
            .OfType<Button>()
            .Where(b => b.Classes.Contains("categoryTab") && b.IsEffectivelyEnabled)
            .OrderBy(b => b.Tag is string s && int.TryParse(s, out var i) ? i : 0)
            .ToList();
        if (tabs.Count == 0) return;

        var activeIndex = tabs.FindIndex(b => b.Classes.Contains("active"));
        if (activeIndex < 0) activeIndex = 0;
        var nextIndex = ((activeIndex + delta) % tabs.Count + tabs.Count) % tabs.Count;
        tabs[nextIndex].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void CycleCombo(ComboBox combo, int delta)
    {
        if (combo.ItemCount == 0) return;
        combo.SelectedIndex = Math.Clamp(combo.SelectedIndex + delta, 0, combo.ItemCount - 1);
    }

    /// <summary>
    /// v0.9.13: primeiro Slider do projeto (volume dos sons, ver
    /// SettingsWindow.axaml) — sem isso, ←/→ num Slider focado cairia no
    /// "else" de MoveFocus acima e só trocaria de campo, sem jeito de
    /// ajustar o valor pelo controle (só mouse/touch arrastando). Passo =
    /// TickFrequency do Slider (5 no caso do volume).
    /// </summary>
    private static void AdjustSlider(Slider slider, int direction)
    {
        var step = slider.TickFrequency > 0 ? slider.TickFrequency : 1;
        slider.Value = Math.Clamp(slider.Value + direction * step, slider.Minimum, slider.Maximum);
    }

    /// <summary>
    /// Procura um ComboBox com o dropdown aberto em QUALQUER lugar da
    /// janela — não só no que está focado agora (ver comentário em Handle
    /// pra entender por quê). Nunca tem mais de um aberto ao mesmo tempo
    /// na prática (abrir um fecha o anterior), então o primeiro que achar
    /// já resolve.
    /// </summary>
    private static ComboBox? FindOpenComboBox(Window dialog)
    {
        foreach (var descendant in dialog.GetLogicalDescendants())
        {
            if (descendant is ComboBox { IsDropDownOpen: true } combo) return combo;
        }
        return null;
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
