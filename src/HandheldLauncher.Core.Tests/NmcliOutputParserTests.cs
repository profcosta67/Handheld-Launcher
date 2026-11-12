using HandheldLauncher.Core.Services;
using Xunit;

namespace HandheldLauncher.Core.Tests;

public class NmcliOutputParserTests
{
    [Fact]
    public void ParseTerseFields_KeepsEscapedColonInsideSsid()
    {
        var fields = NmcliOutputParser.ParseTerseFields(@"Games\: Handheld:73:WPA2:*");

        Assert.Equal(["Games: Handheld", "73", "WPA2", "*"], fields);
    }

    [Fact]
    public void ParseCurrentSsid_UsesActiveMarkerInsteadOfLocalizedBoolean()
    {
        const string output = " :Other Network\n*:Games\\: Handheld\r\n";

        Assert.Equal("Games: Handheld", NmcliOutputParser.ParseCurrentSsid(output));
    }

    [Fact]
    public void ParseCurrentSsid_ReturnsNullWhenNoNetworkIsActive()
    {
        Assert.Null(NmcliOutputParser.ParseCurrentSsid(" :Other Network\n :Games"));
        Assert.Empty(NmcliOutputParser.ParseTerseFields(null));
    }
}
