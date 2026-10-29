using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Nosso "MangoHud": a cada 1 s junta FPS (RTSS) + sensores (SystemMetrics),
/// monta um texto colorido no estilo do MangoHud e manda pro RTSS desenhar
/// por cima do jogo. Roda numa thread de fundo (nunca trava a interface).
///
/// Presets (Configurações, menu do launcher, ou L3+R3 no controle):
/// 0 Desligado · 1 Resumido · 2 Médio · 3 Completo.
/// </summary>
public sealed class PerformanceHud : IDisposable
{
    public static readonly string[] PresetNames = ["Desligado", "Resumido", "Médio", "Completo"];

    private readonly RtssSharedMemory _rtss = new();
    private readonly Timer _timer;
    private SystemMetrics? _metrics;
    private volatile int _preset;
    private int _ticking;
    private bool _isShowing;
    private string? _lastLoggedError;

    public PerformanceHud(int preset)
    {
        _preset = Math.Clamp(preset, 0, PresetNames.Length - 1);
        _timer = new Timer(Tick, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
    }

    public int Preset
    {
        get => _preset;
        set => _preset = Math.Clamp(value, 0, PresetNames.Length - 1);
    }

    public string PresetName => PresetNames[_preset];

    /// <summary>"ativo", "RTSS não está rodando", etc. — pro menu/Configurações.</summary>
    public string Status { get; private set; } = "iniciando";

    /// <summary>Some com o HUD da tela agora (antes de fechar/atualizar o launcher).</summary>
    public void ClearNow()
    {
        try
        {
            lock (_rtss) _rtss.ClearOsd();
            _isShowing = false;
        }
        catch
        {
            // Melhor esforço.
        }
    }

    private void Tick(object? state)
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1) return;

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
                var text = HudFormatter.Format(preset, frames, metrics);
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
        finally
        {
            Volatile.Write(ref _ticking, 0);
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
        _timer.Dispose();
        ClearNow();
        _rtss.Dispose();
        _metrics?.Dispose();
    }
}

/// <summary>
/// Monta o texto do HUD com as tags de cor do RTSS (&lt;C=RRGGBB&gt; ... &lt;C&gt;),
/// usando a paleta padrão do MangoHud: rótulo de cada sensor com sua cor
/// fixa, e os valores de carga/FPS mudando de cor conforme o nível
/// (verde → amarelo → vermelho), igual gpu_load_change/fps_color_change.
/// </summary>
internal static class HudFormatter
{
    // Paleta padrão do MangoHud.
    private const string GpuColor = "2E9762";
    private const string CpuColor = "2E97CB";
    private const string VramColor = "AD64C1";
    private const string RamColor = "C26693";
    private const string BatteryColor = "FF9078";
    private const string FpsColor = "EB5B5B";
    private const string FrameTimeColor = "00FF00";
    private const string UnitColor = "A0A0A0";

    private const string Good = "39F900";
    private const string Medium = "FDFD09";
    private const string Bad = "B22222";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Format(int preset, FrameStats? frames, MetricsSnapshot m)
    {
        var sb = new StringBuilder();

        switch (preset)
        {
            case 1: // Resumido — uma linha só
                AppendFps(sb, frames);
                if (m.BatteryPercent is { } pct)
                {
                    sb.Append("  ");
                    AppendBatteryShort(sb, pct, m.OnAcPower);
                }
                break;

            case 2: // Médio
                Line(sb, Label("GPU", GpuColor), Load(m.GpuPercent));
                Line(sb, Label("CPU", CpuColor), Load(m.CpuPercent));
                Line(sb, Label("RAM", RamColor), Gb(m.RamUsedGb));
                if (m.BatteryPercent is { } pct2)
                {
                    sb.Append(Label("BAT", BatteryColor)).Append(' ');
                    AppendBatteryShort(sb, pct2, m.OnAcPower, withLabel: false);
                    sb.Append('\n');
                }
                AppendFps(sb, frames);
                break;

            default: // Completo
                Line(sb, Label("GPU", GpuColor), Load(m.GpuPercent), m.VramGb is { } vram ? Label("VRAM", VramColor) + " " + Gb(vram) : null);
                Line(sb, Label("CPU", CpuColor), Load(m.CpuPercent), m.CpuMhz is { } mhz ? mhz.ToString("0", Inv) + Unit("MHz") : null);
                Line(sb, Label("RAM", RamColor), Gb(m.RamUsedGb) + Unit("/" + m.RamTotalGb.ToString("0", Inv) + "GB"));
                if (m.BatteryPercent is { } pct3)
                {
                    var parts = new List<string?> { Battery(pct3, m.OnAcPower) };
                    if (m.BatteryWatts is { } w) parts.Add((w >= 0 ? w.ToString("0.0", Inv) : "+" + (-w).ToString("0.0", Inv)) + Unit("W"));
                    if (m.BatteryRemaining is { } left) parts.Add($"{(int)left.TotalHours}h{left.Minutes:00}");
                    Line(sb, Label("BAT", BatteryColor), parts.ToArray());
                }
                AppendFps(sb, frames);
                break;
        }

        return sb.ToString().TrimEnd('\n');
    }

    private static void AppendFps(StringBuilder sb, FrameStats? frames)
    {
        sb.Append(Label("FPS", FpsColor)).Append(' ');
        if (frames is { } f)
        {
            var fpsColor = f.Fps >= 60 ? Good : f.Fps >= 30 ? Medium : Bad;
            sb.Append(Color(fpsColor, f.Fps.ToString("0", Inv)))
              .Append("  ")
              .Append(Color(FrameTimeColor, f.FrameTimeMs.ToString("0.0", Inv)))
              .Append(Unit("ms"));
        }
        else
        {
            sb.Append(Color(UnitColor, "--"));
        }
        sb.Append('\n');
    }

    private static void AppendBatteryShort(StringBuilder sb, int percent, bool onAc, bool withLabel = true)
    {
        if (withLabel) sb.Append(Label("BAT", BatteryColor)).Append(' ');
        sb.Append(Battery(percent, onAc));
    }

    private static string Battery(int percent, bool onAc)
    {
        var color = onAc ? Good : percent > 50 ? Good : percent > 20 ? Medium : Bad;
        return Color(color, percent.ToString(Inv)) + Unit(onAc ? "% AC" : "%");
    }

    private static void Line(StringBuilder sb, string label, params string?[] values)
    {
        sb.Append(label);
        foreach (var value in values)
        {
            if (string.IsNullOrEmpty(value)) continue;
            sb.Append("  ").Append(value);
        }
        sb.Append('\n');
    }

    private static string Load(double? percent)
    {
        if (percent is not { } p) return Color(UnitColor, "--");
        var color = p < 60 ? Good : p < 90 ? Medium : Bad;
        return Color(color, p.ToString("0", Inv)) + Unit("%");
    }

    private static string Gb(double value) => value.ToString("0.0", Inv) + Unit("GB");

    private static string Label(string text, string color) => Color(color, text);

    private static string Unit(string text) => Color(UnitColor, text);

    private static string Color(string hex, string text) => $"<C={hex}>{text}<C>";
}
