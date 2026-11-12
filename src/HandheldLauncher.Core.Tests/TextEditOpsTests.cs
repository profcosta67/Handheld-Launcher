using HandheldLauncher.Core.Services.Keyboard;
using Xunit;

namespace HandheldLauncher.Core.Tests;

public class TextEditOpsTests
{
    [Fact]
    public void Insert_AddsCharacterAtCaret()
    {
        var (text, caret) = TextEditOps.Insert("hos", 2, 0, "u");

        Assert.Equal("hous", text);
        Assert.Equal(3, caret);
    }

    [Fact]
    public void Insert_ReplacesSelection()
    {
        var (text, caret) = TextEditOps.Insert("senha123", 5, 3, "!");

        Assert.Equal("senha!", text);
        Assert.Equal(6, caret);
    }

    [Fact]
    public void Insert_AtEndAppendsAndKeepsCaretAtEnd()
    {
        var (text, caret) = TextEditOps.Insert("abc", 3, 0, "d");

        Assert.Equal("abcd", text);
        Assert.Equal(4, caret);
    }

    [Fact]
    public void Insert_ClampsOutOfRangeCaret()
    {
        var (text, caret) = TextEditOps.Insert("ab", 99, 99, "c");

        Assert.Equal("abc", text);
        Assert.Equal(3, caret);
    }

    [Fact]
    public void Insert_IntoNullTextStartsFresh()
    {
        var (text, caret) = TextEditOps.Insert(null, 0, 0, "o");

        Assert.Equal("o", text);
        Assert.Equal(1, caret);
    }

    [Fact]
    public void Backspace_RemovesCharacterBeforeCaret()
    {
        var (text, caret) = TextEditOps.Backspace("abc", 3, 0);

        Assert.Equal("ab", text);
        Assert.Equal(2, caret);
    }

    [Fact]
    public void Backspace_AtStartDoesNothing()
    {
        var (text, caret) = TextEditOps.Backspace("abc", 0, 0);

        Assert.Equal("abc", text);
        Assert.Equal(0, caret);
    }

    [Fact]
    public void Backspace_RemovesSelectionOnly()
    {
        var (text, caret) = TextEditOps.Backspace("senha123", 5, 3);

        Assert.Equal("senha", text);
        Assert.Equal(5, caret);
    }

    [Fact]
    public void Backspace_OnEmptyTextStaysEmpty()
    {
        var (text, caret) = TextEditOps.Backspace(null, 0, 0);

        Assert.Equal(string.Empty, text);
        Assert.Equal(0, caret);
    }

    [Fact]
    public void MoveCaret_WalksInsideTextAndClampsAtEnds()
    {
        Assert.Equal(1, TextEditOps.MoveCaret("abc", 2, 0, -1));
        Assert.Equal(3, TextEditOps.MoveCaret("abc", 2, 0, +1));
        Assert.Equal(0, TextEditOps.MoveCaret("abc", 0, 0, -1));
        Assert.Equal(3, TextEditOps.MoveCaret("abc", 3, 0, +1));
        Assert.Equal(0, TextEditOps.MoveCaret(null, 0, 0, +1));
    }

    [Fact]
    public void MoveCaret_WithSelectionJumpsToItsEdge()
    {
        Assert.Equal(1, TextEditOps.MoveCaret("abcd", 1, 2, -1));
        Assert.Equal(3, TextEditOps.MoveCaret("abcd", 1, 2, +1));
    }
}
