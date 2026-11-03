using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using HandheldLauncher.App.ViewModels;

namespace HandheldLauncher.App.Views.Layouts;

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
    bool ILibraryLayout.SupportsVerticalGrid => true;
    // Usa o mesmo acompanhamento centralizado do layout Switch.
    // Assim a navegação por controle mantém o jogo selecionado como referência visual.
    bool ILibraryLayout.CentersSelection => false;

    private MainWindow? Owner => TopLevel.GetTopLevel(this) as MainWindow;
    private void OnAddProgramClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Owner?.HandleAddProgramClick();
    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e) => Owner?.HandleTilePointerPressed(sender, e);
    private void OnTileDoubleTapped(object? sender, TappedEventArgs e) => Owner?.HandleTileDoubleTapped(sender, e);
    private void OnSubFilterClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Owner?.HandleSubFilterClick(sender, e);

    private void OnGameRowPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (GameRowScrollViewer is null) return;
        var maxOffset = Math.Max(0, GameRowScrollViewer.Extent.Width - GameRowScrollViewer.Viewport.Width);
        if (maxOffset <= 0) return;
        var wheelDelta = e.Delta.Y != 0 ? e.Delta.Y : -e.Delta.X;
        if (wheelDelta == 0) return;
        var nextOffset = Math.Clamp(GameRowScrollViewer.Offset.X - wheelDelta * 72, 0, maxOffset);
        GameRowScrollViewer.Offset = new Vector(nextOffset, GameRowScrollViewer.Offset.Y);
        e.Handled = true;
    }
}
