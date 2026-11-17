namespace HandheldLauncher.Core.Services.SessionDiary;

/// <summary>
/// Resumo de uma sessão de jogo para o diário. Só guarda o que foi medido: um campo nulo
/// quer dizer "sem leitura nesta sessão" (ex.: sem RTSS não há FPS; na tomada não há consumo).
/// </summary>
public sealed class SessionRecord
{
    public string GameId { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime StartedUtc { get; set; }
    public int DurationSeconds { get; set; }

    /// <summary>Tempo da sessão na bateria (com leitura de consumo).</summary>
    public int BatterySeconds { get; set; }

    /// <summary>Consumo médio do aparelho inteiro na bateria, em watts.</summary>
    public double? AvgBatteryWatts { get; set; }
    public double? PeakBatteryWatts { get; set; }

    public int? BatteryStartPercent { get; set; }
    public int? BatteryEndPercent { get; set; }

    public double? AvgFps { get; set; }
    /// <summary>FPS dos piores momentos (5% das leituras mais baixas).</summary>
    public double? LowFps { get; set; }

    /// <summary>Limite de potência médio do processador (TDP) durante a sessão, em watts.</summary>
    public double? AvgTdpWatts { get; set; }
    public bool SmartTdp { get; set; }
    public int? TargetFps { get; set; }

    /// <summary>FPS médio em cada TDP usado na sessão (base da recomendação energética). null em sessões antigas.</summary>
    public List<TdpFpsBucket>? FpsByTdp { get; set; }
}

/// <summary>O que o diário sabe sobre o consumo de um jogo.</summary>
public sealed record GameEnergySummary(
    int Sessions,
    double TypicalWatts,
    double? AvgFps,
    double? EstimatedHours,
    double? LastWatts,
    bool LastAboveNormal,
    bool IsEarlyEstimate);

/// <summary>Consumo de um jogo na lista geral.</summary>
public sealed record GameEnergyRow(string GameId, string Title, GameEnergySummary Summary, long BatterySeconds);
