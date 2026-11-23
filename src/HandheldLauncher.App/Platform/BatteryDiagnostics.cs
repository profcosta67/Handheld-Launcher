using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Threading;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.Core.Services.Power;

#pragma warning disable CA1416 // só roda no Windows (Start confere)

namespace HandheldLauncher.App.Platform;

/// <summary>O que a tela principal informa ao coletor a cada amostra.</summary>
public readonly record struct LauncherActivityState(string State, string? Game, bool QuickSettingsOpen);

/// <summary>
/// v0.28: coletor do diagnóstico de bateria. Uma thread própria, de baixa prioridade, lê a
/// cada 30 s na bateria (2 min na tomada): consumo e carga da bateria, tempo restante do
/// Windows, quanto do processador o launcher e cada programa usaram, o que o launcher fazia,
/// brilho, Hz e TDP. Guarda em <see cref="PowerLogStore"/> e mantém a estimativa de tempo
/// restante própria (<see cref="CurrentRemaining"/>), usada pelo HUD no lugar da do Windows.
/// A cada 10 min na bateria escreve um resumo no log ([BATERIA]).
/// </summary>
public static class BatteryDiagnostics
{
    private static readonly TimeSpan BatteryInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AcInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan BrightnessEvery = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FullCapacityEvery = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LogSummaryEvery = TimeSpan.FromMinutes(10);
    private const int TopProcessCount = 8;

    private static Thread? _thread;
    private static volatile bool _stop;
    private static readonly AutoResetEvent _wake = new(false);
    private static readonly BatteryLifeEstimator _estimator = new();
    private static readonly object _estimateGate = new();
    private static TimeSpan? _currentRemaining;
    private static DateTime _currentRemainingAt;

    private static Func<LauncherActivityState>? _stateProvider;
    private static Dictionary<int, (string Name, TimeSpan Cpu, DateTime Start)> _lastProcs = new();
    private static TimeSpan _lastOwnCpu;
    private static DateTime _lastSampleUtc;
    private static int? _brightness;
    private static DateTime _brightnessAt = DateTime.MinValue;
    private static int? _fullMwh;
    private static DateTime _fullAt = DateTime.MinValue;
    private static bool _wmiBroken;
    private static readonly List<PowerSample> _summary = new();
    private static DateTime _summaryStart = DateTime.MinValue;

    /// <summary>Tempo restante pela conta do launcher; null na tomada, sem dados ou desatualizado.</summary>
    public static TimeSpan? CurrentRemaining
    {
        get
        {
            lock (_estimateGate)
                return DateTime.UtcNow - _currentRemainingAt < TimeSpan.FromMinutes(2) ? _currentRemaining : null;
        }
    }

    private static volatile PowerSample? _lastSample;
    private static double? _currentWatts;

    /// <summary>Última amostra (para a tela "agora").</summary>
    public static PowerSample? LastSample => _lastSample;

    /// <summary>Consumo usado na conta do tempo restante (suavizado), em W.</summary>
    public static double? CurrentWatts { get { lock (_estimateGate) return _currentWatts; } }

