using Avalonia.Controls;
using Avalonia.Input;
using HandheldLauncher.App.Platform;
using HandheldLauncher.App.ViewModels;

namespace HandheldLauncher.App.Views.Layouts;

/// <summary>
/// v0.18.1 — grade vertical da aba Nuvem (ver comentário no .axaml). Sem
/// lógica própria: toque/duplo toque vão pros mesmos handlers da fileira
/// normal na MainWindow; seleção, navegação e rolagem também são as da
/// MainWindow (que usa <see cref="Items"/>/<see cref="Scroll"/> no lugar da
/// fileira enquanto a aba Nuvem está aberta).
/// </summary>
public partial class CloudGridView : UserControl
{
    public CloudGridView()
    {
        InitializeComponent();
    }

    public ItemsControl Items => CloudItems;
    public ScrollViewer Scroll => CloudScroll;
    public TextBox Search => SearchBox;

    // v0.18.3: busca/letras. Toque no campo abre o teclado de toque (mesmo
    // padrão das outras telas com campo de texto).
    private void OnSearchGotFocus(object? sender, GotFocusEventArgs e) => TouchKeyboard.Show();

    private void OnClearClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => (DataContext as MainViewModel)?.ClearCloudFilters();

    private void OnLetterClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CloudLetterViewModel letter } && DataContext is MainViewModel vm)
            vm.SetCloudLetter(letter.Value);
    }

    private MainWindow? Owner => TopLevel.GetTopLevel(this) as MainWindow;
    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e) => Owner?.HandleTilePointerPressed(sender, e);
    private void OnTileDoubleTapped(object? sender, TappedEventArgs e) => Owner?.HandleTileDoubleTapped(sender, e);
}
