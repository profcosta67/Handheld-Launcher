using System.Text.Json;

namespace HandheldLauncher.Core.Services.SessionDiary;

/// <summary>Uma leitura da capacidade da bateria (mWh) num dia.</summary>
public sealed class BatteryWearEntry
{
    public DateTime Utc { get; set; }
    public int DesignedMwh { get; set; }
    public int FullMwh { get; set; }

    public int Percent => DesignedMwh > 0 ? (int)Math.Round(100.0 * FullMwh / DesignedMwh) : 0;
}

/// <summary>De quanto para quanto a capacidade foi desde a primeira leitura guardada.</summary>
public sealed record BatteryWearTrend(DateTime SinceUtc, int FirstPercent, int NowPercent, double FirstWattHours, double NowWattHours);

/// <summary>
/// Histórico da capacidade real da bateria, uma leitura por semana no máximo. Serve para
/// mostrar o desgaste ao longo dos meses: o Windows só informa o valor de agora.
/// </summary>
public sealed class BatteryWearLog
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromDays(7);

    private static readonly Lazy<BatteryWearLog> _shared = new(() => new BatteryWearLog());
    public static BatteryWearLog Shared => _shared.Value;

    private readonly string _path;
    private readonly object _gate = new();

    public BatteryWearLog(string? pathOverride = null)
    {
        _path = pathOverride ?? Path.Combine(
            Path.GetDirectoryName(AppSettingsStore.DefaultPath) ?? ".", "battery-wear.jsonl");
    }

    /// <summary>Guarda a leitura se a última tiver mais de uma semana. true = gravou.</summary>
    public bool Note(DateTime utcNow, int? designedMwh, int? fullMwh)
    {
        if (designedMwh is not > 0 || fullMwh is not > 0) return false;
        lock (_gate)
        {
            try
            {
                var entries = LoadUnlocked();
                if (entries.Count > 0 && utcNow - entries[^1].Utc < MinInterval) return false;
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var entry = new BatteryWearEntry { Utc = utcNow, DesignedMwh = designedMwh.Value, FullMwh = fullMwh.Value };
                File.AppendAllText(_path, JsonSerializer.Serialize(entry) + Environment.NewLine);
                return true;
            }
            catch
            {
                return false; // melhor esforço
            }
        }
    }

    public IReadOnlyList<BatteryWearEntry> Load()
    {
        lock (_gate) return LoadUnlocked();
    }

    /// <summary>null = ainda não há uma leitura antiga (30 dias ou mais) para comparar.</summary>
    public static BatteryWearTrend? Trend(IReadOnlyList<BatteryWearEntry> entries, DateTime utcNow, int? designedMwh, int? fullMwh)
    {
        if (entries.Count == 0 || designedMwh is not > 0 || fullMwh is not > 0) return null;
        var first = entries[0];
        if (utcNow - first.Utc < TimeSpan.FromDays(30)) return null;
        var nowPercent = (int)Math.Round(100.0 * fullMwh.Value / designedMwh.Value);
        return new BatteryWearTrend(first.Utc, first.Percent, nowPercent, first.FullMwh / 1000.0, fullMwh.Value / 1000.0);
    }

    private List<BatteryWearEntry> LoadUnlocked()
    {
        var result = new List<BatteryWearEntry>();
        try
        {
            if (!File.Exists(_path)) return result;
            foreach (var line in File.ReadLines(_path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    if (JsonSerializer.Deserialize<BatteryWearEntry>(line) is { DesignedMwh: > 0, FullMwh: > 0 } entry) result.Add(entry);
                }
                catch (JsonException)
                {
                    // linha cortada: ignora só ela
                }
            }
        }
        catch
        {
            // sem leitura agora
        }
        return result.OrderBy(e => e.Utc).ToList();
    }
}
