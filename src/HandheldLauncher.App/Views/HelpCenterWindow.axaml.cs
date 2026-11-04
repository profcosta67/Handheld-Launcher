using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Views;

/// <summary>
/// v0.16.2 — Central de Ajuda, etapa 1: mostra as verificações de
/// <see cref="HealthChecks"/> como cartões (✅ ok, ℹ️ informação, ⚠️ atenção,
/// ❌ problema), cada um com resumo e, quando há o que fazer, uma dica.
/// Aberta pelo menu global. Navegável pelo controle via
/// <see cref="DialogGamepadNavigator"/> (cada cartão é um botão focável, então
/// ↑↓ percorre a lista e rola até o cartão). As próximas etapas (manual e
/// Reportar erro) entram nesta mesma janela.
/// </summary>
public partial class HelpCenterWindow : Window
{
    private bool _running;

    public HelpCenterWindow()
    {
        InitializeComponent();

        Opened += async (_, _) =>
        {
            FitToScreen();
            await RunChecksAsync();
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
    }

    private async Task RunChecksAsync()
    {
        if (_running) return;
        _running = true;
        BtnRerun.IsEnabled = false;
        TxtSummary.Text = "Verificando... (leva alguns segundos)";
        ResultsPanel.Children.Clear();

        try
        {
            var results = await HealthChecks.RunAllAsync();
            Render(results);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[AJUDA] Falha ao rodar as verificações: {ex}");
            TxtSummary.Text = "Não foi possível concluir as verificações. Veja o debug.log.";
        }
        finally
        {
            _running = false;
            BtnRerun.IsEnabled = true;
        }
    }

    private void Render(IReadOnlyList<HealthChecks.Result> results)
    {
        ResultsPanel.Children.Clear();

        // Problemas primeiro, depois atenção, informação e ok.
        var ordered = results
            .OrderByDescending(r => r.Status)
            .ToList();

        foreach (var result in ordered)
            ResultsPanel.Children.Add(BuildCard(result));

        var errors = results.Count(r => r.Status == HealthChecks.Status.Error);
        var warnings = results.Count(r => r.Status == HealthChecks.Status.Warning);
        var oks = results.Count(r => r.Status == HealthChecks.Status.Ok);

        TxtSummary.Text = errors == 0 && warnings == 0
            ? $"✅ Tudo certo — {oks} verificação(ões) ok."
            : $"{(errors > 0 ? $"❌ {errors} problema(s)" : "")}{(errors > 0 && warnings > 0 ? " · " : "")}{(warnings > 0 ? $"⚠️ {warnings} ponto(s) de atenção" : "")} · ✅ {oks} ok";

        if (ResultsPanel.Children.FirstOrDefault() is Button first) first.Focus();
    }

    private static Button BuildCard(HealthChecks.Result result)
    {
        var icon = new TextBlock
        {
            Text = HealthChecks.Icon(result.Status),
            FontSize = 22,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Avalonia.Thickness(0, 0, 12, 0),
        };

        var text = new StackPanel { Spacing = 4 };
        text.Children.Add(new TextBlock { Text = result.Title, FontSize = 15, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = result.Summary, FontSize = 13, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(result.Hint))
        {
            var hint = new TextBlock { Text = "💡 " + result.Hint, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 2, 0, 0) };
            hint.Classes.Add("subtle");
            text.Children.Add(hint);
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(text, 1);
        grid.Children.Add(icon);
        grid.Children.Add(text);

        var card = new Button { Content = grid };
        card.Classes.Add("check");
        return card;
    }

    private async void OnRerunClick(object? sender, RoutedEventArgs e)
    {
        try { await RunChecksAsync(); }
        catch (Exception ex) { DebugLog.Write($"[AJUDA] Reverificação falhou: {ex.Message}"); }
    }

    private void OnOpenLogFolderClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HandheldLauncher");
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[AJUDA] Não consegui abrir a pasta do log: {ex.Message}");
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    // Mesmo ajuste da janela de Configurações: em telas baixas, encolhe pra caber.
    private void FitToScreen()
    {
        try
        {
            var screen = Screens.Primary;
            if (screen is null) return;
            var available = screen.WorkingArea.Height / screen.Scaling - 40;
            if (available > 300 && Height > available)
            {
                Height = available;
                Position = new Avalonia.PixelPoint(Position.X, screen.WorkingArea.Y + 20);
            }
        }
        catch { }
    }
}
