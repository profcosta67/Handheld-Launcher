using HandheldLauncher.Core.Services.Artwork;
using Xunit;

namespace HandheldLauncher.Core.Tests;

public class SteamGridDbClientNormalizeTests
{
    // NormalizeGameTitle decides whether a Steam/Eden title and a SteamGridDB
    // search result count as "the same game" — too strict and covers never
    // match (no artwork found); too loose and the wrong game's artwork gets
    // applied. These lock in the current, deliberately narrow behavior.

    [Theory]
    [InlineData("Hollow Knight", "hollow knight")]
    [InlineData("  Hollow Knight  ", "hollow knight")] // leading/trailing whitespace
    [InlineData("HOLLOW KNIGHT", "hollow knight")] // case
    [InlineData("Hollow-Knight", "hollow knight")] // punctuation becomes space
    [InlineData("Hollow:  Knight!!", "hollow knight")] // multiple punctuation collapses
     [InlineData("Pokemon Legends Z-A", "Pokemon Legends Z A")]
    [InlineData("Pokemon Legends: Z-A", "Pokemon Legends Z A")]
    [InlineData("Marvel's Spider-Man", "Marvel s Spider Man")]
    [InlineData("Plants vs. Zombies", "Plants vs Zombies")]
    public void NormalizeGameTitle_LowercasesAndStripsPunctuation(string input, string expected)
    {
        Assert.Equal(expected, SteamGridDbClient.NormalizeGameTitle(input));
    }

    [Fact]
    public void NormalizeGameTitle_StripsDiacritics()
    {
        Assert.Equal("pokemon", SteamGridDbClient.NormalizeGameTitle("Pokémon"));
    }

    [Fact]
    public void NormalizeGameTitle_StripsTrailingThreeDigitSuffix()
    {
        // Eden ROM filenames sometimes carry a trailing "-NNN" revision/region
        // marker that isn't part of the title SteamGridDB would recognize.
        Assert.Equal("some game", SteamGridDbClient.NormalizeGameTitle("Some Game-001"));
    }

    [Fact]
    public void NormalizeGameTitle_DoesNotStripNonThreeDigitTrailingNumbers()
    {
        // Only an exact 3-digit trailing run is stripped — "Portal 2" must
        // not lose its "2" (title, not a revision marker).
        Assert.Equal("portal 2", SteamGridDbClient.NormalizeGameTitle("Portal 2"));
    }

    [Fact]
    public void NormalizeGameTitle_NullOrWhitespace_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SteamGridDbClient.NormalizeGameTitle(null));
        Assert.Equal(string.Empty, SteamGridDbClient.NormalizeGameTitle("   "));
        Assert.Equal(string.Empty, SteamGridDbClient.NormalizeGameTitle(string.Empty));
    }

    [Fact]
    public void NormalizeGameTitle_IsStableWhenAppliedTwice()
    {
        // The matching code normalizes both the local title and the search
        // result's name — if this weren't idempotent, equal-after-one-pass
        // titles could start comparing unequal after a second pass.
        var once = SteamGridDbClient.NormalizeGameTitle("Pokémon: Scarlet!!");
        var twice = SteamGridDbClient.NormalizeGameTitle(once);
        Assert.Equal(once, twice);
    }
}
