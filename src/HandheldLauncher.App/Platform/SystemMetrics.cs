using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HandheldLauncher.App.Platform;

/// <summary>Uma leitura de sensores. Null = indisponível nesta máquina.</summary>
internal readonly record struct MetricsSnapshot(
    double? CpuPercent,
    double? CpuMhz,
    double? GpuPercent,
    double? VramGb,
    double RamUsedGb,
    double RamTotalGb,
    int? BatteryPercent,
    bool OnAcPower,
    double? BatteryWatts,
    TimeSpan? BatteryRemaining);

/// <summary>
/// Sensores do sistema multiplataforma.
/// No Windows usa PerformanceCounter/WMI. No Linux usa arquivos do sistema (/proc e /sys).
/// </summary>
internal sealed class SystemMetrics : IDisposable
{
    private readonly PerformanceCounter? _cpuUtility;
    private readonly PerformanceCounter? _cpuPerformance;
    private readonly PerformanceCounter? _cpuFrequency;

    private Dictionary<string, CounterSample> _gpuPrevious = new();
    private bool _gpuBroken;
    private bool _vramBroken;

    private DateTime _lastWattsRead = DateTime.MinValue;
    private double? _batteryWatts;
    private double? _remainingMilliwattHours;
    private bool _wmiBroken;

    public SystemMetrics()
    {
        if (OperatingSystem.IsWindows())
        {
            _cpuUtility = TryCounter("Processor Information", "% Processor Utility", "_Total");
            _cpuPerformance = TryCounter("Processor Information", "% Processor Performance", "_Total");
            _cpuFrequency = TryCounter("Processor Information", "Processor Frequency", "_Total");

            // Primeira leitura de contador "%" sempre volta 0 — descarta.
            try { _cpuUtility?.NextValue(); } catch { }
            try { _cpuPerformance?.NextValue(); } catch { }
        }
    }

    public MetricsSnapshot Read()
    {
        var (ramUsed, ramTotal) = ReadRam();
        var battery = ReadBattery();
        var watts = ReadBatteryWatts();

        // Muitos aparelhos devolvem "desconhecido" no tempo restante.
        var remaining = battery.Remaining;
        if (remaining is null && !battery.OnAc && watts is > 0.5 && _remainingMilliwattHours is > 0)
        {
            remaining = TimeSpan.FromHours(_remainingMilliwattHours.Value / (watts.Value * 1000.0));
        }
        return new MetricsSnapshot(
            CpuPercent: OperatingSystem.IsWindows() ? ReadCpuPercent() : null,
            CpuMhz: OperatingSystem.IsWindows() ? ReadCpuMhz() : null,
            GpuPercent: OperatingSystem.IsWindows() ? ReadGpuPercent() : null,
            VramGb: OperatingSystem.IsWindows() ? ReadVramGb() : null,
            RamUsedGb: ramUsed,
            RamTotalGb: ramTotal,
            BatteryPercent: battery.Percent,
            OnAcPower: battery.OnAc,
            BatteryWatts: OperatingSystem.IsWindows() ? ReadBatteryWatts() : null,
            BatteryRemaining: remaining);
    }

    // --- CPU ---------------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private double? ReadCpuPercent()
    {
        if (!OperatingSystem.IsWindows()) return null;
        
        try
        {
            return _cpuUtility is null ? null : Math.Clamp(_cpuUtility.NextValue(), 0, 100);
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private double? ReadCpuMhz()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            if (_cpuPerformance is null || _cpuFrequency is null) return null;
            var baseMhz = _cpuFrequency.NextValue();
            var performance = _cpuPerformance.NextValue();
            if (baseMhz <= 0 || performance <= 0) return null;
            return baseMhz * performance / 100.0;
        }
        catch
        {
            return null;
        }
    }

    // --- GPU ---------------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private double? ReadGpuPercent()
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (_gpuBroken) return null;

        try
        {
            var data = new PerformanceCounterCategory("GPU Engine").ReadCategory();
            var utilization = data["Utilization Percentage"];
            if (utilization is null) return null;

            var current = new Dictionary<string, CounterSample>();
            var perEngine = new Dictionary<string, double>();

            foreach (InstanceData instance in utilization.Values)
            {
                var name = instance.InstanceName;
                if (!name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)) continue;

                current[name] = instance.Sample;
                if (!_gpuPrevious.TryGetValue(name, out var previous)) continue;

                var value = CounterSample.Calculate(previous, instance.Sample);
                var luid = name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
                var engineKey = luid >= 0 ? name[luid..] : name;
                perEngine[engineKey] = perEngine.GetValueOrDefault(engineKey) + value;
            }

