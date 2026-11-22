using HandheldLauncher.Core.Services.SmartTdp;

namespace HandheldLauncher.Core.Tests;

/// <summary>
/// 22/11 — banco fixo de cenários do TDP inteligente. Cada cenário é um "jogo de mentira":
/// FPS em função do tempo e dos watts. O controle vê o FPS com ruído e mediana de 3 leituras,
/// como no aparelho. É a cópia exata do banco em Python usado para ajustar o controle
/// (tools/tdp-bank/bank.py): mesmo gerador de números, mesmas contas, mesmos números.
/// </summary>
internal static class SmartTdpScenarioBank
{
    public const int MinW = 8;
    public const int MaxW = 35;

    /// <summary>Gerador simples e determinístico — o MESMO do Python.</summary>
    public sealed class Lcg
    {
        private ulong _x;
        public Lcg(long seed) => _x = unchecked((ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL);
        public double Random()
        {
            _x = unchecked(_x * 6364136223846793005UL + 1442695040888963407UL);
            return (_x >> 11) * (1.0 / 9007199254740992.0);
        }
        public double Uniform(double a, double b) => a + (b - a) * Random();
    }

    public sealed record Limits(double MaxExcessWatts, double MaxStarvePercent, double MaxChangesPerMinute);
    public sealed record Scenario(string Name, Func<Lcg, (int Target, Func<double, int, double> Game, int Start)> Build, Limits Limits);
    public sealed record Result(double AvgW, double Ideal, double Excess, double Starve, double ChangesPerMinute, double Fps);

    private static Func<double, double> LerpCurve(params (double W, double F)[] points)
    {
        var pts = points.OrderBy(p => p.W).ToArray();
        return w =>
        {
            if (w <= pts[0].W) return pts[0].F * w / pts[0].W;
            for (var i = 0; i < pts.Length - 1; i++)
            {
                var (w0, f0) = pts[i];
                var (w1, f1) = pts[i + 1];
                if (w <= w1) return f0 + (f1 - f0) * (w - w0) / (w1 - w0);
            }
            var (a0, b0) = pts[^2];
            var (a1, b1) = pts[^1];
            return b1 + (b1 - b0) / (a1 - a0) * (w - a1) * 0.3;
        };
    }

    // ---------------------------------------------------------------- cenários

    private static (int, Func<double, int, double>, int) Light(Lcg _)
    {
        var f = LerpCurve((10, 70), (20, 95), (30, 105));
        return (60, (t, w) => f(w), 20);
    }

    private static (int, Func<double, int, double>, int) Medium(Lcg _)
    {
        var f = LerpCurve((10, 35), (20, 62), (30, 72));
        return (60, (t, w) => f(w), 20);
    }

    private static (int, Func<double, int, double>, int) HeavyReachable(Lcg _)
    {
        var f = LerpCurve((10, 20), (20, 33), (30, 41), (35, 43));
        return (40, (t, w) => f(w), 20);
    }

    private static (int, Func<double, int, double>, int) GpuWall(Lcg _)
    {
        var f = LerpCurve((10, 18), (20, 29), (26, 34), (35, 34.5));
        return (40, (t, w) => Math.Min(34, f(w)), 20);
    }

    private static (int, Func<double, int, double>, int) JustAbove(Lcg _)
    {
        var f = LerpCurve((10, 20), (20, 32), (26, 38), (35, 38.5));
        return (40, (t, w) => Math.Min(38, f(w)), 20);
    }

    private static (int, Func<double, int, double>, int) Cutscenes(Lcg rnd)
    {
        var f = LerpCurve((10, 45), (15, 62), (20, 80), (30, 100));
        var cuts = new List<(double A, double B)>();
        var t = 30.0;
        while (t < 4000)
        {
            var d = rnd.Uniform(10, 20);
            cuts.Add((t, t + d));
            t += d + rnd.Uniform(40, 90);
        }
        return (60, (tt, w) => cuts.Any(c => c.A <= tt && tt < c.B) ? 30 : f(w), 20);
    }

    private static (int, Func<double, int, double>, int) Replays(Lcg _)
    {
        var f = LerpCurve((10, 45), (15, 62), (20, 80), (30, 100));
        return (60, (t, w) => (t % 30) < 8 && t > 30 ? 30 : f(w), 20);
    }

    private static (int, Func<double, int, double>, int) Menus(Lcg _)
    {
        var f = LerpCurve((10, 35), (20, 62), (30, 72));
        return (60, (t, w) => (t % 120) >= 90 ? f(w) * 2.2 : f(w), 20);
    }

    private static (int, Func<double, int, double>, int) Vsync(Lcg _)
    {
        var f = LerpCurve((10, 35), (20, 62), (30, 72));
        return (60, (t, w) => f(w) >= 60 ? 60 : 30, 20);
    }

    private static (int, Func<double, int, double>, int) HeavyArea(Lcg _)
    {
        var f = LerpCurve((10, 35), (20, 62), (30, 72));
        return (60, (t, w) => (t % 300) >= 200 && (t % 300) < 290 ? f(w - 7) : f(w), 20);
    }

    private static (int, Func<double, int, double>, int) Pauses(Lcg _)
    {
        var f = LerpCurve((10, 35), (20, 62), (30, 72));
        return (60, (t, w) => (t % 240) >= 180 ? 5 : f(w), 20);
    }

