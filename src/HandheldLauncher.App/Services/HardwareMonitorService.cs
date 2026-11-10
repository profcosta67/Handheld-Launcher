#if WINDOWS
using System;
using LibreHardwareMonitor.Hardware;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Services;

/// <summary>
/// Leitura de sensores via LibreHardwareMonitor — <b>só Windows</b>.
///
/// O pacote NuGet 0.9.6 publica o DLL apenas em <c>runtimes/win-*</c>: compila
/// em qualquer TFM (há asset de compile-time) mas um publish
/// <c>-r linux-x64</c> não o copia, e o launcher morria no arranque com
/// <c>FileNotFoundException: LibreHardwareMonitorLib</c> (beta.5). Por isso o
/// ficheiro inteiro está dentro de <c>#if WINDOWS</c> e a referência ao pacote
/// está condicionada no .csproj. Nas outras plataformas o
/// <see cref="HardwareMonitor.Create"/> devolve
/// <see cref="NullHardwareMonitorService"/>.
/// </summary>
public class HardwareMonitorService : IHardwareMonitorService
{
    private readonly Computer _computer;

    // v0.18.3: a leitura agora roda numa thread de fundo (ver MainViewModel,
    // timer de hardware). O Computer do LibreHardwareMonitor não é seguro pra
    // uso simultâneo, então toda chamada passa por esta trava.
    private readonly object _sync = new();

    public HardwareMonitorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true
        };
        _computer.Open();
    }

    public void LogAllSensors()
    {
        lock (_sync)
        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware)
            {
                subHardware.Update();
                foreach (var sensor in subHardware.Sensors)
                {
                    if (sensor.SensorType == SensorType.Temperature)
                        DebugLog.Write($"[SENSOR] {hardware.Name} -> {subHardware.Name} -> {sensor.Name}: {sensor.Value}");
                }
            }
            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.SensorType == SensorType.Temperature)
                    DebugLog.Write($"[SENSOR] {hardware.Name} -> {sensor.Name}: {sensor.Value}");
            }
        }
    }
    public float? GetCpuTemperature()
    {
        lock (_sync)
        {
        float? cpuTemp = null;
        float? socTemp = null;

        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();
            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue && sensor.Value > 0)
                {
                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        cpuTemp = sensor.Value;
                    }
                    else if (hardware.HardwareType == HardwareType.GpuAmd)
                    {
                        if (sensor.Name.Contains("VR SoC") || sensor.Name.Contains("Edge"))
                        {
                            socTemp = sensor.Value;
                        }
                    }
                }
            }
        }

        if (cpuTemp.HasValue && cpuTemp.Value > 0) return cpuTemp;
        return socTemp;
        }
    }

    public void Dispose()
    {
        lock (_sync) _computer.Close();
    }
}
#endif
