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
public partial class EditGameWindow : Window
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
        // SettingsWindow.axaml.cs pra ficar consistente nos dois.
        AddHandler(PointerPressedEvent, (_, e) => _lastPointerWasTouch = e.Pointer.Type != PointerType.Mouse, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox && e.NavigationMethod == NavigationMethod.Pointer && _lastPointerWasTouch)
            {
                TouchKeyboard.Show();
            }
        }, RoutingStrategies.Bubble);
    }

    private bool _lastPointerWasTouch;

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
        TouchKeyboard.Toggle();
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
