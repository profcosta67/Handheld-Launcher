using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;
using Xunit;

namespace HandheldLauncher.Core.Tests;

public class PortsLibraryScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ports-{Guid.NewGuid():N}");

    public PortsLibraryScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Exe(string relative, int bytes = 1000)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private IReadOnlyList<GameEntry> Scan(out PortsScanReport report) =>
        new PortsLibraryScanner(new[] { _root }).ScanInstalledGames(out report);

    [Fact]
    public void OneGamePerSubfolderWithKnownTitles()
    {
        Exe(@"Ship of Harkinian/soh.exe");
        Exe(@"Ship of Harkinian/OTRGui.exe");
        Exe(@"Mario64/sm64.us.f3dex2e.exe");
        Exe(@"Perfect Dark/pd.x86_64.exe");
        var games = Scan(out var report);
        Assert.Equal(3, games.Count);
        Assert.Equal(3, report.Games);
        var soh = games.Single(g => g.ExecutablePath!.EndsWith("soh.exe"));
        Assert.Equal("The Legend of Zelda: Ocarina of Time", soh.Title);
        Assert.Equal(GameSource.Emulator, soh.Source);
        Assert.StartsWith(PortsLibraryScanner.IdPrefix, soh.Id);
        Assert.Equal(Path.GetDirectoryName(soh.ExecutablePath), soh.WorkingDirectory);
        Assert.Contains(games, g => g.Title == "Super Mario 64");
    }

    [Fact]
    public void PicksExeMatchingFolderAndSkipsTools()
    {
        Exe(@"Cool Game/unins000.exe", 5000);
        Exe(@"Cool Game/UnityCrashHandler64.exe", 9000);
        Exe(@"Cool Game/vc_redist.x64.exe", 9000);
        Exe(@"Cool Game/CoolGame.exe", 100);
        Exe(@"Cool Game/helper.exe", 200);
        var g = Assert.Single(Scan(out _));
        Assert.EndsWith("CoolGame.exe", g.ExecutablePath);
        Assert.Equal("Cool Game", g.Title);
    }

    [Fact]
    public void FindsExeInsideSubfolder()
    {
        Exe(@"Some Port/bin/x64/someport.exe");
        var g = Assert.Single(Scan(out _));
        Assert.EndsWith("someport.exe", g.ExecutablePath);
    }

    [Fact]
    public void CategoryFoldersAreSearched()
    {
        Exe(@"Nintendo 64/Ship of Harkinian/soh.exe");
        Exe(@"Nintendo 64/Perfect Dark/pd.x86_64.exe");
        Exe(@"Sega/Sonic 3 AIR/Sonic3AIR.exe");
        Exe(@"Doom/gzdoom.exe");
        var games = Scan(out _);
        Assert.Equal(4, games.Count);
        Assert.Contains(games, g => g.Title == "Perfect Dark");
        Assert.Contains(games, g => g.Title == "Sonic 3 & Knuckles");
    }

    [Fact]
    public void BinFolderStaysOneGame()
    {
        Exe(@"My Port/bin/myport.exe");
        Exe(@"My Port/x64/myport-x64.exe");
        var g = Assert.Single(Scan(out _));
        Assert.Equal("My Port", g.Title);
    }

    [Fact]
    public void FolderWithoutExeIsCounted()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Empty"));
        Assert.Empty(Scan(out var report));
        Assert.Equal(1, report.FoldersWithoutExe);
    }

    [Fact]
    public void PortJsonOverridesEverything()
    {
        Exe(@"Jak/gk.exe");
        Exe(@"Jak/launcher/OpenGOAL-Launcher.exe");
        File.WriteAllText(Path.Combine(_root, "Jak", "port.json"),
            """{ "nome": "Jak and Daxter", "exe": "gk.exe", "args": "-boot -fakeiso" }""");
        var g = Assert.Single(Scan(out _));
        Assert.Equal("Jak and Daxter", g.Title);
        Assert.EndsWith("gk.exe", g.ExecutablePath);
        Assert.Equal("-boot -fakeiso", g.LaunchArguments);
    }

    [Fact]
    public void AcronymFolderMatches()
    {
        Exe(@"Ship of Harkinian/soh.exe", 10);
        Exe(@"Ship of Harkinian/bigtool.exe", 100000);
        Assert.EndsWith("soh.exe", Assert.Single(Scan(out _)).ExecutablePath);
    }

    [Fact]
    public void MissingConfiguredRootIsReported()
    {
        new PortsLibraryScanner(new[] { Path.Combine(_root, "nao-existe") }).ScanInstalledGames(out var report);
        Assert.Equal(1, report.FoldersMissing);
        Assert.False(report.AutoDetected);
    }

    [Fact]
    public void AutoDetectUsedWhenNoFolderConfigured()
    {
        Exe(@"Ports/Sonic 3 AIR/Sonic3AIR.exe");
        var scanner = new PortsLibraryScanner(null, () => new[] { Path.Combine(_root, "Ports"), Path.Combine(_root, "Port") });
        var g = Assert.Single(scanner.ScanInstalledGames(out var report));
        Assert.True(report.AutoDetected);
        Assert.Equal("Sonic 3 & Knuckles", g.Title);
    }

    [Theory]
    [InlineData("Super_Mario_64-PC-Port v1.2", "Super Mario 64")]
    [InlineData("Perfect Dark (x64)", "Perfect Dark")]
    [InlineData("Zelda64Recomp", "Zelda64Recomp")]
    public void CleansFolderNames(string folder, string expected) =>
        Assert.Equal(expected, PortsLibraryScanner.CleanFolderName(folder));
}
