using Avalonia.Controls;
using Avalonia.Input;

namespace HandheldLauncher.App.Views.Layouts;

/// <summary>
/// Tema de layout "BigScreen" (v0.9.20) — ver o comentário dentro do .axaml
/// pra referência combinada com o usuário e pro que é deliberadamente
/// diferente do layout Clássico.
///
/// <para>
/// Note que não tem NADA de lógica aqui: todo o comportamento (navegação
/// pelo controle, scroll, lançar jogo, menus, transição do banner) vem da
/// MainWindow, que acha os quatro controles deste layout por
/// <see cref="ILibraryLayout"/>. Trocar de tema de layout não troca
/// comportamento nenhum de propósito — só a disposição.
/// </para>
/// </summary>
public partial class BigScreenLibraryLayout : UserControl, ILibraryLayout
{
    public BigScreenLibraryLayout()
    {
        InitializeComponent();
    }

    ItemsControl ILibraryLayout.GamesItemsControl => GamesItemsControl;

    ScrollViewer ILibraryLayout.GameRowScrollViewer => GameRowScrollViewer;

    Image ILibraryLayout.HeroImage => HeroImage;

    Image ILibraryLayout.HeroBackImage => HeroBackImage;

    /// <summary>
    /// Fileira horizontal sempre. A grade vertical
    /// (AppSettings.VerticalLayout) não faz sentido nesta disposição — o
    /// palco ocupa a maior parte da tela e a fileira de ícones é a
    /// identidade do tema — então a MainWindow nem tenta trocar o
    /// ItemsPanel aqui (ver ApplyLayoutMode).
    /// </summary>
    bool ILibraryLayout.SupportsVerticalGrid => false;

    /// <summary>A seleção acompanha o fluxo natural da biblioteca Capsule Wide, sem recentralização forçada.</summary>
    bool ILibraryLayout.CentersSelection => false;

    // Mesmos repasses do ClassicLibraryLayout — ver comentário lá.
    private MainWindow? Owner => TopLevel.GetTopLevel(this) as MainWindow;

    private void OnAddProgramClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Owner?.HandleAddProgramClick();

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e) => Owner?.HandleTilePointerPressed(sender, e);

    private void OnTileDoubleTapped(object? sender, TappedEventArgs e) => Owner?.HandleTileDoubleTapped(sender, e);

    private void OnSubFilterClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Owner?.HandleSubFilterClick(sender, e);

    private void OnOpenWifiSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Owner?.OpenWindowsSettings("ms-settings:network-wifi");

    private void OnOpenBluetoothSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Owner?.OpenWindowsSettings("ms-settings:bluetooth");

}
