using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.App.Views;

/// <summary>
/// Modal "Adicionar jogo" dialog. Caller opens it with
/// <c>await new AddGameWindow().ShowDialog&lt;GameEntry?&gt;(owner)</c> and
/// gets the built GameEntry back (or null if cancelled) — see
/// MainWindow.axaml.cs's OnAddGameClick.
/// </summary>
public partial class AddGameWindow : Window
{
    public AddGameViewModel ViewModel { get; } = new();

    public AddGameWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        // This Window IS a TopLevel already (Window -> WindowBase -> TopLevel),
        // so StorageProvider is available directly — no need to look one up
        // via TopLevel.GetTopLevel() like you would from a plain UserControl.
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecione o executável do jogo",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Executável (*.exe)") { Patterns = new[] { "*.exe" } },
            },
        });

        var file = files.FirstOrDefault();
        if (file is null) return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        ViewModel.ExecutablePath = path;

        // Convenience: suggest the exe's filename as the title if the user
        // hasn't typed one yet — they can still overwrite it freely.
        if (string.IsNullOrWhiteSpace(ViewModel.Title))
        {
            ViewModel.Title = Path.GetFileNameWithoutExtension(path);
        }
    }

    private void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanSave) return;
        Close(ViewModel.BuildGameEntry());
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
