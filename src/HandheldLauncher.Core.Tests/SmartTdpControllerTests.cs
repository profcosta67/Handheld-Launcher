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
    public void ResumeBoostIsTemporaryWhenTheTargetHolds()
    {
        var c = new SmartTdpController(60, 8, 35, 12, startIsLearned: true);
        Assert.Equal(SmartTdpChange.None, c.OnSample(60, 0));

        Assert.Equal(18, c.PrepareForResume(nowSeconds: 100, boostWatts: 6));
        Assert.Equal(SmartTdpChange.None, c.OnSample(60, 102));
        Assert.Equal(SmartTdpChange.None, c.OnSample(60, 104));
        Assert.Equal(SmartTdpChange.Down, c.OnSample(60, 108));
        Assert.Equal(12, c.Watts); // jogo leve volta ao que já usava, sem piso de 20 W
    }

    [Fact]
    public void ResumeBoostStaysWhenFpsDropsAndClimbsAfterCooldown()
    {
        var c = new SmartTdpController(60, 8, 35, 16, startIsLearned: true);
        c.OnSample(60, 0);
        Assert.Equal(22, c.PrepareForResume(100, 6));

        // Engasgo logo na volta: espera a pausa normal antes de subir mais.
        Assert.Equal(SmartTdpChange.None, c.OnSample(35, 102));
        Assert.Equal(SmartTdpChange.Up, c.OnSample(35, 104));
        Assert.Equal(25, c.Watts);

        // O reforço não é devolvido depois de faltar FPS.
        for (var t = 106; t <= 114; t += 2) c.OnSample(60, t);
        Assert.True(c.Watts >= 23, $"devolveu o reforço: {c.Watts} W");
    }

    [Fact]
    public void ResumeDoesNotResetTheFutileClimbHold()
    {
        // Jogo que não chega na meta: a subida se mostra inútil e ele volta.
        var c = new SmartTdpController(40, 8, 35, 20);
        double t = 0;
        var reverted = false;
        for (; t < 120 && !reverted; t += 2)
            reverted = c.OnSample(30, t) == SmartTdpChange.Down && c.Watts == 20;
        Assert.True(reverted, "não detectou a subida inútil");

        // Voltar do QS não reabre a subida nem dá reforço.
        Assert.Equal(20, c.PrepareForResume(t, 6));
        for (var u = t + 2; u < t + 60; u += 2) c.OnSample(30, u);
        Assert.Equal(20, c.Watts);
    }

    [Fact]
    public void ReturningDuringAClimbOnACappedGameEndsWhereTheClimbStarted()
    {
        // Jogo travado em 30 FPS com meta 60: a primeira subida sai de 20 W.
        var c = new SmartTdpController(60, 8, 35, 20);
        double t = 0;
        for (; c.Watts == 20 && t < 60; t += 2) c.OnSample(30, t);
        Assert.Equal(23, c.Watts);

        // Abre e fecha o QS no meio da subida.
        c.PrepareForResume(t, 6);
        for (var u = t + 2; u < t + 100; u += 2) c.OnSample(30, u);
        Assert.Equal(20, c.Watts); // nem o reforço nem a subida ficam
    }

    [Fact]
    public void RepeatedReturnsDoNotStackTheBoost()
    {
        var c = new SmartTdpController(60, 8, 35, 12, startIsLearned: true);
        c.OnSample(60, 0);
        Assert.Equal(18, c.PrepareForResume(100, 6));
        Assert.Equal(18, c.PrepareForResume(103, 6));
        Assert.Equal(12, c.BaselineWatts);
        for (var t = 105; t <= 111; t += 2) c.OnSample(60, t);
        Assert.Equal(12, c.Watts);
    }

    [Fact]
    public void OneNoisySampleAfterReturningDoesNotKeepTheBoost()
    {
        var c = new SmartTdpController(60, 8, 35, 12, startIsLearned: true);
        c.OnSample(60, 0);
        c.PrepareForResume(100, 6);
        Assert.Equal(SmartTdpChange.None, c.OnSample(56, 102)); // um pouco abaixo, uma vez
        for (var t = 104; t <= 110; t += 2) c.OnSample(60, t);
        Assert.Equal(12, c.Watts);
    }

    [Fact]
    public void BoostCanBeDroppedWithoutSamples()
    {
        var c = new SmartTdpController(60, 8, 35, 14, startIsLearned: true);
        c.OnSample(60, 0);
        c.PrepareForResume(100, 6);
        Assert.True(c.ResumeBoostPending);
        Assert.Equal(14, c.DropResumeBoost(120));
        Assert.False(c.ResumeBoostPending);
        Assert.Null(c.DropResumeBoost(122));
    }

    [Fact]
    public void ResumeBoostNeverExceedsDeviceMaximum()
    {
        var c = new SmartTdpController(60, 8, 25, 23);
        c.OnSample(60, 0);
        Assert.Equal(25, c.PrepareForResume(100, boostWatts: 6));
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
    public void PauseOrReplayDoesNotPinTheCeiling()
    {
        // Caso real (15/11, EA FC): o jogo segura a meta com 20 W; numa pausa/replay o FPS
        // cai e sobe um pouco com a potência (não parece "inútil"), e o TDP vai ao teto.
        // Quando o jogo volta, tem de voltar aos 20 W — não ficar preso lá em cima.
        var c = new SmartTdpController(60, 8, 35, 20, startIsLearned: true);
        for (var t = 0.0; t < 40; t += 2) c.OnSample(60, t);
        for (var t = 40.0; t < 80; t += 2) c.OnSample(40 + (c.Watts - 20), t);
        Assert.Equal(35, c.Watts);

        for (var t = 80.0; t <= 92; t += 2) c.OnSample(60, t);
        Assert.Equal(20, c.Watts);
    }

    [Fact]
    public void ReturnThatFailsBecomesAFloorWithoutYoYo()
    {
        // O jogo passou a precisar de 26 W de verdade: a volta aos 20 W falha uma vez,
        // vira piso e ele se acomoda perto do que precisa, sem ficar indo e voltando.
        var c = new SmartTdpController(60, 8, 35, 20, startIsLearned: true);
        for (var t = 0.0; t < 40; t += 2) c.OnSample(60, t);
        Run(c, need: 26, target: 60, seconds: 160, start: 40);
        var changes = 0;
        for (var t = 200.0; t < 400; t += 2)
            if (c.OnSample(Fps(c.Watts, 26, 60), t) != SmartTdpChange.None) changes++;
        Assert.InRange(c.Watts, 25, 27);
        Assert.True(changes <= 3, $"{changes} trocas");
    }

    [Fact]
    public void FirstClimbFromAnUnprovenStartDoesNotJumpBack()
    {
        // Partiu baixo demais (10 W) e subiu: não volta para um valor que nunca serviu.
        var c = new SmartTdpController(60, 8, 35, 10);
        Run(c, need: 25, target: 60, seconds: 120);
        Assert.InRange(c.Watts, 24, 26);
    }

    [Fact]
    public void PauseDuringTheDescentIsNotTakenAsAFailedTest()
    {
        // Ainda descendo (sempre há uma descida recente) quando vem a pausa: a queda não é
        // culpa da descida, então não pode virar piso lá em cima.
        var c = new SmartTdpController(60, 8, 35, 30);
        for (var t = 0.0; t < 20; t += 2) c.OnSample(60, t);
        for (var t = 20.0; t < 60; t += 2) c.OnSample(40 + (c.Watts - 20), t);
        for (var t = 60.0; t < 200; t += 2) c.OnSample(Fps(c.Watts, 18, 60), t);
        Assert.InRange(c.Watts, 17, 24); // 17 W só de passagem, num teste de descida
    }

    [Fact]
    public void ReactsWhenTheGameReallyNeedsMoreAfterACappedPause()
    {
        // Pausa travada em 30 FPS (subida inútil, entra em espera) e depois o jogo passa a
        // precisar de 26 W: a espera não pode esconder a falta de potência por minutos.
        var c = new SmartTdpController(60, 8, 35, 20, startIsLearned: true);
        for (var t = 0.0; t < 40; t += 2) c.OnSample(60, t);
        for (var t = 40.0; t < 70; t += 2) c.OnSample(30, t);
        Assert.Equal(20, c.Watts);
        for (var t = 70.0; t < 84; t += 2) c.OnSample(Fps(c.Watts, 26, 60), t);
        Assert.True(c.Watts >= 23, $"não reagiu: {c.Watts} W");
        for (var t = 84.0; t < 260; t += 2) c.OnSample(Fps(c.Watts, 26, 60), t);
        Assert.InRange(c.Watts, 25, 27);
    }

    /// <summary>Jogo com vsync (log de 16/11, EA FC): 60 FPS a partir de 16 W, 30 FPS abaixo disso.</summary>
    private static double VsyncGame(int watts) => watts >= 16 ? 60 : 30;

    [Fact]
    public void StaleLearnedValueDoesNotHoldWhenFpsIsWellAboveTarget()
    {
        // Aprendeu 26 W para 40 FPS numa sessão antiga, mas o jogo dá 60 FPS com 16 W:
        // com folga clara não fica "estável" em 26 W.
        var c = new SmartTdpController(40, 8, 35, 26, startIsLearned: true);
        for (var t = 0.0; t < 120; t += 2) c.OnSample(VsyncGame(c.Watts), t);
        Assert.InRange(c.Watts, 16, 18);
    }

    [Fact]
    public void FrequentReplaysDoNotKeepThePowerHigh()
    {
        // Replays a ~28 FPS a cada minuto (mais potência não muda nada neles).
        var c = new SmartTdpController(40, 8, 35, 26, startIsLearned: true);
        double sum = 0;
        var n = 0;
        var learnedSomething = false;
        for (var t = 0.0; t < 600; t += 2)
        {
            var replay = t % 60 >= 46;
            c.OnSample(replay ? 28 : VsyncGame(c.Watts), t);
            learnedSomething |= c.IsSettled && c.Watts <= 18;
            if (t >= 120) { sum += c.Watts; n++; }
        }
        Assert.InRange(c.Watts, 15, 18);
        Assert.True(sum / n <= 20, $"média de {sum / n:0.0} W");
        Assert.True(learnedSomething, "nunca chegou a aprender um valor");
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
        Run(c, need: 15, target: 60, seconds: 50);
        Assert.Equal(22, c.Watts);
    }

    [Fact]
    public void StaleLearnedValueIsCorrectedEvenWithoutFpsSurplus()
    {
        // Caso real (16/11): aprendeu 34 W para 60 FPS, mas o jogo segura 60 com 20 W e é
        // limitado a 60 (não há "FPS sobrando" para denunciar o exagero).
        var c = new SmartTdpController(60, 8, 35, 34, startIsLearned: true);
        Run(c, need: 20, target: 60, seconds: 180);
        Assert.InRange(c.Watts, 19, 22);
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
