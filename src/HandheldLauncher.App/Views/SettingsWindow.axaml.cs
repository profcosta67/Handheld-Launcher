using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Input;
using HandheldLauncher.App.Platform;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Views;

public partial class SettingsWindow : Window, IGamepadKeyboardHost
{
    public SettingsViewModel ViewModel { get; }

    private readonly AppSettings _settings;
    private enum FocusZone { Tabs, Content }
    private FocusZone _focusZone = FocusZone.Tabs;
    private int _tabIndex = 0;
    private int _contentIndex = 0;
    private AccountsWindow? _accountsWindow;

    // 🟢 Construtor vazio mantido para compatibilidade
    public SettingsWindow() : this(new SettingsViewModel())
    {
    }

    // 🟢 Construtor novo que aceita o ViewModel (usado pelo MainWindow.Dialogs.cs)
    public SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        _settings = AppSettingsStore.Load();
        ViewModel.LoadFrom(_settings);
        ThemeManager.ApplyFont(this, _settings.UiFont);

        Opened += async (_, _) => 
        { 
            EnterConsoleLayoutIfHandheld();
            if (!_consoleLayout) FitToScreen();
            FocusActiveTab(); 
            await ViewModel.LoadReleaseNotesAsync();
        };

        AddHandler(KeyDownEvent, OnSettingsKeyDown, RoutingStrategies.Tunnel);

