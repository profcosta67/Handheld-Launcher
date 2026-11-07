using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using HandheldLauncher.App.ViewModels;

namespace HandheldLauncher.App.Views.Layouts;

public partial class SwitchLibraryLayout : UserControl, ILibraryLayout
{
    public SwitchLibraryLayout()
    {
        InitializeComponent();
    }

    ItemsControl ILibraryLayout.GamesItemsControl => GamesItemsControl;
    ScrollViewer ILibraryLayout.GameRowScrollViewer => GameRowScrollViewer;
    Image ILibraryLayout.HeroImage => HeroImage;
    Image ILibraryLayout.HeroBackImage => HeroBackImage;

    // 🟢 MUDANÇA: Tema Clean é carrossel por natureza (ScaleTransform/Opacity por distância).
    // Em grade vertical, o zoom das capas fica errado e o scroll não faz sentido.
    // Bloqueado aqui — Clássico e BigScreen continuam aceitando grade vertical.
    bool ILibraryLayout.SupportsVerticalGrid => false;

    bool ILibraryLayout.CentersSelection => true;
    CloudGridView ILibraryLayout.CloudGrid => CloudGrid;

    public void SetVertical(bool vertical)
    {
        if (vertical)
        {
            GameOuterPanel.Orientation = Orientation.Vertical;
            GameOuterPanel.HorizontalAlignment = HorizontalAlignment.Left;
            GameRowScrollViewer.Height = double.NaN;
            GameRowScrollViewer.VerticalAlignment = VerticalAlignment.Top;
            AddProgramButton.Margin = new Thickness(0, 28, 0, 0);
            AddProgramButton.HorizontalAlignment = HorizontalAlignment.Left;
        }
        else
        {
            GameOuterPanel.Orientation = Orientation.Horizontal;
            GameOuterPanel.HorizontalAlignment = HorizontalAlignment.Center;
            GameRowScrollViewer.Height = 460;
            GameRowScrollViewer.VerticalAlignment = VerticalAlignment.Center;
            AddProgramButton.Margin = new Thickness(60, 0, 0, 0);
            AddProgramButton.HorizontalAlignment = HorizontalAlignment.Left;
        }
    }

    private MainWindow? Owner => TopLevel.GetTopLevel(this) as MainWindow;
    private void OnAddProgramClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Owner?.HandleAddProgramClick();
    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e) => Owner?.HandleTilePointerPressed(sender, e);
    private void OnTileDoubleTapped(object? sender, TappedEventArgs e) => Owner?.HandleTileDoubleTapped(sender, e);
    private void OnSubFilterClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Owner?.HandleSubFilterClick(sender, e);
}