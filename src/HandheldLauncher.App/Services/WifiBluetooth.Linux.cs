#if !WINDOWS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.Services;

// ---- Serviços Linux de rede (Wi-Fi/Bluetooth) -------------------------------
// No Windows a implementação real usa WinRT (Windows.Devices.WiFi/Bluetooth),
// que não existe no .NET puro — por isso vive em WifiService.cs e
// BluetoothService.cs, ambos compilados só com `#if WINDOWS`. Estas
// implementações mantêm a mesma API pública e usam NetworkManager/BlueZ.

public class WifiNetwork
{
    public string Ssid { get; set; } = "";
    public int SignalStrength { get; set; }
    public bool IsConnected { get; set; }
    public bool IsSecured { get; set; }
    public bool HasProfile { get; set; }

    public string ActionText => IsConnected ? "Desconectar" : HasProfile ? "Conectar" : "Senha";
}

public class BtDevice
{
    public string Name { get; set; } = "";
    public bool IsConnected { get; set; }
    public bool IsPaired { get; set; }
    public bool IsLowEnergy { get; set; }
    public ulong Address { get; set; }

    public string ActionText => IsConnected ? "Desconectar" : IsPaired ? "Conectar" : "Emparelhar";
    public string StatusText => IsConnected ? "Ligado" : IsPaired ? "Pareado" : "Novo";
}

public class WifiService
{
    private static readonly object ScanLock = new();
    private static DateTime _lastScanUtc = DateTime.MinValue;

    public async Task<List<WifiNetwork>> GetAvailableNetworksAsync()
    {
        return await Task.Run(() =>
        {
            var networks = new List<WifiNetwork>();
            lock (ScanLock)
            {
                if (DateTime.UtcNow - _lastScanUtc > TimeSpan.FromSeconds(15))
                {
                    _ = LinuxShell.Run("nmcli", "device", "wifi", "rescan");
                    _lastScanUtc = DateTime.UtcNow;
                }
            }

            var output = LinuxShell.RunCapture("nmcli", "-t", "-f", "SSID,SIGNAL,SECURITY,IN-USE", "device", "wifi", "list");
            if (string.IsNullOrWhiteSpace(output)) return networks;

            var profilesOutput = LinuxShell.RunCapture("nmcli", "-t", "-f", "NAME", "connection", "show") ?? string.Empty;
            var profiles = profilesOutput.Split('\n')
                .Select(line => NmcliOutputParser.ParseTerseFields(line).FirstOrDefault() ?? string.Empty)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var line in output.Split('\n'))
            {
                var fields = NmcliOutputParser.ParseTerseFields(line);
                if (fields.Count < 4) continue;

                var ssid = fields[0];
                var signal = int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : 0;
                var security = fields[2];
                var inUse = fields[3] == "*";

                if (string.IsNullOrWhiteSpace(ssid)) continue;

                var existing = networks.FirstOrDefault(n => n.Ssid == ssid);
                if (existing is null)
                {
                    networks.Add(new WifiNetwork
                    {
                        Ssid = ssid,
                        SignalStrength = signal,
                        IsSecured = !string.IsNullOrEmpty(security) && security != "--",
                        HasProfile = profiles.Contains(ssid),
                        IsConnected = inUse,
                    });
                }
                else
                {
                    existing.SignalStrength = Math.Max(existing.SignalStrength, signal);
                    existing.IsConnected |= inUse;
                }
            }

            return networks.OrderByDescending(n => n.IsConnected).ThenByDescending(n => n.SignalStrength).ToList();
        });
    }

    public async Task<string?> GetCurrentSsidAsync() => await Task.Run(() => GetCurrentSsid());

    public async Task<bool> ConnectAsync(string ssid, string? password = null)
    {
        return await Task.Run(() =>
        {
            var arguments = string.IsNullOrEmpty(password)
                ? new[] { "device", "wifi", "connect", ssid }
                : new[] { "device", "wifi", "connect", ssid, "password", password };
            var ok = LinuxShell.Run("nmcli", arguments);
            DebugLog.Write($"[WIFI] Linux: conectar '{ssid}' {(ok ? "ok" : "falhou")}.");
            return ok;
        });
    }

    public async Task<bool> DisconnectAsync()
    {
        return await Task.Run(() =>
        {
            var devices = LinuxShell.RunCapture("nmcli", "-t", "-f", "DEVICE,TYPE,STATE", "device", "status");
            var wifiDevice = devices?.Split('\n')
                .Select(NmcliOutputParser.ParseTerseFields)
                .FirstOrDefault(fields => fields.Count >= 3
                    && fields[1].Equals("wifi", StringComparison.OrdinalIgnoreCase)
                    && fields[2].StartsWith("connected", StringComparison.OrdinalIgnoreCase));
            if (wifiDevice is null || wifiDevice.Count == 0)
            {
                DebugLog.Write("[WIFI] Linux: não foi encontrada uma interface Wi-Fi conectada.");
                return false;
            }

            var ok = LinuxShell.Run("nmcli", "device", "disconnect", wifiDevice[0]);
            DebugLog.Write($"[WIFI] Linux: desconectar {(ok ? "ok" : "falhou")}.");
            return ok;
        });
    }

    private static string? GetCurrentSsid()
    {
        var output = LinuxShell.RunCapture("nmcli", "-t", "-f", "IN-USE,SSID", "device", "wifi");
        return NmcliOutputParser.ParseCurrentSsid(output);
    }
}

