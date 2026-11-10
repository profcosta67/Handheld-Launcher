namespace HandheldLauncher.App.Services;

/// <summary>
/// Leitura de sensores de hardware (temperatura da CPU e diagnóstico).
///
/// No Windows a implementação é o LibreHardwareMonitor
/// (<see cref="HardwareMonitorService"/>); no Linux/macOS usa-se
/// <see cref="NullHardwareMonitorService"/>, porque o pacote NuGet do
/// LibreHardwareMonitor só publica binários para Windows — ver a nota no
/// HandheldLauncher.App.csproj (bug do beta.5: o publish linux-x64 não copiava
/// o DLL e o launcher morria logo no arranque).
/// </summary>
public interface IHardwareMonitorService : System.IDisposable
{
    /// <summary>
    /// Temperatura da CPU em °C, ou <c>null</c> quando não há leitura
    /// disponível (sem sensores, sem permissões, ou plataforma sem suporte).
    /// </summary>
    float? GetCpuTemperature();

    /// <summary>
    /// Despeja no log de diagnóstico todas as temperaturas encontradas.
    /// Usado pelos comandos de suporte; nunca é chamado no arranque normal.
    /// </summary>
    void LogAllSensors();
}
