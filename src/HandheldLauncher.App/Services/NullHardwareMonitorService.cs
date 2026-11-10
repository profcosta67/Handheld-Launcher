namespace HandheldLauncher.App.Services;

/// <summary>
/// Substituto inerte do <see cref="HardwareMonitorService"/> nas plataformas
/// onde o LibreHardwareMonitor não existe (Linux/macOS): o pacote NuGet
/// 0.9.6 só traz binários em <c>runtimes/win-*</c>, e num publish
/// <c>-r linux-x64</c> eles são filtrados.
///
/// Nada é lido: a temperatura da CPU mostra "--°C" e os alertas de
/// temperatura da Saúde do aparelho ficam desligados. O resto do launcher
/// funciona igual — o que importa é que a aplicação **arranca**.
/// </summary>
public sealed class NullHardwareMonitorService : IHardwareMonitorService
{
    /// <summary>Sem sensores não há leitura — nunca devolve valor.</summary>
    public float? GetCpuTemperature() => null;

    /// <inheritdoc />
    public void LogAllSensors()
    {
    }

    public void Dispose()
    {
    }
}