public class BluetoothService
{
    public async Task<List<BtDevice>> GetDevicesAsync(TimeSpan? discovery = null)
    {
        return await Task.Run(() =>
        {
            if (discovery is { } window && window > TimeSpan.Zero)
            {
                var scanDuration = TimeSpan.FromSeconds(Math.Clamp(window.TotalSeconds, 1, 10));
                _ = LinuxShell.RunSessionAsync(
                    "bluetoothctl",
                    "scan on",
                    scanDuration,
                    ["scan off", "quit"]).GetAwaiter().GetResult();
            }

            var devices = new List<BtDevice>();
            var output = LinuxShell.RunCapture("bluetoothctl", "devices") ?? string.Empty;
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("Device ", StringComparison.Ordinal)) continue;
                var rest = trimmed["Device ".Length..].Trim();
                var space = rest.IndexOf(' ');
                var mac = space > 0 ? rest[..space] : rest;
                var name = space > 0 ? rest[(space + 1)..].Trim() : mac;

                var info = LinuxShell.RunCapture("bluetoothctl", "info", mac) ?? string.Empty;
                devices.Add(new BtDevice
                {
                    Name = string.IsNullOrEmpty(name) ? mac : name,
                    Address = ParseMac(mac),
                    IsPaired = info.Contains("Paired: yes", StringComparison.OrdinalIgnoreCase),
                    IsConnected = info.Contains("Connected: yes", StringComparison.OrdinalIgnoreCase),
                });
            }
            return devices.OrderByDescending(d => d.IsConnected).ThenByDescending(d => d.IsPaired).ThenBy(d => d.Name).ToList();
        });
    }

    public async Task<bool> PairAsync(BtDevice device)
    {
        return await Task.Run(() =>
        {
            var mac = FormatMac(device.Address);
            var pairOk = LinuxShell.Run("bluetoothctl", "pair", mac);
            var trustOk = pairOk && LinuxShell.Run("bluetoothctl", "trust", mac);
            var ok = trustOk && LinuxShell.Run("bluetoothctl", "connect", mac);
            DebugLog.Write($"[BT] Linux: emparelhar '{device.Name}' {(ok ? "ok" : "falhou")}.");
            return ok;
        });
    }

    public Task<bool> ConnectAsync(BtDevice device)
        => Task.Run(() => LinuxShell.Run("bluetoothctl", "connect", FormatMac(device.Address)));

    public Task<bool> DisconnectAsync(BtDevice device)
        => Task.Run(() => LinuxShell.Run("bluetoothctl", "disconnect", FormatMac(device.Address)));

    private static ulong ParseMac(string mac)
    {
        try { return Convert.ToUInt64(mac.Replace(":", ""), 16); }
        catch { return 0; }
    }

    private static string FormatMac(ulong address)
    {
        var hex = address.ToString("X12");
        return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
    }
}

// ---------- helpers de shell (nmcli / bluetoothctl) ----------

internal static class LinuxShell
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

    public static bool Run(string file, params string[] arguments)
    {
        try
        {
            using var process = new Process { StartInfo = CreateStartInfo(file, arguments) };
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)CommandTimeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception ex) { DebugLog.Swallowed(ex); }
                DebugLog.Write($"[LINUX] '{file}' excedeu o tempo limite.");
                return false;
            }
            _ = stdout.GetAwaiter().GetResult();
            _ = stderr.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
                DebugLog.Write($"[LINUX] '{file}' terminou com código {process.ExitCode}.");
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[LINUX] '{file}' indisponível: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public static string? RunCapture(string file, params string[] arguments)
    {
        try
        {
            using var process = new Process { StartInfo = CreateStartInfo(file, arguments) };
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)CommandTimeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception ex) { DebugLog.Swallowed(ex); }
                DebugLog.Write($"[LINUX] '{file}' excedeu o tempo limite.");
                return null;
            }
            var output = stdout.GetAwaiter().GetResult();
            _ = stderr.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                DebugLog.Write($"[LINUX] '{file}' terminou com código {process.ExitCode}.");
                return null;
            }
            return output;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[LINUX] '{file}' indisponível: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    public static async Task<bool> RunSessionAsync(string file, string firstCommand, TimeSpan duration, string[] finalCommands)
    {
        try
        {
            using var process = new Process { StartInfo = CreateStartInfo(file, Array.Empty<string>(), redirectInput: true) };
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            await process.StandardInput.WriteLineAsync(firstCommand);
            await process.StandardInput.FlushAsync();
            await Task.Delay(duration);
            foreach (var command in finalCommands)
                await process.StandardInput.WriteLineAsync(command);
            process.StandardInput.Close();

            if (!process.WaitForExit(5000))
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception ex) { DebugLog.Swallowed(ex); }
                DebugLog.Write($"[LINUX] Sessão '{file}' excedeu o tempo limite.");
                return false;
            }

            _ = stdout.GetAwaiter().GetResult();
            _ = stderr.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
                DebugLog.Write($"[LINUX] Sessão '{file}' terminou com código {process.ExitCode}.");
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[LINUX] Sessão '{file}' falhou: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static ProcessStartInfo CreateStartInfo(string file, IEnumerable<string> arguments, bool redirectInput = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = redirectInput,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }
}
#endif