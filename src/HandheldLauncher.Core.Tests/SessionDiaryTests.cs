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
}
