using System;

namespace HandheldLauncher.App.Platform.Hardware;

public class DeviceProfile
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    
    // Limites de TDP
    public int TdpMin { get; init; } = 5;
    public int TdpMax { get; init; } = 30;
    public int TdpMaxAc { get; init; } = 30;

    // Recursos
    public bool SupportsFanControl { get; init; }
    public bool SupportsCustomTdp { get; init; }
    
    // Status de validação (Resolve o item 4 do Lote 1)
    public bool IsValidated { get; init; }
    public string ValidationMessage { get; init; } = string.Empty;
}