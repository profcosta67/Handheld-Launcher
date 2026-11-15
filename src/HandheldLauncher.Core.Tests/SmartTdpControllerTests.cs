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
        // Com a folga de 5% na meta, 24 W (57,6 FPS de 60) já conta como meta cumprida.
        Assert.True(watts >= 24, $"ficou em {watts} W");
    }

    [Fact]
    public void BoostsImmediatelyWhenReturningFromQuickSettings()
    {
        var c = new SmartTdpController(60, 8, 35, 16, startIsLearned: true);
        var resumed = c.PrepareForResume(nowSeconds: 100, minimumWatts: 20, boostWatts: 6);

        Assert.Equal(22, resumed);
        Assert.Equal(22, c.Watts);

        // Com a carga do jogo de volta, a próxima amostra baixa já pode iniciar a recuperação.
        Assert.Equal(SmartTdpChange.None, c.OnSample(35, 100));
        Assert.Equal(SmartTdpChange.Up, c.OnSample(35, 102));
        Assert.Equal(25, c.Watts);
    }

    [Fact]
    public void ResumeBoostNeverExceedsDeviceMaximum()
    {
        var c = new SmartTdpController(60, 8, 25, 23);
        Assert.Equal(25, c.PrepareForResume(100, minimumWatts: 20, boostWatts: 6));
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
    public void IgnoresShortLoadingScreens()
    {
        var c = new SmartTdpController(60, 8, 35, 20);
        for (var t = 0.0; t < 18; t += 2) Assert.Equal(SmartTdpChange.None, c.OnSample(3, t));
        Assert.Equal(20, c.Watts);
    }

    [Fact]
    public void GameCappedBelowTargetDoesNotClimbToTheCeiling()
    {
        // Jogo travado em 30 FPS com meta 60: mais potência não muda nada.
        var c = new SmartTdpController(60, 8, 35, 20);
        for (var t = 0.0; t < 120; t += 2) c.OnSample(30, t);
        Assert.Equal(20, c.Watts);
        Assert.False(c.IsSettled);
    }

    [Fact]
    public void HeavySceneAtTheCeilingIsNotLearnedAsTheGameNeed()
    {
        var c = new SmartTdpController(60, 8, 35, 33);
        for (var t = 0.0; t < 60; t += 2) c.OnSample(40, t);   // cena pesada: nem o teto resolve
        for (var t = 60.0; t < 100; t += 2) c.OnSample(60, t); // voltou ao normal
        Assert.False(c.IsSettled);
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
    public void FindsALowNeedFromAHighStartWithinTwoMinutes()
    {
        // Caso real (14/11): jogo que segura 30 FPS com 20 W, modo inteligente partindo de 34 W.
        var c = new SmartTdpController(30, 8, 35, 34);
        var watts = Run(c, need: 20, target: 30, seconds: 120);
        Assert.InRange(watts, 19, 23);
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
