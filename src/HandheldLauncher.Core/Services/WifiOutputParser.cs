using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// Lê a saída do <c>netsh wlan ...</c> — que é **localizada** (pt-BR, en-US...),
/// por isso nada aqui depende de rótulos traduzidos: só de "SSID", dos números
/// e da posição dos dois-pontos.
///
/// Vive no Core (sem UI nem WinRT) para poder ser testado — foi aqui que nasceu
/// o bug da "rede fantasma" chamada "Tipo de rede : Infraestrutura".
/// </summary>
public static class WifiOutputParser
{
    /// <summary>Rede vista no scan: nome, sinal em percentagem (0–100) e se exige senha.</summary>
    public readonly record struct ScannedNetwork(string Ssid, int Signal, bool IsSecured);

    // "SSID 1 : nome".
    // ATENÇÃO: [ \t] em vez de \s. Com \s o "espaço" do regex ATRAVESSA a mudança
    // de linha e, numa rede oculta ("SSID 1 :" sem nome), capturava a linha
    // seguinte ("Tipo de rede : Infraestrutura") como se fosse o SSID.
    private static readonly Regex SsidLine =
        new(@"^[ \t]*SSID[ \t]+\d+[ \t]*:[ \t]*(?<name>[^\r\n]*?)[ \t]*\r?$", RegexOptions.Multiline);

    // A linha "SSID : nome" (sem número) só existe na interface ligada.
    private static readonly Regex CurrentSsidLine =
        new(@"^[ \t]*SSID[ \t]*:[ \t]*(?<name>[^\r\n]*?)[ \t]*\r?$", RegexOptions.Multiline);

    // "Sinal : 82%" / "Signal : 82%" — só o número, sem depender do rótulo.
    private static readonly Regex Percent = new(@"(?<pct>\d{1,3})[ \t]*%");

    /// <summary>
    /// Redes do <c>netsh wlan show networks mode=bssid</c>, sem duplicados, com
    /// o melhor sinal de cada SSID e por ordem de sinal (mais forte primeiro).
    /// </summary>
    public static IReadOnlyList<ScannedNetwork> ParseNetworks(string? netshScanOutput)
    {
        if (string.IsNullOrEmpty(netshScanOutput)) return Array.Empty<ScannedNetwork>();

        var best = new Dictionary<string, ScannedNetwork>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in SsidLine.Matches(netshScanOutput))
        {
            var name = match.Groups["name"].Value.Trim();
            if (name.Length == 0) continue; // rede oculta: sem nome, não dá para listar

            var block = BlockAfter(netshScanOutput, match.Index);
            var percent = Percent.Match(block);
            var signal = percent.Success
                && int.TryParse(percent.Groups["pct"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                    ? Math.Clamp(value, 0, 100)
                    : 0;

            // "Autenticação : Aberta" / "Authentication : Open" = sem senha.
            var secured = !block.Contains("Aberta", StringComparison.OrdinalIgnoreCase)
                          && !block.Contains("Open", StringComparison.OrdinalIgnoreCase);

            if (!best.TryGetValue(name, out var current) || signal > current.Signal)
                best[name] = new ScannedNetwork(name, signal, secured);
        }

        return best.Values
            .OrderByDescending(network => network.Signal)
            .ThenBy(network => network.Ssid, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>SSID ligado agora segundo <c>netsh wlan show interfaces</c>.</summary>
    public static string? ParseCurrentSsid(string? netshInterfacesOutput)
    {
        if (string.IsNullOrEmpty(netshInterfacesOutput)) return null;

        foreach (Match match in CurrentSsidLine.Matches(netshInterfacesOutput))
        {
            var name = match.Groups["name"].Value.Trim();
            if (name.Length > 0) return name;
        }

        return null;
    }

    /// <summary>
    /// Perfis salvos, de <c>netsh wlan show profiles</c>: pega o texto depois dos
    /// dois-pontos e descarta cabeçalhos e contadores ("Total de perfis : 5").
    /// </summary>
    public static IReadOnlyCollection<string> ParseProfiles(string? netshProfilesOutput)
    {
        var profiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(netshProfilesOutput)) return profiles;

        foreach (var raw in netshProfilesOutput.Split('\n'))
        {
            var line = raw.Trim();
            var colon = line.LastIndexOf(':');
            if (colon <= 0 || colon == line.Length - 1) continue;

            var value = line[(colon + 1)..].Trim();
            if (value.Length == 0) continue;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) continue; // contadores
            profiles.Add(value);
        }

        return profiles;
    }

    // Texto entre esta rede e a próxima (o sinal aparece nos BSSID do bloco).
    private static string BlockAfter(string output, int index)
    {
        var next = output.IndexOf("\nSSID ", index + 1, StringComparison.Ordinal);
        return next < 0 ? output[index..] : output[index..next];
    }
}
