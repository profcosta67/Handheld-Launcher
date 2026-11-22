using HandheldLauncher.Core.Services.SmartTdp;
using Xunit;
using Xunit.Abstractions;

namespace HandheldLauncher.Core.Tests;

/// <summary>
/// 22/11 — o TDP inteligente v2 precisa passar em TODOS os cenários do banco fixo. Se uma mudança
/// no controle piorar qualquer um (watts sobrando, FPS faltando ou trocas demais), o build falha.
/// </summary>
public class SmartTdpScenarioBankTests
{
    private readonly ITestOutputHelper _out;
    public SmartTdpScenarioBankTests(ITestOutputHelper output) => _out = output;

    private const int Seeds = 12;

    private static ISmartTdpController V2(int target, int min, int max, int start) =>
        new SmartTdpControllerV2(target, min, max, start);

    private static ISmartTdpController V1(int target, int min, int max, int start) =>
        new SmartTdpController(target, min, max, start);

    public static IEnumerable<object[]> ScenarioNames() =>
        SmartTdpScenarioBank.All.Select(s => new object[] { s.Name });

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void V2PassesScenario(string name)
    {
        var scenario = SmartTdpScenarioBank.All.Single(s => s.Name == name);
        var results = Enumerable.Range(0, Seeds).Select(seed => SmartTdpScenarioBank.Run(V2, scenario, seed)).ToList();
        var excess = results.Average(r => r.Excess);
        var starve = results.Average(r => r.Starve);
        var cpm = results.Average(r => r.ChangesPerMinute);
        _out.WriteLine($"{name}: média {results.Average(r => r.AvgW):0.0} W (ideal {results.Average(r => r.Ideal):0.0}), sobra {excess:0.00} W, fome {starve:0.0}% (pior {results.Max(r => r.Starve):0.0}%), trocas {cpm:0.00}/min, FPS {results.Average(r => r.Fps):0.0}");

        Assert.True(excess <= scenario.Limits.MaxExcessWatts, $"{name}: {excess:0.00} W acima do ideal (limite {scenario.Limits.MaxExcessWatts})");
        Assert.True(starve <= scenario.Limits.MaxStarvePercent, $"{name}: {starve:0.0}% do tempo sem o FPS que dava (limite {scenario.Limits.MaxStarvePercent})");
        Assert.True(cpm <= scenario.Limits.MaxChangesPerMinute, $"{name}: {cpm:0.00} trocas por minuto (limite {scenario.Limits.MaxChangesPerMinute})");
    }

    /// <summary>
    /// O porte para C# é exato: mesma soma de watts e mesmas trocas que o Python (tools/tdp-bank).
    /// Se isto falhar, o banco em Python deixou de valer como ferramenta de ajuste.
    /// </summary>
    [Theory]
    [InlineData("leve", 0, 6885, 24)]
    [InlineData("leve", 5, 6882, 28)]
    [InlineData("medio", 0, 14163, 33)]
    [InlineData("medio", 5, 14222, 24)]
    [InlineData("pesado-alcanca", 0, 20628, 25)]
    [InlineData("pesado-alcanca", 5, 20823, 23)]
    [InlineData("teto-placa", 0, 18791, 37)]
    [InlineData("teto-placa", 5, 18107, 43)]
    [InlineData("meta-pouco-acima", 0, 19845, 38)]
    [InlineData("meta-pouco-acima", 5, 20334, 27)]
    [InlineData("cortes-de-cena", 0, 10798, 45)]
    [InlineData("cortes-de-cena", 5, 11024, 46)]
    [InlineData("replay-30s", 0, 10610, 38)]
    [InlineData("replay-30s", 5, 10826, 61)]
    [InlineData("menus", 0, 13591, 49)]
    [InlineData("menus", 5, 14082, 31)]
    [InlineData("vsync-60-30", 0, 14985, 16)]
    [InlineData("vsync-60-30", 5, 14986, 16)]
    [InlineData("area-pesada", 0, 17269, 54)]
    [InlineData("area-pesada", 5, 16995, 44)]
    [InlineData("pausas", 0, 14175, 33)]
    [InlineData("pausas", 5, 14228, 22)]
    [InlineData("cenas-alternadas", 0, 18436, 47)]
    [InlineData("cenas-alternadas", 5, 18526, 40)]
    [InlineData("limitador", 0, 14415, 34)]
    [InlineData("limitador", 5, 14317, 24)]
    [InlineData("comeca-no-teto", 0, 10979, 39)]
    [InlineData("comeca-no-teto", 5, 11089, 27)]
    [InlineData("teto-comeca-alto", 0, 19302, 42)]
    [InlineData("teto-comeca-alto", 5, 19605, 34)]
    [InlineData("parede-mais-pesada", 0, 20770, 56)]
    [InlineData("parede-mais-pesada", 5, 20507, 47)]
    [InlineData("inalcancavel-oscila", 0, 23707, 43)]
    [InlineData("inalcancavel-oscila", 5, 22447, 55)]
    public void PortMatchesPython(string name, int seed, long expectedWattSum, int expectedChanges)
    {
        var scenario = SmartTdpScenarioBank.All.Single(s => s.Name == name);
        SmartTdpScenarioBank.Run(V2, scenario, seed, out var wattSum, out var changes);
        Assert.Equal(expectedWattSum, wattSum);
        Assert.Equal(expectedChanges, changes);
    }

