namespace HandheldLauncher.Core.Services;

/// <summary>Uma resolução oferecida no Quick Settings.</summary>
public sealed record ScreenPreset(string Label, int Width, int Height);

/// <summary>
/// Resoluções disponíveis no painel, **por modelo detetado** (QS 2.0, F1).
///
/// <para>A ideia (pedido do dono, 09/11): em vez de listar seis resoluções
/// genéricas (720p…2560) que muitas vezes o painel nem suporta, cada aparelho
/// mostra **três** — as que fazem sentido para o seu ecrã:</para>
/// <list type="bullet">
/// <item>Legion Go / Go 2 → 900p, 1080p, 1200p</item>
/// <item>ROG Ally / Ally X → 720p, 900p, 1080p</item>
/// <item>Desconhecido → a nativa do painel + as duas abaixo (quando cabem)</item>
/// </list>
///
/// <para>Vive no Core para poder ser testado sem hardware; a deteção do modelo
/// continua no App (<c>DeviceDetector</c>), que é quem conhece WMI/DMI.</para>
/// </summary>
public static class ScreenPresets
{
    private const string LegionGoId = "legion-go";
    private const string RogAllyId = "rog-ally";

    /// <summary>Três resoluções para o aparelho (nunca vazio).</summary>
    public static IReadOnlyList<ScreenPreset> ForDevice(
        string? deviceId,
        int nativeWidth = 1920,
        int nativeHeight = 1080)
    {
        var id = (deviceId ?? string.Empty).Trim().ToLowerInvariant();

        if (id.StartsWith(LegionGoId, StringComparison.Ordinal))
        {
            return
            [
                new ScreenPreset("900p", 1600, 900),
                new ScreenPreset("1080p", 1920, 1080),
                new ScreenPreset("1200p", 2560, 1600),
            ];
        }

        if (id.StartsWith(RogAllyId, StringComparison.Ordinal))
        {
            return
            [
                new ScreenPreset("720p", 1280, 720),
                new ScreenPreset("900p", 1600, 900),
                new ScreenPreset("1080p", 1920, 1080),
            ];
        }

        return ForNativePanel(nativeWidth, nativeHeight);
    }

    /// <summary>
    /// Fallback: a nativa + as duas abaixo dela, sem inventar resoluções que o
    /// painel não tem (ex.: um painel 1280×800 não deve oferecer 1080p).
    /// </summary>
    private static IReadOnlyList<ScreenPreset> ForNativePanel(int nativeWidth, int nativeHeight)
    {
        if (nativeWidth <= 0 || nativeHeight <= 0)
            return [new ScreenPreset("Nativa", 1920, 1080)];

        var presets = new List<ScreenPreset> { new(LabelFor(nativeWidth), nativeWidth, nativeHeight) };

        // 16:9 abaixo da nativa (1080p → 900p → 720p).
        foreach (var (w, h) in new[] { (1920, 1080), (1600, 900), (1280, 720) })
        {
            if (w >= nativeWidth) continue;
            presets.Add(new ScreenPreset(LabelFor(w), w, h));
            if (presets.Count == 3) break;
        }

        return presets;
    }

    private static string LabelFor(int width) => width switch
    {
        2560 => "1200p",
        1920 => "1080p",
        1680 => "1050p",
        1600 => "900p",
        1280 => "720p",
        _ => $"{width}p",
    };
}
