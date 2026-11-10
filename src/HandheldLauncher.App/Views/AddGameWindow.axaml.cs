using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HandheldLauncher.App.ViewModels;
using Avalonia.Platform.Storage;
using System.Linq;

namespace HandheldLauncher.App.Views;

public partial class AddGameWindow : Window, IGamepadKeyboardHost
{
    /// <summary>v0.24.2: com o teclado aberto, o controle é dele.</summary>
    public bool HandleKeyboardGamepadAction(HandheldLauncher.App.Input.GamepadAction action)
        => Keyboard.IsOpen && Keyboard.HandleGamepadAction(action);

    /// <summary>v0.24.2: A sobre um campo de texto abre o teclado do launcher.</summary>
    public bool ShowKeyboardFor(TextBox target)
    {
        Keyboard.Show(target);
        target.Focus();
        return true;
    }

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

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is AddGameViewModel vm) _ = vm.LoadInstalledProgramsAsync();
    }

    private async void OnRefreshProgramsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AddGameViewModel vm) await vm.LoadInstalledProgramsAsync();
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

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AddGameViewModel vm && vm.CanSave)
        {
            // v0.25.3: a MainWindow espera um GameEntry (ShowDialog<GameEntry?>).
            // Antes fechava com o ViewModel → InvalidCastException e o programa
            // nunca era adicionado ("travou e não reconheceu").
            Close(vm.BuildGameEntry());
        }
    }
}