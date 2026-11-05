using System;
using System.Management;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// v0.18 "Saúde do aparelho": saúde real da bateria (capacidade de carga
/// total hoje vs. capacidade de projeto/fábrica) — diferente da bateria %
/// instantânea que já existe em <see cref="BatteryStatus"/>.
///
/// Usa WMI root\WMI, mesmo namespace já acessado em SystemMetrics
/// (ReadBatteryWatts → classe BatteryStatus do WMI), então não abre
/// superfície de acesso nova. BatteryStaticData.DesignedCapacity e
/// BatteryFullChargedCapacity.FullChargedCapacity são classes WMI
/// documentadas da própria Microsoft — é o mesmo dado que o
/// "powercfg /batteryreport" nativo do Windows mostra como "capacidade
/// de projeto" / "capacidade de carga completa".
/// </summary>
public static class BatteryHealth
{
    public readonly record struct Result(int? DesignedCapacityMwh, int? FullChargedCapacityMwh)
    {
        public int? HealthPercent =>
            DesignedCapacityMwh is { } designed && designed > 0 && FullChargedCapacityMwh is { } full
                ? (int)Math.Round(100.0 * full / designed)
                : null;
    }

    public static Result Read()
    {
        if (!OperatingSystem.IsWindows()) return new Result(null, null);

        int? designed = ReadSingleIntColumn(@"root\WMI", "SELECT DesignedCapacity FROM BatteryStaticData", "DesignedCapacity");
        int? fullCharged = ReadSingleIntColumn(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", "FullChargedCapacity");

        return new Result(designed is > 0 ? designed : null, fullCharged is > 0 ? fullCharged : null);
    }

    private static int? ReadSingleIntColumn(string scope, string query, string column)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, query);
            using var results = searcher.Get();
            foreach (var item in results)
            {
                using var mo = (ManagementObject)item;
                return Convert.ToInt32(mo[column] ?? 0);
            }
        }
        catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
        return null;
    }
}
