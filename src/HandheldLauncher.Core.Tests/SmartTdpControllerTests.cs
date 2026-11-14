using HandheldLauncher.Core.Services.SmartTdp;
using Xunit;

namespace HandheldLauncher.Core.Tests;

public class SmartTdpControllerTests
{
    /// <summary>Jogo de mentira: cumpre a meta a partir de `need` watts; abaixo disso cai proporcionalmente.</summary>
    private static double Fps(int watts, int need, int target) => watts >= need ? target : target * (double)watts / need;

    private static int Run(SmartTdpController c, int need, int target, double seconds, double start = 0)
    {
        for (var t = start; t < start + seconds; t += 2) c.OnSample(Fps(c.Watts, need, target), t);
        return c.Watts;
    }

    [Fact]
    public void DescendsWhileThereIsHeadroomAndSettlesJustAboveTheNeed()
    {
        var c = new SmartTdpController(60, 8, 35, 30);
        var watts = Run(c, need: 18, target: 60, seconds: 600);
        Assert.InRange(watts, 18, 21);
        Assert.True(c.IsSettled);
    }

    [Fact]
    public void RisesQuicklyWhenTheGameDropsBelowTarget()
    {
        var c = new SmartTdpController(60, 8, 35, 10);
        var watts = Run(c, need: 25, target: 60, seconds: 60);
        Assert.True(watts >= 25, $"ficou em {watts} W");
    }

    [Fact]
    public void NeverLeavesTheDeviceWindow()
    {
        var c = new SmartTdpController(60, 8, 35, 20);
        for (var t = 0.0; t < 900; t += 2)
        {
            c.OnSample(t % 200 < 100 ? 20 : 60, t);
            Assert.InRange(c.Watts, 8, 35);
        }
    }

    [Fact]
    public void IgnoresLoadingScreens()
    {
        var c = new SmartTdpController(60, 8, 35, 20);
        for (var t = 0.0; t < 120; t += 2) Assert.Equal(SmartTdpChange.None, c.OnSample(3, t));
        Assert.Equal(20, c.Watts);
    }

    [Fact]
    public void OneIsolatedDipDoesNotRaiseThePower()
    {
        var c = new SmartTdpController(60, 8, 35, 20);
        c.OnSample(60, 0);
        Assert.Equal(SmartTdpChange.None, c.OnSample(40, 2));
        Assert.Equal(SmartTdpChange.None, c.OnSample(60, 4));
        Assert.Equal(20, c.Watts);
    }

    [Fact]
    public void LearnedStartDoesNotProbeDownRightAway()
    {
        var c = new SmartTdpController(60, 8, 35, 22, startIsLearned: true);
        Run(c, need: 15, target: 60, seconds: 120);
        Assert.Equal(22, c.Watts);
    }

    [Fact]
    public void DoesNotOscillateAroundTheNeed()
    {
        var c = new SmartTdpController(60, 8, 35, 30);
        Run(c, need: 18, target: 60, seconds: 300);
        var changes = 0;
        for (var t = 300.0; t < 420; t += 2)
            if (c.OnSample(Fps(c.Watts, 18, 60), t) != SmartTdpChange.None) changes++;
        // No máximo uma sondagem curta (desce 1 W, percebe, volta) nesse intervalo.
        Assert.True(changes <= 2, $"{changes} trocas em 2 minutos");
        Assert.InRange(c.Watts, 17, 19);
    }
}