    private static (int, Func<double, int, double>, int) Scenes(Lcg rnd)
    {
        var scenes = new List<(double A, double B, double Cap)>();
        var t = 0.0;
        while (t < 4000)
        {
            var d = rnd.Uniform(60, 240);
            var r = rnd.Random();
            var cap = r < 0.65 ? 33.0 : (r < 0.85 ? 41.0 : 27.0);
            scenes.Add((t, t + d, cap));
            t += d;
        }
        return (35, (tt, w) =>
        {
            var cap = scenes.First(s => s.A <= tt && tt < s.B).Cap;
            var k = w < 20 ? 30.0 * w / 20 : 30 + 0.6 * (w - 20);
            return Math.Min(cap, k);
        }, 20);
    }

    private static (int, Func<double, int, double>, int) Limiter(Lcg _)
    {
        var f = LerpCurve((10, 35), (20, 62), (30, 72));
        return (60, (t, w) => Math.Min(60, f(w)), 20);
    }

    private static (int, Func<double, int, double>, int) StartHigh(Lcg _)
    {
        var f = LerpCurve((10, 45), (15, 62), (20, 80), (30, 100));
        return (60, (t, w) => f(w), 35);
    }

    private static (int, Func<double, int, double>, int) GpuWallStartHigh(Lcg _)
    {
        var f = LerpCurve((10, 18), (20, 29), (26, 34), (35, 34.5));
        return (40, (t, w) => Math.Min(34, f(w)), 35);
    }

    public static readonly Scenario[] All =
    {
        new("leve", Light, new(2.0, 3, 2.0)),
        new("medio", Medium, new(2.5, 4, 2.0)),
        new("pesado-alcanca", HeavyReachable, new(2.5, 5, 2.0)),
        new("teto-placa", GpuWall, new(3.0, 5, 2.0)),
        new("meta-pouco-acima", JustAbove, new(3.0, 5, 2.0)),
        new("cortes-de-cena", Cutscenes, new(4.0, 5, 3.0)),
        new("replay-30s", Replays, new(5.0, 6, 4.0)),
        new("menus", Menus, new(3.0, 4, 3.0)),
        new("vsync-60-30", Vsync, new(3.0, 6, 3.0)),
        new("area-pesada", HeavyArea, new(3.0, 8, 3.0)),
        new("pausas", Pauses, new(3.0, 4, 2.0)),
        new("cenas-alternadas", Scenes, new(4.0, 6, 3.0)),
        new("limitador", Limiter, new(2.5, 4, 2.0)),
        new("comeca-no-teto", StartHigh, new(2.0, 3, 3.0)),
        new("teto-comeca-alto", GpuWallStartHigh, new(3.5, 5, 3.0)),
    };

    private static int IdealWatts(Func<double, int, double> game, double t, int target)
    {
        var best = game(t, MaxW);
        var goal = Math.Min(target * 0.95, best * 0.97);
        for (var w = MinW; w <= MaxW; w++)
            if (game(t, w) >= goal) return w;
        return MaxW;
    }

    public static Result Run(Func<int, int, int, int, ISmartTdpController> make, Scenario scenario, int seed,
        double duration = 1500, double noise = 0.06)
        => Run(make, scenario, seed, out _, out _, duration, noise);

    /// <summary>
    /// Roda o cenário. <paramref name="wattSum"/> e <paramref name="changes"/> são a impressão digital
    /// exata (soma dos watts a cada leitura e número de trocas) para conferir com o Python.
    /// </summary>
    public static Result Run(Func<int, int, int, int, ISmartTdpController> make, Scenario scenario, int seed,
        out long wattSum, out int changes, double duration = 1500, double noise = 0.06)
    {
        var rnd = new Lcg(seed * 7919L + 13);
        var (target, game, start) = scenario.Build(rnd);
        var c = make(target, MinW, MaxW, start);
        var rs = new Lcg(seed);
        var buf = new List<double>();
        var ws = new List<int>();
        var ideal = new List<int>();
        var starve = 0;
        changes = 0;
        wattSum = 0;
        var n = 0;
        var fpsSum = 0.0;
        var t = 0.0;
        while (t < duration)
        {
            var truth = game(t, c.Watts);
            var seen = truth * (1 + rs.Uniform(-noise, noise));
            buf.Add(seen);
            double m;
            if (buf.Count >= 3)
            {
                var last = new[] { buf[^3], buf[^2], buf[^1] };
                Array.Sort(last);
                m = last[1];
            }
            else m = seen;

            if (t >= 20 && c.OnSample(m, t) != SmartTdpChange.None) changes++;
            if (t >= 60 && game(t, MaxW) >= 10)
            {
                ws.Add(c.Watts);
                ideal.Add(IdealWatts(game, t, target));
                n++;
                var reach = Math.Min(target, game(t, MaxW));
                if (truth >= 10 && game(t, c.Watts) < 0.90 * reach && c.Watts < MaxW) starve++;
                fpsSum += truth;
            }
            wattSum += c.Watts;
            t += 2;
        }
        var avg = ws.Average();
        var avgIdeal = ideal.Average();
        return new Result(avg, avgIdeal, avg - avgIdeal, 100.0 * starve / n, changes / (duration / 60), fpsSum / n);
    }
}
