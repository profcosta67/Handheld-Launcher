using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.Services.Syncthing;

namespace HandheldLauncher.App.Views;

/// <summary>
/// Tela cheia com a interface web do Syncthing (127.0.0.1:8384) embutida no
/// launcher — o usuário escolhe as pastas de saves a sincronizar sem sair do app.
/// </summary>
public partial class SyncthingSettingsView : UserControl
{
    public SyncthingSettingsView() => InitializeComponent();

    /// <summary>
    /// Aponta o WebView para a GUI local. Chamado só quando a view fica visível:
    /// assim o processo do motor web não nasce em toda abertura de Configurações.
    /// </summary>
    public void Navigate(string url)
    {
        try
        {
            Browser.Source = new Uri(url);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Syncthing] Não foi possível abrir o WebView: {ex.Message}");
        }
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.CloseSyncthingCommand.Execute(null);
        }
    }

    // Toggle do topo: ligar sobe o processo e recarrega a página; desligar para
    // o processo (o WebView fica a mostrar o erro de ligação até religar).
    private async void OnSyncToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        var enabled = SyncToggle.IsChecked == true;
        await vm.SetSyncthingEnabledAsync(enabled);
        if (enabled) Navigate(SyncthingService.BaseUrl);
    }
}
