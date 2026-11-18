using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;
using Xunit;

namespace HandheldLauncher.Core.Tests;

public class ManualLaunchTargetTests
{
    [Theory]
    [InlineData(@"C:\Jogos\jogo.exe", false)]
    [InlineData(@"""C:\Jogos\jogo.exe""", false)]
    [InlineData(@"C:\Users\m\Desktop\Pokemon.lnk", true)]
    [InlineData(@"C:\Users\m\Desktop\Jogo.url", true)]
    [InlineData("steam://rungameid/1234", true)]
    [InlineData("https://www.xbox.com/play", true)]
    [InlineData(@"D:jogo.exe", false)]
    [InlineData("", false)]
    public void DecidesWhatOpensThroughWindows(string target, bool expected)
        => Assert.Equal(expected, ManualLaunchTarget.OpensThroughWindows(target));

    [Theory]
    [InlineData(@"C:\Emu\Ryujinx.exe", true)]
    [InlineData(@"C:\Emu\eden.exe", true)]
    [InlineData(@"C:\Emu\Ryujinx.Ava.exe", true)]
    [InlineData(@"C:\Emu\pcsx2-qt.exe", false)]
    [InlineData(@"C:\Jogos\Ryujinx Game.exe", false)]
    [InlineData(null, false)]
    public void RecognizesSwitchEmulators(string? exe, bool expected)
        => Assert.Equal(expected, ManualLaunchTarget.IsSwitchEmulator(exe));

    [Fact]
    public void GameFileGoesQuotedAtTheEnd()
    {
        Assert.Equal(@"--fullscreen ""E:\Switch\Pokemon ZA.nsp""",
            ManualLaunchTarget.CombineArguments("--fullscreen", @" ""E:\Switch\Pokemon ZA.nsp"" "));
        Assert.Equal(@"""E:\Switch\Pokemon ZA.nsp""",
            ManualLaunchTarget.CombineArguments("  ", @"E:\Switch\Pokemon ZA.nsp"));
        Assert.Equal("-f", ManualLaunchTarget.CombineArguments(" -f ", ""));
        Assert.Null(ManualLaunchTarget.CombineArguments(null, null));
    }

    [Fact]
    public void EmulatorPlusGameFileBecomesAnEmulatorGame()
    {
        var entry = ManualLaunchTarget.BuildEntry(" Pokémon Legends Z-A ", @"C:\Emu\Ryujinx.exe", null, null,
            @"E:\Switch\Pokemon ZA.nsp", isApplication: false, isXbox: false);

        Assert.Equal("Pokémon Legends Z-A", entry.Title);
        Assert.Equal(GameSource.Emulator, entry.Source);
        Assert.Equal(@"C:\Emu\Ryujinx.exe", entry.ExecutablePath);
        Assert.Equal(@"""E:\Switch\Pokemon ZA.nsp""", entry.LaunchArguments);
        Assert.Null(entry.LaunchUri);
        Assert.False(entry.IsApplication);
    }

    [Fact]
    public void UnresolvedShortcutOpensThroughWindows()
    {
        var entry = ManualLaunchTarget.BuildEntry("Jogo", @"C:\Users\m\Desktop\Jogo.lnk", "-x", @"C:\Pasta",
            null, isApplication: false, isXbox: false);

        Assert.Equal(GameSource.Manual, entry.Source);
        Assert.Null(entry.ExecutablePath);
        Assert.Equal(@"C:\Users\m\Desktop\Jogo.lnk", entry.LaunchUri);
        Assert.Null(entry.LaunchArguments);
        Assert.Null(entry.WorkingDirectory);
    }

    [Fact]
    public void ProgramsAndXboxKeepTheirSource()
    {
        var program = ManualLaunchTarget.BuildEntry("Ryujinx", @"C:\Emu\Ryujinx.exe", null, null, null,
            isApplication: true, isXbox: false);
        Assert.Equal(GameSource.Manual, program.Source);
        Assert.True(program.IsApplication);

        var xbox = ManualLaunchTarget.BuildEntry("Forza", @"C:\XboxGames\Forza\forza.exe", null, null, null,
            isApplication: false, isXbox: true);
        Assert.Equal(GameSource.XboxApp, xbox.Source);
        Assert.Equal(@"C:\XboxGames\Forza\forza.exe", xbox.ExecutablePath);
    }
}
