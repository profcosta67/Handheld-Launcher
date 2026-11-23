namespace HandheldLauncher.Core.Services.Power;

/// <summary>
/// Tempo restante de bateria pela conta do launcher. O do Windows (GetSystemPowerStatus) pula
/// muito: usa o consumo de poucos segundos, então um menu leve promete 6 h e uma cena pesada
/// 1 h. Aqui são duas medidas juntas:
/// <list type="bullet">
/// <item>o consumo informado pela bateria, suavizado (média móvel de ~3 min), que acompanha
/// mudanças reais (abriu um jogo);</item>
/// <item>quanto a carga (mWh) caiu de fato nos últimos ~15 min — o ritmo verdadeiro.</item>
/// </list>
/// Com as duas parecidas, usa a média delas; se o consumo de agora fugiu muito do ritmo
/// recente (o uso mudou), confia no de agora.
/// </summary>
public sealed class BatteryLifeEstimator
{
    public const double TauSeconds = 180;
    public static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MinRateSpan = TimeSpan.FromMinutes(8);
    public const int MinRateDropMwh = 150;
    /// <summary>Diferença acima disto entre o consumo de agora e o ritmo recente = o uso mudou.</summary>
    public const double ChangeRatio = 0.4;
    /// <summary>Intervalo maior que isto entre leituras (suspensão) recomeça a conta.</summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(5);

    private readonly Queue<(DateTime Utc, int Mwh)> _history = new();
    private double? _ema;
    private DateTime? _last;

    /// <summary>Consumo usado na última conta, em W.</summary>
    public double? Watts { get; private set; }

    /// <summary>Ritmo real (queda de carga) dos últimos minutos, em W; null se ainda não deu tempo.</summary>
    public double? MeasuredWatts { get; private set; }

    /// <summary>Nova leitura. Devolve os minutos restantes, ou null (na tomada / sem dados).</summary>
    public double? Update(DateTime utc, bool onAc, double? watts, int? remainingMwh)
    {
        if (onAc || remainingMwh is not > 0)
        {
            Reset();
            return null;
        }
        if (_last is { } last && (utc - last > MaxGap || utc < last)) Reset();

        if (watts is > 0.5)
        {
            if (_ema is { } e && _last is { } l)
            {
                var dt = Math.Max(0, (utc - l).TotalSeconds);
                var alpha = 1 - Math.Exp(-dt / TauSeconds);
                _ema = e + alpha * (watts.Value - e);
            }
            else _ema = watts.Value;
        }
        _last = utc;

        _history.Enqueue((utc, remainingMwh.Value));
        while (_history.Count > 0 && utc - _history.Peek().Utc > RateWindow) _history.Dequeue();

        MeasuredWatts = null;
        if (_history.Count >= 2)
        {
            var first = _history.Peek();
            var span = utc - first.Utc;
            var drop = first.Mwh - remainingMwh.Value;
            if (span >= MinRateSpan && drop >= MinRateDropMwh)
                MeasuredWatts = drop / 1000.0 / span.TotalHours;
        }

        double? w = (_ema, MeasuredWatts) switch
        {
            ({ } e, { } m) when Math.Abs(e - m) / m > ChangeRatio => e,
            ({ } e, { } m) => (e + m) / 2,
            ({ } e, null) => e,
            (null, { } m) => m,
            _ => null,
        };
        Watts = w;
        if (w is not > 0.5) return null;
        return remainingMwh.Value / 1000.0 / w.Value * 60;
    }

    public void Reset()
    {
        _history.Clear();
        _ema = null;
        _last = null;
        Watts = null;
        MeasuredWatts = null;
    }
}
