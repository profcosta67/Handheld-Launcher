using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HandheldLauncher.App.Platform;

namespace HandheldLauncher.App.Views;

/// <summary>
/// Pede a senha de uma rede Wi-Fi protegida. Devolve a senha por
/// <c>ShowDialog&lt;string?&gt;</c> (null = cancelou).
///
/// Existe porque o Windows não conecta a uma rede sem perfil salvo: o WinRT
/// responde InvalidCredential e o netsh exige o perfil já criado. Com a senha
/// aqui, o serviço cria o perfil e liga de verdade.
/// </summary>
public partial class WifiPasswordWindow : Window
{
    public WifiPasswordWindow() : this(string.Empty)
    {
    }

    public WifiPasswordWindow(string ssid, bool retry = false)
    {
        InitializeComponent();
        TitleText.Text = $"Conectar em \"{ssid}\"";
        if (retry)
            HintText.Text = "A senha salva não funcionou. Digite a senha desta rede.";

        Opened += (_, _) =>
        {
            PasswordBox.Focus();
            TouchKeyboard.Show(); // handheld: sem teclado físico à mão
        };

        // Sem isto o teclado virtual ficava aberto por cima do launcher depois
        // de fechar a janela (não dava mais para navegar).
        Closed += (_, _) => TouchKeyboard.Hide();
    }

    private void OnConnectClick(object? sender, RoutedEventArgs e) => Finish();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Finish();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private void Finish()
    {
        var text = PasswordBox.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return; // sem senha não há o que conectar
        Close(text);
    }
}
