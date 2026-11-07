using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.App.Views;   // 🟢 ADICIONADO: pra achar ConnectivityPopupWindow

namespace HandheldLauncher.App.Views.Layouts;

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
    bool ILibraryLayout.SupportsVerticalGrid => true;
    bool ILibraryLayout.CentersSelection => false;
    CloudGridView ILibraryLayout.CloudGrid => CloudGrid;

    public void SetVertical(bool vertical)
    {
        if (vertical)
        {
            GameOuterPanel.Orientation = Orientation.Vertical;
            AddProgramButton.Margin = new Thickness(0, 18, 0, 0);
        }
        else
        {
            GameOuterPanel.Orientation = Orientation.Horizontal;
            AddProgramButton.Margin = new Thickness(20, 18, 20, 18);
        }
    }

    private MainWindow? Owner => TopLevel.GetTopLevel(this) as MainWindow;
    private void OnAddProgramClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Owner?.HandleAddProgramClick();
    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e) => Owner?.HandleTilePointerPressed(sender, e);
    private void OnTileDoubleTapped(object? sender, TappedEventArgs e) => Owner?.HandleTileDoubleTapped(sender, e);
    private void OnSubFilterClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Owner?.HandleSubFilterClick(sender, e);

    // 🟢 CORRIGIDO: abre o popup direto, sem depender do MainWindow ter esse método.
    private async void OnOpenWifiSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Owner is null) return;
        var dialog = new ConnectivityPopupWindow { Topmost = true };
        await dialog.ShowDialog(Owner);
    }

    private async void OnOpenBluetoothSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Owner is null) return;
        var dialog = new ConnectivityPopupWindow { Topmost = true };
        await dialog.ShowDialog(Owner);
    }
}