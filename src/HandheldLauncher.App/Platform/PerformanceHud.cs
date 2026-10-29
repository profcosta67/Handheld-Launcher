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

    // Mini-gráfico de frametime (preset Completo, v0.9.5) — histórico
    // curto só pra desenhar a tendência; não precisa de lock separado
    // porque só o próprio Tick() (protegido por _ticking acima) toca
    // nisso, nunca duas vezes ao mesmo tempo.
    private const int FrameTimeHistorySize = 20;
    private readonly Queue<double> _frameTimeHistory = new(FrameTimeHistorySize + 1);

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

                if (frames is { } f)
                {
                    _frameTimeHistory.Enqueue(f.FrameTimeMs);
                    while (_frameTimeHistory.Count > FrameTimeHistorySize) _frameTimeHistory.Dequeue();
                }
                else
                {
                    // Sem jogo em foco agora — esvazia pra não deixar o
                    // gráfico "congelado" com dados de uma sessão anterior.
                    _frameTimeHistory.Clear();
                }

                var text = HudFormatter.Format(preset, frames, metrics, _frameTimeHistory);
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
/// Monta o texto do HUD com as tags de cor do RTSS (&lt;C=RRGGBB&gt; ... &lt;C&gt;).
///
/// Ajustado depois do primeiro teste real (v0.4.0, pedidos do usuário):
/// <list type="bullet">
/// <item>Tudo numa linha só, com espaço largo entre os grupos
/// ("GPU 99%    CPU 20%    ...").</item>
/// <item>Rótulos sempre presentes, inclusive no Resumido.</item>
/// <item>"Muito colorido": cor só nos rótulos (paleta do MangoHud); valores
/// em branco e unidades em cinza. Vermelho só no que é crítico de verdade
/// (bateria baixa).</item>
/// <item>Bateria com tempo restante e horário atual no fim da linha.</item>
/// </list>
/// Tamanho da fonte vem da configuração do próprio RTSS ("On-Screen
/// Display zoom").
/// </summary>
internal static class HudFormatter
{
    // Rótulos: paleta padrão do MangoHud.
    private const string GpuColor = "2E9762";
    private const string CpuColor = "2E97CB";
    private const string VramColor = "AD64C1";
    private const string RamColor = "C26693";
    private const string BatteryColor = "FF9078";
    private const string FpsColor = "EB5B5B";
    private const string ClockColor = "CCCCCC";

    private const string ValueColor = "FFFFFF";
    private const string UnitColor = "B0B0B0";
    private const string Critical = "FF4040";

    private const string GroupSeparator = "     ";

    // Mini-gráfico de frametime (v0.9.5, pedido antigo do planejamento) —
    // RTSS OSD só desenha TEXTO (sem API de gráfico de verdade), então o
    // "gráfico" é um sparkline feito com caracteres de bloco Unicode
    // (▁▂▃▄▅▆▇█), uma "barrinha" por amostra. NUNCA testado contra um RTSS
    // de verdade: se a fonte que o RTSS está usando não tiver esses
    // glyphs, pode aparecer em branco/quadradinhos em vez da barra — avisar
    // se isso acontecer, dá pra trocar por caracteres ASCII simples.
    private static readonly char[] SparkChars = ['▁', '▂', '▃', '▄', '▅', '▆', '▇', '█'];
    private const double SparklineMaxMs = 33.0; // ~30 FPS pra cima já vira a barra mais alta da escala
    private const string FrameColor = "F2C14E";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Format(int preset, FrameStats? frames, MetricsSnapshot m, IReadOnlyCollection<double>? frameTimeHistoryMs = null)
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
                groups.Add(Group("RAM", RamColor,
                    Value(m.RamUsedGb.ToString("0.0", Inv)) + Unit("/" + m.RamTotalGb.ToString("0", Inv) + "GB")));
                AddBattery(groups, m, withWatts: true);
                groups.Add(Fps(frames, withFrameTime: true));
                if (Sparkline(frameTimeHistoryMs) is { Length: > 0 } spark)
                {
                    groups.Add(Group("FRAME", FrameColor, Value(spark)));
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

    /// <summary>
    /// Uma barra por amostra do histórico (mais recente por último),
    /// escalada de 0 a <see cref="SparklineMaxMs"/> — acima disso já é a
    /// barra mais alta. string.Empty se não tem histórico ainda (jogo
    /// recém-aberto ou nenhum jogo em foco).
    /// </summary>
    private static string Sparkline(IReadOnlyCollection<double>? frameTimeHistoryMs)
    {
        if (frameTimeHistoryMs is null || frameTimeHistoryMs.Count == 0) return string.Empty;

        var sb = new StringBuilder(frameTimeHistoryMs.Count);
        foreach (var ms in frameTimeHistoryMs)
        {
            var t = Math.Clamp(ms / SparklineMaxMs, 0.0, 1.0);
            var level = (int)Math.Round(t * (SparkChars.Length - 1));
            sb.Append(SparkChars[level]);
        }

        return sb.ToString();
    }

    private static string Group(string label, string labelColor, string valueText)
        => Color(labelColor, label) + " " + valueText;

    private static string Percent(double? percent)
        => percent is { } p ? Value(p.ToString("0", Inv)) + Unit("%") : Unit("--");

    private static string Value(string text) => Color(ValueColor, text);

    private static string Unit(string text) => Color(UnitColor, text);

    private static string Color(string hex, string text) => $"<C={hex}>{text}<C>";
}
