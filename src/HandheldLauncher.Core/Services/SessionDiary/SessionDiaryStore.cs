using System.Text.Json;

namespace HandheldLauncher.Core.Services.SessionDiary;

/// <summary>
/// Diário de sessões em disco (uma sessão por linha, JSONL) e as contas sobre ele.
/// Local, sem conta nem serviço remoto. Arquivo próprio: o "sessions.jsonl" antigo (só
/// título e duração) continua existindo e não é tocado.
/// </summary>
public sealed class SessionDiaryStore
{
    /// <summary>Sessão curta demais não diz nada sobre o consumo do jogo.</summary>
    public const int MinSessionSeconds = 120;
    public const int MinBatterySecondsForEnergy = 180;
    public const int SessionsForConfidence = 3;
    private const int RecentSessions = 10;
    private const double AboveNormalRatio = 1.25;
    private const double AboveNormalMinWatts = 3;

    private static readonly Lazy<SessionDiaryStore> _shared = new(() => new SessionDiaryStore());
    public static SessionDiaryStore Shared => _shared.Value;

    private readonly string _path;
    private readonly object _gate = new();

    public SessionDiaryStore(string? pathOverride = null)
    {
        _path = pathOverride ?? Path.Combine(
            Path.GetDirectoryName(AppSettingsStore.DefaultPath) ?? ".", "session-diary.jsonl");
    }

