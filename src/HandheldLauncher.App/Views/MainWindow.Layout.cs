using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Input;
using HandheldLauncher.App.Platform;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.App.Views.Layouts;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Views;

// Parte da MainWindow: tema/layout da biblioteca, aparência (fonte, animações,
// tile size) e os cliques dos botões da topbar/menu.
// Extraído de MainWindow.axaml.cs (v0.16) — nenhuma mudança de comportamento.
public partial class MainWindow : Window
{
    private bool AnimationsEnabled => !Classes.Contains("noAnim");

    private void FadeIn(Control control, int milliseconds)
    {
        if (!AnimationsEnabled) { control.Transitions = null; control.Opacity = 1; return; }
        control.Transitions = null; control.Opacity = 0;
        Dispatcher.UIThread.Post(() =>
        {
            control.Transitions = new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(milliseconds), Easing = new Avalonia.Animation.Easings.CubicEaseOut() } };
            control.Opacity = 1;
        }, DispatcherPriority.Background);
    }

    private void OnHeroImageChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Image.SourceProperty) return;
        if (!AnimationsEnabled || e.OldValue is null || e.NewValue is null) { HeroBackImage.Source = null; HeroImage.Transitions = null; HeroImage.Opacity = 1; return; }
        HeroBackImage.Source = e.OldValue as Avalonia.Media.IImage;
        FadeIn(HeroImage, 260);
    }

    private void OnContextMenuItemPressed(object? sender, PointerPressedEventArgs e) { if (DataContext is MainViewModel vm && sender is Control { DataContext: ContextMenuItemViewModel item }) { vm.ActivateContextMenuItem(item); e.Handled = true; } }

    private void OnGlobalMenuItemPressed(object? sender, PointerPressedEventArgs e) { if (DataContext is MainViewModel vm && sender is Control { DataContext: ContextMenuItemViewModel item }) { vm.ActivateGlobalMenuItem(item); e.Handled = true; } }

    private void OnCoverCandidatePressed(object? sender, PointerPressedEventArgs e) { if (DataContext is MainViewModel vm && sender is Control { DataContext: ArtworkCandidateViewModel candidate }) { vm.ActivateCoverCandidate(candidate); e.Handled = true; } }

    private void OnMenuOverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.IsCoverPickerOpen) vm.CloseCoverPicker();
        else if (vm.IsContextMenuOpen) vm.CloseContextMenu();
        else if (vm.IsGlobalMenuOpen) vm.CloseGlobalMenu();
        else if (vm.IsDownloadsOpen) vm.CloseDownloads();
        e.Handled = true;
    }

    private void OnAboutOverlayPressed(object? sender, PointerPressedEventArgs e) => (DataContext as MainViewModel)?.CloseAbout();

    private void OnOpenSteamDownloadsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.OpenSteamDownloads();

    private void OnShortcutsHelpClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.OpenShortcutsHelp();

    private void OnShortcutsOverlayPressed(object? sender, PointerPressedEventArgs e) => (DataContext as MainViewModel)?.CloseShortcutsHelp();

    private void OnShortcutsCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.CloseShortcutsHelp();

    private bool _verticalLayout;

    private bool _layoutInitialized;

    private ITemplate<Panel?>? _horizontalPanelTemplate;

    private Transitions? _rowScrollTransitions;

    private Thickness ContainerMargin() => _verticalLayout ? new Thickness(0, 0, 18, 24) : default;

    private void ApplyAppearance(MainViewModel vm, AppSettings settings)
    {
        try
        {
            ThemeManager.Apply(settings.Theme);
            ThemeManager.ApplyFont(this, settings.UiFont);
            ThemeManager.ApplyFontSize(settings.UiFontSize);
            Classes.Set("noAnim", !settings.Animations);
            // v0.18.1: sempre a fileira do layout (não a grade da Nuvem, que pode
            // estar ativa agora e tem as próprias Transitions).
            _rowScrollTransitions ??= _libraryLayout.GameRowScrollViewer.Transitions;
            _libraryLayout.GameRowScrollViewer.Transitions = settings.Animations ? _rowScrollTransitions : null;
            UiSounds.Enabled = settings.SoundEffects;
            UiSounds.Pack = settings.SoundTheme;
            UiSounds.Volume = settings.SoundVolume;
            if (OperatingSystem.IsWindows()) DisplayColorManager.Apply(settings.ColorMode);
            var scale = GameTileViewModel.ScaleForSetting(settings.TileSize);
            var scaleChanged = Math.Abs(scale - GameTileViewModel.SizeScale) > 0.001;
            GameTileViewModel.SizeScale = scale;
            if (!_layoutInitialized || settings.VerticalLayout != _verticalLayout) { SetLayout(settings.VerticalLayout); _layoutInitialized = true; }
            if (scaleChanged) vm.ReloadTiles();
            ScrollGameRowSelectionIntoView(vm);
        }
        catch (Exception ex) { DebugLog.Write($"ApplyAppearance EXCEPTION {ex}"); }
    }

    private void ApplyLayoutTheme()
    {
        UserControl layout;
        var layoutTheme = 0;
        try
        {
            var settings = AppSettingsStore.Load();
            layoutTheme = settings.LayoutTheme;
            layout = settings.LayoutTheme switch
            {
                2 => new Layouts.BigScreenLibraryLayout(),
                1 => new Layouts.SwitchLibraryLayout(), // índice legado mantido para instalações existentes (agora chamado Clean)
                _ => new Layouts.ClassicLibraryLayout()
            };
        }
        catch (Exception ex) { DebugLog.Write($"ApplyLayoutTheme: falhou ao ler settings, usando Clássico. {ex}"); layout = new Layouts.ClassicLibraryLayout(); }
        _libraryLayout = (ILibraryLayout)layout;
        // BigScreen has its own compact status header; hide the generic
        // application bar there to avoid duplicating the clock/battery row.
        TopBar.IsVisible = layoutTheme != 2;
        GamesItemsControl = _libraryLayout.GamesItemsControl;
        GameRowScrollViewer = _libraryLayout.GameRowScrollViewer;
        HeroImage = _libraryLayout.HeroImage;
        HeroBackImage = _libraryLayout.HeroBackImage;
        LibraryLayoutHost.Content = layout;
    }

    private void SetLayout(bool vertical)
    {
        if (vertical && !_libraryLayout.SupportsVerticalGrid) return;
        _verticalLayout = vertical;
        // v0.18.1: mexe SEMPRE na fileira do layout — com a aba Nuvem aberta, os
        // campos GamesItemsControl/GameRowScrollViewer apontam pra grade dela.
        var items = _libraryLayout.GamesItemsControl;
        var scroll = _libraryLayout.GameRowScrollViewer;
        _horizontalPanelTemplate ??= items.ItemsPanel;
        items.ItemsPanel = vertical ? new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Orientation.Horizontal }) : _horizontalPanelTemplate;
        scroll.HorizontalScrollBarVisibility = vertical || !_libraryLayout.SupportsVerticalGrid ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;
        scroll.VerticalScrollBarVisibility = vertical ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        scroll.Offset = default;
        foreach (var container in items.GetRealizedContainers()) container.Margin = ContainerMargin();
    }

    // ---- v0.18.1: grade vertical da aba Nuvem -------------------------------------
    // Pedido do usuário: SÓ na aba Nuvem, em todos os layouts, os jogos em
    // blocos numa tela só com rolagem vertical. A grade (Layouts/CloudGridView)
    // existe nos 3 layouts; aqui a MainWindow troca qual lista é "a lista" —
    // assim navegação ↑/↓/←/→ (MoveSelectionVertical) e rolagem que acompanha a
    // seleção (ScrollSelectionIntoViewNow) usam a grade, igual ao modo grade
    // vertical que já existia. Só uma das duas listas recebe os jogos por vez
    // (a outra fica com ItemsSource nulo), pra não montar ~560 blocos em dobro.
    private bool _cloudGridActive;

    private bool IsGridNav => _verticalLayout || _cloudGridActive;

    // v0.18.3: busca da aba Nuvem.
    private bool IsCloudSearchFocused()
    {
        try { return _libraryLayout.CloudGrid.Search.IsKeyboardFocusWithin; }
        catch { return false; }
    }

    private void BeginCloudSearch()
    {
        try
        {
            _libraryLayout.CloudGrid.Search.Focus();
            TouchKeyboard.Show();
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
    }

    private void EndCloudSearch()
    {
        try
        {
            // A grade é focável só pra isso: tirar o foco do campo de texto.
            _libraryLayout.CloudGrid.Items.Focus();
            TouchKeyboard.Hide();
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
    }

    private void UpdateCloudGrid(MainViewModel vm)
    {
        try
        {
            var active = vm.IsFilterCloudActive;
            if (active == _cloudGridActive) return;
            var cloud = _libraryLayout.CloudGrid;
            if (active)
            {
                _libraryLayout.GamesItemsControl.ItemsSource = null;
                cloud.Items.ItemsSource = vm.Games;
                GamesItemsControl = cloud.Items;
                GameRowScrollViewer = cloud.Scroll;
                cloud.Scroll.Offset = default;
            }
            else
            {
                vm.LeaveCloudLetterBar();
                if (IsCloudSearchFocused()) EndCloudSearch();
                cloud.Items.ItemsSource = null;
                _libraryLayout.GamesItemsControl.ItemsSource = vm.Games;
                GamesItemsControl = _libraryLayout.GamesItemsControl;
                GameRowScrollViewer = _libraryLayout.GameRowScrollViewer;
            }
            _cloudGridActive = active;
            DebugLog.Write($"[NUVEM] Grade vertical {(active ? "ligada" : "desligada")}.");
            ScrollGameRowSelectionIntoView(vm);
        }
        catch (Exception ex) { DebugLog.Write($"[NUVEM] Troca de grade falhou: {ex.Message}"); }
    }

    private void OnGearMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.ToggleGlobalMenu();

    private void OnDownloadsShortcutClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.ToggleDownloads();

    private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => MinimizeToTray();

    private void OnPowerMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.TogglePowerMenu();

    private void OnCloseGameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.CloseRunningGame();

    private void OnCloseDownloadsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.CloseDownloads();

}
