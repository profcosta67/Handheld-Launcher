using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Input;

namespace HandheldLauncher.App.Views;

/// <summary>
/// v0.16.2 — Central de Ajuda. Duas abas na mesma janela:
/// <list type="bullet">
/// <item>Verificações (<see cref="HealthChecks"/>): cartões ✅/ℹ️/⚠️/❌ com
/// resumo e dica; A num cartão abre o artigo do manual com a solução.</item>
/// <item>Manual (<see cref="HelpArticles"/>): artigos por categoria.</item>
/// </list>
/// Controle: o MainWindow manda o gamepad pro <see cref="DialogGamepadNavigator"/>
/// (↑↓ percorre os cartões e rola a lista, A abre, B fecha). Antes disso,
/// <see cref="HandleGamepadAction"/> intercepta: LB/RB trocam de aba e B
/// dentro de um artigo volta pra lista em vez de fechar a janela.
/// </summary>
public partial class HelpCenterWindow : Window
{
    private enum View { Checks, Manual, Article }

    private View _view = View.Checks;
    private View _articleReturnView = View.Checks;
    private bool _running;
    private bool _manualBuilt;

    public HelpCenterWindow()
    {
        InitializeComponent();

        Opened += async (_, _) =>
        {
            FitToScreen();
            ShowView(View.Checks);
            await RunChecksAsync();
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (!GoBackFromArticle()) Close();
                e.Handled = true;
            }
        };
    }

    /// <summary>
    /// Chamado pelo MainWindow antes do navegador padrão dos diálogos.
    /// true = ação tratada aqui.
    /// </summary>
    public bool HandleGamepadAction(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.PreviousFilter:
            case GamepadAction.NextFilter:
                ShowView(_view == View.Checks ? View.Manual : View.Checks);
                return true;

            case GamepadAction.Back:
                return GoBackFromArticle();

            default:
                return false;
        }
    }

    private bool GoBackFromArticle()
    {
        if (_view != View.Article) return false;
        ShowView(_articleReturnView);
        return true;
    }

    // --------------------------------------------------------------------

    private void ShowView(View view)
    {
        _view = view;
        ChecksScroll.IsVisible = view == View.Checks;
        ManualScroll.IsVisible = view == View.Manual;
        ArticleScroll.IsVisible = view == View.Article;
        BtnRerun.IsVisible = view == View.Checks;
        TxtSummary.IsVisible = view == View.Checks;

        BtnTabChecks.Classes.Set("primary", view == View.Checks);
        BtnTabManual.Classes.Set("primary", view is View.Manual or View.Article);

        TxtControls.Text = view == View.Article
            ? "🎮 ↑↓: rolar | B: voltar"
            : "🎮 ↑↓: navegar | A: abrir | LB/RB: trocar aba | B: fechar";

        if (view == View.Manual)
        {
            BuildManualOnce();
            FocusFirst(ManualPanel);
        }
        else if (view == View.Checks)
        {
            FocusFirst(ResultsPanel);
        }
    }

    private static void FocusFirst(Panel panel)
    {
        var first = panel.Children.OfType<Button>().FirstOrDefault();
        first?.Focus();
    }

    private void OnTabChecksClick(object? sender, RoutedEventArgs e) => ShowView(View.Checks);
    private void OnTabManualClick(object? sender, RoutedEventArgs e) => ShowView(View.Manual);

    // ---------------- Verificações ----------------

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
            RenderChecks(results);
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

    private void RenderChecks(IReadOnlyList<HealthChecks.Result> results)
    {
        ResultsPanel.Children.Clear();

        // Problemas primeiro, depois atenção, informação e ok.
        foreach (var result in results.OrderByDescending(r => r.Status))
            ResultsPanel.Children.Add(BuildCheckCard(result));

        var errors = results.Count(r => r.Status == HealthChecks.Status.Error);
        var warnings = results.Count(r => r.Status == HealthChecks.Status.Warning);
        var oks = results.Count(r => r.Status == HealthChecks.Status.Ok);

        TxtSummary.Text = errors == 0 && warnings == 0
            ? $"✅ Tudo certo — {oks} verificação(ões) ok."
            : $"{(errors > 0 ? $"❌ {errors} problema(s)" : "")}{(errors > 0 && warnings > 0 ? " · " : "")}{(warnings > 0 ? $"⚠️ {warnings} ponto(s) de atenção" : "")} · ✅ {oks} ok";

        if (_view == View.Checks) FocusFirst(ResultsPanel);
    }

    private Button BuildCheckCard(HealthChecks.Result result)
    {
        var text = new StackPanel { Spacing = 4 };
        text.Children.Add(new TextBlock { Text = result.Title, FontSize = 15, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = result.Summary, FontSize = 13, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(result.Hint))
            text.Children.Add(Subtle("💡 " + result.Hint, 12));

        var article = HelpArticles.Find(result.ArticleId);
        if (article is not null)
            text.Children.Add(Subtle($"📖 A: {article.Title}", 11));

        var card = WithIcon(HealthChecks.Icon(result.Status), text);
        if (article is not null)
            card.Click += (_, _) => OpenArticle(article, View.Checks);
        return card;
    }

    // ---------------- Manual ----------------

    private void BuildManualOnce()
    {
        if (_manualBuilt) return;
        _manualBuilt = true;

        foreach (var group in HelpArticles.All.GroupBy(a => a.Category))
        {
            var header = new TextBlock { Text = group.Key.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeight.Bold, Opacity = 0.55, Margin = new Avalonia.Thickness(4, 8, 0, 0) };
            ManualPanel.Children.Add(header);

            foreach (var article in group)
            {
                var card = new Button
                {
                    Content = new TextBlock { Text = article.Title, FontSize = 14, TextWrapping = TextWrapping.Wrap },
                };
                card.Classes.Add("check");
                var target = article;
                card.Click += (_, _) => OpenArticle(target, View.Manual);
                ManualPanel.Children.Add(card);
            }
        }
    }

    private void OpenArticle(HelpArticles.Article article, View returnTo)
    {
        _articleReturnView = returnTo;
        ArticlePanel.Children.Clear();

        var backTop = BackButton();
        ArticlePanel.Children.Add(backTop);
        ArticlePanel.Children.Add(Subtle(article.Category.ToUpperInvariant(), 10));
        ArticlePanel.Children.Add(new TextBlock { Text = article.Title, FontSize = 18, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });

        foreach (var paragraph in article.Paragraphs)
            ArticlePanel.Children.Add(new TextBlock { Text = paragraph, FontSize = 14, TextWrapping = TextWrapping.Wrap });

        // Segundo "Voltar" no fim: dá ao ↓ do controle um destino pra rolar
        // até o fim de artigos maiores que a tela.
        ArticlePanel.Children.Add(BackButton());

        ShowView(View.Article);
        ArticleScroll.Offset = new Avalonia.Vector(0, 0);
        backTop.Focus();
        DebugLog.Write($"[AJUDA] Artigo aberto: {article.Id}");
    }

    private Button BackButton()
    {
        var back = new Button { Content = "← Voltar", HorizontalAlignment = HorizontalAlignment.Left };
        back.Classes.Add("pill");
        back.Click += (_, _) => GoBackFromArticle();
        return back;
    }

    // ---------------- Comum ----------------

    private static TextBlock Subtle(string text, double size)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 2, 0, 0) };
        block.Classes.Add("subtle");
        return block;
    }

    private static Button WithIcon(string icon, Control content)
    {
        var iconBlock = new TextBlock
        {
            Text = icon,
            FontSize = 22,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Avalonia.Thickness(0, 0, 12, 0),
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(iconBlock, 0);
        Grid.SetColumn(content, 1);
        grid.Children.Add(iconBlock);
        grid.Children.Add(content);

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
