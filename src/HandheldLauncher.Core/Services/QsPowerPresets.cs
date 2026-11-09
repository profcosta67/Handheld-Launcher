namespace HandheldLauncher.Core.Services;

/// <summary>Presets de TDP do Quick Settings 2.0 (quatro, não cinco).</summary>
public enum PowerPreset
{
    Eco,
    Normal,
    Desempenho,
    Custom,
}

/// <summary>
/// Traduz o preset escolhido no painel para **watts**, respeitando os limites
/// reais do aparelho (`TdpLimits`/firmware).
///
/// <para>Decisão do dono (09/11, revista): <b>Eco 8 W · Normal 15 W ·
/// Desempenho 20 W · Custom até ao máximo do aparelho</b>. O `Custom` é o único
/// que pode chegar ao topo, e mesmo assim é sempre limitado pela janela que o
/// firmware reporta — nunca se envia ao EC um valor acima do que ele aguenta.</para>
/// </summary>
public static class QsPowerPresets
{
    /// <summary>Eco: 8 W (elevado ao mínimo do aparelho quando este for maior).</summary>
    public const int EcoWatts = 8;

    /// <summary>Normal: 15 W.</summary>
    public const int NormalWatts = 15;

    /// <summary>Desempenho: 20 W.</summary>
    public const int DesempenhoWatts = 20;

    public static IReadOnlyList<PowerPreset> All { get; } =
        [PowerPreset.Eco, PowerPreset.Normal, PowerPreset.Desempenho, PowerPreset.Custom];

    /// <summary>
    /// Watts a aplicar. <paramref name="customWatts"/> só é usado no preset
    /// <see cref="PowerPreset.Custom"/>; todos os resultados ficam sempre entre
    /// <paramref name="minWatts"/> e <paramref name="maxWatts"/> — esta é a
    /// proteção que impede o Custom de pedir mais do que o aparelho aguenta.
    /// </summary>
    public static int Resolve(PowerPreset preset, int minWatts, int maxWatts, int customWatts = DesempenhoWatts)
    {
        var min = Math.Min(minWatts, maxWatts);
        var max = Math.Max(minWatts, maxWatts);

        var wanted = preset switch
        {
            PowerPreset.Eco => EcoWatts,
            PowerPreset.Normal => NormalWatts,
            PowerPreset.Desempenho => DesempenhoWatts,
            _ => customWatts,
        };

        return Math.Clamp(wanted, min, max);
    }
}