    [Fact]
    public void LcgMatchesPython()
    {
        var r = new SmartTdpScenarioBank.Lcg(13);
        Assert.Equal(0.6247499203239011, r.Random());
        Assert.Equal(0.7106486437284186, r.Random());
        Assert.Equal(0.2590604934379118, r.Random());
    }

    /// <summary>Só para comparar no log: o controle antigo no mesmo banco (sem cobrar limites).</summary>
    [Fact]
    public void V1ForComparison()
    {
        foreach (var scenario in SmartTdpScenarioBank.All)
        {
            var results = Enumerable.Range(0, Seeds).Select(seed => SmartTdpScenarioBank.Run(V1, scenario, seed)).ToList();
            _out.WriteLine($"v1 {scenario.Name}: média {results.Average(r => r.AvgW):0.0} W, sobra {results.Average(r => r.Excess):0.00} W, fome {results.Average(r => r.Starve):0.0}%, trocas {results.Average(r => r.ChangesPerMinute):0.00}/min");
        }
    }

    [Fact]
    public void ResumeBoostReturnsWhenTargetHolds()
    {
        var c = new SmartTdpControllerV2(60, 8, 35, 19);
        for (var t = 20.0; t < 60; t += 2) c.OnSample(60, t);
        var before = c.Watts;
        var boosted = c.PrepareForResume(60, 6);
        Assert.Equal(Math.Min(35, before + 6), boosted);
        Assert.True(c.ResumeBoostPending);
        Assert.Equal(before, c.BaselineWatts);
        for (var t = 62.0; t < 72; t += 2) c.OnSample(60, t);
        Assert.False(c.ResumeBoostPending);
        Assert.Equal(before, c.Watts);
    }

    [Fact]
    public void ResumeBoostDropsOnLoadingScreen()
    {
        // log do beta.74: volta do QS numa tela de carregamento (23 FPS de meta 60) mantinha o reforço
        var c = new SmartTdpControllerV2(60, 8, 35, 19);
        for (var t = 20.0; t < 60; t += 2) c.OnSample(60, t);
        var before = c.Watts;
        c.PrepareForResume(60, 6);
        for (var t = 62.0; t < 72; t += 2) c.OnSample(23, t);
        Assert.False(c.ResumeBoostPending);
        Assert.Equal(before, c.Watts);
    }

    [Fact]
    public void ResumeBoostStaysWhenFpsFalls()
    {
        var c = new SmartTdpControllerV2(60, 8, 35, 19);
        for (var t = 20.0; t < 60; t += 2) c.OnSample(60, t);
        var boosted = c.PrepareForResume(60, 6);
        for (var t = 62.0; t < 72; t += 2) c.OnSample(40, t);
        Assert.False(c.ResumeBoostPending);
        Assert.True(c.Watts >= boosted);   // o reforço era preciso: fica (ou sobe mais)
    }

    [Fact]
    public void DropResumeBoostGivesBackTheWatts()
    {
        var c = new SmartTdpControllerV2(60, 8, 35, 19);
        for (var t = 20.0; t < 60; t += 2) c.OnSample(60, t);
        var before = c.Watts;
        c.PrepareForResume(60, 6);
        Assert.Equal(before, c.DropResumeBoost(61));
        Assert.Equal(before, c.Watts);
        Assert.Null(c.DropResumeBoost(62));
    }

    [Fact]
    public void LoadingScreensDoNotMoveTheWatts()
    {
        var c = new SmartTdpControllerV2(60, 8, 35, 20);
        for (var t = 20.0; t < 200; t += 2)
            Assert.Equal(SmartTdpChange.None, c.OnSample(5, t));
        Assert.Equal(20, c.Watts);
    }
}
