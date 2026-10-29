using Avalonia.Controls;
using Avalonia.Input;

namespace HandheldLauncher.App.Views.Layouts;

/// <summary>
/// O layout de sempre (hero grande em cima, abas, fileira/grade de capas),
/// movido pra cá na v0.9.20 sem mudança de comportamento — ver o
/// comentário dentro do .axaml.
///
/// <para>
/// As quatro propriedades abaixo são implementação EXPLÍCITA da interface
/// de propósito: o nome de cada uma é igual ao do campo que o Avalonia
/// gera a partir do <c>x:Name</c> do XAML, e implementação explícita é o
/// que deixa os dois coexistirem sem conflito (dentro do corpo, o nome se
/// refere ao campo gerado).
/// </para>
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

    // Handler de XAML tem que existir na classe do próprio arquivo, então
    // estes três moraram pra cá junto com as tiles. São só repasse: a
    // lógica continua na MainWindow (ver HandleTilePointerPressed lá pro
    // porquê). Janela nula nunca acontece na prática — o layout só existe
    // dentro dela — mas o "?." evita qualquer chance de exceção se um
    // evento chegar durante o desmonte.
    private MainWindow? Owner => TopLevel.GetTopLevel(this) as MainWindow;

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e) => Owner?.HandleTilePointerPressed(sender, e);

    private void OnTileDoubleTapped(object? sender, TappedEventArgs e) => Owner?.HandleTileDoubleTapped(sender, e);

    private void OnSubFilterClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Owner?.HandleSubFilterClick(sender, e);
}
