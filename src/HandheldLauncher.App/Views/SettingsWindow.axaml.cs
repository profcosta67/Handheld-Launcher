using Avalonia.Controls;
using Avalonia.Interactivity;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Views;

/// <summary>
/// Modal "Configurações" dialog. Mesmo padrão do AddGameWindow — caller
/// abre com <c>await new SettingsWindow().ShowDialog(owner)</c> (ver
/// MainWindow.axaml.cs's OpenSettingsDialogAsync). Diferente do
/// AddGameWindow, esta Window é quem lê/grava settings.json direto via
/// AppSettingsStore (a ViewModel não conhece o arquivo nem o caminho) —
/// não devolve nada pro caller fechar, já que salvar aqui grava direto no
/// disco e só tem efeito depois de reiniciar o launcher (ver aviso na
/// própria tela).
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel { get; } = new();

    private readonly AppSettings _settings;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;

        _settings = AppSettingsStore.Load();
        ViewModel.LoadFrom(_settings);
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.SaveTo(_settings);
    }

    /// <summary>Preenchido por quem abre (MainWindow.OpenSettingsDialogAsync).</summary>
    public Func<Task<string>>? CheckForUpdates { get; set; }

    private async void OnCheckUpdatesClick(object? sender, RoutedEventArgs e)
    {
        if (CheckForUpdates is null) return;
        ViewModel.UpdateResultText = "Verificando…";
        ViewModel.UpdateResultText = await CheckForUpdates();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
