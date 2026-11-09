using HandheldLauncher.Core.Services;
using Xunit;

namespace HandheldLauncher.Core.Tests;

/// <summary>
/// QS 2.0 (F1): as três resoluções por aparelho, sem listar modos que o painel
/// não tem. Pedido do dono (09/11): Legion 900p/1080p/1200p, ROG 720p/900p/1080p.
/// </summary>
public class ScreenPresetsTests
{
    [Theory]
    [InlineData("legion-go")]
    [InlineData("legion-go-2")]
    public void Legion_Offers900p1080pAnd1200p(string deviceId)
    {
        var presets = ScreenPresets.ForDevice(deviceId);

        Assert.Equal(["900p", "1080p", "1200p"], presets.Select(p => p.Label));
        Assert.Equal(2560, presets[^1].Width);
        Assert.Equal(1600, presets[^1].Height);
    }

    [Theory]
    [InlineData("rog-ally")]
    [InlineData("rog-ally-x")]
    public void RogAlly_Offers720p900pAnd1080p(string deviceId)
    {
        var presets = ScreenPresets.ForDevice(deviceId);

        Assert.Equal(["720p", "900p", "1080p"], presets.Select(p => p.Label));
        Assert.Equal(1280, presets[0].Width);
    }

    [Fact]
    public void UnknownDevice_StartsWithTheNativePanelAndGoesDown()
    {
        var presets = ScreenPresets.ForDevice("outro-aparelho", nativeWidth: 1920, nativeHeight: 1080);

        Assert.Equal(["1080p", "900p", "720p"], presets.Select(p => p.Label));
    }

    [Fact]
    public void SmallPanel_DoesNotOfferResolutionsAboveIt()
    {
        var presets = ScreenPresets.ForDevice("outro-aparelho", nativeWidth: 1280, nativeHeight: 800);

        Assert.Equal(["720p"], presets.Select(p => p.Label));
        Assert.Equal(1280, presets[0].Width);
    }

    [Fact]
    public void NullOrEmptyDevice_FallsBackToNativeWithoutThrowing()
    {
        Assert.NotEmpty(ScreenPresets.ForDevice(null));
        Assert.NotEmpty(ScreenPresets.ForDevice(""));
        Assert.NotEmpty(ScreenPresets.ForDevice("   "));
    }

    [Fact]
    public void InvalidNativeSize_FallsBackTo1080p()
    {
        var presets = ScreenPresets.ForDevice("outro", nativeWidth: 0, nativeHeight: 0);

        Assert.Equal(1920, presets[0].Width);
        Assert.Equal(1080, presets[0].Height);
    }
}

/// <summary>
/// QS 2.0 (F1): Eco / Normal / Boost / Custom, sempre dentro dos limites do
/// firmware do aparelho.
/// </summary>
public class QsPowerPresetsTests
{
    // Legion Go 2 em bateria: SPL 5–35 W (log de 09/11).
    private const int LegionMin = 5;
    private const int LegionMax = 35;

    [Fact]
    public void HasExactlyFourPresets()
    {
        Assert.Equal(
            [PowerPreset.Eco, PowerPreset.Normal, PowerPreset.Boost, PowerPreset.Custom],
            QsPowerPresets.All);
    }

    [Theory]
    [InlineData(PowerPreset.Eco, 5)]
    [InlineData(PowerPreset.Normal, 20)]
    [InlineData(PowerPreset.Boost, 35)]
    public void ResolvesEachPresetInsideTheDeviceLimits(PowerPreset preset, int expected)
    {
        Assert.Equal(expected, QsPowerPresets.Resolve(preset, LegionMin, LegionMax));
    }

    [Fact]
    public void Custom_UsesTheSliderValue()
    {
        Assert.Equal(25, QsPowerPresets.Resolve(PowerPreset.Custom, LegionMin, LegionMax, customWatts: 25));
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(99, 35)]
    public void Custom_IsClampedToTheDeviceLimits(int requested, int expected)
    {
        Assert.Equal(expected, QsPowerPresets.Resolve(PowerPreset.Custom, LegionMin, LegionMax, requested));
    }

    [Fact]
    public void Normal_IsClampedWhenTheDeviceDoesNotReach20W()
    {
        // Um aparelho limitado a 15 W: "Normal" não pode pedir 20 W.
        Assert.Equal(15, QsPowerPresets.Resolve(PowerPreset.Normal, 5, 15));
    }

    [Fact]
    public void SwappedLimits_AreNormalized()
    {
        Assert.Equal(35, QsPowerPresets.Resolve(PowerPreset.Boost, maxWatts: 5, minWatts: 35));
    }
}
