using HandheldLauncher.Core.Services.Hud;
using Xunit;

namespace HandheldLauncher.Core.Tests;

public class HudMarkupTests
{
    [Fact]
    public void SplitsColoredRunsAndPlainText()
    {
        var lines = HudMarkup.Parse("<C=EB5B5B>FPS<C> <C=FFFFFF>60<C>   <C=CCCCCC>12:30<C>");
        var runs = Assert.Single(lines);
        Assert.Equal(new[] { "FPS", " ", "60", "   ", "12:30" }, runs.Select(r => r.Text));
        Assert.Equal("EB5B5B", runs[0].Color);
        Assert.Null(runs[1].Color);
    }

    [Fact]
    public void KeepsLinesForNotice()
    {
        var lines = HudMarkup.Parse("<C=FFFFFF>60<C>\n<C=C6B5FF>Modo inteligente<C>");
        Assert.Equal(2, lines.Count);
        Assert.Equal("Modo inteligente", lines[1][0].Text);
    }

    [Fact]
    public void DropsLooseTags()
    {
        var runs = HudMarkup.Parse("a<C>b<C=123456>c").Single();
        Assert.Equal("abc", string.Concat(runs.Select(r => r.Text)));
    }

    [Fact]
    public void EmptyGivesNoLines() => Assert.Empty(HudMarkup.Parse(""));
}
