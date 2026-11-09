using System.Diagnostics;
using System.Globalization;
using HandheldLauncher.Core.Services;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Hardware real no Linux (Fase 2). Substitui as chamadas WinRT/WMI/PawnIO do
/// Windows por ferramentas nativas do Linux:
/// - TDP .......... ryzenadj (fala direto ao SMU, como o PawnIO fazia).
/// - Brilho ....... brightnessctl.
/// - Volume ....... pactl (PipeWire/PulseAudio).
/// - Resolução .... wlr-randr (Wayland) / xrandr (X11).
/// Tudo é melhor esforço: se a ferramenta não existir ou não tiver permissões,
/// loga e devolve "não suportado" sem rebentar — a UI continua a funcionar.
/// </summary>
public class LinuxHardwareService : IHardwareService
{
    public bool SetTdpMode(string mode)
    {
        if (!TryParseWatts(mode, out var watts))
        {
            DebugLog.Write($"[LINUX] TDP: valor não numérico '{mode}' — ignorado.");
            return false;
        }

        // ryzenadj trabalha em mW (15 W = 15000). Aplica nos três limites de
        // energia do APU (stapm = sustentado, fast = rajada, slow = mínimo).
        var mw = watts * 1000;
        var ok = Run("ryzenadj", $"--stapm-limit={mw} --fast-limit={mw} --slow-limit={mw}");
        DebugLog.Write($"[LINUX] TDP {watts} W via ryzenadj: {(ok ? "ok" : "falhou (ryzenadj instalado? permissões?)")}.");
        return ok;
    }

    public void SetVolume(double level)
    {
        var percent = (int)Math.Clamp(level, 0, 100);
        Run("pactl", $"set-sink-volume @DEFAULT_SINK@ {percent}%");
        DebugLog.Write($"[LINUX] Volume: {percent}%");
    }

    public void SetBrightness(double level)
    {
        var percent = (int)Math.Clamp(level, 0, 100);
        var ok = Run("brightnessctl", $"set {percent}%");
        DebugLog.Write($"[LINUX] Brilho: {percent}% (brightnessctl {(ok ? "ok" : "falhou")})");
    }

    public bool SetColorMode(string mode)
    {
        // Ajuste de cor/temperatura no Linux usa wl-gammarelay (Wayland) ou
        // gammastep — chega numa fase posterior.
        DebugLog.Write($"[LINUX] Modo de cor indisponível neste provider: {mode}");
        return false;
    }

    public void SetPerformanceHudEnabled(bool enabled)
    {
        // O MangoHud é o substituto do RTSS no Linux; a integração chega depois.
        DebugLog.Write($"[LINUX] HUD (MangoHud): {enabled}");
    }

    public string GetDisplayInfo()
    {
        var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? string.Empty;

        if (session.Equals("wayland", StringComparison.OrdinalIgnoreCase))
        {
            var text = RunCapture("wlr-randr") ?? string.Empty;
            return ParseCurrentMode(text) ?? "Wayland (resolução desconhecida)";
        }

        var x = RunCapture("xrandr") ?? string.Empty;
        return ParseXrandrCurrentMode(x) ?? "X11 (resolução desconhecida)";
    }

    public void SetDisplayMode(int width, int height, int refreshRate)
    {
        var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? string.Empty;

        if (session.Equals("wayland", StringComparison.OrdinalIgnoreCase))
        {
            var output = FirstWlrOutputName();
            var ok = output is not null
                && Run("wlr-randr", $"--output {output} --mode {width}x{height}@{refreshRate}");
            DebugLog.Write($"[LINUX] Tela {width}x{height}@{refreshRate}Hz (wlr-randr): {(ok ? "ok" : "falhou")}");
            return;
        }

        var xOk = Run("xrandr", $"--output eDP --mode {width}x{height} --rate {refreshRate}");
        DebugLog.Write($"[LINUX] Tela {width}x{height}@{refreshRate}Hz (xrandr): {(xOk ? "ok" : "falhou")}");
    }

    // -------------------------- helpers --------------------------

    private static bool TryParseWatts(string mode, out int watts)
    {
        watts = 0;
        if (string.IsNullOrWhiteSpace(mode)) return false;
        // Aceita "15", "15W", "custom:15" (o prefixo de preset é ignorado).
        var numeric = mode.Split(':').Last().Trim();
        return int.TryParse(numeric, NumberStyles.Integer, CultureInfo.InvariantCulture, out watts);
    }

    private static bool Run(string file, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };
            process.Start();
            process.WaitForExit(10_000);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[LINUX] '{file}' indisponível: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static string? RunCapture(string file)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = file,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);
            return output;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Nome do primeiro output do wlr-randr (ex.: "eDP-1").</summary>
    private static string? FirstWlrOutputName()
    {
        var text = RunCapture("wlr-randr") ?? string.Empty;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(' ')) continue;
            var name = trimmed.Split(' ')[0];
            if (name.Contains('-')) return name;
        }
        return null;
    }

    /// <summary>Extrai o modo atual ("2560x1600 @ 60.00 Hz") do wlr-randr.</summary>
    private static string? ParseCurrentMode(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.Contains('x') || !trimmed.Contains("px", StringComparison.Ordinal)) continue;
            var px = trimmed.IndexOf("px", StringComparison.Ordinal);
            var size = trimmed[..px].Trim();
            var hz = "?";
            var hzIdx = trimmed.IndexOf("Hz", StringComparison.Ordinal);
            if (hzIdx > 0)
            {
                hz = trimmed[(px + 2)..hzIdx].Trim().TrimEnd(',', ' ');
            }
            return $"{size} @ {hz} Hz";
        }
        return null;
    }

    /// <summary>Extrai o modo atual do xrandr (linha marcada com '*').</summary>
    private static string? ParseXrandrCurrentMode(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            if (line.Contains('*') && line.Contains('x'))
            {
                var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2) return $"{parts[0]} {parts[1].TrimEnd('*', '+')}Hz";
            }
        }
        return null;
    }
}