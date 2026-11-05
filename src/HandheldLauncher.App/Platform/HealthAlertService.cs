using System.Collections.ObjectModel;
using System.Text.Json;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

public sealed class HealthAlertItem
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public DateTime RaisedAt { get; init; } = DateTime.Now;
    public string TimeText => RaisedAt.Date == DateTime.Today ? RaisedAt.ToString("HH:mm") : RaisedAt.ToString("dd/MM HH:mm");
}

/// <summary>
/// v0.18 — alertas da "Saúde do aparelho" (no plano ficavam pra depois da
/// v1.0; antecipados a pedido do usuário). Três avisos, com limites
/// conservadores e intervalo mínimo entre repetições pra nunca virar chato:
/// <list type="bullet">
/// <item>Temperatura: CPU ≥ 95°C por pelo menos 10s seguidos (no máximo 1 aviso a cada 30 min).</item>
/// <item>Bateria: saúde (capacidade real vs. de fábrica) abaixo de 80% (no máximo 1 aviso por semana).</item>
/// <item>Disco: menos de 15 GB livres E mais de 90% usado — mesmo critério do
/// Gerenciador de armazenamento (no máximo 1 aviso por dia por disco).</item>
/// </list>
/// Este serviço só DECIDE e registra; quem mostra é o MainWindow, que nunca
/// abre aviso por cima de jogo (ver MainWindow.ShowHealthAlertPopup).
/// </summary>
public sealed class HealthAlertService
{
    public const double HighTemperatureC = 95;
    private const int HighTemperatureSamplesNeeded = 5; // timer de hardware = 2s → 10s
    private static readonly TimeSpan TemperatureCooldown = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan BatteryCooldown = TimeSpan.FromDays(7);
    private static readonly TimeSpan DiskCooldown = TimeSpan.FromDays(1);
    private const int BatteryHealthAlertPercent = 80;
    private const int MaxRecent = 20;

    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HandheldLauncher", "health-alerts.json");

    private Dictionary<string, DateTime> _lastRaised = LoadState();
    private int _hotSamples;
    private double _hotPeak;

    public ObservableCollection<HealthAlertItem> Recent { get; } = new();

    private int _unseenCount;
    public int UnseenCount => _unseenCount;

    /// <summary>Disparado na thread da UI quando um alerta novo passa por todos os filtros.</summary>
    public event Action<HealthAlertItem>? AlertRaised;

    public void MarkAllSeen() => _unseenCount = 0;

    /// <summary>Chamado a cada leitura do timer de hardware (thread da UI).</summary>
    public void OnTemperatureSample(double celsius)
    {
        if (celsius >= HighTemperatureC)
        {
            _hotSamples++;
            _hotPeak = Math.Max(_hotPeak, celsius);
            if (_hotSamples >= HighTemperatureSamplesNeeded)
            {
                var peak = _hotPeak;
                _hotSamples = 0;
                _hotPeak = 0;
                TryRaise("temperatura", TemperatureCooldown, "Aparelho muito quente",
                    $"A CPU passou de {HighTemperatureC:F0}°C por mais de 10 segundos (pico de {peak:F0}°C). Vale baixar o TDP no Quick Settings, tirar a capinha ou deixar a saída de ar livre.");
            }
        }
        else
        {
            _hotSamples = 0;
            _hotPeak = 0;
        }
    }

    /// <summary>Bateria e discos. Leituras (WMI/DriveInfo) em segundo plano; alerta volta pra UI.</summary>
    public async Task RunPeriodicChecksAsync()
    {
        try
        {
            var (health, lowDrives) = await Task.Run(() =>
            {
                int? h = null;
                try { h = BatteryHealth.Read().HealthPercent; } catch (Exception ex) { DebugLog.Swallowed(ex); }

                var drives = new List<(string Name, double FreeGb)>();
                try
                {
                    foreach (var d in DriveInfo.GetDrives())
                    {
                        if (!d.IsReady) continue;
                        if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                        var totalGb = d.TotalSize / 1024.0 / 1024.0 / 1024.0;
                        if (totalGb < 0.5) continue;
                        var freeGb = d.TotalFreeSpace / 1024.0 / 1024.0 / 1024.0;
                        var used = (1.0 - freeGb / totalGb) * 100.0;
                        if (freeGb < 15 && used > 90) drives.Add((d.Name, freeGb));
                    }
                }
                catch (Exception ex) { DebugLog.Swallowed(ex); }
                return (h, drives);
            });

            if (health is { } pct && pct < BatteryHealthAlertPercent)
            {
                TryRaise("bateria", BatteryCooldown, "Bateria desgastada",
                    $"A bateria segura {pct}% da carga que segurava de fábrica. É desgaste normal com o tempo — se a autonomia estiver incomodando, limitar a carga em 80% (no app do fabricante) ajuda a desacelerar.");
            }

            foreach (var (name, freeGb) in lowDrives)
            {
                TryRaise($"disco:{name}", DiskCooldown, "Pouco espaço em disco",
                    $"{name} está com só {freeGb:F0} GB livres. Abra 💾 Armazenamento no menu pra ver o que ocupa mais (dá pra mover jogos e limpar o cache de shader).");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[SAUDE] Verificação periódica falhou: {ex.Message}");
        }
    }

    private void TryRaise(string key, TimeSpan cooldown, string title, string message)
    {
        if (_lastRaised.TryGetValue(key, out var last) && DateTime.Now - last < cooldown) return;
        _lastRaised[key] = DateTime.Now;
        SaveState(_lastRaised);

        var item = new HealthAlertItem { Key = key, Title = title, Message = message };
        Recent.Insert(0, item);
        while (Recent.Count > MaxRecent) Recent.RemoveAt(Recent.Count - 1);
        _unseenCount++;
        DebugLog.Write($"[SAUDE] Alerta: {title} — {message}");
        try { AlertRaised?.Invoke(item); } catch (Exception ex) { DebugLog.Swallowed(ex); }
    }

    private static Dictionary<string, DateTime> LoadState()
    {
        try
        {
            if (File.Exists(StatePath))
                return JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(StatePath)) ?? new();
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
        return new Dictionary<string, DateTime>();
    }

    private static void SaveState(Dictionary<string, DateTime> state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
    }
}
