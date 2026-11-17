namespace HandheldLauncher.Core.Services.SessionDiary;

/// <summary>
/// Junta as leituras de uma sessão (uma a cada poucos segundos) e devolve o resumo.
/// Sem acesso a hardware: quem lê os sensores é o launcher; aqui é só conta.
/// </summary>
public sealed class SessionAccumulator
{
    private const double MinDischargeWatts = 0.5; // abaixo disso é ruído ou bateria cheia na tomada
    private const int MaxFpsSamples = 20000;

    private readonly string _gameId;
    private readonly string _title;
    private readonly DateTime _startedUtc;

    private double _batterySeconds;
    private double _wattSeconds;
    private double _peakWatts;
    private int? _firstPercent;
    private int? _lastPercent;
    private readonly List<double> _fps = new();
    private double _tdpSeconds;
    private double _tdpWattSeconds;
    private bool _smartTdp;
    private int? _targetFps;

    public SessionAccumulator(string gameId, string title, DateTime startedUtc)
    {
        _gameId = gameId;
        _title = title;
        _startedUtc = startedUtc;
    }

    /// <param name="seconds">Tempo coberto por esta leitura.</param>
    /// <param name="dischargeWatts">Consumo do aparelho na bateria (positivo = descarregando).</param>
    /// <param name="tdpWatts">Limite de potência do processador em vigor, se conhecido.</param>
    public void Add(double seconds, bool onAc, double? dischargeWatts, int? batteryPercent, double? fps, int? tdpWatts,
        bool smartTdp = false, int? targetFps = null)
    {
        if (seconds <= 0 || seconds > 120) return; // relógio pulou (suspensão): não conta

        if (batteryPercent is >= 0 and <= 100)
        {
            _firstPercent ??= batteryPercent;
            _lastPercent = batteryPercent;
        }

        if (!onAc && dischargeWatts is { } watts && watts >= MinDischargeWatts && watts < 200)
        {
            _batterySeconds += seconds;
            _wattSeconds += watts * seconds;
            _peakWatts = Math.Max(_peakWatts, watts);
        }

        if (fps is { } f && f >= 1 && f < 1000 && !double.IsNaN(f) && _fps.Count < MaxFpsSamples) _fps.Add(f);

        if (tdpWatts is > 0 and < 200)
        {
            _tdpSeconds += seconds;
            _tdpWattSeconds += tdpWatts.Value * seconds;
        }

        if (smartTdp)
        {
            _smartTdp = true;
            if (targetFps is > 0) _targetFps = targetFps;
        }
    }

    public SessionRecord Build(DateTime endedUtc)
    {
        double? avgFps = null, lowFps = null;
        if (_fps.Count > 0)
        {
            avgFps = Math.Round(_fps.Average(), 1);
            var sorted = _fps.OrderBy(v => v).ToList();
            var take = Math.Max(1, (int)Math.Ceiling(sorted.Count * 0.05));
            lowFps = Math.Round(sorted.Take(take).Average(), 1);
        }

        return new SessionRecord
        {
            GameId = _gameId,
            Title = _title,
            StartedUtc = _startedUtc,
            DurationSeconds = Math.Max(0, (int)Math.Round((endedUtc - _startedUtc).TotalSeconds)),
            BatterySeconds = (int)Math.Round(_batterySeconds),
            AvgBatteryWatts = _batterySeconds > 0 ? Math.Round(_wattSeconds / _batterySeconds, 1) : null,
            PeakBatteryWatts = _batterySeconds > 0 ? Math.Round(_peakWatts, 1) : null,
            BatteryStartPercent = _firstPercent,
            BatteryEndPercent = _lastPercent,
            AvgFps = avgFps,
            LowFps = lowFps,
            AvgTdpWatts = _tdpSeconds > 0 ? Math.Round(_tdpWattSeconds / _tdpSeconds, 1) : null,
            SmartTdp = _smartTdp,
            TargetFps = _targetFps,
        };
    }
}
