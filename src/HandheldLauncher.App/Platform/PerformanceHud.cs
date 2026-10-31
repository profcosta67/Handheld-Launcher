using System.Globalization;
using System.Runtime.InteropServices;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class PerformanceHud : IDisposable
{
    public static readonly string[] PresetNames = ["Desligado", "Resumido", "Médio", "Completo"];

    private readonly RtssSharedMemory _rtss = new();
    private readonly Thread _thread;
    private volatile bool _stop;
    private SystemMetrics? _metrics;
    private volatile int _preset;
    private bool _isShowing;
    private string? _lastLoggedError;

    // NOVO: Propriedade para receber a temperatura do MainViewModel
    public string CpuTemperature { get; set; } = "--°C";

    private const int FrameTimeHistorySize = 20;
    private readonly Queue<double> _frameTimeHistory = new(FrameTimeHistorySize + 1);

    public PerformanceHud(int preset)
    {
        _preset = Math.Clamp(preset, 0, PresetNames.Length - 1);

        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "PerformanceHud",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    private void Run()
    {
        Thread.Sleep(TimeSpan.FromSeconds(2));

        while (!_stop)
        {
            Tick();
            Thread.Sleep(TimeSpan.FromSeconds(1));
        }
    }

    public int Preset
    {
        get => _preset;
        set => _preset = Math.Clamp(value, 0, PresetNames.Length - 1);
    }

    public string PresetName => PresetNames[_preset];
    public string Status { get; private set; } = "iniciando";

    public void ClearNow()
    {
        try
        {
            lock (_rtss) _rtss.ClearOsd();
            _isShowing = false;
        }
        catch { }
    }

    private void Tick()
    {
        try
        {
            var preset = _preset;
            if (preset == 0)
            {
                if (_isShowing) ClearNow();
                Status = "desligado";
                return;
            }

            _metrics ??= new SystemMetrics();
            var metrics = _metrics.Read();

            bool written;
            lock (_rtss)
            {
                var frames = _rtss.ReadFrameStats(GetForegroundProcessId());

                if (frames is { } f)
                {
                    _frameTimeHistory.Enqueue(f.FrameTimeMs);
                    while (_frameTimeHistory.Count > FrameTimeHistorySize) _frameTimeHistory.Dequeue();
                }
                else
                {
                    _frameTimeHistory.Clear();
                }

                // 🟢 NOVO: Passa a temperatura para o formatador
                var text = HudFormatter.Format(preset, frames, metrics, _frameTimeHistory, CpuTemperature);
                written = _rtss.WriteOsd(text);
            }

            if (written)
            {
                _isShowing = true;
                Status = "ativo";
            }
            else
            {
                Status = _rtss.LastError ?? "RTSS indisponível";
                LogOnce(Status);
            }
        }
        catch (Exception ex)
        {
            Status = "erro: " + ex.Message;
            LogOnce(ex.ToString());
        }
    }

    private void LogOnce(string message)
    {
        if (message == _lastLoggedError) return;
        _lastLoggedError = message;
        DebugLog.Write($"PerformanceHud: {message}");
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static int GetForegroundProcessId()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return 0;
            GetWindowThreadProcessId(hwnd, out var pid);
            return (int)pid;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        _stop = true;
        ClearNow();
        _rtss.Dispose();
        _metrics?.Dispose();
    }
}

internal static class HudFormatter
{
    private const string GpuColor = "2E9762";
    private const string CpuColor = "2E97CB";
    private const string VramColor = "AD64C1";
    private const string RamColor = "C26693";
    private const string BatteryColor = "FF9078";
    private const string FpsColor = "EB5B5B";
    private const string ClockColor = "CCCCCC";
    
    // 🟢 NOVO: Cor para a Temperatura (Laranja)
    private const string TempColor = "FF9D00"; 

    private const string ValueColor = "FFFFFF";
    private const string UnitColor = "B0B0B0";
    private const string Critical = "FF4040";

    private const string GroupSeparator = "     ";
    private const string FrameColor = "F2C14E";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // 🟢 NOVO: Parâmetro cpuTemp adicionado na assinatura
    public static string Format(int preset, FrameStats? frames, MetricsSnapshot m, IReadOnlyCollection<double>? frameTimeHistoryMs = null, string? cpuTemp = null)
    {
        var groups = new List<string>();

        switch (preset)
        {
            case 1: // Resumido
                groups.Add(Fps(frames, withFrameTime: false));
                AddBattery(groups, m, withWatts: false);
                break;

            case 2: // Médio
                groups.Add(Group("GPU", GpuColor, Percent(m.GpuPercent)));
                groups.Add(Group("CPU", CpuColor, Percent(m.CpuPercent)));
                // 🟢 NOVO: Adiciona a temperatura no preset Médio
                if (!string.IsNullOrEmpty(cpuTemp) && cpuTemp != "--°C")
                    groups.Add(Group("TEMP", TempColor, Value(cpuTemp.Replace("°C", "")) + Unit("°C")));
                groups.Add(Group("RAM", RamColor, Value(m.RamUsedGb.ToString("0.0", Inv)) + Unit("GB")));
                AddBattery(groups, m, withWatts: false);
                groups.Add(Fps(frames, withFrameTime: true));
                break;

            default: // Completo
                groups.Add(Group("GPU", GpuColor, Percent(m.GpuPercent)));
                if (m.VramGb is { } vram)
                {
                    groups.Add(Group("VRAM", VramColor, Value(vram.ToString("0.0", Inv)) + Unit("GB")));
                }
                groups.Add(Group("CPU", CpuColor,
                    Percent(m.CpuPercent) + (m.CpuMhz is { } mhz ? " " + Value(mhz.ToString("0", Inv)) + Unit("MHz") : string.Empty)));
                
                // 🟢 NOVO: Adiciona a temperatura no preset Completo
                if (!string.IsNullOrEmpty(cpuTemp) && cpuTemp != "--°C")
                    groups.Add(Group("TEMP", TempColor, Value(cpuTemp.Replace("°C", "")) + Unit("°C")));

                groups.Add(Group("RAM", RamColor,
                    Value(m.RamUsedGb.ToString("0.0", Inv)) + Unit("/" + m.RamTotalGb.ToString("0", Inv) + "GB")));
                AddBattery(groups, m, withWatts: true);
                groups.Add(Fps(frames, withFrameTime: true));
                if (FrameTimeRange(frameTimeHistoryMs) is { Length: > 0 } frameRange)
                {
                    groups.Add(Group("FRAME", FrameColor, frameRange));
                }
                break;
        }

        groups.Add(Color(ClockColor, DateTime.Now.ToString("HH:mm", Inv)));
        return string.Join(GroupSeparator, groups);
    }

    private static string Fps(FrameStats? frames, bool withFrameTime)
    {
        if (frames is not { } f) return Group("FPS", FpsColor, Unit("--"));

        var text = Value(f.Fps.ToString("0", Inv));
        if (withFrameTime) text += " " + Value(f.FrameTimeMs.ToString("0.0", Inv)) + Unit("ms");
        return Group("FPS", FpsColor, text);
    }

    private static void AddBattery(List<string> groups, MetricsSnapshot m, bool withWatts)
    {
        if (m.BatteryPercent is not { } percent) return;

        var valueColor = !m.OnAcPower && percent <= 15 ? Critical : ValueColor;
        var text = Color(valueColor, percent.ToString(Inv)) + Unit("%");

        if (m.OnAcPower)
        {
            text += " " + Unit("AC");
        }
        else if (m.BatteryRemaining is { } left)
        {
            text += " " + Value($"{(int)left.TotalHours}h{left.Minutes:00}");
        }

        if (withWatts && m.BatteryWatts is { } w && Math.Abs(w) > 0.05)
        {
            text += " " + Value((w >= 0 ? w : -w).ToString("0.0", Inv)) + Unit(w >= 0 ? "W" : "W+");
        }

        groups.Add(Group("BAT", BatteryColor, text));
    }

    private static string FrameTimeRange(IReadOnlyCollection<double>? frameTimeHistoryMs)
    {
        if (frameTimeHistoryMs is null || frameTimeHistoryMs.Count == 0) return string.Empty;

        var min = double.MaxValue;
        var max = double.MinValue;
        var sum = 0.0;
        foreach (var ms in frameTimeHistoryMs)
        {
            if (ms < min) min = ms;
            if (ms > max) max = ms;
            sum += ms;
        }
        var avg = sum / frameTimeHistoryMs.Count;

        return Value($"{min:0}/{avg:0}/{max:0}") + Unit("ms");
    }

    private static string Group(string label, string labelColor, string valueText)
        => Color(labelColor, label) + " " + valueText;

    private static string Percent(double? percent)
        => percent is { } p ? Value(p.ToString("0", Inv)) + Unit("%") : Unit("--");

    private static string Value(string text) => Color(ValueColor, text);

    private static string Unit(string text) => Color(UnitColor, text);

    private static string Color(string hex, string text) => $"<C={hex}>{text}<C>";
}