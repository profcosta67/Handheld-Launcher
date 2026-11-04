using HandheldLauncher.Core.Services;
using Xunit;

namespace HandheldLauncher.Core.Tests;

public class EdenLibraryScannerTests
{
    // --- LooksLikeAddOnOrUpdate -------------------------------------------
    //
    // DLC/update .nsp files use the same extension as the base game, so this
    // is the only thing standing between "shows up as a playable game" and
    // "silently inflates the library with unplayable entries". A false
    // negative here (an add-on sneaking through) is a nuisance; a false
    // positive (a real game getting filtered out) is worse — it just
    // disappears from the library with no error anywhere.

    [Theory]
    [InlineData("The Legend of Zelda - TOTK (dlc pack)")]
    [InlineData("Some Game (Update v1.2)")]
    [InlineData("Some Game [upd]")]
    [InlineData("v-1.0.3 Patch")]
    public void LooksLikeAddOnOrUpdate_RecognizesKnownAddOnPatterns(string fileName)
    {
        Assert.True(EdenLibraryScanner.LooksLikeAddOnOrUpdate(fileName));
    }

    [Theory]
    [InlineData("Super Mario Odyssey")]
    [InlineData("The Legend of Zelda - Tears of the Kingdom")]
    [InlineData("Pokemon Scarlet")]
    [InlineData("Updateless Game")] // "Update" must be a whole word, not a substring
    [InlineData("DLConomics Simulator")] // "dlc" must be a whole word too
    public void LooksLikeAddOnOrUpdate_DoesNotFlagOrdinaryTitles(string fileName)
    {
        Assert.False(EdenLibraryScanner.LooksLikeAddOnOrUpdate(fileName));
    }

    [Fact]
    public void LooksLikeAddOnOrUpdate_SixteenHexBracketEndingIn000_IsBaseGameNotAddOn()
    {
        // A 16-hex-digit bracket token is a Switch title ID; one ending in
        // "000" is the base game's own id, not a DLC/update variant.
        Assert.False(EdenLibraryScanner.LooksLikeAddOnOrUpdate("Some Game [0100abcdef123000]"));
    }

    [Fact]
    public void LooksLikeAddOnOrUpdate_SixteenHexBracketNotEndingIn000_IsAddOn()
    {
        Assert.True(EdenLibraryScanner.LooksLikeAddOnOrUpdate("Some Game [0100abcdef123001]"));
    }

    // --- SanitizeForId ------------------------------------------------------

    [Fact]
    public void SanitizeForId_IsDeterministic()
    {
        const string path = @"D:\Roms\Switch\Some Game (USA).nsp";
        Assert.Equal(EdenLibraryScanner.SanitizeForId(path), EdenLibraryScanner.SanitizeForId(path));
    }

    [Fact]
    public void SanitizeForId_ReplacesNonAlphanumericsAndLowercases()
    {
        Assert.Equal("d__roms_switch_some_game__usa__nsp",
            EdenLibraryScanner.SanitizeForId(@"D:\Roms\Switch\Some Game (USA).nsp"));
    }

    // --- BracketTokens --------------------------------------------------------

    [Fact]
    public void BracketTokens_ExtractsEachBracketedSegmentInOrder()
    {
        var tokens = EdenLibraryScanner.BracketTokens("Game [ABCD][1234]").ToList();
        Assert.Equal(new[] { "ABCD", "1234" }, tokens);
    }

    [Fact]
    public void BracketTokens_NoBrackets_ReturnsEmpty()
    {
        Assert.Empty(EdenLibraryScanner.BracketTokens("Plain Title"));
    }

    [Fact]
    public void BracketTokens_EmptyBrackets_AreSkipped()
    {
        Assert.Empty(EdenLibraryScanner.BracketTokens("Game []"));
    }

    // --- ContainsWord ---------------------------------------------------------

    [Theory]
    [InlineData("Game Update Pack", "update", true)]
    [InlineData("Game UPDATE Pack", "update", true)] // case-insensitive
    [InlineData("Updateless Game", "update", false)] // not a whole word
    [InlineData("Game-Update", "update", true)] // non-letter boundary counts
    [InlineData("Game", "update", false)]
    public void ContainsWord_MatchesWholeWordsOnly(string text, string word, bool expected)
    {
        Assert.Equal(expected, EdenLibraryScanner.ContainsWord(text, word));
    }
}