    /// <summary>Liga o coletor. <paramref name="stateProvider"/> roda na thread da tela.</summary>
    public static void Start(Func<LauncherActivityState> stateProvider)
    {
        if (!OperatingSystem.IsWindows() || _thread is not null) return;
        _stateProvider = stateProvider;
        _thread = new Thread(Run) { IsBackground = true, Name = "BatteryDiagnostics", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    /// <summary>Para o coletor e grava o que estiver no buffer (ao fechar o launcher).</summary>
    public static void Stop()
    {
        _stop = true;
        _wake.Set();
        try { PowerLogStore.Shared.Flush(); } catch (Exception ex) { DebugLog.Swallowed(ex); }
    }

    /// <summary>Pede uma amostra agora (ao voltar da suspensão a estimativa recomeça).</summary>
    public static void SampleNow() => _wake.Set();

    private static void Run()
    {
        // começa depois da abertura, que é o momento mais pesado do launcher
        if (_wake.WaitOne(TimeSpan.FromSeconds(20)) && _stop) return;
        _lastSampleUtc = DateTime.UtcNow;
        try { _lastOwnCpu = Process.GetCurrentProcess().TotalProcessorTime; } catch (Exception ex) { DebugLog.Swallowed(ex); }
        _lastProcs = ReadProcesses();
        ActivityMeter.TakeSnapshot(); // zera o que a abertura acumulou

        var nextWait = BatteryInterval;
        while (!_stop)
        {
            _wake.WaitOne(nextWait);
            if (_stop) break;
            try
            {
                using var _ = ActivityMeter.Measure("diagnóstico de bateria");
                nextWait = Sample() ? BatteryInterval : AcInterval;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[BATERIA] Falha na amostra: {ex.Message}");
                nextWait = AcInterval;
            }
        }
    }

    /// <summary>Lê, guarda e atualiza a estimativa. true = está na bateria.</summary>
    private static bool Sample()
    {
        var now = DateTime.UtcNow;
        var seconds = (now - _lastSampleUtc).TotalSeconds;
        _lastSampleUtc = now;
        if (seconds <= 0) return true;

        var power = ReadPowerStatus();
        var onAc = power?.OnAc ?? true;
        var (watts, remainingMwh) = ReadBatteryWmi();
        if (!onAc && (now - _fullAt > FullCapacityEvery || _fullMwh is null)) { _fullMwh = ReadFullCapacity(); _fullAt = now; }
        if (now - _brightnessAt > BrightnessEvery) { _brightness = ReadBrightness(); _brightnessAt = now; }

        var cores = Math.Max(1, Environment.ProcessorCount);
        double? ownCpu = null;
        try
        {
            var cpu = Process.GetCurrentProcess().TotalProcessorTime;
            ownCpu = Math.Max(0, (cpu - _lastOwnCpu).TotalSeconds) / (seconds * cores) * 100;
            _lastOwnCpu = cpu;
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }

        // processos só na bateria: na tomada o consumo não importa e a varredura custa
        List<ProcessCpu>? top = null;
        double? systemCpu = null;
        var procs = onAc ? null : ReadProcesses();
        if (procs is not null)
        {
            var byName = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var (pid, p) in procs)
            {
                if (!_lastProcs.TryGetValue(pid, out var prev) || prev.Start != p.Start) continue;
                var d = (p.Cpu - prev.Cpu).TotalSeconds;
                if (d <= 0) continue;
                byName[p.Name] = byName.GetValueOrDefault(p.Name) + d;
            }
            systemCpu = byName.Values.Sum() / (seconds * cores) * 100;
            top = byName.OrderByDescending(kv => kv.Value).Take(TopProcessCount)
                .Select(kv => new ProcessCpu { Name = kv.Key, CpuPercent = Math.Round(kv.Value / (seconds * cores) * 100, 2) })
                .ToList();
            _lastProcs = procs;
        }
        else _lastProcs.Clear();

        var activities = ActivityMeter.TakeSnapshot();
        var state = ReadState();

        double? ourMin;
        lock (_estimateGate)
        {
            ourMin = _estimator.Update(now, onAc, watts, remainingMwh);
            _currentRemaining = ourMin is { } m ? TimeSpan.FromMinutes(m) : null;
            _currentRemainingAt = now;
            _currentWatts = _estimator.Watts;
        }

        var sample = new PowerSample
        {
            Utc = now,
            Seconds = Math.Round(seconds, 1),
            OnAc = onAc,
            Percent = power?.Percent,
            RemainingMwh = remainingMwh,
            FullMwh = _fullMwh,
            Watts = watts is { } w ? Math.Round(w, 2) : null,
            WindowsRemainingMinutes = power?.RemainingSeconds is { } rs and > 0 ? Math.Round(rs / 60.0, 1) : null,
            OurRemainingMinutes = ourMin is { } om ? Math.Round(om, 1) : null,
            State = state.State,
            Game = state.Game,
            QuickSettingsOpen = state.QuickSettingsOpen,
            LauncherCpuPercent = ownCpu is { } oc ? Math.Round(oc, 3) : null,
            SystemCpuPercent = systemCpu is { } sc ? Math.Round(sc, 2) : null,
            TopProcesses = top,
            ActivityMs = activities.Count > 0 ? activities.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value.Ms, 2)) : null,
            ActivityCount = activities.Count > 0 ? activities.ToDictionary(kv => kv.Key, kv => kv.Value.Count) : null,
            BrightnessPercent = _brightness,
            RefreshHz = ReadRefreshHz(),
            TdpWatts = GameSessionInfo.SmartTdpWatts > 0 ? GameSessionInfo.SmartTdpWatts
                : WindowsHardwareService.LastAppliedSplWatts > 0 ? WindowsHardwareService.LastAppliedSplWatts : null,
        };
        PowerLogStore.Shared.Append(sample);
        _lastSample = sample;
        NoteForSummary(sample);
        return !onAc;
    }

    private static void NoteForSummary(PowerSample s)
    {
        if (s.OnAc) { _summary.Clear(); _summaryStart = DateTime.MinValue; return; }
        if (_summary.Count == 0) _summaryStart = s.Utc;
        _summary.Add(s);
        if (s.Utc - _summaryStart < LogSummaryEvery) return;
        try
        {
            var r = PowerReport.Build(_summary);
            var states = string.Join(", ", r.ByState.Select(x => $"{x.State} {x.Minutes:0} min {x.AvgWatts:0.0} W"));
            var fx = string.Join(", ", r.Activities.Take(5).Select(a => $"{a.Name} {a.MsPerMinute:0} ms/min ({a.WakeupsPerMinute:0}x)"));
            var procs = string.Join(", ", r.TopProcesses.Take(6).Select(p => $"{p.Name} {p.CpuPercent:0.0}%"));
            DebugLog.Write($"[BATERIA] {r.BatteryMinutes:0} min: média {r.AvgWatts:0.0} W ({states}); launcher {r.LauncherCpuPercent:0.00}% do processador — {fx}. Processos: {procs}. Restante: Windows {s.WindowsRemainingMinutes:0} min, launcher {s.OurRemainingMinutes:0} min.");
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
        _summary.Clear();
    }

    private static LauncherActivityState ReadState()
    {
        var provider = _stateProvider;
        if (provider is null) return new(PowerState.Background, null, false);
        try
        {
            var op = Dispatcher.UIThread.InvokeAsync(provider, DispatcherPriority.Background);
            return op.GetTask().Wait(TimeSpan.FromSeconds(2)) ? op.Result : new(PowerState.Background, null, false);
        }
        catch (Exception ex)
        {
            DebugLog.Swallowed(ex);
            return new(PowerState.Background, null, false);
        }
    }

    private static Dictionary<int, (string Name, TimeSpan Cpu, DateTime Start)> ReadProcesses()
    {
        var result = new Dictionary<int, (string, TimeSpan, DateTime)>();
        Process[] all;
        try { all = Process.GetProcesses(); }
        catch (Exception ex) { DebugLog.Swallowed(ex); return result; }
        foreach (var p in all)
        {
            try
            {
                if (p.Id == 0) continue;
                result[p.Id] = (p.ProcessName, p.TotalProcessorTime, p.StartTime);
            }
            catch
            {
                // processo protegido ou que acabou de fechar: fica de fora
            }
            finally { p.Dispose(); }
        }
        return result;
    }

    // --- leituras do Windows ---------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved1;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    private static (bool OnAc, int? Percent, int? RemainingSeconds)? ReadPowerStatus()
    {
        try
        {
            if (!GetSystemPowerStatus(out var st)) return null;
            if ((st.BatteryFlag & 128) != 0) return null; // sem bateria
            int? pct = st.BatteryLifePercent <= 100 ? st.BatteryLifePercent : null;
            int? left = st.BatteryLifeTime > 0 ? st.BatteryLifeTime : null;
            return (st.ACLineStatus == 1, pct, left);
        }
        catch (Exception ex)
        {
            DebugLog.Swallowed(ex);
            return null;
        }
    }

    private static (double? Watts, int? RemainingMwh) ReadBatteryWmi()
    {
        if (_wmiBroken) return (null, null);
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI",
                "SELECT DischargeRate, ChargeRate, RemainingCapacity FROM BatteryStatus");
            double? watts = null;
            int? mwh = null;
            using var results = searcher.Get();
            foreach (var item in results)
            {
                using var mo = (ManagementObject)item;
                var discharge = Convert.ToDouble(mo["DischargeRate"] ?? 0);
                var charge = Convert.ToDouble(mo["ChargeRate"] ?? 0);
                var capacity = Convert.ToInt32(mo["RemainingCapacity"] ?? 0);
                if (capacity > 0) mwh = (mwh ?? 0) + capacity;
                if (discharge > 0) watts = (watts ?? 0) + discharge / 1000.0;
                else if (charge > 0) watts = (watts ?? 0) - charge / 1000.0;
            }
            return (watts, mwh);
        }
        catch (Exception ex)
        {
            _wmiBroken = true;
            DebugLog.Write($"[BATERIA] WMI BatteryStatus indisponível: {ex.Message}");
            return (null, null);
        }
    }

    private static int? ReadFullCapacity()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity");
            using var results = searcher.Get();
            int? total = null;
            foreach (var item in results)
            {
                using var mo = (ManagementObject)item;
                var v = Convert.ToInt32(mo["FullChargedCapacity"] ?? 0);
                if (v > 0) total = (total ?? 0) + v;
            }
            return total;
        }
        catch (Exception ex)
        {
            DebugLog.Swallowed(ex);
            return null;
        }
    }

    private static int? ReadBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            using var results = searcher.Get();
            foreach (var item in results)
            {
                using var mo = (ManagementObject)item;
                return Convert.ToInt32(mo["CurrentBrightness"] ?? 0);
            }
        }
        catch
        {
            // monitor externo / sem suporte: sem leitura
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(string? deviceName, int modeNum, ref DEVMODE devMode);

    private static int? ReadRefreshHz()
    {
        try
        {
            var dm = new DEVMODE { dmDeviceName = "", dmFormName = "" };
            dm.dmSize = (short)Marshal.SizeOf<DEVMODE>();
            return EnumDisplaySettingsW(null, -1, ref dm) && dm.dmDisplayFrequency > 1 ? dm.dmDisplayFrequency : null;
        }
        catch (Exception ex)
        {
            DebugLog.Swallowed(ex);
            return null;
        }
    }
}
