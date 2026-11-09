#if WINDOWS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.Core.Services;
using Windows.Devices.WiFi;
using Windows.Networking.Connectivity;
using Windows.Security.Credentials;

namespace HandheldLauncher.Services;

/// <summary>
/// Uma rede Wi-Fi visível. <see cref="SignalStrength"/> passou a ser a
/// percentagem real (antes era 100 fixo, por isso nenhuma rede se distinguia
/// das outras) e <see cref="IsConnected"/> vem da interface ativa, o que
/// permite destacar a rede em uso na lista.
/// </summary>
public class WifiNetwork
{
    public string Ssid { get; set; } = "";
    public int SignalStrength { get; set; }
    public bool IsConnected { get; set; }
    public bool IsSecured { get; set; }
    public bool HasProfile { get; set; }

    /// <summary>Rótulo do botão de ação na lista.</summary>
    public string ActionText => IsConnected ? "Desconectar" : HasProfile ? "Conectar" : "Senha";
}

public class WifiService
{
    // O parsing do texto do netsh vive em HandheldLauncher.Core (WifiOutputParser)
    // — é lógica pura e testável, e foi lá que se corrigiu a "rede fantasma".

    // Último resultado bom: serve de rede de segurança quando uma leitura volta
    // degenerada (só a rede conectada) logo depois de conectar.
    private static List<WifiNetwork> _lastScan = new();

    // O Windows limita scans e "castiga" quem insiste: passa a devolver listas
    // vazias/parciais para TODO o sistema (inclusive a tela de Wi-Fi do
    // Windows). Por isso só forçamos um scan de verdade a cada 20s — no resto
    // lemos o relatório atual da placa.
    private static DateTime _lastForcedScan = DateTime.MinValue;
    private static readonly TimeSpan MinScanInterval = TimeSpan.FromSeconds(20);

    public async Task<List<WifiNetwork>> GetAvailableNetworksAsync()
    {
        // WinRT e netsh só existem no Windows; nos outros SOs devolve vazio em vez
        // de rebentar (o release.yml também publica linux-x64).
        if (!OperatingSystem.IsWindows()) return new List<WifiNetwork>();

        var winrtScan = await ScanWithWinRtAsync();
        var netshScan = await Task.Run(ScanWithNetsh);

        var merged = Merge(winrtScan, netshScan);

        // Leitura degenerada: completa com o último resultado bom, em vez de
        // mostrar só a rede conectada (era o sintoma "não aparecem as outras").
        if (merged.Count < 2 && _lastScan.Count > merged.Count)
            merged = Merge(winrtScan, netshScan, _lastScan);

        DebugLog.Write(
            $"[WIFI] Scan: winrt={winrtScan.Count}, netsh={netshScan.Count}, exibidas={merged.Count}" +
            (merged.Count > 0 ? $" → {string.Join(" | ", merged.Select(n => n.Ssid))}" : string.Empty));

        if (merged.Count > 0) _lastScan = merged;
        return merged;
    }

