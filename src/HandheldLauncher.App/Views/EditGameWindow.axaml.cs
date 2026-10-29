using Avalonia.Controls;
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
