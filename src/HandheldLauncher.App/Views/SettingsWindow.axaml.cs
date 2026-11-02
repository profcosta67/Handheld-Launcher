using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HandheldLauncher.App.Platform;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Views;

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
        ThemeManager.ApplyFont(this, _settings.UiFont);

        Opened += (_, _) => FitToScreen();

        AddHandler(PointerPressedEvent, (_, e) => _lastPointerWasTouch = e.Pointer.Type != PointerType.Mouse, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox && e.NavigationMethod == NavigationMethod.Pointer && _lastPointerWasTouch)
            {
                TouchKeyboard.Show();
            }
        }, RoutingStrategies.Bubble);
    }

    private bool _lastPointerWasTouch;

    private void FitToScreen()
    {
        try
        {
            var screen = Screens.Primary;
            if (screen is null) return;
            var available = screen.WorkingArea.Height / screen.Scaling - 40;
            if (available > 300 && Height > available)
            {
                Height = available;
                Position = new Avalonia.PixelPoint(Position.X, screen.WorkingArea.Y + 20);
            }
        }
        catch { }
    }

    private async void OnManageAccountsClick(object? sender, RoutedEventArgs e)
    {
        await new AccountsWindow(ViewModel).ShowDialog(this);
    }

    private void OnCategoryTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var index))
        {
            ViewModel.CategoryIndex = index;
        }
    }

    private async void OnBrowseEdenClick(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var topLevel = GetTopLevel(this);
            if (topLevel?.StorageProvider is not { } storage) return;

            var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Selecione o eden.exe",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Executável")
                    {
                        Patterns = new[] { "*.exe" }
                    }
                }
            });

            if (files.Count > 0)
            {
                ViewModel.EdenExecutablePath = files[0].Path.LocalPath;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[SETTINGS] Falha ao abrir file picker (eden): {ex.Message}");
        }
    }

    private async void OnBrowseLosslessClick(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var topLevel = GetTopLevel(this);
            if (topLevel?.StorageProvider is not { } storage) return;

            var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Selecione o LosslessScaling.exe",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Executável")
                    {
                        Patterns = new[] { "*.exe" }
                    }
                }
            });

            if (files.Count > 0)
            {
                ViewModel.LosslessScalingPath = files[0].Path.LocalPath;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[SETTINGS] Falha ao abrir file picker (lossless): {ex.Message}");
        }
    }

    private async void OnAddRomFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Selecione a pasta com as ROMs",
            AllowMultiple = false,
        });

        var folder = folders.FirstOrDefault();
        if (folder is null) return;

        var path = folder.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        var existing = ViewModel.EdenRomFoldersText
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (existing.Contains(path, StringComparer.OrdinalIgnoreCase)) return;

        ViewModel.EdenRomFoldersText = existing.Length == 0
            ? path
            : string.Join(Environment.NewLine, existing.Append(path));
    }

    private void OnTestEdenScanClick(object? sender, RoutedEventArgs e)
    {
        var folders = ViewModel.EdenRomFoldersText
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        var exePath = ViewModel.EdenExecutablePath?.Trim();

        try
        {
            var scanner = new EdenLibraryScanner(string.IsNullOrWhiteSpace(exePath) ? null : exePath, folders);
            scanner.ScanInstalledGames(out var report);
            ViewModel.EdenScanResultText = DescribeScan(report);
        }
        catch (Exception ex)
        {
            ViewModel.EdenScanResultText = $"Não deu pra escanear: {ex.Message}";
        }
    }

    private static string DescribeScan(EdenScanReport report)
    {
        if (!report.EdenExecutableFound)
        {
            return "O eden.exe não foi encontrado no caminho acima — sem ele nenhum jogo é listado.";
        }

        var parts = new List<string>
        {
            report.Games == 1 ? "1 jogo encontrado" : $"{report.Games} jogos encontrados",
        };

        if (report.SkippedAddOns > 0)
        {
            parts.Add(report.SkippedAddOns == 1
                ? "1 arquivo ignorado (DLC ou atualização)"
                : $"{report.SkippedAddOns} arquivos ignorados (DLC ou atualização)");
        }

        if (report.FoldersMissing > 0)
        {
            parts.Add(report.FoldersMissing == 1
                ? "1 pasta da lista não existe"
                : $"{report.FoldersMissing} pastas da lista não existem");
        }

        if (report.UsedEdenConfigFallback)
        {
            parts.Add(report.FoldersScanned > 0
                ? "nenhuma pasta informada aqui, então foi usada a configuração do próprio Eden"
                : "nenhuma pasta informada aqui e a configuração do próprio Eden não tem pasta nenhuma");
        }

        return string.Join(" · ", parts) + ".";
    }

    private void OnKeyboardClick(object? sender, RoutedEventArgs e)
    {
        TouchKeyboard.Toggle();
    }

    private void OnOpenApiKeySiteClick(object? sender, RoutedEventArgs e)
    {
        const string url = "https://www.steamgriddb.com/profile/preferences/api";
        if (Owner is MainWindow mainWindow)
        {
            mainWindow.OpenExternalUrl(url);
        }
        else
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.SaveTo(_settings);
        SavedOkButton.Focus();
    }

    private void OnSavedOkClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.DismissSavedMessage();
    }

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