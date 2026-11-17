namespace HandheldLauncher.Core.Services.SessionDiary;

/// <summary>FPS medido com um certo limite de potência (TDP), somado numa sessão.</summary>
public sealed class TdpFpsBucket
{
    public int Watts { get; set; }
    public int Seconds { get; set; }
    public double AvgFps { get; set; }
    /// <summary>FPS mediano: menus e pausas a centenas de FPS não puxam a média para cima. null em sessões antigas.</summary>
    public double? MedianFps { get; set; }
    public double Fps => MedianFps ?? AvgFps;
}

public enum EnergyAdviceKind
{
    /// <summary>Há um TDP medido que segura essa taxa.</summary>
    Recommended,
    /// <summary>O jogo não chegou nessa taxa, nem com o TDP mais alto medido.</summary>
    NotReached,
    /// <summary>Ainda não foi medido com TDP suficiente para saber.</summary>
    NotMeasured,
}

public sealed record EnergyTargetAdvice(int TargetFps, EnergyAdviceKind Kind, int? Watts);

/// <summary>
/// O que as sessões medidas dizem sobre FPS × TDP de um jogo: o máximo que ele rendeu e o
/// menor TDP que segurou cada taxa fixa. Só afirma o que foi medido.
/// </summary>
public sealed record EnergyGuide(
    double MaxFps,
    int MaxFpsWatts,
    int HighestWattsMeasured,
    IReadOnlyList<EnergyTargetAdvice> Targets)
{
    public static readonly int[] DefaultTargets = { 30, 60, 120 };

    /// <summary>Tempo mínimo num mesmo TDP para a leitura valer.</summary>
    public const int MinSecondsPerWatt = 60;
    private const int RecentSessions = 20;
    private const double MeetRatio = 0.95;
    private const double PlateauRatio = 0.97;
    private const int PlateauSpanWatts = 6;
    private const int NearCeilingWatts = 3;
    private const int SolidSeconds = 300;

    /// <param name="deviceMaxWatts">Teto de TDP do aparelho; 0 = desconhecido.</param>
    /// <param name="notBeforeUtc">Ignora sessões anteriores (ex.: o usuário resetou o aprendizado).</param>
    public static EnergyGuide? Build(IEnumerable<SessionRecord> sessionsOfOneGame, int deviceMaxWatts, IReadOnlyList<int>? targets = null,
        DateTime? notBeforeUtc = null)
    {
        targets ??= DefaultTargets;
        var seconds = new SortedDictionary<int, double>();
        var frames = new Dictionary<int, double>();
        var sessionCount = new Dictionary<int, int>();
        foreach (var session in sessionsOfOneGame.OrderBy(r => r.StartedUtc).TakeLast(RecentSessions))
        {
            if (session.FpsByTdp is null) continue;
            if (notBeforeUtc is { } cutoff && session.StartedUtc < cutoff) continue;
            foreach (var bucket in session.FpsByTdp)
            {
                if (bucket.Watts <= 0 || bucket.Seconds <= 0 || bucket.Fps <= 0) continue;
                seconds[bucket.Watts] = seconds.GetValueOrDefault(bucket.Watts) + bucket.Seconds;
                frames[bucket.Watts] = frames.GetValueOrDefault(bucket.Watts) + bucket.Fps * bucket.Seconds;
                sessionCount[bucket.Watts] = sessionCount.GetValueOrDefault(bucket.Watts) + 1;
            }
        }

        var points = seconds.Where(p => p.Value >= MinSecondsPerWatt)
            .Select(p => (Watts: p.Key, Fps: frames[p.Key] / p.Value, Seconds: p.Value, Sessions: sessionCount[p.Key]))
            .ToList(); // já em ordem crescente de watts
        if (points.Count == 0) return null;

        var best = points.Max(p => p.Fps);
        var bestWatts = points.First(p => p.Fps >= best * PlateauRatio).Watts;
        var highest = points[^1];
        // O ponto mais alto só serve de prova com dados suficientes (não uma tela de carregamento solta).
        var highestSolid = highest.Sessions >= 2 || highest.Seconds >= SolidSeconds;

        // "Não chega" só com prova: o teto do aparelho foi medido de verdade, ou o FPS já estava
        // parado 6 W abaixo do ponto mais alto, e esse ponto rende o máximo medido (limite da
        // placa de vídeo, vsync ou limitador).
        var atCeiling = deviceMaxWatts > 0 && highest.Watts >= deviceMaxWatts - NearCeilingWatts && highestSolid;
        var plateau = highestSolid && highest.Fps >= best * PlateauRatio
            && points.Any(p => p.Watts <= highest.Watts - PlateauSpanWatts && p.Fps >= highest.Fps * PlateauRatio);

        var advice = new List<EnergyTargetAdvice>();
        foreach (var target in targets)
        {
            // Menor TDP a partir do qual todos os TDPs medidos seguram a taxa: um TDP baixo que
            // "rendeu" muito só porque ficou em menus não vira recomendação.
            // Ponto com pouco dado (um carregamento solto) não derruba a recomendação.
            int? hit = null;
            for (var i = points.Count - 1; i >= 0; i--)
            {
                var point = points[i];
                if (point.Fps >= target * MeetRatio) hit = point.Watts;
                else if (point.Sessions >= 2 || point.Seconds >= SolidSeconds) break;
            }
            if (hit is not null) advice.Add(new EnergyTargetAdvice(target, EnergyAdviceKind.Recommended, hit));
            else if (atCeiling || plateau) advice.Add(new EnergyTargetAdvice(target, EnergyAdviceKind.NotReached, null));
            else advice.Add(new EnergyTargetAdvice(target, EnergyAdviceKind.NotMeasured, null));
        }

        return new EnergyGuide(Math.Round(best, 0), bestWatts, highest.Watts, advice);
    }

    /// <summary>"30 FPS: 12 W · 60 FPS: 17 W · 120 FPS: não chega".</summary>
    public bool HasAnyMeasurement => Targets.Any(t => t.Kind != EnergyAdviceKind.NotMeasured);

    public string ShortText() => string.Join(" · ", Targets.Select(t => $"{t.TargetFps} FPS: {Describe(t, false)}"));

    public static string Describe(EnergyTargetAdvice advice, bool detailed) => advice.Kind switch
    {
        EnergyAdviceKind.Recommended => $"{advice.Watts} W",
        EnergyAdviceKind.NotReached => detailed ? "não chega nessa taxa" : "não chega",
        _ => detailed ? "ainda sem medição" : "sem medição",
    };
}
