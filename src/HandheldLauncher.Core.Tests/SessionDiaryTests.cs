using HandheldLauncher.Core.Services.SessionDiary;
using Xunit;

namespace HandheldLauncher.Core.Tests;

public class SessionDiaryTests
{
    private static readonly DateTime Start = new(2026, 11, 16, 20, 0, 0, DateTimeKind.Utc);

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"session-diary-{Guid.NewGuid():N}.jsonl");

    private static SessionRecord Session(string id, int day, double watts, int batterySeconds = 1800, double? fps = 60) => new()
    {
        GameId = id,
        Title = id,
        StartedUtc = Start.AddDays(day),
        DurationSeconds = Math.Max(batterySeconds, 600),
        BatterySeconds = batterySeconds,
        AvgBatteryWatts = watts,
        AvgFps = fps,
    };

    [Fact]
    public void AccumulatorAveragesOnlyBatteryTime()
    {
        var acc = new SessionAccumulator("fc", "FC", Start);
        for (var i = 0; i < 60; i++) acc.Add(5, onAc: false, dischargeWatts: 30, batteryPercent: 80 - i / 6, fps: 60, tdpWatts: 20);
        // Na tomada: o consumo não conta (nem leituras negativas de carga).
        for (var i = 0; i < 60; i++) acc.Add(5, onAc: true, dischargeWatts: -45, batteryPercent: 75, fps: 60, tdpWatts: 30);
        for (var i = 0; i < 60; i++) acc.Add(5, onAc: false, dischargeWatts: 20, batteryPercent: 74, fps: 30, tdpWatts: 10);

        var record = acc.Build(Start.AddSeconds(900));
        Assert.Equal(900, record.DurationSeconds);
        Assert.Equal(600, record.BatterySeconds);
        Assert.Equal(25, record.AvgBatteryWatts);
        Assert.Equal(30, record.PeakBatteryWatts);
        Assert.Equal(80, record.BatteryStartPercent);
        Assert.Equal(74, record.BatteryEndPercent);
        Assert.Equal(50, record.AvgFps);
        Assert.Equal(30, record.LowFps);
        Assert.Equal(20, record.AvgTdpWatts);
        Assert.False(record.SmartTdp);
    }

    [Fact]
    public void AccumulatorKeepsUnknownsAsNull()
    {
        var acc = new SessionAccumulator("x", "X", Start);
        acc.Add(5, onAc: true, dischargeWatts: null, batteryPercent: null, fps: null, tdpWatts: null);
        acc.Add(3600, onAc: false, dischargeWatts: 30, batteryPercent: 50, fps: 60, tdpWatts: 20); // salto de relógio
        var record = acc.Build(Start.AddSeconds(300));
        Assert.Null(record.AvgBatteryWatts);
        Assert.Null(record.AvgFps);
        Assert.Null(record.AvgTdpWatts);
        Assert.Equal(0, record.BatterySeconds);
    }

    [Fact]
    public void AccumulatorRemembersSmartTdpTarget()
    {
        var acc = new SessionAccumulator("fc", "FC", Start);
        acc.Add(5, false, 25, 90, 60, 20);
        acc.Add(5, false, 25, 90, 60, 18, smartTdp: true, targetFps: 60);
        var record = acc.Build(Start.AddSeconds(200));
        Assert.True(record.SmartTdp);
        Assert.Equal(60, record.TargetFps);
    }

    [Fact]
    public void StoreAppendsAndReadsBackPerGame()
    {
        var path = TempPath();
        try
        {
            var store = new SessionDiaryStore(path);
            Assert.True(store.Append(Session("fc", 0, 25)));
            Assert.True(store.Append(Session("dg", 1, 40)));
            Assert.False(store.Append(new SessionRecord { GameId = "fc", DurationSeconds = 30 })); // curta demais
            File.AppendAllText(path, "{ linha cortada\n");
            Assert.True(store.Append(Session("fc", 2, 27)));

            var reread = new SessionDiaryStore(path);
            Assert.Equal(3, reread.Load().Count);
            Assert.Equal(2, reread.ForGame("fc").Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SummaryNeedsBatteryTime()
    {
        Assert.Null(SessionDiaryStore.Summarize(Array.Empty<SessionRecord>(), 74));
        // Só na tomada, ou poucos segundos na bateria: sem consumo para mostrar.
        var plugged = new[] { new SessionRecord { GameId = "fc", StartedUtc = Start, DurationSeconds = 3600 }, Session("fc", 1, 30, batterySeconds: 60) };
        Assert.Null(SessionDiaryStore.Summarize(plugged, 74));
    }

    [Fact]
    public void SummaryWeighsByTimeAndEstimatesRuntime()
    {
        var sessions = new[] { Session("fc", 0, 20, batterySeconds: 3000), Session("fc", 1, 30, batterySeconds: 1000) };
        var summary = SessionDiaryStore.Summarize(sessions, fullChargeWattHours: 74);
        Assert.NotNull(summary);
        Assert.Equal(2, summary!.Sessions);
        Assert.Equal(22.5, summary.TypicalWatts);
        Assert.Equal(3.3, summary.EstimatedHours);
        Assert.True(summary.IsEarlyEstimate);
        Assert.False(summary.LastAboveNormal); // poucas sessões: não acusa nada

        Assert.Null(SessionDiaryStore.Summarize(sessions, fullChargeWattHours: null)!.EstimatedHours);
    }

    [Fact]
    public void SummaryFlagsASessionWellAboveTheUsual()
    {
        var usual = new[] { Session("fc", 0, 24), Session("fc", 1, 26), Session("fc", 2, 25), Session("fc", 3, 25) };
        Assert.False(SessionDiaryStore.Summarize(usual, 74)!.LastAboveNormal);

        var spike = usual.Append(Session("fc", 4, 34)).ToArray();
        var summary = SessionDiaryStore.Summarize(spike, 74)!;
        Assert.True(summary.LastAboveNormal);
        Assert.Equal(34, summary.LastWatts);
        Assert.False(summary.IsEarlyEstimate);

        // 10% a mais não é "fora do normal".
        var mild = usual.Append(Session("fc", 4, 27.5)).ToArray();
        Assert.False(SessionDiaryStore.Summarize(mild, 74)!.LastAboveNormal);
    }

    [Fact]
    public void HoursNowScaleWithTheCurrentCharge()
    {
        Assert.Equal(1.48, SessionDiaryStore.EstimateHoursNow(25, 74, 50));
        Assert.Equal(0, SessionDiaryStore.EstimateHoursNow(25, 74, 0));
        Assert.Null(SessionDiaryStore.EstimateHoursNow(25, null, 50));
        Assert.Null(SessionDiaryStore.EstimateHoursNow(25, 74, null));
        Assert.Null(SessionDiaryStore.EstimateHoursNow(0, 74, 50));
        Assert.Null(SessionDiaryStore.EstimateHoursNow(25, 74, 255)); // "desconhecido" do Windows
    }

    [Fact]
    public void HistoryLineShowsOnlyWhatWasMeasured()
    {
        var onBattery = new SessionRecord
        {
            GameId = "fc", StartedUtc = new DateTime(2026, 11, 17, 2, 30, 0, DateTimeKind.Utc), DurationSeconds = 4800,
            BatterySeconds = 4800, AvgBatteryWatts = 24.46, AvgFps = 58.4, BatteryStartPercent = 80, BatteryEndPercent = 45,
            SmartTdp = true, TargetFps = 60,
        };
        // 02:30 UTC ainda é dia 16 em Campo Grande (UTC-4).
        Assert.Equal("16/11 · 1h20 · 24,5 W · 58 FPS · bateria 80% → 45% · Auto 60 FPS",
            SessionDiaryStore.DescribeSession(onBattery, TimeSpan.FromHours(-4)));

        var plugged = new SessionRecord
        {
            GameId = "fc", StartedUtc = new DateTime(2026, 11, 17, 15, 0, 0, DateTimeKind.Utc), DurationSeconds = 1500,
            AvgTdpWatts = 30, BatteryStartPercent = 100, BatteryEndPercent = 100,
        };
        Assert.Equal("17/11 · 25 min · na tomada · TDP 30 W", SessionDiaryStore.DescribeSession(plugged, TimeSpan.FromHours(-4)));
    }

    [Fact]
    public void AllGamesAreListedByTimeOnBattery()
    {
        var sessions = new[]
        {
            Session("fc", 0, 20, batterySeconds: 1000), Session("dg", 1, 40, batterySeconds: 3000),
            Session("fc", 2, 22, batterySeconds: 1000), Session("plug", 3, 0, batterySeconds: 0),
        };
        var rows = SessionDiaryStore.SummarizeAll(sessions, 74);
        Assert.Equal(new[] { "dg", "fc" }, rows.Select(r => r.GameId).ToArray());
        Assert.Equal(40, rows[0].Summary.TypicalWatts);
        Assert.Equal(21, rows[1].Summary.TypicalWatts);
        Assert.Equal(2000, rows[1].BatterySeconds);
    }

    [Fact]
    public void BatteryWearKeepsOneReadingAWeekAndShowsTheTrend()
    {
        var path = Path.Combine(Path.GetTempPath(), $"battery-wear-{Guid.NewGuid():N}.jsonl");
        try
        {
            var log = new BatteryWearLog(path);
            Assert.False(log.Note(Start, null, 72000));
            Assert.True(log.Note(Start, 74000, 73000));
            Assert.False(log.Note(Start.AddDays(3), 74000, 72900));
            Assert.True(log.Note(Start.AddDays(8), 74000, 72800));
            File.AppendAllText(path, "{corte");
            var entries = new BatteryWearLog(path).Load();
            Assert.Equal(2, entries.Count);
            Assert.Equal(99, entries[0].Percent);

            Assert.Null(BatteryWearLog.Trend(entries, Start.AddDays(10), 74000, 72800)); // cedo demais
            var trend = BatteryWearLog.Trend(entries, Start.AddDays(90), 74000, 70300)!;
            Assert.Equal(99, trend.FirstPercent);
            Assert.Equal(95, trend.NowPercent);
            Assert.Equal(70.3, trend.NowWattHours);
            Assert.Null(BatteryWearLog.Trend(Array.Empty<BatteryWearEntry>(), Start, 74000, 70000));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static SessionRecord Curve(int day, params (int Watts, int Seconds, double Fps)[] points) => new()
    {
        GameId = "g", Title = "g", StartedUtc = Start.AddDays(day), DurationSeconds = points.Sum(p => p.Seconds),
        FpsByTdp = points.Select(p => new TdpFpsBucket { Watts = p.Watts, Seconds = p.Seconds, AvgFps = p.Fps }).ToList(),
    };

    [Fact]
    public void AccumulatorKeepsFpsPerTdp()
    {
        var acc = new SessionAccumulator("fc", "FC", Start);
        for (var i = 0; i < 20; i++) acc.Add(5, onAc: true, dischargeWatts: null, batteryPercent: 100, fps: 40, tdpWatts: 15);
        for (var i = 0; i < 40; i++) acc.Add(5, onAc: true, dischargeWatts: null, batteryPercent: 100, fps: 60, tdpWatts: 17);
        acc.Add(5, onAc: true, dischargeWatts: null, batteryPercent: 100, fps: null, tdpWatts: 30); // sem FPS: não entra
        var record = acc.Build(Start.AddMinutes(6));
        Assert.Equal(2, record.FpsByTdp!.Count);
        Assert.Equal((15, 100, 40.0, 40.0), (record.FpsByTdp[0].Watts, record.FpsByTdp[0].Seconds, record.FpsByTdp[0].AvgFps, record.FpsByTdp[0].MedianFps));
        Assert.Equal((17, 200, 60.0), (record.FpsByTdp[1].Watts, record.FpsByTdp[1].Seconds, record.FpsByTdp[1].AvgFps));
    }

    [Fact]
    public void GuideRecommendsTheLowestTdpThatHoldsEachRate()
    {
        // Jogo leve com limite em 60: 30 FPS a 10 W, 58 a 17 W, parado em 60 dali em diante.
        var sessions = new[]
        {
            Curve(0, (10, 300, 31), (13, 300, 44), (17, 600, 59)),
            Curve(1, (20, 300, 60), (25, 300, 60), (17, 300, 59)),
            Curve(2, (25, 300, 60)),
        };
        var guide = EnergyGuide.Build(sessions, deviceMaxWatts: 35)!;
        Assert.Equal(60, guide.MaxFps);
        Assert.Equal(17, guide.MaxFpsWatts); // 59 já está a 3% do máximo
        Assert.Equal(new EnergyTargetAdvice(30, EnergyAdviceKind.Recommended, 10), guide.Targets[0]);
        Assert.Equal(new EnergyTargetAdvice(60, EnergyAdviceKind.Recommended, 17), guide.Targets[1]);
        // 25 W não rendeu mais que 17 W: mais TDP não leva a 120.
        Assert.Equal(EnergyAdviceKind.NotReached, guide.Targets[2].Kind);
        Assert.Equal("30 FPS: 10 W · 60 FPS: 17 W · 120 FPS: não chega", guide.ShortText());
    }

    [Fact]
    public void GuideSaysNotReachedOnlyWhenTheCeilingOrAPlateauWasMeasured()
    {
        // Jogo pesado medido até o teto: 30 FPS a 26 W, 41 a 34 W.
        var heavy = EnergyGuide.Build(new[] { Curve(0, (20, 300, 24), (26, 300, 30), (34, 300, 41)) }, deviceMaxWatts: 35)!;
        Assert.Equal(new EnergyTargetAdvice(30, EnergyAdviceKind.Recommended, 26), heavy.Targets[0]);
        Assert.Equal(EnergyAdviceKind.NotReached, heavy.Targets[1].Kind);
        Assert.Equal(41, heavy.MaxFps);
        Assert.Equal(34, heavy.MaxFpsWatts);

        // Só medido com TDP baixo e ainda subindo: não dá para afirmar nada sobre 60.
        var partial = EnergyGuide.Build(new[] { Curve(0, (12, 300, 29), (15, 300, 36)) }, deviceMaxWatts: 35)!;
        Assert.Equal(EnergyAdviceKind.Recommended, partial.Targets[0].Kind);
        Assert.Equal(EnergyAdviceKind.NotMeasured, partial.Targets[1].Kind);
        Assert.Equal("30 FPS: 12 W · 60 FPS: sem medição · 120 FPS: sem medição", partial.ShortText());
    }

    [Fact]
    public void GuideNeedsAMinuteAtATdpAndIgnoresOldSessionsWithoutTheCurve()
    {
        Assert.Null(EnergyGuide.Build(new[] { Session("g", 0, 25) }, 35));
        Assert.Null(EnergyGuide.Build(new[] { Curve(0, (20, 30, 60)) }, 35));
        // 30 s + 40 s em sessões diferentes somam.
        Assert.NotNull(EnergyGuide.Build(new[] { Curve(0, (20, 30, 60)), Curve(1, (20, 40, 60)) }, 35));
    }

    [Fact]
    public void GuideIgnoresALowTdpThatOnlyLookedFastInMenus()
    {
        // 8 W "rendeu" 70 FPS em menus, 12 W fez 50 no jogo: 60 FPS precisa de 16 W.
        var guide = EnergyGuide.Build(new[] { Curve(0, (8, 300, 70), (12, 300, 50), (16, 300, 62)) }, 35)!;
        Assert.Equal(new EnergyTargetAdvice(60, EnergyAdviceKind.Recommended, 16), guide.Targets[1]);
    }

    [Fact]
    public void GuideDoesNotClaimNotReachedFromALoneLoadingScreenAtTheTop()
    {
        // Um minuto a 30 W com 20 FPS (carregamento) não prova que 60 é impossível.
        var guide = EnergyGuide.Build(new[] { Curve(0, (15, 600, 40), (24, 600, 45), (30, 60, 20)) }, 35)!;
        Assert.Equal(EnergyAdviceKind.NotMeasured, guide.Targets[1].Kind);
        Assert.True(guide.HasAnyMeasurement);
    }

    [Fact]
    public void GuideSkipsSessionsBeforeAReset()
    {
        var sessions = new[] { Curve(0, (30, 600, 60)), Curve(5, (18, 600, 61)) };
        Assert.Equal(18, EnergyGuide.Build(sessions, 35, new[] { 60 })!.Targets[0].Watts);
        Assert.Null(EnergyGuide.Build(new[] { Curve(0, (30, 600, 60)) }, 35, notBeforeUtc: Start.AddDays(1)));
    }

    [Fact]
    public void DurationLeavesOutTimeAsleep()
    {
        var acc = new SessionAccumulator("fc", "FC", Start);
        for (var i = 0; i < 24; i++) acc.Add(5, onAc: true, dischargeWatts: null, batteryPercent: 90, fps: 60, tdpWatts: 15);
        acc.Add(8 * 3600, onAc: true, dischargeWatts: null, batteryPercent: 60, fps: null, tdpWatts: 15); // suspenso a noite toda
        for (var i = 0; i < 24; i++) acc.Add(5, onAc: true, dischargeWatts: null, batteryPercent: 60, fps: 60, tdpWatts: 15);
        Assert.Equal(240, acc.Build(Start.AddHours(8).AddMinutes(4)).DurationSeconds);
    }
}
