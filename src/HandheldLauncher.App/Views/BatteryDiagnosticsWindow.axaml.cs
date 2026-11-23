using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Platform;
using HandheldLauncher.Core.Services.Power;

namespace HandheldLauncher.App.Views;

/// <summary>
/// v0.28 (pedido do usuário: "algumas coisas estão drenando a bateria, só não sei o que é"):
/// relatório do diagnóstico de bateria sobre as amostras do coletor (BatteryDiagnostics).
/// Mostra o consumo de agora, o resumo do período, os achados, quanto o launcher gastou e com
/// o quê, os programas que mais usaram o processador e quão certo está o tempo restante.
/// Cada bloco recebe foco para o controle conseguir rolar a tela (↑/↓).
/// </summary>
public partial class BatteryDiagnosticsWindow : Window
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private int _hours = 24;

    public BatteryDiagnosticsWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Rebuild();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => Rebuild();

    private void OnPeriodClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var h)) _hours = h;
        Rebuild();
    }

    private void Rebuild()
    {
        try
        {
            foreach (var b in new[] { BtnHour, BtnDay, BtnThreeDays })
                b.Classes.Set("active", b.Tag is string t && t == _hours.ToString(CultureInfo.InvariantCulture));

            PowerLogStore.Shared.Flush();
            var samples = PowerLogStore.Shared.Load(DateTime.UtcNow.AddHours(-_hours));
            var report = PowerReport.Build(samples);

            Host.Children.Clear();
            Host.Children.Add(NowCard());
            if (!report.HasData)
            {
                Host.Children.Add(Card("Ainda sem dados neste período",
                    "O launcher anota o consumo a cada 30 s enquanto o aparelho está fora da tomada. Use um pouco na bateria e volte aqui."));
                return;
            }
            Host.Children.Add(SummaryCard(report));
            if (report.Findings.Count > 0) Host.Children.Add(FindingsCard(report));
            Host.Children.Add(LauncherCard(report));
            if (report.TopProcesses.Count > 0) Host.Children.Add(ProcessesCard(report));
            Host.Children.Add(AccuracyCard(report));
        }
        catch (Exception ex) { DebugLog.Write($"[BATERIA] Falha ao montar o relatório: {ex.Message}"); }
    }

    // --- blocos --------------------------------------------------------------------------

    private Control NowCard()
    {
        var last = BatteryDiagnostics.LastSample;
        if (last is null || DateTime.UtcNow - last.Utc > TimeSpan.FromMinutes(5))
            return Card("Agora", "Lendo a bateria… (a primeira leitura sai uns 20 s depois de abrir o launcher)");
        if (last.OnAc)
            return Card("Agora", $"Na tomada{(last.Percent is { } p ? $", {p}%" : "")}. O diagnóstico conta só o tempo na bateria.");

        var lines = new List<string>();
        var w = BatteryDiagnostics.CurrentWatts ?? last.Watts;
        lines.Add($"Consumo: {Num(w, "0.0")} W{(last.Percent is { } pct ? $"  ·  carga {pct}%" : "")}");
        var ours = BatteryDiagnostics.CurrentRemaining;
        lines.Add($"Tempo restante: {Duration(ours?.TotalMinutes)} pela conta do launcher  ·  o Windows diz {Duration(last.WindowsRemainingMinutes)}");
        var extras = new List<string>();
        if (last.BrightnessPercent is { } br) extras.Add($"brilho {br}%");
        if (last.RefreshHz is { } hz) extras.Add($"tela a {hz} Hz");
        if (last.TdpWatts is { } tdp) extras.Add($"TDP {tdp} W");
        if (extras.Count > 0) lines.Add(string.Join("  ·  ", extras));
        return Card("Agora", string.Join("\n", lines));
    }

    private Control SummaryCard(PowerReportResult r)
    {
        var lines = new List<string>
        {
            $"Na bateria: {Duration(r.BatteryMinutes)}{(r.PercentDrop is { } d ? $"  ·  a carga caiu {d}%" : "")}  ·  média {Num(r.AvgWatts, "0.0")} W",
        };
        foreach (var s in r.ByState)
            lines.Add($"{StateName(s.State)}: {Duration(s.Minutes)}, {Num(s.AvgWatts, "0.0")} W em média");
        return Card("Resumo do período", string.Join("\n", lines));
    }

    private Control FindingsCard(PowerReportResult r)
    {
        var text = string.Join("\n\n", r.Findings.Select(f => (f.Level == FindingLevel.Attention ? "⚠️ " : "ℹ️ ") + f.Text));
        return Card("O que foi encontrado", text);
    }

    private Control LauncherCard(PowerReportResult r)
    {
        var lines = new List<string>
        {
            $"Processador usado pelo launcher: {Num(r.LauncherCpuPercent, "0.00")}% no período, {Num(r.LauncherCpuPercentWithoutGame, "0.00")}% sem jogo aberto.",
            "(100% = todos os núcleos ocupados o tempo todo. Abaixo de 0,3% é praticamente nada.)",
        };
        if (r.Activities.Count > 0)
        {
            lines.Add("");
            lines.Add("O que mais trabalhou (por minuto na bateria):");
            foreach (var a in r.Activities.Take(10))
                lines.Add($"•  {a.Name}: {Num(a.MsPerMinute, "0")} ms de trabalho, acordou {Num(a.WakeupsPerMinute, "0")} vezes");
        }
        return Card("O próprio launcher", string.Join("\n", lines));
    }

    private Control ProcessesCard(PowerReportResult r)
    {
        var lines = r.TopProcesses.Select(p =>
        {
            var name = p.IsLauncher ? $"{p.Name} (launcher + ajudante do controle)" : p.Name;
            var why = PowerReport.KnownBackground.TryGetValue(p.Name, out var w) ? $" — {w}" : "";
            return $"•  {name}: {Num(p.CpuPercent, "0.0")}% (sem jogo: {Num(p.CpuPercentWithoutGame, "0.0")}%){why}";
        });
        return Card("Programas que mais usaram o processador",
            string.Join("\n", lines) + "\n\nTela, Wi‑Fi e a placa de vídeo também gastam, mas o Windows não informa por programa.");
    }

    private Control AccuracyCard(PowerReportResult r)
    {
        var a = r.Accuracy;
        string text;
        if (a.Pairs == 0)
            text = "Para conferir, o launcher compara a previsão com o que a carga caiu nos 20–40 min seguintes. Ainda não há trecho longo o bastante na bateria neste período.";
        else
            text = $"Comparando a previsão com o que aconteceu de fato ({a.Pairs} conferências):\n" +
                   $"•  tempo do Windows: errou {Num(a.WindowsErrorPercent, "0")}% em média\n" +
                   $"•  conta do launcher: errou {Num(a.OurErrorPercent, "0")}% em média\n\n" +
                   "O HUD mostra a conta do launcher. Ela junta o consumo dos últimos minutos com quanto a carga caiu de verdade, então não pula a cada menu ou cena pesada.";
        return Card("O tempo restante está certo?", text);
    }

    // --- montagem ------------------------------------------------------------------------

    private Control Card(string title, string body)
    {
        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            Foreground = this.FindResource("TextPrimaryBrush") as IBrush ?? Brushes.White,
        });
        content.Children.Add(new TextBlock
        {
            Text = body,
            TextWrapping = TextWrapping.Wrap,
            Foreground = this.FindResource("TextSecondaryBrush") as IBrush ?? Brushes.LightGray,
        });

        var accent = this.FindResource("AccentBrush") as IBrush ?? Brushes.DodgerBlue;
        var border = new Border
        {
            Background = this.FindResource("PanelSurfaceAltBrush") as IBrush,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            Focusable = true,
            Child = content,
        };
        border.GotFocus += (_, _) => border.BorderBrush = accent;
        border.LostFocus += (_, _) => border.BorderBrush = Brushes.Transparent;
        return border;
    }

    private static string StateName(string state) => state switch
    {
        PowerState.Game => "Jogando",
        PowerState.Launcher => "No launcher",
        PowerState.Background => "Fora do launcher (área de trabalho, outros programas)",
        _ => state,
    };

    private static string Num(double? v, string format) => v is { } x ? x.ToString(format, Pt) : "—";

    private static string Duration(double? minutes)
    {
        if (minutes is not { } m || m <= 0) return "—";
        var t = TimeSpan.FromMinutes(m);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes:00}" : $"{t.Minutes} min";
    }
}
