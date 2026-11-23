using HandheldLauncher.Core.Services.Power;
using Xunit;

namespace HandheldLauncher.Core.Tests;

public class PowerDiagnosticsTests
{
    private static readonly DateTime T0 = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EstimatorIsNullOnAc()
    {
        var e = new BatteryLifeEstimator();
        Assert.Null(e.Update(T0, onAc: true, watts: 10, remainingMwh: 40000));
    }

    [Fact]
    public void EstimatorUsesCurrentWattsAtFirst()
    {
        var e = new BatteryLifeEstimator();
        var min = e.Update(T0, false, 10, 50000);
        Assert.NotNull(min);
        Assert.Equal(300, min!.Value, 1); // 50 Wh / 10 W = 5 h
    }

    [Fact]
    public void EstimatorSmoothsSpikes()
    {
        // um pico de 30 W por 30 s não pode derrubar a previsão de 10 W para a de 30 W
        var e = new BatteryLifeEstimator();
        var mwh = 50000;
        for (var i = 0; i < 20; i++) e.Update(T0.AddSeconds(30 * i), false, 10, mwh -= 83);
        var min = e.Update(T0.AddSeconds(600), false, 30, mwh -= 250)!.Value;
        var naive = mwh / 1000.0 / 30 * 60;
        Assert.True(min > naive * 2, $"{min} vs {naive}");
    }

    [Fact]
    public void EstimatorUsesRealDropWhenConsistent()
    {
        // a bateria informa 8 W, mas a carga cai no ritmo de 12 W: a conta fica entre os dois
        var e = new BatteryLifeEstimator();
        var mwh = 50000.0;
        double? min = null;
        for (var i = 0; i <= 30; i++)
        {
            min = e.Update(T0.AddSeconds(30 * i), false, 8, (int)mwh);
            mwh -= 12000.0 / 120; // 12 W por 30 s
        }
        Assert.NotNull(e.MeasuredWatts);
        Assert.InRange(e.MeasuredWatts!.Value, 11.5, 12.5);
        Assert.InRange(e.Watts!.Value, 9.5, 10.5);
    }

    [Fact]
    public void EstimatorTrustsCurrentWhenUsageChanged()
    {
        // 15 min a 6 W, depois abre um jogo de 20 W: o ritmo antigo não pode segurar a previsão
        var e = new BatteryLifeEstimator();
        var mwh = 50000.0;
        var t = T0;
        for (var i = 0; i < 30; i++) { e.Update(t, false, 6, (int)mwh); mwh -= 50; t = t.AddSeconds(30); }
        for (var i = 0; i < 20; i++) { e.Update(t, false, 20, (int)mwh); mwh -= 167; t = t.AddSeconds(30); }
        Assert.True(e.Watts > 15, $"{e.Watts}");
    }

    [Fact]
    public void EstimatorResetsAfterSuspend()
    {
        var e = new BatteryLifeEstimator();
        e.Update(T0, false, 20, 50000);
        e.Update(T0.AddHours(2), false, 5, 48000);
        Assert.Equal(5, e.Watts!.Value, 3);
    }

    private static PowerSample S(int i, string state, double w, int mwh, double lcpu = 0.2, List<ProcessCpu>? top = null,
        double? winMin = null, double? ourMin = null, bool ac = false, int? pct = null) => new()
    {
        Utc = T0.AddSeconds(30 * i), Seconds = 30, OnAc = ac, Watts = w, RemainingMwh = mwh, State = state,
        LauncherCpuPercent = lcpu, TopProcesses = top, WindowsRemainingMinutes = winMin, OurRemainingMinutes = ourMin,
        Percent = pct ?? mwh / 500,
        ActivityMs = new() { ["controle"] = 30, ["sensores"] = 90 },
        ActivityCount = new() { ["controle"] = 600, ["sensores"] = 15 },
    };

    [Fact]
    public void ReportSplitsByStateAndRanksLauncherActivities()
    {
        var list = new List<PowerSample>();
        var mwh = 50000;
        for (var i = 0; i < 40; i++) list.Add(S(i, PowerState.Launcher, 7, mwh -= 58));
        for (var i = 40; i < 80; i++) list.Add(S(i, PowerState.Game, 20, mwh -= 167));
        var r = PowerReport.Build(list);
        Assert.True(r.HasData);
        Assert.Equal(40, r.BatteryMinutes, 1);
        Assert.Equal(2, r.ByState.Count);
        Assert.Equal(7, r.ByState.Single(s => s.State == PowerState.Launcher).AvgWatts!.Value, 3);
        Assert.Equal("sensores", r.Activities[0].Name);
        Assert.Equal(180, r.Activities[0].MsPerMinute, 3);   // 90 ms a cada 30 s
        Assert.Equal(1200, r.Activities[1].WakeupsPerMinute, 3);
    }

