using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using HandheldLauncher.App.Input;
using HandheldLauncher.App.ViewModels;

namespace HandheldLauncher.App.Views;

public partial class AccountsWindow : Window, IGamepadKeyboardHost
{
    private readonly Action<string>? _openExternalUrl;

    public AccountsWindow() : this(new SettingsViewModel())
    {
    }

    public AccountsWindow(SettingsViewModel viewModel, Action<string>? openExternalUrl = null)
    {
        InitializeComponent();
        DataContext = viewModel;
        _openExternalUrl = openExternalUrl;
        Opened += (_, _) => GridDbKeyBox.Focus(NavigationMethod.Directional);

        AddHandler(GotFocusEvent, (_, e) =>
        {
            // v0.24.2: abre o teclado do launcher (o TabTip não recebia o controle).
            if (e.Source is TextBox box && !ReferenceEquals(Keyboard.Target, box))
                Keyboard.Show(box);
        }, RoutingStrategies.Bubble);

        Keyboard.SubmitRequested += (_, _) => Keyboard.Hide();
    }

    /// <summary>Com o teclado aberto, o controle é dele.</summary>
    public bool HandleKeyboardGamepadAction(GamepadAction action)
        => Keyboard.IsOpen && Keyboard.HandleGamepadAction(action);

    /// <summary>A sobre um campo de texto abre o teclado do launcher.</summary>
    public bool ShowKeyboardFor(TextBox target)
    {
        Keyboard.Show(target);
        target.Focus();
        return true;
    }

    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext!;

    private void OnUseSteamAccountClick(object? sender, RoutedEventArgs e) => ViewModel.UseDetectedSteamAccount();

    private void OnGetSteamKeyClick(object? sender, RoutedEventArgs e) => OpenUrl("https://steamcommunity.com/dev/apikey");

    private void OnGetGridDbKeyClick(object? sender, RoutedEventArgs e) => OpenUrl("https://www.steamgriddb.com/profile/preferences/api");

    private void OnGetRawgKeyClick(object? sender, RoutedEventArgs e) => OpenUrl("https://rawg.io/apidocs");

    // 🟢 NOVO: IGDB (Twitch) — abre o console de dev onde se cria o Client ID/Secret
    private void OnGetIgdbKeyClick(object? sender, RoutedEventArgs e) => OpenUrl("https://dev.twitch.tv/console/apps");

    private void OpenUrl(string url)
    {
        try
        {
            Topmost = false;
            if (Owner is Window owner) owner.Topmost = false;
            if (_openExternalUrl is not null) _openExternalUrl(url);
            else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* Melhor esforço */ }
    }

    private static void ToggleMask(TextBox box) => box.PasswordChar = box.PasswordChar == default ? '•' : default;

    private void OnToggleGridDbVisibilityClick(object? sender, RoutedEventArgs e) => ToggleMask(GridDbKeyBox);

    private void OnToggleSteamWebVisibilityClick(object? sender, RoutedEventArgs e) => ToggleMask(SteamWebKeyBox);

    private void OnToggleSteamIdVisibilityClick(object? sender, RoutedEventArgs e) => ToggleMask(SteamIdBox);

    private void OnToggleRawgVisibilityClick(object? sender, RoutedEventArgs e) => ToggleMask(RawgKeyBox);

    // 🟢 NOVO: toggles do IGDB (2 campos)
    private void OnToggleIgdbIdVisibilityClick(object? sender, RoutedEventArgs e) => ToggleMask(IgdbIdBox);

    private void OnToggleIgdbSecretVisibilityClick(object? sender, RoutedEventArgs e) => ToggleMask(IgdbSecretBox);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}