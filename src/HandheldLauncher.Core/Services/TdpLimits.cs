using System;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// v0.18.5: teto de TDP do aparelho NO MOMENTO (tomada ou bateria).
///
/// SPL = limite sustentado (STAPM), SPPT = pico lento (Slow PPT), FPPT = pico
/// rápido (Fast PPT). De onde vem (ver DeviceTdpLimits no App):
/// - Legion (Go, Go 2, Go S): o próprio firmware da Lenovo informa
///   (LENOVO_CAPABILITY_DATA_01, modo Personalizado) — o mesmo dado que a
///   Legion Space usa pras barras. No Go 2 na tomada: 35/37/45 W.
/// - ROG Ally / Ally X: tabela por modelo (a ASUS não expõe no firmware; mesmos
///   números do Armoury Crate que o driver asus-armoury do Linux usa).
/// - Qualquer outro: <see cref="Fallback"/> = a trava de sempre, 5–30 W.
/// </summary>
public sealed record TdpLimits(int SplMin, int SplMax, int SpptMax, int FpptMax, bool OnAc, string Source)
{
    /// <summary>
    /// Teto absoluto de sanidade pra qualquer valor vindo de firmware/tabela.
    /// Os maiores handhelds conhecidos ficam abaixo disso (Legion Go S: FPPT
    /// 53 W; Ally X: 53 W). Valor acima = leitura errada → usa o Fallback.
    /// </summary>
    public const int AbsoluteMaxWatts = 55;

    public static TdpLimits Fallback(bool onAc) =>
        new(TdpProfiles.MinWatts, 30, 30, 30, onAc, "padrão (limite do aparelho desconhecido)");

    public bool IsFallback => Source.StartsWith("padrão", StringComparison.Ordinal);

    /// <summary>
    /// Os números fazem sentido? Mínimo ≥ 5 W, máximo ≤ 55 W e
    /// SPL ≤ SPPT ≤ FPPT. Fora disso a fonte é ignorada (fallback 30 W).
    /// </summary>
    public bool IsSane =>
        SplMin >= TdpProfiles.MinWatts
        && SplMin <= SplMax
        && SplMax <= SpptMax
        && SpptMax <= FpptMax
        && FpptMax <= AbsoluteMaxWatts;

    public int ClampSpl(int watts) => Math.Clamp(watts, SplMin, SplMax);

    /// <summary>
    /// Personalizado: o usuário escolhe o SPL; os picos acompanham na mesma
    /// proporção do teto do fabricante (no Go 2: 35 → 37/45; 20 → 21/26), sem
    /// passar do máximo de cada um e nunca abaixo do SPL. No fallback a
    /// proporção é 1 — os três iguais, como sempre foi.
    /// </summary>
    public (int Spl, int Sppt, int Fppt) PeaksFor(int splWatts)
    {
        var spl = ClampSpl(splWatts);
        var sppt = Math.Clamp((int)Math.Round(spl * (double)SpptMax / SplMax, MidpointRounding.AwayFromZero), spl, SpptMax);
        var fppt = Math.Clamp((int)Math.Round(spl * (double)FpptMax / SplMax, MidpointRounding.AwayFromZero), sppt, FpptMax);
        return (spl, sppt, fppt);
    }
}
