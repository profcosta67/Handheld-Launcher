using System;
using System.Globalization;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// v0.18.5: os perfis de TDP num lugar só, mais o TDP personalizado.
///
/// O modo salvo em AppSettings.TdpMode continua sendo texto. Os 5 perfis de
/// sempre ("UltraEco", "Eco", "Normal", "Desempenho", "Boost") não mudam. O
/// personalizado é salvo como "Custom:18" (watts dentro do próprio texto).
/// Assim TODOS os caminhos que já reaplicam o TDP salvo (abertura do app,
/// tentativas da inicialização, volta da suspensão) funcionam sem mudança,
/// porque só repassam o texto. Uma versão antiga que leia "Custom:18" cai no
/// padrão dela (20 W), que é seguro.
///
/// Faixa: aqui só o piso (5 W) e o teto ABSOLUTO de sanidade (55 W). O teto
/// de verdade é o do aparelho no momento (TdpLimits — firmware/tabela, tomada
/// ou bateria) e é aplicado no WindowsHardwareService/RyzenSmuService, que
/// continua bloqueando por conta própria qualquer coisa fora do permitido.
/// </summary>
public static class TdpProfiles
{
    public const int MinWatts = 5;
    /// <summary>Teto absoluto (sanidade). O teto real do aparelho vem de TdpLimits.</summary>
    public const int MaxWatts = TdpLimits.AbsoluteMaxWatts;
    public const int DefaultCustomWatts = 20;
    public const string CustomPrefix = "Custom:";

    public static readonly string[] Presets = ["UltraEco", "Eco", "Normal", "Desempenho", "Boost"];

    public static bool IsCustom(string? mode)
        => mode is not null && mode.StartsWith(CustomPrefix, StringComparison.Ordinal);

    public static string CustomMode(int watts) => CustomPrefix + ClampWatts(watts).ToString(CultureInfo.InvariantCulture);

    public static int ClampWatts(int watts) => Math.Clamp(watts, MinWatts, MaxWatts);

    /// <summary>Watts do modo, ou null se o texto não for nenhum modo conhecido.</summary>
    public static int? WattsFor(string? mode)
    {
        switch (mode)
        {
            case "UltraEco": return 8;
            case "Eco": return 15;
            case "Normal": return 20;
            case "Desempenho": return 25;
            case "Boost": return 30;
        }

        if (IsCustom(mode)
            && int.TryParse(mode!.AsSpan(CustomPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var watts))
        {
            return ClampWatts(watts);
        }

        return null;
    }

    /// <summary>
    /// Curva da ventoinha que acompanha o TDP (mesma ligação de antes pros 5
    /// perfis). O personalizado segue a faixa de watts do perfil mais próximo
    /// acima dele.
    /// </summary>
    public static string LinkedFanMode(string? mode)
    {
        switch (mode)
        {
            case "UltraEco":
            case "Eco": return "Silencioso";
            case "Normal": return "Normal";
            case "Desempenho": return "Desempenho";
            case "Boost": return "Boost";
        }

        if (IsCustom(mode) && WattsFor(mode) is int watts)
        {
            return watts <= 15 ? "Silencioso"
                : watts <= 20 ? "Normal"
                : watts <= 25 ? "Desempenho"
                : "Boost";
        }

        return "Automático";
    }
}
