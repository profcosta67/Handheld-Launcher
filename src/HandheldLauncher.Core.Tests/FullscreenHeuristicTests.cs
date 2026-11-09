using HandheldLauncher.Core.Services;
using Xunit;

namespace HandheldLauncher.Core.Tests;

/// <summary>
/// Aviso de ecrã inteiro no Quick Settings (0.24.1): o jogo em fullscreen
/// exclusivo fica preto atrás do painel. Esta é a decisão pura; o detector lê
/// o rect/estilos da janela e passa-os aqui.
/// </summary>
public class FullscreenHeuristicTests
{
    private const int ScreenW = 1920;
    private const int ScreenH = 1080;

    [Fact]
    public void Windowed_SmallWindowIsNeverWarned()
    {
        var mode = FullscreenHeuristic.Classify(1280, 720, ScreenW, ScreenH,
            hasCaption: true, hasThickFrame: true, isPopup: false);

        Assert.Equal(GameWindowMode.Windowed, mode);
        Assert.Null(FullscreenHeuristic.WarningFor(mode));
    }

    [Fact]
    public void MaximizedWithCaption_IsTreatedAsWindowed()
    {
        var mode = FullscreenHeuristic.Classify(ScreenW, ScreenH, ScreenW, ScreenH,
            hasCaption: true, hasThickFrame: true, isPopup: false);

        Assert.Equal(GameWindowMode.Windowed, mode);
    }

    [Fact]
    public void PopupWithoutCaptionCoveringTheScreen_IsWarned()
    {
        var mode = FullscreenHeuristic.Classify(ScreenW, ScreenH, ScreenW, ScreenH,
            hasCaption: false, hasThickFrame: false, isPopup: true);

        Assert.Equal(GameWindowMode.FullscreenLikely, mode);
        Assert.NotNull(FullscreenHeuristic.WarningFor(mode));
        Assert.Contains("sem bordas", FullscreenHeuristic.WarningFor(mode), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CoveringWithoutCaptionButNotPopup_IsBorderlessWithoutWarning()
    {
        var mode = FullscreenHeuristic.Classify(ScreenW, ScreenH, ScreenW, ScreenH,
            hasCaption: false, hasThickFrame: false, isPopup: false);

        Assert.Equal(GameWindowMode.BorderlessFullscreen, mode);
        Assert.Null(FullscreenHeuristic.WarningFor(mode));
    }

    [Fact]
    public void OnePixelShort_StillCountsAsCovering()
    {
        var mode = FullscreenHeuristic.Classify(ScreenW - 1, ScreenH - 1, ScreenW, ScreenH,
            hasCaption: false, hasThickFrame: false, isPopup: true);

        Assert.Equal(GameWindowMode.FullscreenLikely, mode);
    }

    [Fact]
    public void InvalidSizes_DefaultToWindowedInsteadOfWarning()
    {
        Assert.Equal(GameWindowMode.Windowed, FullscreenHeuristic.Classify(0, 0, ScreenW, ScreenH, false, false, true));
        Assert.Equal(GameWindowMode.Windowed, FullscreenHeuristic.Classify(ScreenW, ScreenH, 0, 0, false, false, true));
    }

    [Theory]
    [InlineData(2, "DESLIGADAS")]
    [InlineData(0, "ligadas")]
    [InlineData(null, "desconhecida")]
    public void DescribeFullscreenOptimizations_ExplainsTheRegistryValue(int? value, string expectedFragment)
    {
        Assert.Contains(expectedFragment, FullscreenHeuristic.DescribeFullscreenOptimizations(value));
    }
}
