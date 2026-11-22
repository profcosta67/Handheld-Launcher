using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Platform;

namespace HandheldLauncher.App.Views;

/// <summary>
/// 18/11: tela dos componentes de fora (PawnIO do TDP, HidHide do controle isolado e o
/// RivaTuner Statistics Server, que mede o FPS). Depois de uma instalação limpa o TDP não
/// funcionava porque o driver do PawnIO não tinha sido instalado e nada avisava — esta tela
/// mostra o estado de cada um e instala o que estiver faltando.
///
/// O RTSS não pode ser redistribuído junto com o launcher, então aqui ele é instalado pelo winget
/// do Windows (fonte oficial) e só quando o usuário aperta o botão.
/// </summary>
public partial class ComponentsWindow : Window
{
    private bool _busy;

    public ComponentsWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Rebuild();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        try
        {
            Host.Children.Clear();
            foreach (var status in SystemComponents.CheckAll())
                Host.Children.Add(BuildRow(status));
        }
        catch (Exception ex) { DebugLog.Write($"[COMPONENTES] Falha ao montar a lista: {ex.Message}"); }
    }

    private Control BuildRow(SystemComponents.Status status)
    {
        var icon = status.State switch
        {
            SystemComponents.State.Installed => "✅",
            SystemComponents.State.Missing => "⚠️",
            _ => "ℹ️",
        };

        var title = new TextBlock
        {
            Text = $"{icon}  {status.Name}",
            FontWeight = FontWeight.SemiBold,
            Foreground = this.FindResource("TextPrimaryBrush") as IBrush ?? Brushes.White,
        };

        var detail = new TextBlock
        {
            Text = status.Detail,
            TextWrapping = TextWrapping.Wrap,
            Foreground = this.FindResource("TextSecondaryBrush") as IBrush ?? Brushes.LightGray,
        };

        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(title);
        content.Children.Add(detail);

        if (status.State == SystemComponents.State.Missing && status.CanInstallHere)
        {
            var button = new Button
            {
                Content = status.Kind == SystemComponents.Kind.Rtss
                    ? $"Instalar o {SystemComponents.RtssFullName}"
                    : "Instalar agora",
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Avalonia.Thickness(0, 8, 0, 0),
                Height = 42,
                Padding = new Avalonia.Thickness(18, 0, 18, 0),
            };
            button.Classes.Add("pill");
            button.Classes.Add("primary");
            button.Click += (_, _) => HandheldLauncher.App.Diagnostics.UiSafe.Run(
                () => InstallAsync(status, button), "ComponentsInstall");
            content.Children.Add(button);
        }

        return new Border
        {
            Background = this.FindResource("PanelSurfaceAltBrush") as IBrush,
            CornerRadius = new Avalonia.CornerRadius(12),
            Padding = new Avalonia.Thickness(16),
            Child = content,
        };
    }

    private async Task InstallAsync(SystemComponents.Status status, Button button)
    {
        if (_busy) return;
        _busy = true;
        var original = button.Content;
        button.IsEnabled = false;
        button.Content = "Instalando...";
        // Fechar no meio da instalação deixaria a tela sendo mexida depois de morta.
        BtnClose.IsEnabled = false;
        BtnRefresh.IsEnabled = false;
        try
        {
            var ok = await Task.Run(() => SystemComponents.Install(status.Kind));
            DebugLog.Write($"[COMPONENTES] Instalação de {status.Name} pedida na tela: {(ok ? "ok" : "não confirmou")}.");
            if (!IsVisible) return;         // a janela já foi fechada: nada a atualizar
            Rebuild();                      // monta os botões de novo (o antigo saiu da tela)
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[COMPONENTES] Instalação de {status.Name} falhou: {ex.Message}");
            if (IsVisible)
            {
                button.IsEnabled = true;
                button.Content = original;
            }
        }
        finally
        {
            _busy = false;
            if (IsVisible)
            {
                BtnClose.IsEnabled = true;
                BtnRefresh.IsEnabled = true;
            }
        }
    }
}
