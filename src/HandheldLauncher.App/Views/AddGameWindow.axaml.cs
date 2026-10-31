using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Win32;
using System.Runtime.Versioning;
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

    public AddGameWindow(bool isApplication = false)
    {
        InitializeComponent();
        ViewModel.IsApplication = isApplication;
        // No fluxo "Adicionar programa", o tipo é obrigatório: não permitir
        // que o usuário desmarque e cadastre o executável como jogo.
        ApplicationCheckBox.IsVisible = !isApplication;
        HandheldLauncher.App.Platform.ThemeManager.ApplyFont(this, HandheldLauncher.Core.Services.AppSettingsStore.Load().UiFont);
        DataContext = ViewModel;
    }

    // Construtor vazio para o Avalonia conseguir instanciar via XAML (resolve o warning AVLN3001)
    public AddGameWindow() : this(isApplication: false) { }

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

    [SupportedOSPlatform("windows")]
    private void OnSearchInstalledClick(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows()) return; // Trava de segurança para o Linux

        try
        {
            var found = new Dictionary<string, InstalledProgramOption>(StringComparer.OrdinalIgnoreCase);
            ScanUninstallRegistry(RegistryHive.LocalMachine, RegistryView.Registry64, found);
            ScanUninstallRegistry(RegistryHive.LocalMachine, RegistryView.Registry32, found);
            ScanUninstallRegistry(RegistryHive.CurrentUser, RegistryView.Registry64, found);
            InstalledProgramsList.ItemsSource = found.Values.OrderBy(x => x.DisplayName).ToList();
            InstalledProgramsList.IsVisible = true;
            if (found.Count == 0)
            {
                InstalledProgramsList.ItemsSource = new[] { new InstalledProgramOption("Nenhum programa com executável identificado no registro do Windows.", string.Empty) };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AddGameWindow: falha ao buscar programas instalados: {ex}");
            InstalledProgramsList.ItemsSource = new[] { new InstalledProgramOption("Não foi possível consultar os programas instalados.", string.Empty) };
            InstalledProgramsList.IsVisible = true;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ScanUninstallRegistry(RegistryHive hive, RegistryView view, IDictionary<string, InstalledProgramOption> found)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall");
        if (uninstall is null) return;
        foreach (var subName in uninstall.GetSubKeyNames())
        {
            using var app = uninstall.OpenSubKey(subName);
            var name = app?.GetValue("DisplayName") as string;
            var icon = app?.GetValue("DisplayIcon") as string;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(icon)) continue;
            var candidate = Environment.ExpandEnvironmentVariables(icon.Split(',')[0].Trim().Trim('"'));
            if (!candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate)) continue;
            if (!found.ContainsKey(candidate)) found[candidate] = new InstalledProgramOption(name.Trim(), candidate);
        }
    }

    private void OnInstalledProgramSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (InstalledProgramsList.SelectedItem is not InstalledProgramOption selected || string.IsNullOrWhiteSpace(selected.ExecutablePath)) return;
        ViewModel.Title = selected.DisplayName;
        ViewModel.ExecutablePath = selected.ExecutablePath;
        ViewModel.WorkingDirectory = Path.GetDirectoryName(selected.ExecutablePath) ?? string.Empty;
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