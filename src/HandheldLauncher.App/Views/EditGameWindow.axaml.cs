using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HandheldLauncher.App.Platform;

namespace HandheldLauncher.App.Views;

/// <summary>
/// "Editar" do menu de contexto (v0.9.19) — renomear a entrada e marcar
/// se ela é um programa em vez de um jogo. Ver EditGameWindow.axaml pro
/// porquê de não ter ViewModel própria.
///
/// <para>
/// Devolve <c>true</c> por <c>ShowDialog&lt;bool&gt;</c> quando o usuário
/// salvou (e aí quem abriu lê <see cref="EditedTitle"/>/
/// <see cref="IsApplication"/>), ou <c>false</c> ao cancelar/fechar —
/// mesmo contrato de resultado do AddGameWindow, só com bool em vez de
/// GameEntry.
/// </para>
/// </summary>
public partial class EditGameWindow : Window, IGamepadKeyboardHost
{
    public EditGameWindow(string currentTitle, bool isApplication)
    {
        InitializeComponent();
        ThemeManager.ApplyFont(this, HandheldLauncher.Core.Services.AppSettingsStore.Load().UiFont);

        TitleBox.Text = currentTitle;
        IsApplicationBox.IsChecked = isApplication;

        // v0.17 (05/11, bug reportado "clico na caixa do nome e o teclado
        // não aparece"): esse diálogo só abria o teclado virtual pelo botão
        // "⌨ Teclado" dedicado — clicar/tocar direto na caixa de texto não
        // fazia nada, diferente de Configurações, que já tinha esse mesmo
        // padrão (tocar um TextBox abre o teclado). Copiado de
        // v0.24.2: qualquer campo de texto que receba foco abre o teclado do
        // launcher (o TabTip não recebia o controle no aparelho).
        AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox box && !ReferenceEquals(Keyboard.Target, box))
                Keyboard.Show(box);
        }, RoutingStrategies.Bubble);

        Keyboard.SubmitRequested += (_, _) => Keyboard.Hide();
    }

    /// <summary>v0.24.2: com o teclado aberto, o controle é dele.</summary>
    public bool HandleKeyboardGamepadAction(HandheldLauncher.App.Input.GamepadAction action)
        => Keyboard.IsOpen && Keyboard.HandleGamepadAction(action);

    /// <summary>v0.24.2: A sobre um campo de texto abre o teclado do launcher.</summary>
    public bool ShowKeyboardFor(TextBox target)
    {
        Keyboard.Show(target);
        target.Focus();
        return true;
    }

    /// <summary>
    /// Construtor sem parâmetros só pro carregador de XAML do Avalonia
    /// (previewer/d:DataContext) — o app sempre usa o de cima.
    /// </summary>
    public EditGameWindow() : this(string.Empty, false)
    {
    }

    /// <summary>Nome digitado. Vazio = limpar o override e voltar pro nome do scanner (ver LibraryService.SaveEntryEdits).</summary>
    public string? EditedTitle => TitleBox.Text;

    public bool IsApplication => IsApplicationBox.IsChecked == true;

    private void OnKeyboardClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Keyboard.IsOpen) Keyboard.Hide();
        else Keyboard.Show(TitleBox);
    }

    private void OnSaveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }
}
