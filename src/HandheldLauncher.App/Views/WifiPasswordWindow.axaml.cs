using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HandheldLauncher.App.Input;
using HandheldLauncher.App.Platform;

namespace HandheldLauncher.App.Views;

/// <summary>
/// Pede a senha de uma rede Wi-Fi protegida. Devolve a senha por
/// <c>ShowDialog&lt;string?&gt;</c> (null = cancelou).
///
/// Existe porque o Windows não conecta a uma rede sem perfil salvo: o WinRT
/// responde InvalidCredential e o netsh exige o perfil já criado. Com a senha
/// aqui, o serviço cria o perfil e liga de verdade.
///
/// v0.24.2: a janela passou a usar o teclado do launcher
/// (<see cref="OnScreenKeyboardView"/>) em vez do TabTip — o TabTip é uma
/// janela do Windows, fora da árvore do Avalonia, e o controle não chegava
/// às teclas (a senha só era utilizável com teclado físico).
/// </summary>
public partial class WifiPasswordWindow : Window, IGamepadKeyboardHost
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
            Keyboard.Show(PasswordBox);
        };

        // ⏎ no teclado liga; B/"fechar" só esconde o teclado (a janela fica).
        Keyboard.SubmitRequested += (_, _) => Finish();

        Closed += (_, _) =>
        {
            Keyboard.Hide();
            // O TabTip de uma sessão anterior podia ter ficado aberto por cima.
            TouchKeyboard.Hide();
        };
    }

    /// <summary>Com o teclado aberto, o controle é dele (D-Pad move, A escreve).</summary>
    public bool HandleKeyboardGamepadAction(GamepadAction action)
        => Keyboard.IsOpen && Keyboard.HandleGamepadAction(action);

    /// <summary>v0.24.2: A sobre o campo abre o teclado do launcher.</summary>
    public bool ShowKeyboardFor(TextBox target)
    {
        Keyboard.Show(target);
        target.Focus();
        return true;
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
