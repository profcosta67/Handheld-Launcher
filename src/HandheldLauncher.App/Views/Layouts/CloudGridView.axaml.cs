using Avalonia.Controls;
using Avalonia.Input;

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

    private MainWindow? Owner => TopLevel.GetTopLevel(this) as MainWindow;
    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e) => Owner?.HandleTilePointerPressed(sender, e);
    private void OnTileDoubleTapped(object? sender, TappedEventArgs e) => Owner?.HandleTileDoubleTapped(sender, e);
}
