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

    private void OnSearchClick(object? sender, RoutedEventArgs e) =>
        HandheldLauncher.App.Diagnostics.UiSafe.Run(() => OnSearchClickCore(sender, e), "OnSearchClick");

    private async Task OnSearchClickCore(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SystemUpdateViewModel vm)
            await vm.SearchAsync();
    }

    private void OnInstallClick(object? sender, RoutedEventArgs e) =>
        HandheldLauncher.App.Diagnostics.UiSafe.Run(() => OnInstallClickCore(sender, e), "OnInstallClick");

    private async Task OnInstallClickCore(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SystemUpdateViewModel vm)
            await vm.InstallSelectedAsync();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}