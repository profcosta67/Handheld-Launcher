namespace HandheldLauncher.App.Services;

/// <summary>
/// Escolhe a implementação de <see cref="IHardwareMonitorService"/> para a
/// plataforma actual: o LibreHardwareMonitor no Windows, o substituto inerte
/// no resto.
///
/// Existe um único ponto de decisão (#if WINDOWS) para o MainViewModel e para
/// o autoteste de arranque (Program.cs --selftest) não divergirem.
/// </summary>
public static class HardwareMonitor
{
    public static IHardwareMonitorService Create() =>
#if WINDOWS
        new HardwareMonitorService();
#else
        new NullHardwareMonitorService();
#endif
}
