using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HandheldLauncher.App.ViewModels;
using Avalonia.Platform.Storage;
using System.Linq;

namespace HandheldLauncher.App.Views;

public partial class AddGameWindow : Window
{
    // 🟢 Construtor padrão (Designer / chamadas sem parâmetro)
    public AddGameWindow() : this(isApplication: false) { }

    // 🟢 Construtor com isApplication — restaurado pra MainWindow.Dialogs.cs voltar a compilar.
    public AddGameWindow(bool isApplication)
    {
        InitializeComponent();
        var vm = new AddGameViewModel();
        if (isApplication) vm.IsApplication = true;
        DataContext = vm;
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var storage = GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecione o executável",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Executável") { Patterns = new[] { "*.exe" } } }
        });

        if (files.Count > 0 && DataContext is AddGameViewModel vm)
        {
            vm.ExecutablePath = files[0].Path.LocalPath;
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AddGameViewModel vm && vm.CanSave)
        {
            Close(vm);
        }
    }
}