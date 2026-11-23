using System.Text.Json.Serialization;

namespace HandheldLauncher.Core.Services.Power;

/// <summary>O que o aparelho estava fazendo na hora da amostra.</summary>
public static class PowerState
{
    /// <summary>Launcher na frente, sem jogo.</summary>
    public const string Launcher = "launcher";
    /// <summary>Jogo aberto.</summary>
    public const string Game = "jogo";
    /// <summary>Sem jogo, launcher minimizado ou atrás de outro programa.</summary>
    public const string Background = "fundo";
}

/// <summary>Um processo na amostra: nome e % do processador (de todos os núcleos) no intervalo.</summary>
public sealed class ProcessCpu
{
    [JsonPropertyName("n")] public string Name { get; set; } = "";
    [JsonPropertyName("c")] public double CpuPercent { get; set; }
}

/// <summary>
/// Uma leitura do coletor de bateria (a cada 30 s na bateria, 2 min na tomada). Nomes curtos
/// no JSON: o arquivo guarda até 3 dias.
/// </summary>
public sealed class PowerSample
{
    [JsonPropertyName("t")] public DateTime Utc { get; set; }
    /// <summary>Segundos cobertos por esta amostra (desde a anterior).</summary>
    [JsonPropertyName("dt")] public double Seconds { get; set; }
    [JsonPropertyName("ac")] public bool OnAc { get; set; }
    [JsonPropertyName("pct")] public int? Percent { get; set; }
    /// <summary>Carga restante em mWh (Windows, WMI BatteryStatus).</summary>
    [JsonPropertyName("mwh")] public int? RemainingMwh { get; set; }
    /// <summary>Capacidade cheia real em mWh.</summary>
    [JsonPropertyName("full")] public int? FullMwh { get; set; }
    /// <summary>Consumo da bateria em W (positivo = descarregando).</summary>
    [JsonPropertyName("w")] public double? Watts { get; set; }
    /// <summary>Tempo restante que o Windows informa, em minutos.</summary>
    [JsonPropertyName("winMin")] public double? WindowsRemainingMinutes { get; set; }
    /// <summary>Tempo restante pela conta do launcher, em minutos.</summary>
    [JsonPropertyName("ourMin")] public double? OurRemainingMinutes { get; set; }
    [JsonPropertyName("st")] public string State { get; set; } = PowerState.Background;
    [JsonPropertyName("game")] public string? Game { get; set; }
    [JsonPropertyName("qs")] public bool QuickSettingsOpen { get; set; }
    /// <summary>% do processador (todos os núcleos) usado pelo launcher no intervalo.</summary>
    [JsonPropertyName("lcpu")] public double? LauncherCpuPercent { get; set; }
    /// <summary>% do processador usado por tudo no intervalo.</summary>
    [JsonPropertyName("cpu")] public double? SystemCpuPercent { get; set; }
    /// <summary>Processos que mais usaram o processador no intervalo (até 8).</summary>
    [JsonPropertyName("top")] public List<ProcessCpu>? TopProcesses { get; set; }
    /// <summary>Milissegundos de trabalho por função do launcher no intervalo.</summary>
    [JsonPropertyName("fx")] public Dictionary<string, double>? ActivityMs { get; set; }
    /// <summary>Vezes que cada função do launcher acordou no intervalo.</summary>
    [JsonPropertyName("fxn")] public Dictionary<string, long>? ActivityCount { get; set; }
    [JsonPropertyName("bright")] public int? BrightnessPercent { get; set; }
    [JsonPropertyName("hz")] public int? RefreshHz { get; set; }
    [JsonPropertyName("tdp")] public int? TdpWatts { get; set; }
}
