using System;
using System.Linq;
using System.Threading.Tasks;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.Services;

// RadioKind passou a ser um enum do próprio projeto (antes vinha do WinRT
// Windows.Devices.Radios, que só existe no Windows). A UI usa estes valores em
// qualquer plataforma; a implementação real de rádio é Windows-only por agora
// (a Fase 4 liga o Linux ao rfkill/bluetoothctl).
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
        // rfkill: "Soft blocked: no" = rádio ligado.
        var output = LinuxShell.RunCapture("rfkill", $"list {RfkillName(kind)}") ?? string.Empty;
        return output.Contains("Soft blocked: no", StringComparison.OrdinalIgnoreCase);
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
        var ok = LinuxShell.Run("rfkill", $"{(turnOn ? "unblock" : "block")} {RfkillName(kind)}");
        DebugLog.Write($"[RADIO] Linux: {(turnOn ? "ligar" : "desligar")} {RfkillName(kind)} {(ok ? "ok" : "falhou")}.");
        return ok;
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

            // Se o Wi-Fi estiver ligado, desliga tudo (Avião ON)
            bool turnOn = wifi?.State != Windows.Devices.Radios.RadioState.On;

            if (wifi != null) await wifi.SetStateAsync(turnOn ? Windows.Devices.Radios.RadioState.On : Windows.Devices.Radios.RadioState.Off);
            if (bt != null) await bt.SetStateAsync(turnOn ? Windows.Devices.Radios.RadioState.On : Windows.Devices.Radios.RadioState.Off);
            return true;
        }
        catch { return false; }
#else
        // Modo avião: bloqueia/desbloqueia tudo (Wi-Fi + Bluetooth) de uma vez.
        var wifiOn = (LinuxShell.RunCapture("rfkill", "list wifi") ?? string.Empty)
            .Contains("Soft blocked: no", StringComparison.OrdinalIgnoreCase);
        var ok = LinuxShell.Run("rfkill", wifiOn ? "block all" : "unblock all");
        DebugLog.Write($"[RADIO] Linux: modo avião {(wifiOn ? "LIGADO" : "DESLIGADO")} {(ok ? "ok" : "falhou")}.");
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