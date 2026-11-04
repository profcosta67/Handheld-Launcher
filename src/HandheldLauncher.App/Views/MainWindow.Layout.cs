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
            _rowScrollTransitions ??= GameRowScrollViewer.Transitions;
            GameRowScrollViewer.Transitions = settings.Animations ? _rowScrollTransitions : null;
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
        _horizontalPanelTemplate ??= GamesItemsControl.ItemsPanel;
        GamesItemsControl.ItemsPanel = vertical ? new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Orientation.Horizontal }) : _horizontalPanelTemplate;
        GameRowScrollViewer.HorizontalScrollBarVisibility = vertical || !_libraryLayout.SupportsVerticalGrid ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;
        GameRowScrollViewer.VerticalScrollBarVisibility = vertical ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        GameRowScrollViewer.Offset = default;
        foreach (var container in GamesItemsControl.GetRealizedContainers()) container.Margin = ContainerMargin();
    }

    private void OnGearMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.ToggleGlobalMenu();

    private void OnDownloadsShortcutClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.ToggleDownloads();

    private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => MinimizeToTray();

    private void OnPowerMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.TogglePowerMenu();

    private void OnCloseGameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.CloseRunningGame();

    private void OnCloseDownloadsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as MainViewModel)?.CloseDownloads();

}
