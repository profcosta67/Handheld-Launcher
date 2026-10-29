using Avalonia.Controls;
using Avalonia.Interactivity;
using HandheldLauncher.App.Platform;
using HandheldLauncher.App.ViewModels;

namespace HandheldLauncher.App.Views;

/// <summary>
/// Diálogo "Atualização disponível" (v0.9.6), aberto a partir de
/// MainWindow.OnUpdateBadgeClick. Mesma MainViewModel do caller (não uma
/// cópia) — "Atualizar e reiniciar" chama ViewModel.ApplyUpdate() direto,
/// igual o antigo toque na barra de cima fazia.
/// </summary>
public partial class UpdateWindow : Window
{
    // Sem construtor sem parâmetro de propósito: diferente de
    // SettingsViewModel (AccountsWindow), MainViewModel não tem um —
    // precisa de ILibraryService/IGameLauncher/IArtworkService/etc. de
    // verdade (ver MainViewModel.MainViewModel) — e inventar um fake aqui
    // só pro designer arriscaria mais bug do que vale.
    public UpdateWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext!;

    private void OnOpenGitHubClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                $"{UpdateService.RepoUrl}/releases") { UseShellExecute = true });
        }
        catch
        {
            // Melhor esforço.
        }
    }

    private void OnLaterClick(object? sender, RoutedEventArgs e) => Close();

    private void OnUpdateNowClick(object? sender, RoutedEventArgs e)
    {
        // ApplyUpdate() fecha o launcher (ver MainViewModel) — fechar esta
        // janela primeiro evita um Window órfão caso o restart demore um
        // instante pra efetivar.
        Close();
        ViewModel.ApplyUpdate();
    }
}
