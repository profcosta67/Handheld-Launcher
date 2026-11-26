namespace HandheldLauncher.Core.Services.SmartTdp;

/// <summary>
/// 0.27 v2 (22/11): o que o serviço do TDP inteligente usa de um "cérebro". Existem dois:
/// <see cref="SmartTdpController"/> (o da beta.66–73, mantido como reserva) e
/// <see cref="SmartTdpControllerV2"/> (regra única "watt a mais só se comprar FPS"). Qual roda é
/// escolhido pela configuração <c>SmartTdpEngine</c>.
/// </summary>
public interface ISmartTdpController
{
    int Watts { get; }
    int TargetFps { get; }

    /// <summary>Uma leitura de FPS (mediana de ~6 s) no instante <paramref name="nowSeconds"/>.</summary>
    SmartTdpChange OnSample(double fps, double nowSeconds);

    bool IsSettled { get; }
    bool TargetUnreachable { get; }

    /// <summary>A meta real (não o patamar de "fora de alcance") já se segurou nesta sessão: só aí vale aprender.</summary>
    bool MetRealTarget { get; }
    int EffectiveTargetFps { get; }
    double UnreachableSinceSeconds { get; }

    int BaselineWatts { get; }
    int ProbeExtraWatts { get; }

    int PrepareForResume(double nowSeconds, int boostWatts);
    bool ResumeBoostPending { get; }
    int? DropResumeBoost(double nowSeconds);
}