            _gpuPrevious = current;
            return perEngine.Count == 0 ? 0 : Math.Clamp(perEngine.Values.Max(), 0, 100);
        }
        catch
        {
            _gpuBroken = true;
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private double? ReadVramGb()
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (_vramBroken) return null;

        try
        {
            var data = new PerformanceCounterCategory("GPU Adapter Memory").ReadCategory();
            double bytes = 0;
            foreach (var counterName in new[] { "Dedicated Usage", "Shared Usage" })
            {
                var collection = data[counterName];
                if (collection is null) continue;
                foreach (InstanceData instance in collection.Values)
                {
                    bytes += instance.RawValue;
                }
            }
            return bytes / (1024.0 * 1024 * 1024);
        }
        catch
        {
            _vramBroken = true;
            return null;
        }
    }

    // --- RAM ---------------------------------------------------------------

    [SupportedOSPlatform("windows")]
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    private static (double Used, double Total) ReadRam()
    {
        const double gb = 1024.0 * 1024 * 1024;

        if (OperatingSystem.IsWindows())
        {
            var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref status)) return (0, 0);
            return ((status.ullTotalPhys - status.ullAvailPhys) / gb, status.ullTotalPhys / gb);
        }
        
        if (OperatingSystem.IsLinux())
        {
            try
            {
                double totalKb = 0, availableKb = 0;
                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    if (line.StartsWith("MemTotal:"))
                        totalKb = double.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
                    else if (line.StartsWith("MemAvailable:"))
                        availableKb = double.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
                }
                if (totalKb > 0)
                {
                    const double kbToGb = 1024.0 * 1024.0;
                    return ((totalKb - availableKb) / kbToGb, totalKb / kbToGb);
                }
            }
            catch { /* ignora se não conseguir ler */ }
        }

        return (0, 0);
    }

    // --- Bateria -----------------------------------------------------------

    [SupportedOSPlatform("windows")]
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    private static (int? Percent, bool OnAc, TimeSpan? Remaining) ReadBattery()
    {
        if (OperatingSystem.IsWindows())
        {
            if (!GetSystemPowerStatus(out var status)) return (null, true, null);

            // 128 = sem bateria (PC de mesa); 255 = desconhecido.
            if ((status.BatteryFlag & 128) != 0 || status.BatteryLifePercent == 255)
            {
                return (null, true, null);
            }

            var onAc = status.ACLineStatus == 1;
            TimeSpan? remaining = !onAc && status.BatteryLifeTime > 0
                ? TimeSpan.FromSeconds(status.BatteryLifeTime)
                : null;
            return (status.BatteryLifePercent, onAc, remaining);
        }

        if (OperatingSystem.IsLinux())
        {
            try
            {
                var capacityPath = "/sys/class/power_supply/BAT0/capacity";
                var statusPath = "/sys/class/power_supply/BAT0/status";
                if (!File.Exists(capacityPath)) return (null, true, null); // Sem bateria

                var percentStr = File.ReadAllText(capacityPath).Trim();
                var statusStr = File.ReadAllText(statusPath).Trim();

                if (int.TryParse(percentStr, out var percent))
                {
                    var onAc = statusStr.Equals("Charging", StringComparison.OrdinalIgnoreCase) 
                            || statusStr.Equals("Full", StringComparison.OrdinalIgnoreCase);
                    return (percent, onAc, null);
                }
            }
            catch { /* ignora */ }
        }

        return (null, true, null);
    }

    [SupportedOSPlatform("windows")]
    private double? ReadBatteryWatts()
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (_wmiBroken) return null;
        if (DateTime.UtcNow - _lastWattsRead < TimeSpan.FromSeconds(3)) return _batteryWatts;
        _lastWattsRead = DateTime.UtcNow;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT DischargeRate, ChargeRate, RemainingCapacity FROM BatteryStatus");
            double? watts = null;
            double? remainingMwh = null;
            using var results = searcher.Get();
            foreach (var item in results)
            {
                using var mo = (ManagementObject)item;
                var discharge = Convert.ToDouble(mo["DischargeRate"] ?? 0);
                var charge = Convert.ToDouble(mo["ChargeRate"] ?? 0);
                var capacity = Convert.ToDouble(mo["RemainingCapacity"] ?? 0);
                if (capacity > 0) remainingMwh = (remainingMwh ?? 0) + capacity;
                // Valores em mW.
                if (discharge > 0) watts = (watts ?? 0) + discharge / 1000.0;
                else if (charge > 0) watts = (watts ?? 0) - charge / 1000.0;
            }
            _batteryWatts = watts;
            _remainingMilliwattHours = remainingMwh;
            return watts;
        }
        catch
        {
            _wmiBroken = true;
            return null;
        }
    }

    // --- util --------------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private static PerformanceCounter? TryCounter(string category, string counter, string instance)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            return new PerformanceCounter(category, counter, instance, readOnly: true);
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    public void Dispose()
    {
        _cpuUtility?.Dispose();
        _cpuPerformance?.Dispose();
        _cpuFrequency?.Dispose();
    }
}