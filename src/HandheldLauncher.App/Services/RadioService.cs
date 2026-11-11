using System;
using System.Linq;
using System.Threading.Tasks;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.Services;

// RadioKind é independente da plataforma; cada sistema usa a API de rádio
// disponível, com rfkill como alternativa no Linux.
public enum RadioKind
{
    WiFi,
    Bluetooth,
}

public class RadioService
{
    public async Task<bool> IsRadioOnAsync(RadioKind kind)
    {
#if WINDOWS
        try
        {
            var radios = await Windows.Devices.Radios.Radio.GetRadiosAsync();
            var radio = radios.FirstOrDefault(r => r.Kind == ToWinRt(kind));
            return radio?.State == Windows.Devices.Radios.RadioState.On;
        }
        catch { return false; }
#else
        return await Task.Run(() =>
        {
            var output = kind switch
            {
                RadioKind.WiFi => LinuxShell.RunCapture("nmcli", "radio", "wifi"),
                RadioKind.Bluetooth => LinuxShell.RunCapture("bluetoothctl", "show"),
                _ => null,
            };

            if (kind == RadioKind.WiFi && output is not null)
            {
                if (output.Trim().Equals("enabled", StringComparison.OrdinalIgnoreCase)) return true;
                if (output.Trim().Equals("disabled", StringComparison.OrdinalIgnoreCase)) return false;
            }
            else if (kind == RadioKind.Bluetooth && output is not null)
            {
                if (output.Contains("Powered: yes", StringComparison.OrdinalIgnoreCase)) return true;
                if (output.Contains("Powered: no", StringComparison.OrdinalIgnoreCase)) return false;
            }

            var rfkillOutput = LinuxShell.RunCapture("rfkill", "list", RfkillName(kind)) ?? string.Empty;
            return rfkillOutput.Contains("Soft blocked: no", StringComparison.OrdinalIgnoreCase)
                && !rfkillOutput.Contains("Hard blocked: yes", StringComparison.OrdinalIgnoreCase);
        });
#endif
    }

    public async Task<bool> SetRadioStateAsync(RadioKind kind, bool turnOn)
    {
#if WINDOWS
        try
        {
            var radios = await Windows.Devices.Radios.Radio.GetRadiosAsync();
            var radio = radios.FirstOrDefault(r => r.Kind == ToWinRt(kind));
            if (radio != null)
            {
                var result = await radio.SetStateAsync(turnOn ? Windows.Devices.Radios.RadioState.On : Windows.Devices.Radios.RadioState.Off);
                return result == Windows.Devices.Radios.RadioAccessStatus.Allowed;
            }
        }
        catch { }
        return false;
#else
        return await Task.Run(() =>
        {
            var ok = kind switch
            {
                RadioKind.WiFi => LinuxShell.Run("nmcli", "radio", "wifi", turnOn ? "on" : "off"),
                RadioKind.Bluetooth => LinuxShell.Run("bluetoothctl", "power", turnOn ? "on" : "off"),
                _ => false,
            };
            if (!ok)
                ok = LinuxShell.Run("rfkill", turnOn ? "unblock" : "block", RfkillName(kind));
            DebugLog.Write($"[RADIO] Linux: {(turnOn ? "ligar" : "desligar")} {RfkillName(kind)} {(ok ? "ok" : "falhou")}.");
            return ok;
        });
#endif
    }

    public async Task<bool> ToggleAirplaneModeAsync()
    {
#if WINDOWS
        try
        {
            var radios = await Windows.Devices.Radios.Radio.GetRadiosAsync();
            var wifi = radios.FirstOrDefault(r => r.Kind == Windows.Devices.Radios.RadioKind.WiFi);
            var bt = radios.FirstOrDefault(r => r.Kind == Windows.Devices.Radios.RadioKind.Bluetooth);

            bool turnOn = wifi?.State != Windows.Devices.Radios.RadioState.On
                && bt?.State != Windows.Devices.Radios.RadioState.On;

            if (wifi != null) await wifi.SetStateAsync(turnOn ? Windows.Devices.Radios.RadioState.On : Windows.Devices.Radios.RadioState.Off);
            if (bt != null) await bt.SetStateAsync(turnOn ? Windows.Devices.Radios.RadioState.On : Windows.Devices.Radios.RadioState.Off);
            return true;
        }
        catch { return false; }
#else
        var wifiOn = await IsRadioOnAsync(RadioKind.WiFi);
        var bluetoothOn = await IsRadioOnAsync(RadioKind.Bluetooth);
        var turnOn = !wifiOn && !bluetoothOn;
        var wifiOk = await SetRadioStateAsync(RadioKind.WiFi, turnOn);
        var bluetoothOk = await SetRadioStateAsync(RadioKind.Bluetooth, turnOn);
        var ok = wifiOk && bluetoothOk;
        DebugLog.Write($"[RADIO] Modo avião {(turnOn ? "desligado" : "ligado")} {(ok ? "ok" : "falhou")}.");
        return ok;
#endif
    }

#if !WINDOWS
    private static string RfkillName(RadioKind kind) => kind switch
    {
        RadioKind.WiFi => "wifi",
        RadioKind.Bluetooth => "bluetooth",
        _ => "all",
    };
#endif

#if WINDOWS
    private static Windows.Devices.Radios.RadioKind ToWinRt(RadioKind kind) => kind switch
    {
        RadioKind.WiFi => Windows.Devices.Radios.RadioKind.WiFi,
        RadioKind.Bluetooth => Windows.Devices.Radios.RadioKind.Bluetooth,
        _ => Windows.Devices.Radios.RadioKind.Other,
    };
#endif
}