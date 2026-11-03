using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using HandheldLauncher.App.ViewModels;

namespace HandheldLauncher.App.Views.Layouts;

public partial class ClassicLibraryLayout : UserControl, ILibraryLayout
{
    public ClassicLibraryLayout()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    ItemsControl ILibraryLayout.GamesItemsControl => GamesItemsControl;
    ScrollViewer ILibraryLayout.GameRowScrollViewer => GameRowScrollViewer;
    Image ILibraryLayout.HeroImage => HeroImage;
    Image ILibraryLayout.HeroBackImage => HeroBackImage;
    bool ILibraryLayout.SupportsVerticalGrid => true;
    bool ILibraryLayout.CentersSelection => false;

    private MainWindow? Owner => TopLevel.GetTopLevel(this) as MainWindow;
    private void OnAddProgramClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Owner?.HandleAddProgramClick();
    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e) => Owner?.HandleTilePointerPressed(sender, e);
    private void OnTileDoubleTapped(object? sender, TappedEventArgs e) => Owner?.HandleTileDoubleTapped(sender, e);
    private void OnSubFilterClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Owner?.HandleSubFilterClick(sender, e);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged -= OnViewModelPropertyChanged;
            npc.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedIndex))
            Dispatcher.UIThread.Post(ScrollSelectionIntoView, DispatcherPriority.Background);
    }

    /// <summary>
    /// ✅ v15.6-R4: cálculo IDÊNTICO ao MainWindow.axaml.cs do projeto antigo
    /// (que o usuário confirmou que funcionava bem). Sem padding extra,
    /// sem centralização forçada — só "traz pra dentro da viewport quando
    /// sai". Se o item já está visível, NÃO MEXE no scroll.
    /// </summary>
    private void ScrollSelectionIntoView()
    {
        if (DataContext is not MainViewModel vm) return;
        if (GameRowScrollViewer is null) return;
        if (vm.Games.Count == 0 || vm.SelectedIndex < 0) return;

        const double spacing = 28;  // igual ao StackPanel Spacing no .axaml

        double itemLeft = 0;
        for (var i = 0; i < vm.SelectedIndex; i++)
            itemLeft += vm.Games[i].TileWidth + spacing;

        var itemWidth = vm.Games[vm.SelectedIndex].TileWidth;
        var itemRight = itemLeft + itemWidth;

        var viewLeft = GameRowScrollViewer.Offset.X;
        var viewRight = viewLeft + GameRowScrollViewer.Viewport.Width;

        if (itemLeft < viewLeft)
        {
            GameRowScrollViewer.Offset = new Vector(itemLeft, GameRowScrollViewer.Offset.Y);
        }
        else if (itemRight > viewRight)
        {
            GameRowScrollViewer.Offset = new Vector(itemRight - GameRowScrollViewer.Viewport.Width, GameRowScrollViewer.Offset.Y);
        }
        // Se já está visível, não mexe — era isso que causava o "pulo/recuo"
    }
}