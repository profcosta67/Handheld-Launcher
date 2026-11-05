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

// Parte da MainWindow: abertura dos diálogos modais (Configurações, Adicionar
// Jogo, Editar Jogo, Detalhes, Atualização, Driver, Conectividade, etc.).
// Extraído de MainWindow.axaml.cs (v0.16) — nenhuma mudança de comportamento.
public partial class MainWindow : Window
{
    private void OnUpdateBadgeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { if (DataContext is MainViewModel vm) _ = OpenUpdateDialogAsync(vm); }

    private async Task OpenUpdateDialogAsync(MainViewModel vm)
    {
        if (!vm.IsUpdateReady) return;
        var dialog = new UpdateWindow(vm);
        _activeDialog = dialog;
        try { await dialog.ShowDialog(this); } finally { _activeDialog = null; }
    }

    private void OnInstallOwnedGameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OwnedSteamGame game } && DataContext is MainViewModel vm) vm.InstallOwnedGame(game);
    }

    public void HandleAddProgramClick() { if (DataContext is MainViewModel vm) _ = OpenAddProgramDialogAsync(vm); }

    private async Task OpenAddProgramDialogAsync(MainViewModel vm)
    {
        var dialog = new AddGameWindow(isApplication: true); _activeDialog = dialog; dialog.Topmost = true;
        GameEntry? result; try { result = await dialog.ShowDialog<GameEntry?>(this); } finally { _activeDialog = null; }
        if (result is null) return; vm.AddManualGame(result); vm.SetFilterApplicationsCommand.Execute(null);
    }

    private async Task OpenAddGameDialogAsync(MainViewModel vm)
    {
        var dialog = new AddGameWindow(); _activeDialog = dialog; dialog.Topmost = true;
        GameEntry? result; try { result = await dialog.ShowDialog<GameEntry?>(this); } finally { _activeDialog = null; }
        if (result is null) return; vm.AddManualGame(result);
    }

    private async Task OpenLayoutPickerAsync()
    {
        AppSettings settings;
        try { settings = AppSettingsStore.Load(); } catch (Exception ex) { DebugLog.Write($"OpenLayoutPicker: falhou ao ler settings: {ex.Message}"); return; }
        var dialog = new LayoutPickerWindow(settings.LayoutTheme); _activeDialog = dialog; dialog.Topmost = true;
        bool apply; try { apply = await dialog.ShowDialog<bool>(this); } finally { _activeDialog = null; }
        if (!apply || dialog.SelectedLayout == settings.LayoutTheme) return;
        settings.LayoutTheme = dialog.SelectedLayout;
        // v0.16.4: o layout novo traz o som e a fonte que combinam com ele.
        var (font, sound) = ThemeManager.LayoutDefaults(settings.LayoutTheme);
        settings.UiFont = font;
        settings.SoundTheme = sound;
        DebugLog.Write($"OpenLayoutPicker: layout {settings.LayoutTheme} → fonte '{ThemeManager.Fonts[font].Name}', som '{UiSounds.PackNames[sound]}'.");
        try { AppSettingsStore.Save(settings); } catch (Exception ex) { DebugLog.Write($"OpenLayoutPicker: falhou ao salvar: {ex.Message}"); return; }
        if (LauncherRestart.TryStartReplacement()) (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        else DebugLog.Write("OpenLayoutPicker: layout salvo, mas não deu pra reiniciar — vale na próxima abertura.");
    }

    private async Task OpenEditGameDialogAsync(MainViewModel vm, GameEntry game)
    {
        var dialog = new EditGameWindow(game.TitleOverride ?? game.Title, game.IsApplication); _activeDialog = dialog; dialog.Topmost = true;
        bool saved; try { saved = await dialog.ShowDialog<bool>(this); } finally { _activeDialog = null; }
        if (!saved) return; vm.ApplyEntryEdits(game, dialog.EditedTitle, dialog.IsApplication);
    }

    private async Task OpenSettingsDialogAsync(MainViewModel vm)
    {
        var dialog = new SettingsWindow();
        dialog.ViewModel.VersionText = vm.AppVersionText;
        dialog.ViewModel.UpdatesAvailable = vm.UpdatesInstalled;
        dialog.CheckForUpdates = vm.CheckForUpdatesWithResultAsync;
        dialog.Topmost = true;
        _activeDialog = dialog;
        try { await dialog.ShowDialog(this); }
        finally
        {
            _activeDialog = null;
            // A janela principal volta a ficar Topmost somente depois que o modal fecha.
            if (IsVisible && WindowState != WindowState.Minimized)
            {
                Topmost = true;
                _topmostEnforcerTimer?.Start();
            }
        }
        ApplyAppearance(vm, AppSettingsStore.Load());
        vm.RefreshArtworkState();
    }

    private async Task OpenGameDetailsDialogAsync(MainViewModel vm, GameEntry game)
    {
        var dialog = new GameDetailsWindow { DataContext = vm.CreateGameDetailsViewModel(game) };
        var settings = AppSettingsStore.Load();
        ThemeManager.ApplyFont(dialog, settings.UiFont); dialog.Topmost = true;
        _activeDialog = dialog;
        try { await dialog.ShowDialog(this); } finally { _activeDialog = null; }
    }

    private async Task OpenSystemUpdateDialogAsync()
    {
        var dialog = new SystemUpdateWindow(); _activeDialog = dialog; dialog.Topmost = true;
        try { await dialog.ShowDialog(this); } finally { _activeDialog = null; }
    }

    private async Task OpenDriverUpdateDialogAsync()
    {
        var dialog = new DriverUpdateWindow(); _activeDialog = dialog; dialog.Topmost = true;
        try { await dialog.ShowDialog(this); } finally { _activeDialog = null; }
    }

    // v0.16.2: Central de Ajuda (verificações de saúde). Mesmo padrão dos
    // outros diálogos: _activeDialog faz o OnGamepadTick mandar o controle
    // pro DialogGamepadNavigator enquanto ela está aberta.
    private async Task OpenHelpCenterDialogAsync()
    {
        var dialog = new HelpCenterWindow(); _activeDialog = dialog; dialog.Topmost = true;
        var wasTopmost = Topmost;
        // Explorer/navegador abertos pela Central não podem ficar escondidos
        // atrás do launcher "sempre por cima" (modo Handheld).
        dialog.ExternalAppOpening += () => { _topmostEnforcerTimer?.Stop(); Topmost = false; };
        try { await dialog.ShowDialog(this); }
        finally
        {
            _activeDialog = null;
            if (dialog.OpenedExternalApp && IsVisible) Topmost = wasTopmost;
        }
    }

    public void OpenWindowsSettings(string uri)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            // O launcher é normalmente Topmost. Rebaixa-o antes de abrir
            // as Configurações do Windows para não cobrir a janela externa.
            _externalSettingsOpen = true;
            Topmost = false;
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle != IntPtr.Zero)
                SetWindowPos(handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
            DebugLog.Write($"[CONNECTIVITY] Configurações externas solicitadas: {uri}");
        }
        catch (Exception ex)
        {
            _externalSettingsOpen = false;
            DebugLog.Write($"[CONNECTIVITY] Não foi possível abrir {uri}: {ex.Message}");
            Topmost = true;
        }
    }

    public async Task OpenConnectivityPopup()
    {
        var dialog = new ConnectivityPopupWindow { Topmost = true };
        _activeDialog = dialog;
        try { await dialog.ShowDialog(this); }
        finally { _activeDialog = null; }
    }

    private void OnOpenWifiSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _ = OpenConnectivityPopup();

    private void OnOpenBluetoothSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _ = OpenConnectivityPopup();

}