    /// <summary>Grava a sessão. false = curta demais ou sem disco.</summary>
    public bool Append(SessionRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.GameId) || record.DurationSeconds < MinSessionSeconds) return false;
        lock (_gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(_path, JsonSerializer.Serialize(record) + Environment.NewLine);
                return true;
            }
            catch
            {
                return false; // diário é melhor esforço
            }
        }
    }

    /// <summary>Todas as sessões gravadas, da mais antiga para a mais nova. Linhas estragadas são puladas.</summary>
    public IReadOnlyList<SessionRecord> Load()
    {
        var result = new List<SessionRecord>();
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path)) return result;
                foreach (var line in File.ReadLines(_path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        if (JsonSerializer.Deserialize<SessionRecord>(line) is { GameId.Length: > 0 } record) result.Add(record);
                    }
                    catch (JsonException)
                    {
                        // linha cortada por um desligamento: ignora só ela
                    }
                }
            }
            catch
            {
                // sem leitura agora: devolve o que deu
            }
        }
        return result;
    }

    public IReadOnlyList<SessionRecord> ForGame(string gameId) =>
        Load().Where(r => r.GameId == gameId).ToList();

    public GameEnergySummary? Summarize(string gameId, double? fullChargeWattHours) =>
        Summarize(ForGame(gameId), fullChargeWattHours);

    /// <summary>
    /// Consumo típico do jogo na bateria (média ponderada pelo tempo das últimas sessões),
    /// autonomia estimada e se a última sessão gastou bem mais que o costume.
    /// null = ainda não há sessão na bateria com leitura de consumo.
    /// </summary>
    public static GameEnergySummary? Summarize(IEnumerable<SessionRecord> sessionsOfOneGame, double? fullChargeWattHours)
    {
        var all = sessionsOfOneGame.OrderBy(r => r.StartedUtc).ToList();
        var measured = all
            .Where(r => r.AvgBatteryWatts is > 0 && r.BatterySeconds >= MinBatterySecondsForEnergy)
            .ToList();
        if (measured.Count == 0) return null;

        var recent = measured.TakeLast(RecentSessions).ToList();
        var seconds = recent.Sum(r => (double)r.BatterySeconds);
        var typical = recent.Sum(r => r.AvgBatteryWatts!.Value * r.BatterySeconds) / seconds;

        var withFps = all.Where(r => r.AvgFps is > 0).TakeLast(RecentSessions).ToList();
        double? avgFps = withFps.Count > 0 ? Math.Round(withFps.Average(r => r.AvgFps!.Value), 0) : null;

        double? hours = fullChargeWattHours is > 0 && typical > 0
            ? Math.Round(fullChargeWattHours.Value / typical, 1)
            : null;

        var last = measured[^1];
        var above = false;
        if (measured.Count > SessionsForConfidence)
        {
            // Compara a última com a mediana das anteriores (a mediana não se deixa levar por uma sessão fora da curva).
            var previous = measured.Take(measured.Count - 1).TakeLast(RecentSessions)
                .Select(r => r.AvgBatteryWatts!.Value).OrderBy(v => v).ToList();
            var median = previous.Count % 2 == 1
                ? previous[previous.Count / 2]
                : (previous[previous.Count / 2 - 1] + previous[previous.Count / 2]) / 2;
            var lastWatts = last.AvgBatteryWatts!.Value;
            above = lastWatts >= median * AboveNormalRatio && lastWatts - median >= AboveNormalMinWatts;
        }

        return new GameEnergySummary(
            Sessions: measured.Count,
            TypicalWatts: Math.Round(typical, 1),
            AvgFps: avgFps,
            EstimatedHours: hours,
            LastWatts: last.AvgBatteryWatts,
            LastAboveNormal: above,
            IsEarlyEstimate: measured.Count < SessionsForConfidence);
    }

    /// <summary>
    /// Horas de jogo que a carga de agora rende no consumo típico do jogo.
    /// null = falta alguma leitura (capacidade, carga ou consumo).
    /// </summary>
    public static double? EstimateHoursNow(double typicalWatts, double? fullChargeWattHours, int? batteryPercent)
    {
        if (typicalWatts <= 0 || fullChargeWattHours is not > 0 || batteryPercent is not (>= 0 and <= 100)) return null;
        return Math.Round(fullChargeWattHours.Value * batteryPercent.Value / 100.0 / typicalWatts, 2);
    }

    /// <summary>Uma linha por jogo com consumo medido, do que mais tempo ficou na bateria para o que menos.</summary>
    public static IReadOnlyList<GameEnergyRow> SummarizeAll(IEnumerable<SessionRecord> sessions, double? fullChargeWattHours)
    {
        var rows = new List<GameEnergyRow>();
        foreach (var group in sessions.GroupBy(r => r.GameId))
        {
            var list = group.OrderBy(r => r.StartedUtc).ToList();
            if (Summarize(list, fullChargeWattHours) is not { } summary) continue;
            var title = list.Select(r => r.Title).LastOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? group.Key;
            rows.Add(new GameEnergyRow(group.Key, title, summary, list.Sum(r => (long)r.BatterySeconds)));
        }
        return rows.OrderByDescending(r => r.BatterySeconds).ToList();
    }

    /// <summary>
    /// Linha do histórico: "16/11 · 1h20 · 24,5 W · 58 FPS · bateria 80% → 45%".
    /// Só entra o que foi medido naquela sessão.
    /// </summary>
    public static string DescribeSession(SessionRecord record, TimeSpan utcOffset)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        var local = record.StartedUtc + utcOffset;
        var minutes = Math.Max(1, (int)Math.Round(record.DurationSeconds / 60.0));
        var parts = new List<string>
        {
            local.ToString("dd/MM", culture),
            minutes >= 60 ? $"{minutes / 60}h{minutes % 60:00}" : $"{minutes} min",
        };
        if (record.AvgBatteryWatts is > 0 && record.BatterySeconds >= MinBatterySecondsForEnergy)
            parts.Add(record.AvgBatteryWatts.Value.ToString("0.0", culture) + " W");
        else
            parts.Add("na tomada");
        if (record.AvgFps is > 0) parts.Add(record.AvgFps.Value.ToString("0", culture) + " FPS");
        if (record.BatteryStartPercent is { } from && record.BatteryEndPercent is { } to && from != to)
            parts.Add($"bateria {from}% → {to}%");
        if (record.SmartTdp) parts.Add(record.TargetFps is { } target ? $"Auto {target} FPS" : "Auto");
        else if (record.AvgTdpWatts is > 0) parts.Add("TDP " + record.AvgTdpWatts.Value.ToString("0", culture) + " W");
        return string.Join(" · ", parts);
    }
}