    // Une as fontes por SSID: fica com o melhor sinal e com o estado "conectado"
    // de qualquer uma delas (o netsh e o WinRT discordam às vezes).
    private static List<WifiNetwork> Merge(params IEnumerable<WifiNetwork>[] sources)
    {
        var merged = new List<WifiNetwork>();

        foreach (var network in sources.SelectMany(source => source))
        {
            if (string.IsNullOrWhiteSpace(network.Ssid)) continue;

            var existing = merged.FirstOrDefault(n =>
                string.Equals(n.Ssid, network.Ssid, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                merged.Add(network);
                continue;
            }

            existing.SignalStrength = Math.Max(existing.SignalStrength, network.SignalStrength);
            existing.IsConnected |= network.IsConnected;
            existing.HasProfile |= network.HasProfile;
            existing.IsSecured |= network.IsSecured;
        }

        return merged
            .OrderByDescending(n => n.IsConnected)
            .ThenByDescending(n => n.SignalStrength)
            .ToList();
    }

    // Reserva: lê o cache do Windows via netsh (não força scan).
    private static List<WifiNetwork> ScanWithNetsh()
    {
        try
        {
            var currentSsid = WifiOutputParser.ParseCurrentSsid(Run("wlan show interfaces"));
            var profiles = WifiOutputParser.ParseProfiles(Run("wlan show profiles"));
            var output = Run("wlan show networks mode=bssid");

            return WifiOutputParser.ParseNetworks(output)
                .Select(network => new WifiNetwork
                {
                    Ssid = network.Ssid,
                    SignalStrength = network.Signal,
                    IsSecured = network.IsSecured,
                    IsConnected = string.Equals(network.Ssid, currentSsid, StringComparison.OrdinalIgnoreCase),
                    HasProfile = profiles.Contains(network.Ssid),
                })
                .ToList();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[WIFI] Erro ao listar redes: {ex.Message}");
            return new List<WifiNetwork>();
        }
    }

    /// <summary>SSID ligado agora (null quando está desconectado).</summary>
    public async Task<string?> GetCurrentSsidAsync() => await Task.Run(() =>
        WifiOutputParser.ParseCurrentSsid(Run("wlan show interfaces")));

    /// <summary>
    /// Conecta. Se a rede já tem perfil salvo, liga direto; senão cria o perfil
    /// na hora com a <paramref name="password"/> (sem isso não há como ligar).
    /// </summary>
    public async Task<bool> ConnectAsync(string ssid, string? password = null)
    {
        // 1) WinRT: conecta sem precisar de perfil salvo (cria na hora) e aceita a senha.
        try
        {
            var adapter = await GetAdapterAsync();
            var target = adapter is null ? null : await FindNetworkAsync(ssid);
            if (adapter is not null && target is not null)
            {
                var credential = string.IsNullOrEmpty(password) ? null : new PasswordCredential { Password = password };
                var result = await adapter.ConnectAsync(target, WiFiReconnectionKind.Automatic, credential);
                DebugLog.Write($"[WIFI] WinRT connect '{ssid}': {result.ConnectionStatus}");
                if (result.ConnectionStatus == WiFiConnectionStatus.Success) return true;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[WIFI] WinRT connect falhou ({ex.Message}); tentando netsh.");
        }

        // 2) Reserva: netsh (exige perfil salvo, ou a senha para criar um).
        return await Task.Run(() =>
        {
            try
            {
                if (!WifiOutputParser.ParseProfiles(Run("wlan show profiles")).Contains(ssid))
                {
                    if (string.IsNullOrEmpty(password)) return false;
                    if (!AddProfile(ssid, password)) return false;
                }

                var result = Run($"wlan connect name=\"{ssid}\" ssid=\"{ssid}\"");
                return !result.Contains("não", StringComparison.OrdinalIgnoreCase)
                       && !result.Contains("fail", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[WIFI] Erro ao conectar em {ssid}: {ex.Message}");
                return false;
            }
        });
    }

    public async Task<bool> DisconnectAsync()
    {
        try
        {
            var adapter = await GetAdapterAsync();
            if (adapter is not null)
            {
                adapter.Disconnect();
                return true;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[WIFI] WinRT disconnect falhou ({ex.Message}); tentando netsh.");
        }

        return await Task.Run(() =>
        {
            try
            {
                Run("wlan disconnect");
                return true;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[WIFI] Erro ao desconectar: {ex.Message}");
                return false;
            }
        });
    }

    // ---------- WinRT (fonte principal: força scan e conecta sem perfil) ----------

    private static WiFiAdapter? _cachedAdapter;

    private static async Task<WiFiAdapter?> GetAdapterAsync()
    {
        if (_cachedAdapter is not null) return _cachedAdapter;

        try
        {
            // Em app desktop sem empacotamento isto costuma devolver Allowed;
            // se negar, o ScanWithNetsh continua a servir de reserva.
            await WiFiAdapter.RequestAccessAsync();
            var adapters = await WiFiAdapter.FindAllAdaptersAsync();
            _cachedAdapter = adapters.FirstOrDefault();
            if (_cachedAdapter is null) DebugLog.Write("[WIFI] WinRT: nenhum adaptador Wi-Fi encontrado.");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[WIFI] WinRT indisponível: {ex.Message}");
        }

        return _cachedAdapter;
    }

    private static async Task<List<WifiNetwork>> ScanWithWinRtAsync()
    {
        var adapter = await GetAdapterAsync();
        if (adapter is null) return new List<WifiNetwork>();

        // Scan real no máximo a cada 20s (ver MinScanInterval): insistir faz o
        // Windows parar de devolver redes — para nós E para a UI do sistema.
        if (DateTime.UtcNow - _lastForcedScan >= MinScanInterval)
        {
            try
            {
                await adapter.ScanAsync();
                _lastForcedScan = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[WIFI] Scan não concluiu ({ex.GetType().Name}); usando o último relatório.");
            }
        }

        var profiles = WifiOutputParser.ParseProfiles(Run("wlan show profiles")); // só para o rótulo "Senha"
        var connectedSsid = GetConnectedSsid();

        return adapter.NetworkReport.AvailableNetworks
            .Where(n => !string.IsNullOrWhiteSpace(n.Ssid))
            .GroupBy(n => n.Ssid, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(n => n.SignalBars).First())
            .Select(n => new WifiNetwork
            {
                Ssid = n.Ssid,
                SignalStrength = PercentFromSignal(n),
                IsConnected = string.Equals(n.Ssid, connectedSsid, StringComparison.OrdinalIgnoreCase),
                IsSecured = n.SecuritySettings.NetworkAuthenticationType != NetworkAuthenticationType.Open80211,
                HasProfile = profiles.Contains(n.Ssid),
            })
            .OrderByDescending(n => n.IsConnected)
            .ThenByDescending(n => n.SignalStrength)
            .ToList();
    }

    // SSID ligado agora, pelo WinRT (o WiFiAvailableNetwork não traz esse estado).
    private static string? GetConnectedSsid()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            return profile?.WlanConnectionProfileDetails?.GetConnectedSsid();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[WIFI] Não foi possível ler o SSID conectado: {ex.Message}");
            return null;
        }
    }

    private static async Task<WiFiAvailableNetwork?> FindNetworkAsync(string ssid)
    {
        var adapter = await GetAdapterAsync();
        return adapter?.NetworkReport.AvailableNetworks
            .FirstOrDefault(n => string.Equals(n.Ssid, ssid, StringComparison.OrdinalIgnoreCase));
    }

    // O Windows dá RSSI (dBm) e barras (0–5); convertemos para % como no resto do app.
    private static int PercentFromSignal(WiFiAvailableNetwork network)
    {
        var rssi = network.NetworkRssiInDecibelMilliwatts;
        if (rssi < 0) return Math.Clamp((int)(2 * (rssi + 100)), 0, 100);
        return Math.Clamp(network.SignalBars * 20, 0, 100);
    }

    // ---------- helpers ----------

    private static string Run(string arguments)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }

    private static bool AddProfile(string ssid, string password)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hl-wifi-{Guid.NewGuid():N}.xml");
        var xml = $"""
            <?xml version="1.0"?>
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
              <name>{SecurityElement.Escape(ssid)}</name>
              <SSIDConfig><SSID><name>{SecurityElement.Escape(ssid)}</name></SSID></SSIDConfig>
              <connectionType>ESS</connectionType>
              <connectionMode>auto</connectionMode>
              <MSM><security>
                <authEncryption><authentication>WPA2PSK</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption>
                <sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>{SecurityElement.Escape(password)}</keyMaterial></sharedKey>
              </security></MSM>
            </WLANProfile>
            """;

        try
        {
            File.WriteAllText(path, xml, Encoding.UTF8);
            var result = Run($"wlan add profile filename=\"{path}\" user=current");
            return !result.Contains("fail", StringComparison.OrdinalIgnoreCase)
                   && !result.Contains("não", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[WIFI] Erro ao criar perfil de {ssid}: {ex.Message}");
            return false;
        }
        finally
        {
            try { File.Delete(path); } catch { /* arquivo temporário */ }
        }
    }
}
#endif
