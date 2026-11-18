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

    private void OnRefreshProgramsClick(object? sender, RoutedEventArgs e) =>
        HandheldLauncher.App.Diagnostics.UiSafe.Run(() => OnRefreshProgramsClickCore(sender, e), "OnRefreshProgramsClick");

    private async Task OnRefreshProgramsClickCore(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AddGameViewModel vm) await vm.LoadInstalledProgramsAsync();
    }

    private void OnBrowseClick(object? sender, RoutedEventArgs e) =>
        HandheldLauncher.App.Diagnostics.UiSafe.Run(() => OnBrowseClickCore(sender, e), "OnBrowseClick");

    private async Task OnBrowseClickCore(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddGameViewModel vm) return;

        // 18/11: no Windows, seletor que devolve o próprio atalho (.lnk) — o do Avalonia troca o
        // atalho pelo destino e os argumentos dele (ex.: o jogo que o Ryujinx abre) se perdiam.
        if (OperatingSystem.IsWindows())
        {
            var owner = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            var picked = HandheldLauncher.App.Platform.NativeFilePicker.PickFile(owner,
                vm.IsApplication ? "Selecione o programa ou atalho" : "Selecione o jogo, o emulador ou um atalho",
                "Executável ou atalho|*.exe;*.lnk;*.url;*.bat;*.cmd",
                "Todos os arquivos|*.*");
            if (picked is null) return;
            UsePickedFile(vm, picked);
            return;
        }

        var storage = GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecione o executável",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Executável") { Patterns = new[] { "*.exe" } } }
        });

        if (files.Count > 0) UsePickedFile(vm, files[0].Path.LocalPath);
    }

    private static void UsePickedFile(AddGameViewModel vm, string path)
    {
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
        {
            var shortcut = HandheldLauncher.App.Platform.WindowsShortcut.TryRead(path);
            vm.ApplyShortcut(path, shortcut?.TargetPath, shortcut?.Arguments, shortcut?.WorkingDirectory, shortcut?.Url);
            HandheldLauncher.App.Diagnostics.DebugLog.Write(
                $"[ADICIONAR] Atalho {System.IO.Path.GetFileName(path)} → {(shortcut is null ? "não lido" : shortcut.Url ?? shortcut.TargetPath ?? "sem destino")}.");
            return;
        }
        vm.ShortcutNote = string.Empty;
        vm.ExecutablePath = path;
    }

    private void OnBrowseGameFileClick(object? sender, RoutedEventArgs e) =>
        HandheldLauncher.App.Diagnostics.UiSafe.Run(() => OnBrowseGameFileClickCore(sender, e), "OnBrowseGameFileClick");

    /// <summary>18/11: arquivo do jogo para emulador (.nsp, .xci, .iso...) — qualquer tipo.</summary>
    private async Task OnBrowseGameFileClickCore(object? sender, RoutedEventArgs e)
    {
        var storage = GetTopLevel(this)?.StorageProvider;
        if (storage is null || DataContext is not AddGameViewModel vm) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecione o arquivo do jogo",
            AllowMultiple = false,
        });

        if (files.Count > 0) vm.GameFilePath = files[0].Path.LocalPath;
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