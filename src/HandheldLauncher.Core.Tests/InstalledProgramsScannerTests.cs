using HandheldLauncher.Core.Services;
using Xunit;

namespace HandheldLauncher.Core.Tests;

/// <summary>
/// v0.25.3: regras puras da lista de programas instalados. O scan do registo
/// não é testável sem uma máquina real; o que aqui se trava é a limpeza do
/// DisplayIcon e os filtros que impedem desinstaladores/redistributables de
/// aparecerem como se fossem programas.
/// </summary>
public class InstalledProgramsScannerTests
{
    [Fact]
    public void DisplayIconWithIndex_ExtractsExecutable()
    {
        Assert.Equal(@"C:\Games\App.exe", InstalledProgramsScanner.ExeFromDisplayIcon(@"""C:\Games\App.exe"",0"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Games\app.ico")]
    [InlineData(@"C:\Games\app.dll")]
    public void DisplayIconWithoutExecutable_ReturnsNull(string? value)
    {
        Assert.Null(InstalledProgramsScanner.ExeFromDisplayIcon(value));
    }

    [Theory]
    [InlineData("Microsoft Visual C++ 2015 Redistributable")]
    [InlineData("Update for Windows 11")]
    [InlineData("{12345678-1234-1234-1234-1234567890AB}")]
    [InlineData("Windows SDK")]
    [InlineData("")]
    public void NoiseNames_AreRejected(string name)
    {
        Assert.True(InstalledProgramsScanner.IsNoiseName(name));
    }

    [Theory]
    [InlineData("Spotify")]
    [InlineData("VLC media player")]
    [InlineData("Discord")]
    [InlineData("Steam")]
    public void RealNames_AreAccepted(string name)
    {
        Assert.False(InstalledProgramsScanner.IsNoiseName(name));
    }

    [Theory]
    [InlineData(@"C:\Games\unins000.exe")]
    [InlineData(@"C:\Games\Updater.exe")]
    [InlineData(@"C:\Games\vc_redist.x64.exe")]
    [InlineData(@"C:\Games\setup.exe")]
    public void InstallerExecutables_AreRejected(string path)
    {
        Assert.True(InstalledProgramsScanner.IsNoiseExecutable(path));
    }

    [Fact]
    public void GameExecutable_IsAccepted()
    {
        Assert.False(InstalledProgramsScanner.IsNoiseExecutable(@"C:\Games\DaysGone.exe"));
    }

    [Fact]
    public void Normalize_IgnoresAccentsAndCase()
    {
        Assert.Equal("pokemon", InstalledProgramsScanner.Normalize("Pokémon"));
    }
}
