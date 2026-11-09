using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Radios;

namespace HandheldLauncher.Services;

public class RadioService
{
    public async Task<bool> IsRadioOnAsync(RadioKind kind)
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            var radio = radios.FirstOrDefault(r => r.Kind == kind);
            return radio?.State == RadioState.On;
        }
        catch { return false; }
    }

    public async Task<bool> SetRadioStateAsync(RadioKind kind, bool turnOn)
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            var radio = radios.FirstOrDefault(r => r.Kind == kind);
            if (radio != null)
            {
                var result = await radio.SetStateAsync(turnOn ? RadioState.On : RadioState.Off);
                return result == RadioAccessStatus.Allowed;
            }
        }
        catch { }
        return false;
    }

    public async Task<bool> ToggleAirplaneModeAsync()
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            var wifi = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
            var bt = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            
            // Se o Wi-Fi estiver ligado, desliga tudo (Avião ON)
            bool turnOn = wifi?.State != RadioState.On;
            
            if (wifi != null) await wifi.SetStateAsync(turnOn ? RadioState.On : RadioState.Off);
            if (bt != null) await bt.SetStateAsync(turnOn ? RadioState.On : RadioState.Off);
            return true;
        }
        catch { return false; }
    }
}