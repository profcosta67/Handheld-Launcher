using HandheldLauncher.Core.Services.Keyboard;
using Xunit;

namespace HandheldLauncher.Core.Tests;

public class OnScreenKeyboardLayoutTests
{
    [Fact]
    public void Letters_HasFourRowsAndStartsWithQwerty()
    {
        var rows = OnScreenKeyboardLayout.Letters(shifted: false);

        Assert.Equal(4, rows.Count);
        Assert.Equal("qwertyuiop", string.Concat(rows[0].Take(10).Select(k => k.Label)));
    }

    [Fact]
    public void Letters_ShiftChangesLettersNotSpecialKeys()
    {
        var lower = OnScreenKeyboardLayout.Letters(shifted: false);
        var upper = OnScreenKeyboardLayout.Letters(shifted: true);

        Assert.Equal("q", lower[0][0].Text);
        Assert.Equal("Q", upper[0][0].Text);
        Assert.Equal(KeyboardKeyKind.Backspace, upper[0][^1].Kind);
        Assert.Equal(KeyboardKeyKind.Shift, upper[2][^1].Kind);
    }

    [Fact]
    public void Letters_BottomRowHasPageSpaceClearClose()
    {
        var bottom = OnScreenKeyboardLayout.Letters(shifted: false)[3];

        Assert.Equal(
            [KeyboardKeyKind.Page, KeyboardKeyKind.Space, KeyboardKeyKind.Clear, KeyboardKeyKind.Close],
            bottom.Select(k => k.Kind));
    }

    [Fact]
    public void Symbols_HasDigitsFirstAndReturnsToLetters()
    {
        var rows = OnScreenKeyboardLayout.Symbols();

        Assert.Equal("1234567890", string.Concat(rows[0].Select(k => k.Label)));
        Assert.Equal("abc", rows[^1][0].Label);
        Assert.Equal(KeyboardKeyKind.Page, rows[^1][0].Kind);
    }

    [Fact]
    public void Symbols_ContainsCharactersMissingFromLettersPage()
    {
        var labels = OnScreenKeyboardLayout.Symbols().SelectMany(r => r).Select(k => k.Label).ToHashSet();

        foreach (var needed in new[] { "@", ".", "-", "_", "!", "#", "?" })
            Assert.Contains(needed, labels);
    }

    [Fact]
    public void Move_LeftAndRightStayInsideTheRow()
    {
        var rows = OnScreenKeyboardLayout.Letters(shifted: false);

        Assert.Equal((0, 0), OnScreenKeyboardLayout.Move(rows, 0, 0, KeyboardDirection.Left));
        Assert.Equal((0, 10), OnScreenKeyboardLayout.Move(rows, 0, 10, KeyboardDirection.Right));
        Assert.Equal((0, 1), OnScreenKeyboardLayout.Move(rows, 0, 0, KeyboardDirection.Right));
    }

    [Fact]
    public void Move_UpFromSpaceLandsOnTheNearestKeyOfPreviousRow()
    {
        // O "espaço" tem 4 unidades de largura: o centro cai sobre o "v".
        var rows = OnScreenKeyboardLayout.Letters(shifted: false);

        Assert.Equal((2, 3), OnScreenKeyboardLayout.Move(rows, 3, 1, KeyboardDirection.Up));
        Assert.Equal("v", rows[2][3].Label);
    }

    [Fact]
    public void Move_DownFromShiftLandsOnClose()
    {
        // ⇧ (1.5) e "fechar" (1.5) têm o mesmo centro.
        var rows = OnScreenKeyboardLayout.Letters(shifted: false);

        Assert.Equal((3, 3), OnScreenKeyboardLayout.Move(rows, 2, 7, KeyboardDirection.Down));
        Assert.Equal("fechar", rows[3][3].Label);
    }

    [Fact]
    public void Move_UpOnFirstRowStaysPut()
    {
        var rows = OnScreenKeyboardLayout.Letters(shifted: false);

        Assert.Equal((0, 4), OnScreenKeyboardLayout.Move(rows, 0, 4, KeyboardDirection.Up));
    }

    [Fact]
    public void Move_ClampsWhenPositionIsOutOfRange()
    {
        var rows = OnScreenKeyboardLayout.Letters(shifted: false);

        Assert.Equal((0, 0), OnScreenKeyboardLayout.Move(rows, -5, -5, KeyboardDirection.Left));
        Assert.Equal((3, 3), OnScreenKeyboardLayout.Move(rows, 99, 99, KeyboardDirection.Right));
    }

    [Fact]
    public void Move_OnEmptyLayoutIsSafe()
    {
        Assert.Equal((0, 0), OnScreenKeyboardLayout.Move([], 0, 0, KeyboardDirection.Down));
    }
}
