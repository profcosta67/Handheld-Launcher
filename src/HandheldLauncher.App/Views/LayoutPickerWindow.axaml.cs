using Avalonia.Controls;
using HandheldLauncher.App.Platform;

namespace HandheldLauncher.App.Views;

/// <summary>
/// Seletor de tema de LAYOUT (v0.9.21) — ver o comentário dentro do
/// .axaml pro pedido do usuário que originou esta tela.
///
/// <para>
/// Devolve por <c>ShowDialog&lt;bool&gt;</c>: true = aplicar (e quem abriu
/// lê <see cref="SelectedLayout"/>, salva e reinicia), false = cancelou.
/// Quem reinicia é o chamador, não esta janela — ela não sabe nada sobre
/// settings.json, igual o EditGameWindow.
/// </para>
/// </summary>
public partial class LayoutPickerWindow : Window
{
    public LayoutPickerWindow(int currentLayout)
    {
        InitializeComponent();
        ThemeManager.ApplyFont(this, HandheldLauncher.Core.Services.AppSettingsStore.Load().UiFont);

        if (currentLayout == 1) SwitchOption.IsChecked = true;
        else ClassicOption.IsChecked = true;
    }

    /// <summary>Construtor sem parâmetros só pro carregador de XAML do Avalonia; o app usa o de cima.</summary>
    public LayoutPickerWindow() : this(0)
    {
    }

    /// <summary>Índice escolhido, no mesmo esquema de ThemeManager.LayoutThemeNames: 0 Clássico, 1 Switch.</summary>
    public int SelectedLayout => SwitchOption.IsChecked == true ? 1 : 0;

    private void OnApplyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }
}