        // v0.24.2: qualquer campo de texto que receba foco abre o NOSSO teclado
        // (antes só em toque, e era o TabTip — inalcançável pelo controle).
        AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox box && !ReferenceEquals(Keyboard.Target, box))
                Keyboard.Show(box);
            if (_consoleLayout && e.Source is Visual focused) UpdateHelp(focused);
        }, RoutingStrategies.Bubble);

        // ⏎ ou "fechar" só escondem o teclado; a janela continua aberta.
        Keyboard.SubmitRequested += (_, _) => Keyboard.Hide();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.SaveTo(_settings);
        base.OnClosing(e);
    }

    // O WebView só recebe uma URL quando a tela do Syncthing é realmente aberta:
    // assim nenhum processo do motor web nasce ao abrir as Configurações.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsSyncthingOpen) && ViewModel.IsSyncthingOpen)
        {
            SyncthingView.Navigate(HandheldLauncher.Services.Syncthing.SyncthingService.BaseUrl);
            return;
        }

        // Entrar na aba "Central de Saves" (por toque, teclado ou D-Pad) abre a
        // central direto, que é o comportamento pedido para essa aba.
        if (e.PropertyName == nameof(SettingsViewModel.CategoryIndex)
            && ViewModel.CategoryIndex == SettingsViewModel.SaveCenterCategoryIndex)
        {
            ViewModel.OpenSyncthingCommand.Execute(null);
        }
    }

    private void OnSettingsKeyDown(object? sender, KeyEventArgs e)
    {
        // Com a tela do Syncthing aberta, o teclado/D-Pad pertence ao navegador:
        // só o Escape volta para as Configurações.
        if (ViewModel.IsSyncthingOpen)
        {
            if (e.Key == Key.Escape)
            {
                ViewModel.CloseSyncthingCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }

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
        catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
    }

    public bool HandleSettingsGamepadAction(GamepadAction action)
    {
        // v0.24.2: o teclado na tela tem prioridade sobre abas e campos.
        if (HandleKeyboardGamepadAction(action)) return true;

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

        // v0.25.9: lista de um ComboBox aberta pelo controle. ↑/↓ só movem o destaque; o valor (e,
        // no caso do tema, as cores da janela inteira) só muda ao confirmar com A. Antes cada passo
        // aplicava o tema ao vivo: as cores/ThemeVariant mudavam por baixo da lista, ela fechava
        // e o direcional passava a navegar pelos controles — o tema "seguia o joystick" sem travar.
        // A referência do ComboBox é guardada (não é mais procurada pelo índice na lista de
        // controles, que muda quando a janela é reestilizada).
        if (_activeCombo is not { IsDropDownOpen: true }
            && _focusZone == FocusZone.Content && _contentIndex >= 0 && _contentIndex < content.Count
            && content[_contentIndex] is ComboBox adopted && adopted.IsDropDownOpen)
        {
            BeginComboPick(adopted); // aberto pelo mouse/toque: adota
        }

        if (_activeCombo is { IsDropDownOpen: true } openCombo)
        {
            switch (action)
            {
                case GamepadAction.MoveDown:
                    MoveComboPick(openCombo, +1);
                    return true;
                case GamepadAction.MoveUp:
                    MoveComboPick(openCombo, -1);
                    return true;
                case GamepadAction.Confirm:
                    EndComboPick(openCombo, commit: true);
                    return true;
                case GamepadAction.Back:
                    EndComboPick(openCombo, commit: false);
                    return true;
                case GamepadAction.MoveLeft:
                case GamepadAction.MoveRight:
                    return true;
            }
        }
        else
        {
            _activeCombo = null;
        }

        if (_focusZone == FocusZone.Tabs)
        {
            switch (action)
            {
                case GamepadAction.MoveUp:
                case GamepadAction.PreviousFilter:
                    _tabIndex = (_tabIndex - 1 + tabs.Count) % tabs.Count;
                    ViewModel.CategoryIndex = TabCategory(tabs[_tabIndex]);
                    FocusControl(tabs[_tabIndex]);
                    return true;

                case GamepadAction.MoveDown:
                case GamepadAction.NextFilter:
                    _tabIndex = (_tabIndex + 1) % tabs.Count;
                    ViewModel.CategoryIndex = TabCategory(tabs[_tabIndex]);
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
                    if (content.Count > 0 && content[_contentIndex] is ComboBox toOpen)
                    {
                        toOpen.IsDropDownOpen = true;
                        BeginComboPick(toOpen);
                    }
                    else if (content.Count > 0) ActivateControl(content[_contentIndex]);
                    return true;

                case GamepadAction.Back:
                    _focusZone = FocusZone.Tabs;
                    FocusControl(tabs[_tabIndex]);
                    return true;

                case GamepadAction.PreviousFilter:
                    _tabIndex = (_tabIndex - 1 + tabs.Count) % tabs.Count;
                    ViewModel.CategoryIndex = TabCategory(tabs[_tabIndex]);
                    _focusZone = FocusZone.Tabs;
                    _contentIndex = 0;
                    FocusControl(tabs[_tabIndex]);
                    return true;

                case GamepadAction.NextFilter:
                    _tabIndex = (_tabIndex + 1) % tabs.Count;
                    ViewModel.CategoryIndex = TabCategory(tabs[_tabIndex]);
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
        _tabIndex = TabPositionOf(tabs, ViewModel.CategoryIndex);
        _focusZone = FocusZone.Tabs;
        FocusControl(tabs[_tabIndex]);
    }

    // v0.26: as abas estão agrupadas na tela (Sistema, Aparência, Jogos), então a posição
    // na lista não é mais o índice da categoria — o índice vem do Tag do botão.
    private static int TabCategory(Button tab) => tab.Tag is string tag && int.TryParse(tag, out var i) ? i : 0;

    private static int TabPositionOf(List<Button> tabs, int category)
    {
        var position = tabs.FindIndex(b => TabCategory(b) == category);
        return position < 0 ? 0 : position;
    }

    // ---- v0.26: layout de console (modo handheld) --------------------------------
    // Tela cheia sem a moldura do Windows, e a explicação aparece só para a opção em
    // foco, no painel da direita. Em "Uso em PC" a janela continua como era.
    private bool _consoleLayout;
    private readonly Dictionary<Control, (TextBlock? Title, List<TextBlock> Text)> _help = new();

    private void EnterConsoleLayoutIfHandheld()
    {
        try
        {
            if (Owner is not Window { WindowState: WindowState.FullScreen }) return;
            SystemDecorations = SystemDecorations.None;
            WindowState = WindowState.FullScreen;
            _consoleLayout = true;
            // Opções encostadas na navegação (antes ficavam centralizadas, com um vão no meio).
            ContentHost.MaxWidth = double.PositiveInfinity;
            ContentHost.Width = 860;
            ContentHost.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            ContentHost.Margin = new Thickness(28, 0, 0, 0);
            AdoptDescriptions();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[CONFIG] Layout de console não aplicado: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Liga cada opção ao seu rótulo e às explicações vizinhas, e tira as explicações da
    /// lista (passam a aparecer no painel da direita). Explicações com visibilidade
    /// própria (ligada a uma condição) ficam onde estão.
    /// </summary>
    private void AdoptDescriptions()
    {
        var adopted = new HashSet<TextBlock>();
        foreach (var panel in ContentHost.GetLogicalDescendants().OfType<Panel>().ToList())
        {
            var kids = panel.Children;
            for (var i = 0; i < kids.Count; i++)
            {
                if (kids[i] is not (ComboBox or ToggleSwitch or Slider or TextBox)) continue;
                TextBlock? title = null;
                var text = new List<TextBlock>();

                for (var j = i - 1; j >= 0 && kids[j] is TextBlock before && !adopted.Contains(before); j--)
                {
                    if (!before.Classes.Contains("subtle")) { title = before; break; }
                    if (before.IsSet(IsVisibleProperty)) break;
                    text.Insert(0, before);
                }
                for (var j = i + 1; j < kids.Count && kids[j] is TextBlock after && after.Classes.Contains("subtle"); j++)
                {
                    if (after.IsSet(IsVisibleProperty)) break;
                    text.Add(after);
                }

                foreach (var t in text) { adopted.Add(t); t.IsVisible = false; }
                _help[(Control)kids[i]] = (title, text);
            }
        }
    }

    private void UpdateHelp(Visual focused)
    {
        try
        {
            string title = "", text = "";
            for (Visual? v = focused; v is not null && !ReferenceEquals(v, this); v = v.GetVisualParent())
            {
                if (v is Control c && _help.TryGetValue(c, out var entry))
                {
                    title = entry.Title?.Text ?? "";
                    text = string.Join("\n\n", entry.Text.Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
                    break;
                }
                if (v is RadioButton radio)
                {
                    var lines = radio.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
                    title = lines.Count > 0 ? lines[0] : radio.Content as string ?? "";
                    text = string.Join("\n\n", lines.Skip(1));
                    break;
                }
                if (v is Button button)
                {
                    title = button.Content as string
                            ?? button.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault()?.Text ?? "";
                    text = button.Classes.Contains("categoryTab") ? "Aperte A ou direita para entrar nesta categoria." : "";
                    break;
                }
            }

            HelpTitle.Text = title;
            HelpText.Text = text;
            HelpText.IsVisible = text.Length > 0;
            HelpPane.IsVisible = title.Length > 0 || text.Length > 0;
        }
        catch (Exception ex)
        {
            DebugLog.Swallowed(ex);
        }
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
                        && c is ComboBox or Slider or ToggleSwitch or RadioButton or TextBox or Button)
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
            // v0.25.10: só o grupo do próprio botão (a página tem "Modo" e o modo do QS).
            var group = ((RadioButton)control).GroupName;
            var siblings = content.OfType<RadioButton>().Where(r => r.GroupName == group).ToList();
            var idx = siblings.IndexOf((RadioButton)control);
            if (idx < 0) return false;
            var next = Math.Clamp(idx + direction, 0, siblings.Count - 1);
            siblings[next].IsChecked = true;
            FocusControl(siblings[next]);
            return true;
        }

        if (control is ToggleSwitch ts)
        {
            ts.IsChecked = !ts.IsChecked;
            return true;
        }

        return false;
    }

    private ComboBox? _activeCombo;
    private int _pendingComboIndex;

    private void BeginComboPick(ComboBox combo)
    {
        _activeCombo = combo;
        _pendingComboIndex = Math.Max(0, combo.SelectedIndex);
        HighlightComboPick(combo);
    }

    private void MoveComboPick(ComboBox combo, int delta)
    {
        if (combo.ItemCount == 0) return;
        _pendingComboIndex = Math.Clamp(_pendingComboIndex + delta, 0, combo.ItemCount - 1);
        if (!HighlightComboPick(combo))
            combo.SelectedIndex = _pendingComboIndex; // sem contêiner para destacar: cai no modo ao vivo
    }

    private void EndComboPick(ComboBox combo, bool commit)
    {
        ClearComboHighlight(combo);
        if (commit && combo.ItemCount > 0) combo.SelectedIndex = _pendingComboIndex;
        combo.IsDropDownOpen = false;
        _activeCombo = null;
        FocusControl(combo);
    }

    /// <summary>Destaca o item pendente. Devolve false se a lista ainda não criou os itens.</summary>
    private bool HighlightComboPick(ComboBox combo)
    {
        var highlighted = false;
        IBrush brush = this.TryFindResource("AccentBrush", ActualThemeVariant, out var res) && res is IBrush b
            ? b
            : Brushes.DodgerBlue;

        for (var i = 0; i < combo.ItemCount; i++)
        {
            if (combo.ContainerFromIndex(i) is not ComboBoxItem item) continue;
            if (i == _pendingComboIndex)
            {
                item.Background = brush;
                item.BringIntoView();
                highlighted = true;
            }
            else item.ClearValue(TemplatedControl.BackgroundProperty);
        }

        return highlighted;
    }

    private static void ClearComboHighlight(ComboBox combo)
    {
        for (var i = 0; i < combo.ItemCount; i++)
            if (combo.ContainerFromIndex(i) is ComboBoxItem item) item.ClearValue(TemplatedControl.BackgroundProperty);
    }

    private static void ActivateControl(Control control)
    {
        switch (control)
        {
            case ToggleSwitch ts: ts.IsChecked = !ts.IsChecked; break;
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
            // "Central de Saves": a troca de aba já abre o WebView (ver
            // OnViewModelPropertyChanged); re-clicar na aba ativa reabre depois
            // de um "Voltar", quando o índice não muda.
            var wasAlreadyHere = ViewModel.CategoryIndex == index;

            ViewModel.CategoryIndex = index;
            _tabIndex = TabPositionOf(GetTabButtons(), index);
            _focusZone = FocusZone.Tabs;
            _contentIndex = 0;

            if (wasAlreadyHere && index == SettingsViewModel.SaveCenterCategoryIndex)
                ViewModel.OpenSyncthingCommand.Execute(null);
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

    /// <summary>
    /// v0.24.2: o botão "⌨ Teclado" abre o NOSSO teclado no campo focado.
    /// Sem campo de texto focado não haveria onde escrever, por isso aí abre-se
    /// o do Windows (TabTip), como antes.
    /// </summary>
    private void OnKeyboardClick(object? sender, RoutedEventArgs e)
    {
        if (Keyboard.IsOpen)
        {
            Keyboard.Hide();
            return;
        }

        if (FocusManager?.GetFocusedElement() is TextBox box)
        {
            Keyboard.Show(box);
            return;
        }

        TouchKeyboard.Toggle();
    }

    /// <summary>Com o teclado aberto, o controle é dele (D-Pad move, A escreve, B esconde).</summary>
    public bool HandleKeyboardGamepadAction(GamepadAction action)
        => Keyboard.IsOpen && Keyboard.HandleGamepadAction(action);

    /// <summary>v0.24.2: A sobre um campo de texto abre o teclado do launcher.</summary>
    public bool ShowKeyboardFor(TextBox target)
    {
        Keyboard.Show(target);
        target.Focus();
        return true;
    }

    /// <summary>
    /// v0.24.1: congelamento opt-in. Ligar mostra o aviso de risco; desligar é
    /// imediato (não há nada a confirmar).
    /// </summary>
    private void OnFreezeClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.FreezeGameOnOverlay)
        {
            ViewModel.FreezeGameOnOverlay = false;
            DebugLog.Write("[CONGELAR] Desligado pelo utilizador.");
            return;
        }

        ViewModel.IsFreezeConfirmOpen = true;
    }

    private void OnFreezeAcceptClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.FreezeGameOnOverlay = true;
        ViewModel.IsFreezeConfirmOpen = false;
        DebugLog.Write("[CONGELAR] Ativado pelo utilizador depois de aceitar o aviso de risco.");
    }

    private void OnFreezeCancelClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.IsFreezeConfirmOpen = false;
        DebugLog.Write("[CONGELAR] Aviso de risco cancelado; modo do QS mantido.");
    }

    /// <summary>
    /// com o driver do HidHide (e o TDP) a funcionar de verdade. O reinício usa
    /// <c>--relaunch-after</c>, então a instância elevada espera esta morrer
    /// antes de disputar o mutex de instância única (ver LauncherRestart).
    /// Se o UAC for cancelado, TryStartReplacementAsAdmin devolve false e o
    /// launcher continua aberto — nada de ficar sem janela.
    /// </summary>
    private void OnRestartAsAdminClick(object? sender, RoutedEventArgs e)
    {
        if (!LauncherRestart.TryStartReplacementAsAdmin()) return;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

      private void OnOpenSyncthingWebUiClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            // 1. Rebaixa o SettingsWindow
            Topmost = false;

            // 2. Rebaixa o MainWindow (dono deste modal)
            if (Owner is MainWindow mainWindow)
            {
                mainWindow.Topmost = false;
            }

            // 3. Abre o navegador (vai ficar por cima agora)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo 
            { 
                FileName = "http://localhost:8384", 
                UseShellExecute = true 
            });
        }
        catch { }
    }

    // Aba Rede: uma implementação só — a Central de Controlo da janela principal.
    // O ConnectivityPopupWindow antigo (lista BT via PowerShell) ficou legacy.
    private void OnOpenWifiSettingsClick(object? sender, RoutedEventArgs e) => OpenControlCenter();

    private void OnOpenBluetoothSettingsClick(object? sender, RoutedEventArgs e) => OpenControlCenter();

    private void OpenControlCenter()
    {
        if (Owner is not MainWindow main) return;

        Close();                     // fecha as Configurações…
        main.ToggleControlCenter();  // …e abre a Central de Controlo
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