namespace HandheldLauncher.Core.Services;

/// <summary>Presets de TDP do Quick Settings 2.0 (quatro, não cinco).</summary>
public enum PowerPreset
{
    Eco,
    Normal,
    Boost,
    Custom,
}

/// <summary>
/// Traduz o preset escolhido no painel para **watts**, respeitando os limites
/// reais do aparelho (`TdpLimits`/firmware).
///
/// <para>Decisão do dono (09/11): o painel deixa de ter `U.Eco`/`Desemp.` e fica
/// com **Eco / Normal / Boost / Custom** — um slider e três atalhos. O clamp é
/// feito aqui, em código puro, para nunca enviar ao firmware um valor fora da
/// janela suportada (no Legion em bateria é 5–35 W; no ROG é outra).</para>
/// </summary>
public static class QsPowerPresets
{
    /// <summary>Preset de "uso normal" — 20 W é o valor de referência do projeto.</summary>
    public const int NormalWatts = 20;

    public static IReadOnlyList<PowerPreset> All { get; } =
        [PowerPreset.Eco, PowerPreset.Normal, PowerPreset.Boost, PowerPreset.Custom];

    /// <summary>
    /// Watts a aplicar. <paramref name="customWatts"/> só é usado no preset
    /// <see cref="PowerPreset.Custom"/>; todos os resultados ficam sempre entre
    /// <paramref name="minWatts"/> e <paramref name="maxWatts"/>.
    /// </summary>
    public static int Resolve(PowerPreset preset, int minWatts, int maxWatts, int customWatts = NormalWatts)
    {
        var min = Math.Min(minWatts, maxWatts);
        var max = Math.Max(minWatts, maxWatts);

        var wanted = preset switch
        {
            PowerPreset.Eco => min,
            PowerPreset.Normal => NormalWatts,
            PowerPreset.Boost => max,
            _ => customWatts,
        };

        return Math.Clamp(wanted, min, max);
    }
}
