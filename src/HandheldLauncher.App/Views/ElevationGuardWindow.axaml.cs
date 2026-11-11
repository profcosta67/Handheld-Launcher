using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HandheldLauncher.App.Views;

public partial class ElevationGuardWindow : Window
{
    public ElevationGuardWindow() : this(string.Empty, string.Empty) { }

    public ElevationGuardWindow(string gameTitle, string tdpMode)
    {
        InitializeComponent();

        TxtTitle.Text = "⚠ TDP e ventoinha sem administrador";
        TxtMessage.Text =
            $"O launcher está a correr sem administrador, por isso o perfil de TDP “{tdpMode}” " +
            $"e a curva da ventoinha não foram aplicados.\n\n“{gameTitle}” pode perder performance " +
            $"(engasgos) por falta de potência ou de arrefecimento.\n\n" +
            $"Reiniciar como administrador aplica tudo de uma vez — o UAC aparece só uma vez.";

        Opened += (_, _) => BtnRestartAdmin.Focus();
    }

    private void OnPlayAnywayClick(object? sender, RoutedEventArgs e) => Close(false);
    private void OnRestartAdminClick(object? sender, RoutedEventArgs e) => Close(true);
}
