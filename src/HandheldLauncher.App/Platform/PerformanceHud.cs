using System.Globalization;
using System.Runtime.InteropServices;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class PerformanceHud : IDisposable
{
    public static readonly string[] PresetNames = ["Desligado", "Resumido", "Médio", "Completo"];

    private readonly RtssSharedMemory _rtss = new();
    private readonly NativeHudSurface _native = new();
    private readonly string _renderer;
    private readonly bool _ownFps;
    private volatile bool _etwHeld;
    // começa "sim": se um launcher anterior caiu com texto no RTSS, a primeira passada limpa
    private bool _rtssShowing = true;
    private static readonly int OwnPid = Environment.ProcessId;
    private readonly Thread _thread;
    private volatile bool _stop;
    private SystemMetrics? _metrics;
    private volatile int _preset;
    private bool _isShowing;
    private string? _lastLoggedError;

    public string CpuTemperature { get; set; } = "--°C";
    public string TdpStatusText { get; set; } = "";
    public string ColorModeText { get; set; } = "";
    public string RealTdpText { get; set; } = "";
    public string RealGpuWatts { get; set; } = "";

    private volatile bool _isRecording;
    public bool IsRecording { get => _isRecording; set => _isRecording = value; }

    private int _blinkTick;

    [DllImport("user32.dll")]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
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

    private string _resolutionText = "";
    private DateTime _lastResolutionCheck = DateTime.MinValue;

    private string GetResolutionInfo()
    {
        if (DateTime.Now - _lastResolutionCheck < TimeSpan.FromSeconds(5)) return _resolutionText;
        try
        {
            var devMode = new DEVMODE { dmDeviceName = new string(new char[32]) };
            devMode.dmSize = (short)Marshal.SizeOf(devMode);
            if (EnumDisplaySettings(null, -1, ref devMode))
            {
                var height = devMode.dmPelsHeight;
                _resolutionText = height switch
                {
                    2160 => "2160p",
                    1440 => "1440p",
                    1200 => "1200p",
                    1080 => "1080p",
                    900 => "900p",
                    720 => "720p",
                    _ => $"{height}p"
                };
            }
            _lastResolutionCheck = DateTime.Now;
        }
        catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
        return _resolutionText;
    }

    private const int FrameTimeHistorySize = 20;
    private readonly Queue<double> _frameTimeHistory = new(FrameTimeHistorySize + 1);

    public PerformanceHud(int preset)
    {
        _preset = Math.Clamp(preset, 0, PresetNames.Length - 1);
        var settings = HandheldLauncher.Core.Services.AppSettingsStore.Load();
        _renderer = (settings.HudRenderer ?? "auto").Trim().ToLowerInvariant();
        _ownFps = settings.OwnFpsMeter;
        DebugLog.Write($"[HUD] Desenho do HUD: {_renderer} (auto = launcher; RTSS só em tela cheia exclusiva).");
        _thread = new Thread(Run) { IsBackground = true, Name = "PerformanceHud", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    private void Run()
    {
        Thread.Sleep(TimeSpan.FromSeconds(2));
        while (!_stop) { Tick(); Thread.Sleep(TimeSpan.FromSeconds(1)); }
    }

    public int Preset { get => _preset; set => _preset = Math.Clamp(value, 0, PresetNames.Length - 1); }
    public string PresetName => PresetNames[_preset];
    public string Status { get; private set; } = "iniciando";

    public void ClearNow()
    {
        try { lock (_rtss) _rtss.ClearOsd(); _isShowing = false; _rtssShowing = false; } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
        try { _native.Hide(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
    }

    /// <summary>
    /// v0.28: true = o launcher desenha; false = o RTSS. No "auto" só o jogo em tela cheia
    /// exclusiva vai para o RTSS (nada desenha por cima dele).
    /// </summary>
    private bool UseNative() => _renderer switch
    {
        "rtss" => false,
        "launcher" => true,
        _ => !NativeHudSurface.IsExclusiveFullscreen(),
    };

    /// <summary>
    /// A janela do launcher só aparece com jogo na frente: o HUD do RTSS só existia dentro do
    /// jogo, e um HUD por cima da área de trabalho ou do próprio launcher seria estranho.
    /// </summary>
    private static bool GameOnScreen(int foregroundPid)
    {
        if (foregroundPid == 0 || foregroundPid == OwnPid) return false;
        if (GameSessionInfo.DetectedGamePid is var g && g > 0 && g == foregroundPid) return true;
        return NativeHudSurface.ForegroundCoversScreen();
    }

    /// <summary>Escreve no desenho escolhido e apaga o outro (para nunca aparecerem dois).</summary>
    private bool Present(string text, bool native, bool onScreen)
    {
        if (native)
        {
            if (_rtssShowing) { lock (_rtss) _rtss.ClearOsd(); _rtssShowing = false; }
            if (onScreen) _native.Show(text); else _native.Hide();
            return true;
        }
        bool ok;
        lock (_rtss) ok = _rtss.WriteOsd(text);
        _rtssShowing = ok;
        if (ok) { _native.Hide(); return true; }
        // "auto" em tela cheia exclusiva sem RTSS: tenta a janela do launcher mesmo assim — muitos
        // jogos "tela cheia" do Windows 10/11 ainda deixam janelas por cima.
        if (_renderer is not ("rtss" or "launcher") && GameOnScreen(GetForegroundProcessId()))
        {
            _native.Show(text);
            return true;
        }
        _native.Hide();
        return false;
    }

    /// <summary>FPS: o do RTSS quando ele mede o jogo; senão, a medição própria (DirectX 9–12).</summary>
    private FrameStats? ReadFrames(int pid)
    {
        FrameStats? frames;
        lock (_rtss) frames = _rtss.ReadFrameStats(pid);
        if (frames is not null || !_etwHeld || pid == 0) return frames;
        return EtwFrameCounter.Shared.TryGetFps(pid) is { } fps && fps >= 1
            ? new FrameStats(fps, 1000.0 / fps, pid)
            : null;
    }

    private void HoldOwnFps(bool want)
    {
        want &= _ownFps && !_stop;
        if (want == _etwHeld) return;
        if (want)
        {
            // não ligou (ex.: sessão antiga ainda fechando): solta e tenta de novo no próximo segundo
            if (EtwFrameCounter.Shared.Acquire("hud")) _etwHeld = true;
            else EtwFrameCounter.Shared.Release("hud");
        }
        else
        {
            _etwHeld = false;
            EtwFrameCounter.Shared.Release("hud");
        }
    }

    private void Tick()
    {
        using var __meter = HandheldLauncher.Core.Services.Power.ActivityMeter.Measure("HUD");
        try
        {
            var preset = _preset;
            var notice = CurrentNotice();
            if (preset == 0 && notice is null)
            {
                // HUD desligado e sem aviso: nada a consultar
                HoldOwnFps(false);
                if (_isShowing) ClearNow();
                Status = "desligado";
                return;
            }
            var native = UseNative();
            var pid = GetForegroundProcessId();
            var onScreen = native && GameOnScreen(pid);
            // medição própria só com jogo aberto pelo launcher (fora dele ficaria contando tudo à toa)
            HoldOwnFps(preset > 0 && native && GameSessionInfo.DetectedGamePid > 0);
            if (preset == 0)
            {
                // HUD desligado: o aviso aparece sozinho, pelo tempo dele, e some.
                if (Present($"<C=C6B5FF>{notice}<C>", native, onScreen)) _isShowing = true;
                Status = "desligado";
                return;
            }

            _metrics ??= new SystemMetrics();
            var metrics = _metrics.Read();

            _blinkTick++;
            var recIndicator = (IsRecording && (_blinkTick % 2 == 0)) ? "[ REC ]" : "";

            var frames = ReadFrames(pid);
            if (frames is { } f) { _frameTimeHistory.Enqueue(f.FrameTimeMs); while (_frameTimeHistory.Count > FrameTimeHistorySize) _frameTimeHistory.Dequeue(); }
            else _frameTimeHistory.Clear();

            var text = HudFormatter.Format(
                preset, frames, metrics, _frameTimeHistory,
                CpuTemperature, GetResolutionInfo(),
                TdpStatusText, RealTdpText, recIndicator, ColorModeText,
                RealGpuWatts);
            // v0.27: aviso curto logo abaixo do HUD (ex.: "Modo inteligente aplicado").
            if (notice is not null) text += $"\n<C=C6B5FF>{notice}<C>";
            var written = Present(text, native, onScreen);

            if (written) { _isShowing = true; Status = native ? (onScreen ? "ativo (launcher)" : "aguardando jogo") : "ativo (RTSS)"; }
            else { Status = _rtss.LastError ?? "RTSS indisponível"; LogOnce(Status); }
        }
        catch (Exception ex) { Status = "erro: " + ex.Message; LogOnce(ex.ToString()); }
    }

    private static string _notice = "";
    private static DateTime _noticeUntilUtc = DateTime.MinValue;
    private static readonly object NoticeGate = new();

    /// <summary>Mostra uma linha discreta abaixo do HUD por alguns segundos.</summary>
    public static void ShowNotice(string text, double seconds = 6)
    {
        lock (NoticeGate)
        {
            _notice = text;
            _noticeUntilUtc = DateTime.UtcNow.AddSeconds(seconds);
        }
    }

    private static string? CurrentNotice()
    {
        lock (NoticeGate) return DateTime.UtcNow < _noticeUntilUtc ? _notice : null;
    }

    private void LogOnce(string message)
    {
        if (message == _lastLoggedError) return;
        _lastLoggedError = message;
        DebugLog.Write($"PerformanceHud: {message}");
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static int GetForegroundProcessId()
    {
        try { var hwnd = GetForegroundWindow(); if (hwnd == IntPtr.Zero) return 0; GetWindowThreadProcessId(hwnd, out var pid); return (int)pid; }
        catch { return 0; }
    }

    public void Dispose()
    {
        _stop = true;
        try { _thread.Join(1500); } catch (Exception ex) { DebugLog.Swallowed(ex); }   // a passada em andamento termina antes
        ClearNow();
        HoldOwnFps(false);
        _native.Dispose();
        lock (_rtss) _rtss.Dispose();
        _metrics?.Dispose();
    }
}

internal static class HudFormatter
{
    private const string GpuColor = "2E9762", CpuColor = "2E97CB", VramColor = "AD64C1", RamColor = "C26693", BatteryColor = "FF9078", FpsColor = "EB5B5B", ClockColor = "CCCCCC", TempColor = "FF9D00", ResColor = "CCCCCC";
    private const string TdpColor = "FFB84D", RecColor = "FF4040";
    private const string ValueColor = "FFFFFF", UnitColor = "B0B0B0", Critical = "FF4040", GroupSeparator = "   ", FrameColor = "F2C14E";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Format(
        int preset, FrameStats? frames, MetricsSnapshot m,
        IReadOnlyCollection<double>? frameTimeHistoryMs = null,
        string? cpuTemp = null, string? resolution = null,
        string? tdpStatus = null, string? realTdp = null,
        string? recIndicator = null, string? colorMode = null,
        string? realGpuWatts = null)
    {
        var groups = new List<string>();

        // ORDEM: FPS -> TEMP -> BAT -> (Resto) -> RES -> HORA -> REC
        switch (preset)
        {
            case 1: // Mínimo
                groups.Add(Fps(frames, false));
                AddTemp(groups, cpuTemp);
                AddBattery(groups, m, false);
                break;

            case 2: // Médio
                groups.Add(Fps(frames, true));
                AddTemp(groups, cpuTemp);
                AddBattery(groups, m, false);
                groups.Add(Group("GPU", GpuColor, Percent(m.GpuPercent)));
                groups.Add(Group("CPU", CpuColor, Percent(m.CpuPercent)));
                AddPowerGroups(groups, tdpStatus, realTdp, realGpuWatts);
                AddResolutionGroup(groups, resolution, colorMode);
                break;

            default: // Completo
                groups.Add(Fps(frames, true));
                AddTemp(groups, cpuTemp);
                AddBattery(groups, m, true);
                groups.Add(Group("GPU", GpuColor, Percent(m.GpuPercent)));
                groups.Add(Group("CPU", CpuColor, Percent(m.CpuPercent) + (m.CpuMhz is { } mhz ? " " + Value(mhz.ToString("0", Inv)) + Unit("MHz") : string.Empty)));
                if (m.VramGb is { } vram) groups.Add(Group("VRAM", VramColor, Value(vram.ToString("0.0", Inv)) + Unit("GB")));
                groups.Add(Group("RAM", RamColor, Value(m.RamUsedGb.ToString("0.0", Inv)) + Unit("/" + m.RamTotalGb.ToString("0", Inv) + "GB")));
                AddPowerGroups(groups, tdpStatus, realTdp, realGpuWatts);
                if (FrameTimeRange(frameTimeHistoryMs) is { Length: > 0 } frameRange) groups.Add(Group("FRAME", FrameColor, frameRange));
                AddResolutionGroup(groups, resolution, colorMode);
                break;
        }

        groups.Add(Color(ClockColor, DateTime.Now.ToString("HH:mm", Inv)));
        if (!string.IsNullOrEmpty(recIndicator)) groups.Add(Color(RecColor, recIndicator));

        return string.Join(GroupSeparator, groups);
    }

    // v0.20 — mostra CPU W e GPU W quando o HWiNFO fornece; cai pro TDP-alvo se não vier.
    private static void AddPowerGroups(List<string> groups, string? tdpStatus, string? realCpuWatts, string? realGpuWatts)
    {
        var cpu = ParseWatts(realCpuWatts) ?? ParseWatts(tdpStatus);
        if (cpu is { } c) groups.Add(Group("CPU", CpuColor, Value(c.ToString(Inv)) + Unit("W")));
        if (ParseWatts(realGpuWatts) is { } g) groups.Add(Group("GPU", GpuColor, Value(g.ToString(Inv)) + Unit("W")));
    }

    private static int? ParseWatts(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(
            text, @"(\d+)\s*W",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var v) ? v : null;
    }

    private static void AddTemp(List<string> groups, string? cpuTemp)
    {
        if (!string.IsNullOrEmpty(cpuTemp) && cpuTemp != "--°C")
            groups.Add(Group("TEMP", TempColor, Value(cpuTemp.Replace("°C", "")) + Unit("°C")));
    }

    private static void AddResolutionGroup(List<string> groups, string? resolution, string? colorMode)
    {
        if (string.IsNullOrEmpty(resolution)) return;
        var text = resolution;
        if (!string.IsNullOrEmpty(colorMode)) text += " | Modo: " + colorMode;
        groups.Add(Group("RES", ResColor, Value(text)));
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
        if (m.OnAcPower) text += " " + Unit("AC");
        // v0.28: tempo restante pela conta do launcher (o do Windows pula com o consumo de poucos segundos)
        else if ((BatteryDiagnostics.CurrentRemaining ?? m.BatteryRemaining) is { } left) text += " " + Value($"{(int)left.TotalHours}h{left.Minutes:00}");
        if (withWatts && m.BatteryWatts is { } w && Math.Abs(w) > 0.05) text += " " + Value((w >= 0 ? w : -w).ToString("0.0", Inv)) + Unit(w >= 0 ? "W" : "W+");
        groups.Add(Group("BAT", BatteryColor, text));
    }

    private static string FrameTimeRange(IReadOnlyCollection<double>? frameTimeHistoryMs)
    {
        if (frameTimeHistoryMs is null || frameTimeHistoryMs.Count == 0) return string.Empty;
        var min = double.MaxValue; var max = double.MinValue; var sum = 0.0;
        foreach (var ms in frameTimeHistoryMs) { if (ms < min) min = ms; if (ms > max) max = ms; sum += ms; }
        var avg = sum / frameTimeHistoryMs.Count;
        return Value($"{min:0}/{avg:0}/{max:0}") + Unit("ms");
    }

    private static string Group(string label, string labelColor, string valueText) => Color(labelColor, label) + " " + valueText;
    private static string Percent(double? percent) => percent is { } p ? Value(p.ToString("0", Inv)) + Unit("%") : Unit("--");
    private static string Value(string text) => Color(ValueColor, text);
    private static string Unit(string text) => Color(UnitColor, text);
    private static string Color(string hex, string text) => $"<C={hex}>{text}<C>";
}