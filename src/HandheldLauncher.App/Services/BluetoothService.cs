#if WINDOWS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Platform;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;

namespace HandheldLauncher.Services;

public class BtDevice
{
    public string Name { get; set; } = "";
    public bool IsConnected { get; set; }
    public bool IsPaired { get; set; }
    public bool IsLowEnergy { get; set; }
    public ulong Address { get; set; }

    public BluetoothDevice? Device { get; set; }
    public BluetoothLEDevice? LeDevice { get; set; }

    /// <summary>Rótulo do botão de ação (A).</summary>
    public string ActionText => IsConnected ? "Desconectar" : IsPaired ? "Conectar" : "Emparelhar";

    /// <summary>Estado mostrado na linha da lista.</summary>
    public string StatusText => IsConnected ? "Ligado" : IsPaired ? "Pareado" : "Novo";
}

/// <summary>
/// Lista e opera dispositivos Bluetooth. Cobre **BT clássico e BLE**: um mouse
/// novo (como o R1mouse2) é LE e não aparecia na enumeração clássica — era o
/// "o Windows detecta mas o launcher não mostra".
/// </summary>
public class BluetoothService
{
    // Propriedade do AEP que diz se está pareado. `info.Pairing.IsPaired` vinha
    // false para aparelhos já conhecidos (HAVIT, Xbox) — a lista mostrava todos
    // como "Novo" e o A tentava emparelhar de novo em vez de conectar.
    private static readonly string[] AepProperties = { "System.Devices.Aep.IsPaired" };

