using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Input;
using HandheldLauncher.App.Platform;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Views;

/// <summary>
/// v0.18 "Saúde do aparelho" (Fase 1): temperatura, bateria e disco, só
/// leitura. Recebe a MainViewModel só pra ler CpuTemperature/
/// PeakCpuTemperatureText (já mantidos vivos pelo _hardwareTimer que já
/// existia) — não cria nenhum segundo leitor de sensor concorrente.
/// </summary>
public partial class DeviceHealthWindow : Window
{
    private readonly MainViewModel? _vm;

    public ObservableCollection<DriveRowViewModel> Drives { get; } = new();
    public ObservableCollection<EnergyRowViewModel> EnergyRows { get; } = new();

    // Construtor sem parâmetro só pra o designer/XAML previewer do Avalonia
    // não quebrar — a janela de verdade sempre é criada com a MainViewModel.
    public DeviceHealthWindow() : this(null) { }

    public DeviceHealthWindow(MainViewModel? vm)
    {
        _vm = vm;
        InitializeComponent();
        DriveList.ItemsSource = Drives;
        EnergyList.ItemsSource = EnergyRows;

        // v0.18: alertas recentes (a lista vive no HealthAlertService da MainViewModel).
        if (vm is not null)
        {
            AlertList.ItemsSource = vm.HealthAlerts.Recent;
            TxtNoAlerts.IsVisible = vm.HealthAlerts.Recent.Count == 0;
            vm.HealthAlerts.Recent.CollectionChanged += OnAlertsChanged;
            Closed += (_, _) => vm.HealthAlerts.Recent.CollectionChanged -= OnAlertsChanged;
        }

        try { ThemeManager.ApplyFont(this, AppSettingsStore.Load().UiFont); }
        catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); }

        RefreshAll();
    }

    private void OnAlertsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_vm is not null) TxtNoAlerts.IsVisible = _vm.HealthAlerts.Recent.Count == 0;
    }

    private void OnRefreshClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => RefreshAll();
    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    /// <summary>v0.28: pediram o diagnóstico de bateria; quem abre é o MainWindow (navegação do controle).</summary>
    public bool BatteryRequested { get; private set; }

    private void OnBatteryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        BatteryRequested = true;
        Close();
    }

    // v0.18: mesmo problema do StorageManagerWindow (ver comentário lá) —
    // só "Atualizar"/"Fechar" são focáveis, então ↑/↓ não tinham conteúdo
    // pra focar/rolar dentro do ScrollViewer. Mesma correção: interceptado
    // em OnGamepadTick ANTES do DialogGamepadNavigator genérico.
    private const double GamepadScrollStepPx = 90;

    public bool HandleGamepadAction(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.MoveUp:
                ContentScroll.Offset = new Vector(ContentScroll.Offset.X, Math.Max(0, ContentScroll.Offset.Y - GamepadScrollStepPx));
                return true;
            case GamepadAction.MoveDown:
                var maxOffset = Math.Max(0, ContentScroll.Extent.Height - ContentScroll.Viewport.Height);
                ContentScroll.Offset = new Vector(ContentScroll.Offset.X, Math.Min(maxOffset, ContentScroll.Offset.Y + GamepadScrollStepPx));
                return true;
            default:
                return false;
        }
    }

    private void RefreshAll()
    {
        RefreshTemperature();
        RefreshBattery();
        RefreshDrives();
    }

    private void RefreshTemperature()
    {
        TxtTempNow.Text = _vm?.CpuTemperature ?? "--°C";
        TxtTempPeak.Text = _vm?.PeakCpuTemperatureText ?? "--°C";
    }

    private void RefreshBattery()
    {
        try
        {
            var pct = BatteryStatus.GetDisplayText();
            TxtBatteryNow.Text = string.IsNullOrEmpty(pct) ? "Sem bateria (PC de mesa)" : pct;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[SAUDE] Falha ao ler % de bateria: {ex.Message}");
            TxtBatteryNow.Text = "--";
        }

        try
        {
            var health = BatteryHealth.Read();
            if (health.HealthPercent is { } pct)
            {
                TxtBatteryHealth.Text = $"{pct}%";
                TxtBatteryHealthNote.IsVisible = false;
            }
            else
            {
                TxtBatteryHealth.Text = "Indisponível";
                TxtBatteryHealthNote.Text = "O Windows não reportou a capacidade de fábrica desta bateria (comum em alguns modelos/drivers).";
                TxtBatteryHealthNote.IsVisible = true;
            }
            ShowBatteryCapacity(health);
            RefreshEnergyRows(health.FullChargedCapacityMwh is { } fullMwh ? fullMwh / 1000.0 : null);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[SAUDE] Falha ao ler saúde da bateria: {ex.Message}");
            TxtBatteryHealth.Text = "Indisponível";
        }
    }

    private void ShowBatteryCapacity(BatteryHealth.Result health)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        RowBatteryCapacity.IsVisible = false;
        TxtBatteryTrend.IsVisible = false;
        if (health.FullChargedCapacityMwh is not { } full) return;

        var text = (full / 1000.0).ToString("0.0", culture) + " Wh";
        if (health.DesignedCapacityMwh is { } designed) text += " de " + (designed / 1000.0).ToString("0.0", culture) + " Wh de fábrica";
        TxtBatteryCapacity.Text = text;
        RowBatteryCapacity.IsVisible = true;

        _ = ShowBatteryTrendAsync(health);
    }

    private async System.Threading.Tasks.Task ShowBatteryTrendAsync(BatteryHealth.Result health)
    {
        HandheldLauncher.Core.Services.SessionDiary.BatteryWearTrend? trend = null;
        try
        {
            // Disco fora da thread da tela.
            trend = await System.Threading.Tasks.Task.Run(() =>
            {
                var log = HandheldLauncher.Core.Services.SessionDiary.BatteryWearLog.Shared;
                log.Note(DateTime.UtcNow, health.DesignedCapacityMwh, health.FullChargedCapacityMwh);
                return HandheldLauncher.Core.Services.SessionDiary.BatteryWearLog.Trend(
                    log.Load(), DateTime.UtcNow, health.DesignedCapacityMwh, health.FullChargedCapacityMwh);
            });
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
        if (trend is null) return;
        TxtBatteryTrend.Text = trend.NowPercent == trend.FirstPercent
            ? $"Sem perda desde {trend.SinceUtc.ToLocalTime():dd/MM/yyyy} ({trend.NowPercent}%)."
            : $"Em {trend.SinceUtc.ToLocalTime():dd/MM/yyyy} era {trend.FirstPercent}%; hoje é {trend.NowPercent}%.";
        TxtBatteryTrend.IsVisible = true;
    }

    private void RefreshEnergyRows(double? fullWattHours) => _ = RefreshEnergyRowsAsync(fullWattHours);

    private async System.Threading.Tasks.Task RefreshEnergyRowsAsync(double? fullWattHours)
    {
        EnergyRows.Clear();
        try
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
            // Diário lido fora da thread da tela.
            var rows = await System.Threading.Tasks.Task.Run(() =>
                HandheldLauncher.Core.Services.SessionDiary.SessionDiaryStore.SummarizeAll(
                    HandheldLauncher.Core.Services.SessionDiary.SessionDiaryStore.Shared.Load(), fullWattHours));
            EnergyRows.Clear();
            foreach (var row in rows.Take(8))
            {
                var detail = row.Summary.Sessions == 1 ? "1 sessão na bateria" : $"{row.Summary.Sessions} sessões na bateria";
                if (row.Summary.EstimatedHours is { } hours)
                {
                    var minutes = (int)Math.Round(hours * 60);
                    detail += $" · cerca de {minutes / 60}h{minutes % 60:00} por carga";
                }
                if (row.Summary.LastAboveNormal) detail += " · última sessão acima do costume";
                EnergyRows.Add(new EnergyRowViewModel(row.Title, detail, row.Summary.TypicalWatts.ToString("0.0", culture) + " W"));
            }
        }
        catch (Exception ex)
        {
            DebugLog.Swallowed(ex);
        }
        TxtNoEnergyRows.IsVisible = EnergyRows.Count == 0;
    }

    private void RefreshDrives()
    {
        Drives.Clear();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable) continue;

                var totalGb = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
                if (totalGb < 0.5) continue; // ignora unidades minúsculas (ex: partição de recuperação)

                var freeGb = drive.TotalFreeSpace / 1024.0 / 1024.0 / 1024.0;
                var usedPercent = totalGb > 0 ? (1.0 - freeGb / totalGb) * 100.0 : 0;

                Drives.Add(new DriveRowViewModel
                {
                    Label = $"{drive.Name} ({(drive.DriveType == DriveType.Removable ? "microSD/externo" : "interno")})",
                    UsageText = $"{freeGb:F0} GB livres de {totalGb:F0} GB",
                    UsedPercent = Math.Clamp(usedPercent, 0, 100),
                });
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[SAUDE] Falha ao listar discos: {ex.Message}");
        }

        if (Drives.Count == 0)
        {
            Drives.Add(new DriveRowViewModel { Label = "Nenhuma unidade encontrada", UsageText = "", UsedPercent = 0 });
        }
    }
}
