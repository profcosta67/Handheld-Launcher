using System;
using LibreHardwareMonitor.Hardware;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Services;

public class HardwareMonitorService : IDisposable
{
    private readonly Computer _computer;

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

    public void Dispose()
    {
        _computer.Close();
    }
}