    /// <summary>
    /// Conhecidos (pareados) + descobertos durante <paramref name="discovery"/>.
    /// TimeSpan.Zero = só os conhecidos (refresh periódico, sem varrer o rádio).
    /// </summary>
    public async Task<List<BtDevice>> GetDevicesAsync(TimeSpan? discovery = null)
    {
        if (!OperatingSystem.IsWindows()) return new List<BtDevice>();

        var classic = new Dictionary<string, DeviceInformation>(StringComparer.OrdinalIgnoreCase);
        var lowEnergy = new Dictionary<string, DeviceInformation>(StringComparer.OrdinalIgnoreCase);
        var advertised = new Dictionary<ulong, string>();

        // 1) O que o Windows já conhece — clássico e BLE.
        await FindAllAsync(BluetoothDevice.GetDeviceSelector(), classic);
        await FindAllAsync(BluetoothLEDevice.GetDeviceSelector(), lowEnergy);

        // 2) Descoberta ativa: o "scan". Três fontes em paralelo, cada uma com o
        //    seu try/catch — se uma falhar, as outras continuam.
        var window = discovery ?? TimeSpan.FromSeconds(8);
        if (window > TimeSpan.Zero)
        {
            await Task.WhenAll(
                WatchAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(false), window, classic),
                WatchAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(false), window, lowEnergy),
                WatchAdvertisementsAsync(window, advertised));
        }

        var result = new List<BtDevice>();

        foreach (var info in classic.Values)
            result.Add(await ToDeviceAsync(info, lowEnergy: false));

        foreach (var info in lowEnergy.Values)
        {
            var device = await ToDeviceAsync(info, lowEnergy: true);
            // O mesmo aparelho pode vir nas duas fontes: fica uma entrada só.
            if (!result.Any(d => d.Address != 0 && d.Address == device.Address))
                result.Add(device);
        }

        // 3) Anúncios BLE que ainda não estão na lista (aparelho nunca visto pelo
        //    Windows): resolvemos pelo endereço para poder emparelhar.
        foreach (var advert in advertised)
        {
            if (result.Any(d => d.Address == advert.Key)) continue;

            var device = await FromAdvertisementAsync(advert.Key, advert.Value);
            if (device is not null) result.Add(device);
        }

        // O mesmo aparelho chega por várias fontes (clássico, LE, anúncio) e a
        // entrada clássica às vezes vem sem endereço — daí fundir por endereço
        // OU por nome, senão o R1mouse2 aparecia duas vezes na lista.
        var merged = new List<BtDevice>();
        foreach (var device in result)
        {
            var existing = merged.FirstOrDefault(m =>
                (device.Address != 0 && m.Address == device.Address)
                || string.Equals(m.Name, device.Name, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                merged.Add(device);
                continue;
            }

            existing.IsConnected |= device.IsConnected;
            existing.IsPaired |= device.IsPaired;
            existing.Device ??= device.Device;
            existing.LeDevice ??= device.LeDevice;
            existing.IsLowEnergy |= device.IsLowEnergy;
            if (existing.Address == 0 && device.Address != 0) existing.Address = device.Address;
        }

        result = merged
            .Where(d => !string.IsNullOrWhiteSpace(d.Name))
            .OrderByDescending(d => d.IsConnected)
            .ThenByDescending(d => d.IsPaired)
            .ThenBy(d => d.Name)
            .ToList();

        DebugLog.Write($"[BT] {result.Count} dispositivo(s): " +
            string.Join(" | ", result.Select(d => $"{d.Name} ({d.StatusText}{(d.IsLowEnergy ? ",LE" : string.Empty)})")));

        return result;
    }

    // ---------------------------------------------------------------- descoberta

    private static async Task FindAllAsync(string selector, IDictionary<string, DeviceInformation> sink)
    {
        try
        {
            foreach (var info in await DeviceInformation.FindAllAsync(selector, AepProperties))
            {
                lock (sink) sink[info.Id] = info;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[BT] FindAll falhou: {ex.GetType().Name} hr=0x{ex.HResult:X8}");
        }
    }

    private static async Task WatchAsync(string selector, TimeSpan window, IDictionary<string, DeviceInformation> sink)
    {
        try
        {
            var watcher = DeviceInformation.CreateWatcher(selector, AepProperties, DeviceInformationKind.AssociationEndpoint);
            void OnAdded(DeviceWatcher _, DeviceInformation info)
            {
                lock (sink) sink[info.Id] = info;
            }

            watcher.Added += OnAdded;
            watcher.Start();
            await Task.Delay(window);
            watcher.Stop();
            watcher.Added -= OnAdded;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[BT] Watcher falhou: {ex.GetType().Name} hr=0x{ex.HResult:X8}");
        }
    }

    // Anúncios BLE: é o que mostra aparelho no ar que o Windows ainda não listou.
    private static async Task WatchAdvertisementsAsync(TimeSpan window, IDictionary<ulong, string> sink)
    {
        try
        {
            var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
            void OnReceived(BluetoothLEAdvertisementWatcher _, BluetoothLEAdvertisementReceivedEventArgs args)
            {
                var name = args.Advertisement.LocalName;
                if (string.IsNullOrWhiteSpace(name)) return;
                lock (sink) sink[args.BluetoothAddress] = name.Trim();
            }

            watcher.Received += OnReceived;
            watcher.Start();
            await Task.Delay(window);
            watcher.Stop();
            watcher.Received -= OnReceived;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[BT] Anúncios BLE falharam: {ex.GetType().Name} hr=0x{ex.HResult:X8}");
        }
    }

    // ---------------------------------------------------------------- conversão

    /// <summary>Pareado? Confia no AEP primeiro; `Pairing.IsPaired` é o fallback.</summary>
    private static bool IsPaired(DeviceInformation info)
    {
        if (info.Properties.TryGetValue("System.Devices.Aep.IsPaired", out var value)
            && value is bool fromAep)
        {
            return fromAep;
        }

        return info.Pairing?.IsPaired == true;
    }

    private static async Task<BtDevice> ToDeviceAsync(DeviceInformation info, bool lowEnergy)
    {
        var device = new BtDevice
        {
            Name = info.Name ?? string.Empty,
            IsLowEnergy = lowEnergy,
            IsPaired = IsPaired(info),
        };

        try
        {
            if (lowEnergy)
            {
                var le = await BluetoothLEDevice.FromIdAsync(info.Id);
                if (le is not null)
                {
                    device.LeDevice = le;
                    device.Address = le.BluetoothAddress;
                    device.IsConnected = le.ConnectionStatus == BluetoothConnectionStatus.Connected;
                }
            }
            else
            {
                var bt = await BluetoothDevice.FromIdAsync(info.Id);
                if (bt is not null)
                {
                    device.Device = bt;
                    device.Address = bt.BluetoothAddress;
                    device.IsConnected = bt.ConnectionStatus == BluetoothConnectionStatus.Connected;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[BT] Não consegui abrir '{device.Name}': {ex.GetType().Name}");
        }

        return device;
    }

    private static async Task<BtDevice?> FromAdvertisementAsync(ulong address, string name)
    {
        try
        {
            var le = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (le is null) return null;

            return new BtDevice
            {
                Name = name,
                Address = address,
                IsLowEnergy = true,
                LeDevice = le,
                IsPaired = IsPaired(le.DeviceInformation),
                IsConnected = le.ConnectionStatus == BluetoothConnectionStatus.Connected,
            };
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[BT] Anúncio '{name}' não resolvido: {ex.GetType().Name}");
            return null;
        }
    }

    // ---------------------------------------------------------------- ações

    /// <summary>Emparelha um dispositivo que ainda não é conhecido (clássico ou LE).</summary>
    public async Task<bool> PairAsync(BtDevice device)
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            var pairing = device.LeDevice?.DeviceInformation.Pairing ?? device.Device?.DeviceInformation.Pairing;
            if (pairing is null || !pairing.CanPair) return false;

            var result = await pairing.PairAsync();
            DebugLog.Write($"[BT] Emparelhar '{device.Name}': {result.Status}");
            return result.Status is DevicePairingResultStatus.Paired
                or DevicePairingResultStatus.AlreadyPaired;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[BT] Erro ao emparelhar '{device.Name}': {ex.GetType().Name} hr=0x{ex.HResult:X8}");
            return false;
        }
    }

    public Task<bool> ConnectAsync(BtDevice device) => SetEnabledAsync(device, true);

    public Task<bool> DisconnectAsync(BtDevice device) => SetEnabledAsync(device, false);

    /// <summary>
    /// Liga/desliga o dispositivo. O WinRT só informa o estado — não expõe
    /// conectar/desconectar — então usamos o PnP do Windows (o launcher já corre
    /// como Administrador), que é o mesmo que "Conectar/Desconectar" do Windows.
    /// </summary>
    private static async Task<bool> SetEnabledAsync(BtDevice device, bool enabled) => await Task.Run(() =>
    {
        try
        {
            // PnP (ligar/desligar dispositivo) exige administrador. O launcher corre
            // em modo padrão, então a tarefa é delegada ao ElevationService — que
            // reabre o exe elevado e pede UAC uma vez por ação.
            var ok = ElevationService.RunElevated(
                enabled ? "pnp-enable" : "pnp-disable",
                "--name", device.Name);

            DebugLog.Write($"[BT] {(enabled ? "Conectar" : "Desconectar")} '{device.Name}': {(ok ? "ok" : "falhou (sem elevação?)")}");
            return ok;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[BT] Erro ao alterar '{device.Name}': {ex.GetType().Name} hr=0x{ex.HResult:X8}");
            return false;
        }
    });
}
#endif
