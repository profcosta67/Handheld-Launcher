using HandheldLauncher.Core.Services;
using Xunit;

namespace HandheldLauncher.Core.Tests;

public class TdpLimitsTests
{
    private static readonly TdpLimits LegionGo2Ac = new(5, 35, 37, 45, OnAc: true, "teste");

    [Theory]
    [InlineData(35, 35, 37, 45)] // o "Personalizado no máximo" da Legion Space
    [InlineData(20, 20, 21, 26)]
    [InlineData(8, 8, 8, 10)]
    [InlineData(50, 35, 37, 45)] // acima do teto → teto
    [InlineData(1, 5, 5, 6)]     // abaixo do mínimo → mínimo
    public void PeaksFor_FollowsManufacturerRatio(int asked, int spl, int sppt, int fppt)
    {
        Assert.Equal((spl, sppt, fppt), LegionGo2Ac.PeaksFor(asked));
    }

    [Fact]
    public void PeaksFor_NeverExceedsEachMaxNorGoesBelowSpl()
    {
        var limits = new TdpLimits(7, 30, 43, 53, OnAc: true, "teste"); // ROG Ally na tomada
        for (var w = 0; w <= 60; w++)
        {
            var (spl, sppt, fppt) = limits.PeaksFor(w);
            Assert.InRange(spl, 7, 30);
            Assert.InRange(sppt, spl, 43);
            Assert.InRange(fppt, sppt, 53);
        }
    }

    [Fact]
    public void Fallback_IsTheOldSafetyLockWithAllThreeEqual()
    {
        var fb = TdpLimits.Fallback(onAc: true);
        Assert.True(fb.IsFallback);
        Assert.True(fb.IsSane);
        Assert.Equal((30, 30, 30), fb.PeaksFor(30));
        Assert.Equal((18, 18, 18), fb.PeaksFor(18));
        Assert.Equal((30, 30, 30), fb.PeaksFor(45));
    }

    [Theory]
    [InlineData(5, 35, 37, 45, true)]
    [InlineData(7, 25, 30, 35, true)]   // Ally na bateria
    [InlineData(0, 35, 37, 45, false)]  // mínimo abaixo de 5
    [InlineData(5, 35, 30, 45, false)]  // SPPT menor que SPL
    [InlineData(5, 35, 37, 120, false)] // leitura absurda
    [InlineData(40, 35, 37, 45, false)] // mínimo acima do máximo
    public void IsSane_RejectsImpossibleNumbers(int min, int spl, int sppt, int fppt, bool expected)
    {
        Assert.Equal(expected, new TdpLimits(min, spl, sppt, fppt, OnAc: true, "teste").IsSane);
    }
}
