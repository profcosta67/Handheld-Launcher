using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HandheldLauncher.App.Views;

/// <summary>
/// Modal "Detalhes do jogo" — v0.9.11, etapa 2. Diferente de
/// SettingsWindow/AddGameWindow, esta janela NÃO monta sua própria
/// ViewModel: quem abre (MainWindow.OpenGameDetailsDialogAsync) já define
/// DataContext com uma GameDetailsViewModel pronta pra ESTE jogo (ver
/// MainViewModel.CreateGameDetailsViewModel), porque só a MainViewModel
/// tem acesso aos clients Steam (ISteamOwnedGamesClient/
/// ISteamAchievementsClient) montados na composition root.
/// </summary>
public partial class GameDetailsWindow : Window
{
    public GameDetailsWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
