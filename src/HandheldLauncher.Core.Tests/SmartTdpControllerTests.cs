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
        // O reforço não volta de uma vez para os 16 W: depois de faltar FPS, a descida é a normal.
        for (var t = 106; t <= 130; t += 2) c.OnSample(60, t);
        Assert.True(c.Watts > 16, $"devolveu o reforço: {c.Watts} W");
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
        Assert.True(c.TargetUnreachable);
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
        Assert.True(c.TargetUnreachable);
        Assert.False(c.IsSettled);
    }

    [Fact]
    public void UnreachableTargetOnAGpuBoundGameStopsClimbing()
    {
        // Caso real (16/11, Days Gone): ~28 FPS de 14 W para cima, com metas de 35 a 55.
        // Sobe 6 W, vê que não adianta, volta e para de subir — em vez de ir ao teto.
        static double Game(int watts) => Math.Min(28.0, 28.0 * watts / 14);

        var c = new SmartTdpController(45, 8, 35, 20);
        var peakAfterFirstTry = 0;
        for (var t = 0.0; t < 100; t += 2)
        {
            c.OnSample(Game(c.Watts), t);
            if (t >= 20) peakAfterFirstTry = Math.Max(peakAfterFirstTry, c.Watts);
        }
        Assert.Equal(20, c.Watts);
        // Só o teste curto de +3 W (0.27 beta.69) passa dos 20 W, e volta logo.
        Assert.InRange(peakAfterFirstTry, 20, 23);
        Assert.True(c.TargetUnreachable);
        Assert.InRange(c.EffectiveTargetFps, 26, 28);
        Assert.False(c.IsSettled); // não vira "aprendido" para a meta de 45

        // Tenta de novo de tempos em tempos, mas nunca passa de +6 W nem fica lá.
        var peakLater = 0;
        double sumLater = 0;
        var nLater = 0;
        for (var t = 100.0; t < 600; t += 2) { c.OnSample(Game(c.Watts), t); peakLater = Math.Max(peakLater, c.Watts); sumLater += c.Watts; nLater++; }
        Assert.InRange(peakLater, 20, 26);
        Assert.True(sumLater / nLater <= 21, $"média de {sumLater / nLater:0.0} W");
        Assert.InRange(c.Watts, 20, 23); // pode terminar no meio de um teste de +3 W
    }

    [Fact]
    public void UnreachableTargetDoesNotDropPowerDuringALongPause()
    {
        // Pausa longa travada em 30 FPS num jogo que segura 60 com 20 W: o TDP não pode
        // descer na pausa (o jogo voltaria sem potência).
        var c = new SmartTdpController(60, 8, 35, 20, startIsLearned: true);
        for (var t = 0.0; t < 40; t += 2) c.OnSample(60, t);
        for (var t = 40.0; t < 440; t += 2) c.OnSample(30, t);
        Assert.True(c.Watts >= 20, $"desceu para {c.Watts} W na pausa");
    }

    [Fact]
    public void UnreachableTargetKeepsThePowerWhenMoreWattsStillGiveMoreFps()
    {
        // Jogo que não chega a 60 mas ganha FPS a cada watt (40 FPS no teto): fica no teto.
        var c = new SmartTdpController(60, 8, 35, 20);
        for (var t = 0.0; t < 300; t += 2) c.OnSample(40.0 * c.Watts / 35, t);
        Assert.Equal(35, c.Watts);
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

        for (var t = 80.0; t <= 100; t += 2) c.OnSample(60, t);
        Assert.InRange(c.Watts, 18, 20);
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
        // Termina fora de um replay (t = 644): no meio de um, o valor do instante é o da subida.
        for (var t = 0.0; t < 646; t += 2)
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
        Assert.InRange(c.Watts, 18, 22);
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

    /// <summary>
    /// Days Gone com meta 35 (log de 17/11): o FPS fica em ~33 com qualquer potência acima de
    /// ~25 W e oscila em volta de 33–34. Antes cada oscilação abaixo da meta subia de novo e o
    /// TDP passava a maior parte do tempo em 34–35 W para o mesmo FPS.
    /// </summary>
    [Fact]
    public void TargetJustAboveAFlatPlateauDoesNotLiveAtTheCeiling()
    {
        static double Game(int watts) => Math.Min(33.0, watts < 20 ? 30.0 * watts / 20 : 30 + 0.6 * (watts - 20));
        foreach (var start in new[] { 20, 34 })
        {
            var c = new SmartTdpController(35, 8, 35, start);
            long seed = 12345;
            var recent = new List<double>();
            double wattsSum = 0, fpsSum = 0;
            var count = 0;
            for (var t = 0.0; t < 1800; t += 2)
            {
                seed = (seed * 1103515245 + 12345) % 2147483648;
                recent.Add(Game(c.Watts) * (0.92 + 0.16 * seed / 2147483648.0));
                var last = recent.Skip(Math.Max(0, recent.Count - 3)).OrderBy(v => v).ToList();
                c.OnSample(last.Count == 3 ? last[1] : last[^1], t);
                if (t < 300) continue;
                wattsSum += c.Watts;
                fpsSum += Game(c.Watts);
                count++;
            }
            Assert.True(wattsSum / count <= 28, $"início {start} W: média {wattsSum / count:0.0} W");
            Assert.True(fpsSum / count >= 32, $"início {start} W: FPS médio {fpsSum / count:0.0}");
        }
    }

    [Fact]
    public void ProbeFindsWhenMorePowerHelpsAgainWhileUnreachable()
    {
        // Preso na placa de vídeo (28 FPS com qualquer potência); depois a cena muda e o jogo
        // passa a precisar de 30 W para 40 FPS. O teste de +3 W percebe e o controle volta a subir.
        var heavy = true;
        double Game(int watts) => heavy ? Math.Min(28.0, 28.0 * watts / 14) : Math.Min(42.0, 40.0 * watts / 30);
        var c = new SmartTdpController(40, 8, 35, 20);
        for (var t = 0.0; t < 200; t += 2) c.OnSample(Game(c.Watts), t);
        Assert.True(c.TargetUnreachable);
        heavy = false;
        for (var t = 200.0; t < 330; t += 2) c.OnSample(Game(c.Watts), t);
        Assert.True(c.Watts >= 28, $"ficou em {c.Watts} W");
    }

    /// <summary>
    /// EA FC (log de 17/11, beta.69): a cada lance parado o FPS cai para 25–45 por 10–20 s. Uma
    /// subida logo depois da volta de outra perdia a volta rápida, e o TDP descia de 1 em 1 W
    /// por minutos (33 → 28 W com o jogo a 70 FPS).
    /// </summary>
    [Fact]
    public void CutscenesRightAfterAReturnStillComeBackQuickly()
    {
        static double Play(int watts) => 58 + 1.4 * (watts - 18);
        var cuts = new[] { (60.0, 80.0), (120.0, 140.0), (165.0, 185.0) };
        var c = new SmartTdpController(60, 8, 35, 26);
        var ups = 0;
        for (var t = 0.0; t < 200; t += 2)
        {
            var fps = Play(c.Watts);
            foreach (var (from, to) in cuts)
                if (t >= from && t < to) fps = Math.Min(fps, 28 + (t - from) * 1.2);
            if (c.OnSample(fps, t) == SmartTdpChange.Up) ups++;
        }
        Assert.True(c.Watts <= 19, $"ficou em {c.Watts} W");
        Assert.True(ups <= 8, $"{ups} subidas");
    }

    [Fact]
    public void SuddenDropToHalfTheTargetDoesNotClimbAtOnce()
    {
        var c = new SmartTdpController(60, 8, 35, 18);
        for (var t = 0.0; t < 40; t += 2) c.OnSample(70, t);
        var before = c.Watts;
        for (var t = 40.0; t < 52; t += 2) Assert.NotEqual(SmartTdpChange.Up, c.OnSample(30, t));
        Assert.Equal(before, c.Watts);
        // Se durar além de 15 s, aí sim é falta de potência e sobe.
        var climbed = false;
        for (var t = 52.0; t < 80; t += 2) climbed |= c.OnSample(30, t) == SmartTdpChange.Up;
        Assert.True(climbed);
    }
}
