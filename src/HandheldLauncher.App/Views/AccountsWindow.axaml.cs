using Avalonia.Controls;
using Avalonia.Interactivity;
using HandheldLauncher.App.ViewModels;

namespace HandheldLauncher.App.Views;

/// <summary>
/// Diálogo "Gerenciar contas e chaves de API", aberto a partir de
/// SettingsWindow (botão 🔒). Ver comentário no .axaml pra decisão de
/// design (mesma ViewModel do caller, sem Save próprio, campos mascarados
/// por padrão).
/// </summary>
public partial class AccountsWindow : Window
{
    // Só pro designer/casos sem parâmetro — SettingsWindow sempre usa o
    // outro construtor, passando a MESMA ViewModel (não uma cópia).
    public AccountsWindow() : this(new SettingsViewModel())
    {
    }

    public AccountsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext!;

    private void OnUseSteamAccountClick(object? sender, RoutedEventArgs e) => ViewModel.UseDetectedSteamAccount();

    private void OnGetSteamKeyClick(object? sender, RoutedEventArgs e) => OpenUrl("https://steamcommunity.com/dev/apikey");

    private void OnGetGridDbKeyClick(object? sender, RoutedEventArgs e) => OpenUrl("https://www.steamgriddb.com/profile/preferences/api");

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Melhor esforço.
        }
    }

    // "Proteção de visualização" pedida pelo usuário: mascarado (•) por
    // padrão, 👁 alterna pra texto puro — só estado de UI, por isso vive
    // aqui e não na ViewModel (zera sempre que a tela reabre, de propósito).
    private static void ToggleMask(TextBox box) => box.PasswordChar = box.PasswordChar == default ? '•' : default;

    private void OnToggleGridDbVisibilityClick(object? sender, RoutedEventArgs e) => ToggleMask(GridDbKeyBox);

    private void OnToggleSteamWebVisibilityClick(object? sender, RoutedEventArgs e) => ToggleMask(SteamWebKeyBox);

    private void OnToggleSteamIdVisibilityClick(object? sender, RoutedEventArgs e) => ToggleMask(SteamIdBox);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
