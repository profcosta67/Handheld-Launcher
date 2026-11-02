using Avalonia.Controls;
using Avalonia.Interactivity;
using HandheldLauncher.App.ViewModels;

namespace HandheldLauncher.App.Views;

public partial class SystemUpdateWindow : Window
{
    public SystemUpdateWindow()
    {
        InitializeComponent();
        DataContext = new SystemUpdateViewModel();
    }

    private async void OnSearchClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SystemUpdateViewModel vm)
            await vm.SearchAsync();
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SystemUpdateViewModel vm)
            await vm.InstallSelectedAsync();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}