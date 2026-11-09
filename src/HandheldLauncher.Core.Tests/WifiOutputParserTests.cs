using HandheldLauncher.Core.Services;
using Xunit;

namespace HandheldLauncher.Core.Tests;

/// <summary>
/// Regressões do parser do `netsh`. A "rede fantasma" chamada
/// "Tipo de rede : Infraestrutura" aparecia na lista do Wi-Fi porque o regex
/// usava \s (que atravessa a mudança de linha) depois dos dois-pontos.
/// </summary>
public class WifiOutputParserTests
{
    // Saída real (pt-BR) com uma rede oculta a preceder as outras.
    private const string Scan = """
        Nome da interface: Wi-Fi
        Há 3 redes visíveis no momento.

        SSID 1 :
            Tipo de rede            : Infraestrutura
            Autenticação            : WPA2-Personal
            Criptografia            : CCMP
            BSSID 1                 : 54:bb:d9:00:d6:3f
                 Sinal             : 91%
                 Tipo de Rádio     : 802.11ac

        SSID 2 : G@mes91
            Tipo de rede            : Infraestrutura
            Autenticação            : WPA2-Personal
            BSSID 1                 : 3c:6a:d2:5b:3e:7f
                 Sinal             : 47%
            BSSID 2                 : 3c:6a:d2:5b:3e:7e
                 Sinal             : 40%

        SSID 3 : COSTA
            Tipo de rede            : Infraestrutura
            Autenticação            : WPA3-Personal
            BSSID 1                 : 54:bb:d9:00:d6:3a
                 Sinal             : 85%
        """;

    [Fact]
    public void ParseNetworks_NaoInventaRedeAPartirDaLinhaSeguinte()
    {
        var networks = WifiOutputParser.ParseNetworks(Scan);

        Assert.All(networks, network =>
            Assert.DoesNotContain("Infraestrutura", network.Ssid, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, networks.Count); // a rede oculta não entra
    }

    [Fact]
    public void ParseNetworks_LeSinalEOrdenaDoMaisForteParaOFraco()
    {
        var networks = WifiOutputParser.ParseNetworks(Scan);

        Assert.Equal(["COSTA", "G@mes91"], networks.Select(n => n.Ssid));
        Assert.Equal(85, networks[0].Signal);
        Assert.Equal(47, networks[1].Signal); // o melhor BSSID do G@mes91
        Assert.All(networks, network => Assert.True(network.IsSecured)); // WPA2/WPA3
    }

    [Fact]
    public void ParseNetworks_MarcaRedeAbertaComoSemSenha()
    {
        const string aberta = """
            SSID 1 : Cafe
                Autenticação            : Aberta
                Criptografia            : Nenhuma
                BSSID 1                 : 00:11:22:33:44:55
                     Sinal             : 60%
            """;

        var network = Assert.Single(WifiOutputParser.ParseNetworks(aberta));
        Assert.False(network.IsSecured);
    }

    [Fact]
    public void ParseNetworks_SemSaidaNaoRebenta()
    {
        Assert.Empty(WifiOutputParser.ParseNetworks(null));
        Assert.Empty(WifiOutputParser.ParseNetworks(string.Empty));
    }

    [Fact]
    public void ParseCurrentSsid_LeALinhaSemNumero()
    {
        const string interfaces = """
            Nome                   : Wi-Fi
            Descrição              : RZ616 Wi-Fi 6E
            Estado                 : Conectado
            SSID                   : G@mes91
            BSSID                  : 3c:6a:d2:5b:3e:7f
            """;

        Assert.Equal("G@mes91", WifiOutputParser.ParseCurrentSsid(interfaces));
        Assert.Null(WifiOutputParser.ParseCurrentSsid("Estado : Desconectado"));
    }

    [Theory]
    [InlineData("""
            Perfis na interface Wi-Fi:
            Perfil de Todos os Usuários : G@mes91
            Perfil de Todos os Usuários : COSTA-5G
            Total de perfis             : 2
            """)]
    [InlineData("""
            Profiles on interface Wi-Fi:
            All User Profile     : G@mes91
            All User Profile     : COSTA-5G
            Total profiles       : 2
            """)]
    public void ParseProfiles_FuncionaEmPortuguesEIngles(string output)
    {
        var profiles = WifiOutputParser.ParseProfiles(output);

        Assert.Equal(2, profiles.Count);
        Assert.Contains("G@mes91", profiles);
        Assert.Contains("COSTA-5G", profiles);
    }
}
