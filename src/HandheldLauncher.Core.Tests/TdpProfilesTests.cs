using HandheldLauncher.Core.Services;
using Xunit;

namespace HandheldLauncher.Core.Tests;

public class TdpProfilesTests
{
    [Theory]
    [InlineData("UltraEco", 8)]
    [InlineData("Eco", 15)]
    [InlineData("Normal", 20)]
    [InlineData("Desempenho", 25)]
    [InlineData("Boost", 30)]
    [InlineData("Custom:18", 18)]
    [InlineData("Custom:5", 5)]
    [InlineData("Custom:30", 30)]
    public void WattsFor_KnownModes(string mode, int expected)
    {
        Assert.Equal(expected, TdpProfiles.WattsFor(mode));
    }

    [Theory]
    [InlineData("Custom:2", 5)]     // abaixo do piso
    [InlineData("Custom:150", 55)]  // acima do teto absoluto de sanidade
    [InlineData("Custom:-4", 5)]
    [InlineData("Custom:35", 35)]   // acima de 30 agora é válido (teto real vem do aparelho)
    public void WattsFor_CustomIsClampedToSafetyRange(string mode, int expected)
    {
        Assert.Equal(expected, TdpProfiles.WattsFor(mode));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Custom:")]
    [InlineData("Custom:abc")]
    [InlineData("Turbo")]
    public void WattsFor_UnknownIsNull(string? mode)
    {
        Assert.Null(TdpProfiles.WattsFor(mode));
    }

    [Theory]
    [InlineData(18, "Custom:18")]
    [InlineData(1, "Custom:5")]
    [InlineData(99, "Custom:55")]
    public void CustomMode_RoundTripsAndClamps(int watts, string expected)
    {
        Assert.Equal(expected, TdpProfiles.CustomMode(watts));
        Assert.True(TdpProfiles.IsCustom(expected));
    }

    [Theory]
    [InlineData("UltraEco", "Silencioso")]
    [InlineData("Eco", "Silencioso")]
    [InlineData("Normal", "Normal")]
    [InlineData("Desempenho", "Desempenho")]
    [InlineData("Boost", "Boost")]
    [InlineData("Custom:12", "Silencioso")]
    [InlineData("Custom:15", "Silencioso")]
    [InlineData("Custom:18", "Normal")]
    [InlineData("Custom:23", "Desempenho")]
    [InlineData("Custom:28", "Boost")]
    [InlineData("Turbo", "Automático")]
    public void LinkedFanMode_MatchesPresetBands(string mode, string expected)
    {
        Assert.Equal(expected, TdpProfiles.LinkedFanMode(mode));
    }

    [Fact]
    public void PresetsAreNotCustom()
    {
        foreach (var preset in TdpProfiles.Presets) Assert.False(TdpProfiles.IsCustom(preset));
    }
}