    [Fact]
    public void ReportIgnoresAcSamples()
    {
        var list = new List<PowerSample> { S(0, PowerState.Launcher, 5, 50000, ac: true), S(1, PowerState.Launcher, 5, 50000, ac: true) };
        Assert.False(PowerReport.Build(list).HasData);
    }

    [Fact]
    public void ReportFlagsBackgroundHogAndLauncher()
    {
        var hog = new List<ProcessCpu> { new() { Name = "MsMpEng", CpuPercent = 8 }, new() { Name = "HandheldLauncher", CpuPercent = 1.5 } };
        var list = Enumerable.Range(0, 40).Select(i => S(i, PowerState.Background, 6, 50000 - 50 * i, lcpu: 1.5, top: hog)).ToList();
        var r = PowerReport.Build(list);
        Assert.Contains(r.Findings, f => f.Level == FindingLevel.Attention && f.Text.Contains("MsMpEng") && f.Text.Contains("antivírus"));
        Assert.Contains(r.Findings, f => f.Level == FindingLevel.Attention && f.Text.StartsWith("O launcher usou"));
        Assert.True(r.TopProcesses.Single(p => p.Name == "HandheldLauncher").IsLauncher);
    }

    [Fact]
    public void ReportFlagsHighIdleDrainWithCauses()
    {
        var list = Enumerable.Range(0, 40).Select(i =>
        {
            var s = S(i, PowerState.Launcher, 11, 50000 - 90 * i);
            s.BrightnessPercent = 100; s.RefreshHz = 144;
            return s;
        }).ToList();
        var f = PowerReport.Build(list).Findings;
        Assert.Contains(f, x => x.Level == FindingLevel.Attention && x.Text.Contains("brilho em 100%") && x.Text.Contains("144 Hz"));
    }

    [Fact]
    public void AccuracyComparesWithWhatHappened()
    {
        // ritmo real: 12 W com 50 Wh = 250 min. Windows dizia 500 (erro 100%), nós 250 (erro ~0%).
        var list = new List<PowerSample>();
        var mwh = 50000.0;
        for (var i = 0; i <= 60; i++)
        {
            list.Add(S(i, PowerState.Game, 12, (int)mwh, winMin: 500, ourMin: mwh / 1000 / 12 * 60));
            mwh -= 100;
        }
        var a = PowerReport.Accuracy(list);
        Assert.True(a.Pairs > 0);
        Assert.InRange(a.WindowsErrorPercent!.Value, 90, 110);
        Assert.InRange(a.OurErrorPercent!.Value, 0, 5);
    }

    [Fact]
    public void AccuracySkipsAcrossCharger()
    {
        var list = new List<PowerSample>();
        for (var i = 0; i <= 60; i++)
            list.Add(S(i, PowerState.Game, 12, 50000 - 100 * i, winMin: 500, ac: i == 20));
        var a = PowerReport.Accuracy(list);
        // pares que cruzam a amostra na tomada não contam
        Assert.True(a.Pairs < 60 - 40);
    }

    [Fact]
    public void ActivityMeterCountsAndResets()
    {
        using (ActivityMeter.Measure("teste-a")) { }
        using (ActivityMeter.Measure("teste-a")) { }
        var snap = ActivityMeter.TakeSnapshot();
        Assert.Equal(2, snap["teste-a"].Count);
        Assert.False(ActivityMeter.TakeSnapshot().ContainsKey("teste-a"));
    }

    [Fact]
    public void StoreRoundTripsAndKeepsBuffered()
    {
        var path = Path.Combine(Path.GetTempPath(), $"power-{Guid.NewGuid():N}", "amostras.jsonl");
        var store = new PowerLogStore(path);
        for (var i = 0; i < 12; i++) store.Append(S(i, PowerState.Launcher, 7, 50000));
        var loaded = store.Load(T0.AddMinutes(-1));
        Assert.Equal(12, loaded.Count);   // 10 no disco + 2 no buffer
        Assert.Equal("launcher", loaded[0].State);
        Assert.Equal(90, loaded[0].ActivityMs!["sensores"]);
    }
}
