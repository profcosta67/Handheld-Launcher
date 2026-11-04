using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Input;
using HandheldLauncher.App.Platform;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel { get; } = new();

    private readonly AppSettings _settings;
    private enum FocusZone { Tabs, Content }
    private FocusZone _focusZone = FocusZone.Tabs;
    private int _tabIndex = 0;
    private int _contentIndex = 0;
    private AccountsWindow? _accountsWindow;
    private ConnectivityPopupWindow? _connectivityDialog;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;

        _settings = AppSettingsStore.Load();
        ViewModel.LoadFrom(_settings);
        ThemeManager.ApplyFont(this, _settings.UiFont);

        Opened += async (_, _) => 
        { 
            FitToScreen(); 
            FocusActiveTab(); 
            await ViewModel.LoadReleaseNotesAsync();
        };

        AddHandler(PointerPressedEvent, (_, e) => _lastPointerWasTouch = e.Pointer.Type != PointerType.Mouse, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnSettingsKeyDown, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox && e.NavigationMethod == NavigationMethod.Pointer && _lastPointerWasTouch)
            {
                TouchKeyboard.Show();
            }
        }, RoutingStrategies.Bubble);
    }

    // v0.16.1: rede de segurança — garante que as configurações sejam
    // persistidas independente de COMO a janela é fechada (botão "Salvar
    // e fechar", botão nativo da titlebar, Alt+F4, ou "Back" do gamepad
    // com o foco na zona de Tabs). Antes, só o clique no botão "Salvar e
    // fechar" chamava SaveTo(); os outros caminhos de fechamento (em
    // especial o "Back" do gamepad, o jeito mais natural de sair num
    // handheld) fechavam a janela sem salvar nada, descartando qualquer
    // alteração feita — incluindo o toggle "Receber atualizações beta".
    // SaveTo() já é idempotente (reescreve os mesmos valores do
    // ViewModel em AppSettings e persiste em disco), então chamá-la de
    // novo aqui não tem efeito colateral nos caminhos que já salvavam.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ViewModel.SaveTo(_settings);
        base.OnClosing(e);
    }

    private void OnSettingsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox && e.Key is not Key.Escape) return;

        GamepadAction? action = e.Key switch
        {
            Key.Up => GamepadAction.MoveUp,
            Key.Down => GamepadAction.MoveDown,
            Key.Left => GamepadAction.MoveLeft,
            Key.Right => GamepadAction.MoveRight,
            Key.Enter or Key.Space => GamepadAction.Confirm,
            Key.Escape => GamepadAction.Back,
            Key.Q => GamepadAction.PreviousFilter,
            Key.E => GamepadAction.NextFilter,
            _ => null
        };

        if (action is { } mapped && HandleSettingsGamepadAction(mapped))
            e.Handled = true;
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

    public bool HandleSettingsGamepadAction(GamepadAction action)
    {
        if (_connectivityDialog is { IsVisible: true })
        {
            _connectivityDialog.HandleGamepadAction(action);
            return true;
        }

        if (_accountsWindow is { IsVisible: true } accounts)
        {
            DialogGamepadNavigator.Handle(accounts, action);
            return true;
        }

        if (action == GamepadAction.GameDetails)
        {
            ViewModel.SaveTo(_settings);
            Close();
            return true;
        }

        if (action == GamepadAction.Menu)
        {
            ViewModel.SaveTo(_settings);
            return true;
        }

        var tabs = GetTabButtons();
        if (tabs.Count == 0) return false;

        var content = GetContentFocusables();

        if (_focusZone == FocusZone.Content && _contentIndex >= 0 && _contentIndex < content.Count
            && content[_contentIndex] is ComboBox openCombo && openCombo.IsDropDownOpen)
        {
            switch (action)
            {
                case GamepadAction.MoveDown:
                    openCombo.SelectedIndex = Math.Min(openCombo.ItemCount - 1, Math.Max(0, openCombo.SelectedIndex + 1));
                    return true;
                case GamepadAction.MoveUp:
                    openCombo.SelectedIndex = Math.Max(0, openCombo.SelectedIndex - 1);
                    return true;
                case GamepadAction.Confirm:
                case GamepadAction.Back:
                    openCombo.IsDropDownOpen = false;
                    FocusControl(openCombo);
                    return true;
                case GamepadAction.MoveLeft:
                case GamepadAction.MoveRight:
                    return true;
            }
        }

        if (_focusZone == FocusZone.Tabs)
        {
            switch (action)
            {
                case GamepadAction.MoveUp:
                case GamepadAction.PreviousFilter:
                    _tabIndex = (_tabIndex - 1 + tabs.Count) % tabs.Count;
                    ViewModel.CategoryIndex = _tabIndex;
                    FocusControl(tabs[_tabIndex]);
                    return true;

                case GamepadAction.MoveDown:
                case GamepadAction.NextFilter:
                    _tabIndex = (_tabIndex + 1) % tabs.Count;
                    ViewModel.CategoryIndex = _tabIndex;
                    FocusControl(tabs[_tabIndex]);
                    return true;

                case GamepadAction.MoveRight:
                case GamepadAction.Confirm:
                    if (content.Count > 0)
                    {
                        _focusZone = FocusZone.Content;
                        _contentIndex = 0;
                        FocusControl(content[0]);
                    }
                    return true;

                case GamepadAction.Back:
                    Close();
                    return true;

                case GamepadAction.MoveLeft:
                    return true;
            }
        }
        else
        {
            switch (action)
            {
                case GamepadAction.MoveDown:
                    if (content.Count == 0) { _focusZone = FocusZone.Tabs; FocusControl(tabs[_tabIndex]); return true; }
                    _contentIndex = (_contentIndex + 1) % content.Count;
                    FocusControl(content[_contentIndex]);
                    return true;

                case GamepadAction.MoveUp:
                    if (content.Count == 0 || _contentIndex == 0)
                    {
                        _focusZone = FocusZone.Tabs;
                        FocusControl(tabs[_tabIndex]);
                        return true;
                    }
                    _contentIndex--;
                    FocusControl(content[_contentIndex]);
                    return true;

                case GamepadAction.MoveLeft:
                    if (TryChangeValue(content, _contentIndex, -1)) return true;
                    return true;

                case GamepadAction.MoveRight:
                    if (TryChangeValue(content, _contentIndex, +1)) return true;
                    return true;

                case GamepadAction.Confirm:
                    if (content.Count > 0) ActivateControl(content[_contentIndex]);
                    return true;

                case GamepadAction.Back:
                    _focusZone = FocusZone.Tabs;
                    FocusControl(tabs[_tabIndex]);
                    return true;

                case GamepadAction.PreviousFilter:
                    _tabIndex = (_tabIndex - 1 + tabs.Count) % tabs.Count;
                    ViewModel.CategoryIndex = _tabIndex;
                    _focusZone = FocusZone.Tabs;
                    _contentIndex = 0;
                    FocusControl(tabs[_tabIndex]);
                    return true;

                case GamepadAction.NextFilter:
                    _tabIndex = (_tabIndex + 1) % tabs.Count;
                    ViewModel.CategoryIndex = _tabIndex;
                    _focusZone = FocusZone.Tabs;
                    _contentIndex = 0;
                    FocusControl(tabs[_tabIndex]);
                    return true;
            }
        }

        return false;
    }

    private void FocusActiveTab()
    {
        var tabs = GetTabButtons();
        if (tabs.Count == 0) return;
        _tabIndex = Math.Clamp(ViewModel.CategoryIndex, 0, tabs.Count - 1);
        _focusZone = FocusZone.Tabs;
        FocusControl(tabs[_tabIndex]);
    }

    private void FocusControl(Control control)
    {
        control.Focus(NavigationMethod.Directional);
        control.BringIntoView();
    }

    private List<Button> GetTabButtons() => this.GetVisualDescendants()
        .OfType<Button>()
        .Where(b => b.Classes.Contains("categoryTab") && b.IsEffectivelyVisible)
        .ToList();

    private List<Control> GetContentFocusables()
    {
        var tabButtons = new HashSet<Control>(GetTabButtons().Cast<Control>());
        return this.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled && c != this && !tabButtons.Contains(c)
                        && c is ComboBox or Slider or CheckBox or RadioButton or TextBox or Button)
            .ToList();
    }

    private bool TryChangeValue(List<Control> content, int index, int direction)
    {
        if (index < 0 || index >= content.Count) return false;
        var control = content[index];

        if (control is ComboBox combo)
        {
            var count = combo.ItemCount;
            if (count == 0) return false;
            var current = combo.SelectedIndex < 0 ? 0 : combo.SelectedIndex;
            combo.SelectedIndex = Math.Clamp(current + direction, 0, count - 1);
            return true;
        }

        if (control is Slider slider)
        {
            var step = slider.TickFrequency > 0 ? slider.TickFrequency : (slider.Maximum - slider.Minimum) / 10;
            slider.Value = Math.Clamp(slider.Value + direction * step, slider.Minimum, slider.Maximum);
            return true;
        }

        if (control is RadioButton)
        {
            var siblings = content.OfType<RadioButton>().ToList();
            var idx = siblings.IndexOf((RadioButton)control);
            if (idx < 0) return false;
            var next = Math.Clamp(idx + direction, 0, siblings.Count - 1);
            siblings[next].IsChecked = true;
            FocusControl(siblings[next]);
            return true;
        }
        return false;
    }

    private static void ActivateControl(Control control)
    {
        switch (control)
        {
            case CheckBox cb: cb.IsChecked = !cb.IsChecked; break;
            case RadioButton rb: rb.IsChecked = true; break;
            case ComboBox combo: combo.IsDropDownOpen = !combo.IsDropDownOpen; break;
            case Slider sl:
                var step = sl.TickFrequency > 0 ? sl.TickFrequency : (sl.Maximum - sl.Minimum) / 10;
                sl.Value = sl.Value + step > sl.Maximum ? sl.Minimum : sl.Value + step;
                break;
            case TextBox: control.Focus(); break;
            case Button btn: btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); break;
        }
    }

    private async void OnManageAccountsClick(object? sender, RoutedEventArgs e)
    {
        var accounts = new AccountsWindow(ViewModel, OpenExternalUrlFromDialog);
        _accountsWindow = accounts;
        try { await accounts.ShowDialog(this); }
        finally { _accountsWindow = null; }
    }

    private void OpenExternalUrlFromDialog(string url)
    {
        Topmost = false;
        if (Owner is MainWindow mainWindow)
        {
            mainWindow.Topmost = false;
            mainWindow.OpenExternalUrl(url);
        }
        else
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OnCategoryTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var index))
        {
            ViewModel.CategoryIndex = index;
            _tabIndex = index;
            _focusZone = FocusZone.Tabs;
            _contentIndex = 0;
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
                FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Executável") { Patterns = new[] { "*.exe" } } }
            });

            if (files.Count > 0) ViewModel.EdenExecutablePath = files[0].Path.LocalPath;
        }
        catch (Exception ex) { DebugLog.Write($"[SETTINGS] Falha ao abrir file picker (eden): {ex.Message}"); }
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
                FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Executável") { Patterns = new[] { "*.exe" } } }
            });

            if (files.Count > 0) ViewModel.LosslessScalingPath = files[0].Path.LocalPath;
        }
        catch (Exception ex) { DebugLog.Write($"[SETTINGS] Falha ao abrir file picker (lossless): {ex.Message}"); }
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

        var existing = ViewModel.EdenRomFoldersText.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (existing.Contains(path, StringComparer.OrdinalIgnoreCase)) return;

        ViewModel.EdenRomFoldersText = existing.Length == 0 ? path : string.Join(Environment.NewLine, existing.Append(path));
    }

    private void OnTestEdenScanClick(object? sender, RoutedEventArgs e)
    {
        var folders = ViewModel.EdenRomFoldersText.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
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
        if (!report.EdenExecutableFound) return "O eden.exe não foi encontrado no caminho acima — sem ele nenhum jogo é listado.";
        
        var parts = new List<string> { report.Games == 1 ? "1 jogo encontrado" : $"{report.Games} jogos encontrados" };
        if (report.SkippedAddOns > 0) parts.Add(report.SkippedAddOns == 1 ? "1 arquivo ignorado (DLC ou atualização)" : $"{report.SkippedAddOns} arquivos ignorados (DLC ou atualização)");
        if (report.FoldersMissing > 0) parts.Add(report.FoldersMissing == 1 ? "1 pasta da lista não existe" : $"{report.FoldersMissing} pastas da lista não existem");
        if (report.UsedEdenConfigFallback) parts.Add(report.FoldersScanned > 0 ? "nenhuma pasta informada aqui, então foi usada a configuração do próprio Eden" : "nenhuma pasta informada aqui e a configuração do próprio Eden não tem pasta nenhuma");
        
        return string.Join(" · ", parts) + ".";
    }

    private void OnKeyboardClick(object? sender, RoutedEventArgs e) => TouchKeyboard.Toggle();

    private async void OnOpenWifiSettingsClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new ConnectivityPopupWindow { Topmost = true };
        _connectivityDialog = dialog;
        try { await dialog.ShowDialog(this); }
        finally { _connectivityDialog = null; }
    }

    private async void OnOpenBluetoothSettingsClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new ConnectivityPopupWindow { Topmost = true };
        _connectivityDialog = dialog;
        try { await dialog.ShowDialog(this); }
        finally { _connectivityDialog = null; }
    }

    private void OnOpenReleaseNotesClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Topmost = false;
            if (Owner is MainWindow mainWindow)
                mainWindow.OpenWindowsSettings("https://github.com/profcosta67/Handheld-Launcher/tree/main/release-notes");
            else
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/profcosta67/Handheld-Launcher/tree/main/release-notes") { UseShellExecute = true });
        }
        catch { }
    }

    private void OnSavedOkClick(object? sender, RoutedEventArgs e) => ViewModel.DismissSavedMessage();

    public Func<Task<string>>? CheckForUpdates { get; set; }

    private async void OnCheckUpdatesClick(object? sender, RoutedEventArgs e)
    {
        if (CheckForUpdates is null) return;
        ViewModel.UpdateResultText = "Verificando…";
        ViewModel.UpdateResultText = await CheckForUpdates();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnSaveAndCloseClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.SaveTo(_settings);
        Close();
    }
}