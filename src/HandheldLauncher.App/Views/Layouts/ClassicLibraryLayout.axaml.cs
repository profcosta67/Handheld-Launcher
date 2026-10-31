using Avalonia.Controls;
using Avalonia.Input;

namespace HandheldLauncher.App.Views.Layouts;

/// <summary>
/// O layout de sempre (hero grande em cima, abas, fileira/grade de capas),
/// movido pra cá na v0.9.20 sem mudança de comportamento — ver o
/// comentário dentro do .axaml.
/// </summary>
public partial class ClassicLibraryLayout : UserControl, ILibraryLayout
{
    public ClassicLibraryLayout()
    {
        InitializeComponent();
    }

    ItemsControl ILibraryLayout.GamesItemsControl => GamesItemsControl;

    ScrollViewer ILibraryLayout.GameRowScrollViewer => GameRowScrollViewer;

    Image ILibraryLayout.HeroImage => HeroImage;

    Image ILibraryLayout.HeroBackImage => HeroBackImage;

    /// <summary>Este layout tem os dois modos (fileira horizontal e grade vertical), como sempre teve.</summary>
    bool ILibraryLayout.SupportsVerticalGrid => true;

    /// <summary>Scroll mínimo, como sempre foi — ver ILibraryLayout.CentersSelection.</summary>
    bool ILibraryLayout.CentersSelection => false;

    private MainWindow? Owner => TopLevel.GetTopLevel(this) as MainWindow;

    private void OnAddProgramClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Owner?.HandleAddProgramClick();

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e) => Owner?.HandleTilePointerPressed(sender, e);

    private void OnTileDoubleTapped(object? sender, TappedEventArgs e) => Owner?.HandleTileDoubleTapped(sender, e);

    private void OnSubFilterClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Owner?.HandleSubFilterClick(sender, e);
}