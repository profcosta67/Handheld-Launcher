using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services.Artwork;
using Xunit;

namespace HandheldLauncher.Core.Tests;

public class RomTitleCleanerTests
{
    // v0.18.4: termo de busca da SteamGridDB pra jogo de emulador cujo título
    // é o nome do arquivo. Exemplos reais da pasta do usuário + padrões comuns.
    [Theory]
    [InlineData("Pokemon Legends Arceus-002", "Pokemon Legends Arceus")]
    [InlineData("Pokemon NSP", "Pokemon")]
    [InlineData("Digimon Story Time Stranger Switch NSP Base Game", "Digimon Story Time Stranger")]
    [InlineData("Pokemon Scarlet [0100A3D008C5C000][v0]", "Pokemon Scarlet")]
    [InlineData("Pokemon Violet (USA) [v131072]", "Pokemon Violet")]
    [InlineData("Super_Mario_Odyssey", "Super Mario Odyssey")]
    [InlineData("Pokemon.Legends.Arceus", "Pokemon Legends Arceus")]
    [InlineData("Some Game v1.0.2", "Some Game")]
    [InlineData("Some Game - 001", "Some Game")]
    public void Clean_RemovesFileNameJunk(string input, string expected)
    {
        Assert.Equal(expected, RomTitleCleaner.Clean(input));
    }

    [Theory]
    [InlineData("Super Mario Odyssey")]
    [InlineData("The Legend of Zelda - Tears of the Kingdom")]
    [InlineData("Nintendo Switch Sports")] // "Switch" no meio é parte do nome
    [InlineData("Mario Kart 8 Deluxe")]
    [InlineData("Pokémon Legends: Arceus")]
    public void Clean_KeepsOrdinaryTitles(string title)
    {
        Assert.Equal(title, RomTitleCleaner.Clean(title));
    }

    [Theory]
    [InlineData("NSP")]
    [InlineData("[0100A3D008C5C000]")]
    public void Clean_NeverReturnsEmpty(string title)
    {
        Assert.False(string.IsNullOrWhiteSpace(RomTitleCleaner.Clean(title)));
    }

    [Fact]
    public void SearchTitleFor_CleansOnlyEmulatorGamesWithoutEditedName()
    {
        var rom = new GameEntry { Id = "a", Title = "Pokemon Legends Arceus-002", Source = GameSource.Emulator };
        Assert.Equal("Pokemon Legends Arceus", ArtworkService.SearchTitleFor(rom));

        var renamed = new GameEntry { Id = "b", Title = "Pokémon Legends: Arceus (2022)", TitleOverride = "Pokémon Legends: Arceus (2022)", Source = GameSource.Emulator };
        Assert.Equal("Pokémon Legends: Arceus (2022)", ArtworkService.SearchTitleFor(renamed));

        var manual = new GameEntry { Id = "c", Title = "Prince of Persia (2008)", Source = GameSource.Manual };
        Assert.Equal("Prince of Persia (2008)", ArtworkService.SearchTitleFor(manual));
    }

    [Theory]
    [InlineData("Pokemon Legends Arceus", "Pokémon Legends Arceus")]
    [InlineData("POKEMON Scarlet", "Pokémon Scarlet")]
    public void AccentVariant_RestoresPokemonAccent(string input, string expected)
    {
        Assert.Equal(expected, SteamGridDbClient.AccentVariant(input));
    }

    [Theory]
    [InlineData("Pokémon Scarlet")] // já tem acento
    [InlineData("Hollow Knight")]
    [InlineData("Pokemonish Game")] // só palavra inteira
    public void AccentVariant_NullWhenNothingToChange(string input)
    {
        Assert.Null(SteamGridDbClient.AccentVariant(input));
    }
}
