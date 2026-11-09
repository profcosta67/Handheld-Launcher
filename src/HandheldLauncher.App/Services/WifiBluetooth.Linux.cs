#if !WINDOWS
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.Services;

// ---- Stubs Linux dos serviços de rede (Wi-Fi/Bluetooth) ---------------------
// No Windows a implementação real usa WinRT (Windows.Devices.WiFi/Bluetooth),
// que não existe no .NET puro — por isso vive em WifiService.cs e
// BluetoothService.cs, ambos compilados só com `#if WINDOWS`. Estes stubs
// mantêm a MESMA API pública para o resto do app compilar em net8.0; a
// implementação real via NetworkManager (nmcli) e bluetoothctl chega na Fase 4.

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
    public async Task<List<WifiNetwork>> GetAvailableNetworksAsync()
    {
        return await Task.Run(() =>
        {
            var networks = new List<WifiNetwork>();
            var output = LinuxShell.RunCapture("nmcli", "-t -f SSID,SIGNAL,SECURITY,IN-USE device wifi list");
            if (string.IsNullOrWhiteSpace(output)) return networks;

            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                // Formato terse: SSID:SINAL:SEGURANCA:IN-USE (o SSID pode conter ':').
                var fields = trimmed.Split(':');
                if (fields.Length < 3) continue;

                var ssid = string.Join(':', fields[..^3]);
                var signal = int.TryParse(fields[^3], out var s) ? s : 0;
                var security = fields[^2];
                var inUse = fields[^1] == "*";

                if (string.IsNullOrWhiteSpace(ssid)) continue;

                var existing = networks.FirstOrDefault(n => n.Ssid == ssid);
                if (existing is null)
                {
                    networks.Add(new WifiNetwork
                    {
                        Ssid = ssid,
                        SignalStrength = signal,
                        IsSecured = !string.IsNullOrEmpty(security),
                        HasProfile = HasProfile(ssid),
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
            var args = string.IsNullOrEmpty(password)
                ? $"device wifi connect \"{ssid}\""
                : $"device wifi connect \"{ssid}\" password \"{password}\"";
            var ok = LinuxShell.Run("nmcli", args);
            DebugLog.Write($"[WIFI] Linux: conectar '{ssid}' {(ok ? "ok" : "falhou")}.");
            return ok;
        });
    }

    public async Task<bool> DisconnectAsync()
    {
        return await Task.Run(() =>
        {
            var ssid = GetCurrentSsid();
            var ok = LinuxShell.Run("nmcli", string.IsNullOrEmpty(ssid) ? "device disconnect" : $"connection down \"{ssid}\"");
            DebugLog.Write($"[WIFI] Linux: desconectar {(ok ? "ok" : "falhou")}.");
            return ok;
        });
    }

    private static bool HasProfile(string ssid)
    {
        var output = LinuxShell.RunCapture("nmcli", "-t -f NAME connection show") ?? string.Empty;
        return output.Split('\n').Any(l => string.Equals(l.Trim(), ssid, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetCurrentSsid()
    {
        var output = LinuxShell.RunCapture("nmcli", "-t -f active,ssid device wifi");
        foreach (var line in output?.Split('\n') ?? Array.Empty<string>())
        {
            if (line.TrimStart().StartsWith("sim:", StringComparison.OrdinalIgnoreCase))
            {
                var ssid = line.Trim()["sim:".Length..].Trim();
                return string.IsNullOrEmpty(ssid) ? null : ssid;
            }
        }
        return null;
    }
}

public class BluetoothService
{
    public async Task<List<BtDevice>> GetDevicesAsync(TimeSpan? discovery = null)
    {
        return await Task.Run(() =>
        {
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

                var info = LinuxShell.RunCapture("bluetoothctl", $"info {mac}") ?? string.Empty;
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
            LinuxShell.Run("bluetoothctl", $"pair {mac}");
            LinuxShell.Run("bluetoothctl", $"trust {mac}");
            var ok = LinuxShell.Run("bluetoothctl", $"connect {mac}");
            DebugLog.Write($"[BT] Linux: emparelhar '{device.Name}' {(ok ? "ok" : "falhou")}.");
            return ok;
        });
    }

    public Task<bool> ConnectAsync(BtDevice device)
        => Task.Run(() => LinuxShell.Run("bluetoothctl", $"connect {FormatMac(device.Address)}"));

    public Task<bool> DisconnectAsync(BtDevice device)
        => Task.Run(() => LinuxShell.Run("bluetoothctl", $"disconnect {FormatMac(device.Address)}"));

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
    public static bool Run(string file, string arguments)
    {
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = file,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };
            process.Start();
            if (!process.WaitForExit(10_000)) { try { process.Kill(); } catch { } return false; }
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[LINUX] '{file}' indisponível: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public static string? RunCapture(string file, string arguments)
    {
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = file,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(10_000)) { try { process.Kill(); } catch { } return null; }
            return output;
        }
        catch
        {
            return null;
        }
    }
}
#endif