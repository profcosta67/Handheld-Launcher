using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HandheldLauncher.App.Platform;

namespace HandheldLauncher.App.Views;

public partial class DriverUpdateWindow : Window
{
    private readonly WindowsDriverService _driverService = new();
    private string? _selectedInfPath;

    public DriverUpdateWindow()
    {
        InitializeComponent();
    }

    private async void OnSelectFileClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecione o arquivo de driver (.inf)",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Driver INF") { Patterns = new[] { "*.inf" } } }
        });

        if (files.Count > 0)
        {
            _selectedInfPath = files[0].TryGetLocalPath();
            TxtFilePath.Text = _selectedInfPath;
            BtnInstall.IsEnabled = true;
        }
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedInfPath)) return;

        BtnInstall.IsEnabled = false;
        TxtFilePath.Text = "Instalando... (isso pode demorar alguns segundos)";
        
        var result = await _driverService.InstallDriverAsync(_selectedInfPath);
        
        TxtFilePath.Text = result;
        if (result.Contains("sucesso"))
        {
            // Opcional: Fechar a janela após sucesso
            // Close();
        }
        else
        {
            BtnInstall.IsEnabled = true;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}