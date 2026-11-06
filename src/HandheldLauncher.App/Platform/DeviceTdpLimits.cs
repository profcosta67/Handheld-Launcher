using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// v0.18.5: descobre o teto de TDP do aparelho em que o launcher está rodando,
/// pra tomada e pra bateria (pedido do usuário: "detectar automaticamente o
/// pico máximo de cada aparelho, igual SimpleDecky — não ir além do que o
/// aparelho permite").
///
/// Só LÊ informações; nunca escreve nada no firmware.
///
/// 1. Lenovo (Legion Go / Go 2 / Go S): classe WMI LENOVO_CAPABILITY_DATA_01
///    (root\WMI, GUID 7A8F5407-CB67-4D6E-B547-39B3BE018154), a mesma que a
///    Legion Space usa pras barras do modo Personalizado e que o driver
///    lenovo-wmi-other do Linux e o LeGoTDP leem. Cada linha tem IDs,
///    Capability, DefaultValue, Step, MinValue, MaxValue. O ID é
///    (dispositivo &lt;&lt; 24) | (recurso &lt;&lt; 16) | (modo &lt;&lt; 8) | tipo, com
///    dispositivo CPU = 0x01, recurso SPPT = 0x01 / SPL = 0x02 / FPPT = 0x03,
///    modo Personalizado = 0xFF, tipo 0. O driver do Linux relê esses dados
///    quando muda tomada/bateria — por isso o cache aqui é por fonte de energia.
/// 2. ASUS ROG Ally (RC71) e Ally X (RC72): tabela fixa — a ASUS não expõe isso
///    no firmware. Números do Armoury Crate coletados pelo projeto asus-linux
///    (asusctl #564), os mesmos do driver asus-armoury:
///    tomada 7–30 / 43 / 53 W; bateria 7–25 / 30 / 35 W.
/// 3. Qualquer outro, ou leitura que não faz sentido: 5–30 W, a trava de sempre.
/// </summary>
public static class DeviceTdpLimits
{
    private const uint LenovoSplId = 0x0102FF00;
    private const uint LenovoSpptId = 0x0101FF00;
    private const uint LenovoFpptId = 0x0103FF00;

    private static readonly object Sync = new();
    private static readonly Dictionary<bool, TdpLimits> Cache = new();
    private static (string Manufacturer, string BoardProduct)? _dmi;

    /// <summary>Limites agora (tomada ou bateria). Nunca lança; no pior caso, fallback 30 W.</summary>
    public static TdpLimits Current() => For(IsOnAcPower());

    public static TdpLimits For(bool onAc)
    {
        lock (Sync)
        {
            if (Cache.TryGetValue(onAc, out var cached)) return cached;
            var limits = Detect(onAc);
            Cache[onAc] = limits;
            DebugLog.Write($"[TDP-LIMITES] {(onAc ? "Tomada" : "Bateria")}: SPL {limits.SplMin}–{limits.SplMax} W, SPPT até {limits.SpptMax} W, FPPT até {limits.FpptMax} W — fonte: {limits.Source}.");
            return limits;
        }
    }

    private static TdpLimits Detect(bool onAc)
    {
        if (!OperatingSystem.IsWindows()) return TdpLimits.Fallback(onAc);
        try
        {
            var (manufacturer, board) = ReadDmi();

            if (manufacturer.Contains("LENOVO", StringComparison.OrdinalIgnoreCase))
            {
                var lenovo = TryReadLenovoFirmware(onAc);
                if (lenovo is not null) return lenovo;
            }

            if (manufacturer.Contains("ASUS", StringComparison.OrdinalIgnoreCase))
            {
                // RC71L = ROG Ally (Z1/Z1 Extreme); RC72LA = ROG Ally X.
                if (board.StartsWith("RC71", StringComparison.OrdinalIgnoreCase)
                    || board.StartsWith("RC72", StringComparison.OrdinalIgnoreCase))
                {
                    var model = board.StartsWith("RC72", StringComparison.OrdinalIgnoreCase) ? "ROG Ally X" : "ROG Ally";
                    return onAc
                        ? new TdpLimits(7, 30, 43, 53, onAc, $"tabela {model} ({board}), tomada")
                        : new TdpLimits(7, 25, 30, 35, onAc, $"tabela {model} ({board}), bateria");
                }
            }

            DebugLog.Write($"[TDP-LIMITES] Aparelho sem limite conhecido (fabricante='{manufacturer}', placa='{board}') — usando 5–30 W.");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[TDP-LIMITES] Falha ao detectar limites: {ex.Message} — usando 5–30 W.");
        }
        return TdpLimits.Fallback(onAc);
    }

    private static TdpLimits? TryReadLenovoFirmware(bool onAc)
    {
        try
        {
            uint? splMin = null, splMax = null, spptMax = null, fpptMax = null;
            using var searcher = new ManagementObjectSearcher(@"root\WMI",
                "SELECT IDs, Capability, MinValue, MaxValue FROM LENOVO_CAPABILITY_DATA_01");
            using var results = searcher.Get();
            foreach (ManagementObject row in results)
            {
                using (row)
                {
                    var id = Convert.ToUInt32(row["IDs"]);
                    if (id != LenovoSplId && id != LenovoSpptId && id != LenovoFpptId) continue;
                    var capability = Convert.ToUInt32(row["Capability"]);
                    var min = Convert.ToUInt32(row["MinValue"]);
                    var max = Convert.ToUInt32(row["MaxValue"]);
                    DebugLog.Write($"[TDP-LIMITES] Lenovo capdata 0x{id:X8}: capability={capability}, min={min}, max={max}.");
                    if (capability == 0) continue; // atributo não suportado neste aparelho
                    if (id == LenovoSplId) { splMin = min; splMax = max; }
                    else if (id == LenovoSpptId) spptMax = max;
                    else fpptMax = max;
                }
            }

            if (splMin is null || splMax is null || spptMax is null || fpptMax is null)
            {
                DebugLog.Write("[TDP-LIMITES] Firmware Lenovo não informou SPL/SPPT/FPPT do modo Personalizado.");
                return null;
            }

            // O firmware às vezes informa mínimo 0; o piso do launcher é 5 W.
            var limits = new TdpLimits(
                Math.Max(TdpProfiles.MinWatts, (int)splMin.Value), (int)splMax.Value, (int)spptMax.Value, (int)fpptMax.Value,
                onAc, $"firmware Lenovo ({(onAc ? "tomada" : "bateria")})");
            if (!limits.IsSane)
            {
                DebugLog.Write($"[TDP-LIMITES] Valores do firmware Lenovo não fazem sentido ({limits.SplMin}/{limits.SplMax}/{limits.SpptMax}/{limits.FpptMax}) — ignorados.");
                return null;
            }
            return limits;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[TDP-LIMITES] Não deu pra ler LENOVO_CAPABILITY_DATA_01: {ex.Message}");
            return null;
        }
    }

    private static (string Manufacturer, string BoardProduct) ReadDmi()
    {
        if (_dmi is { } known) return known;
        string manufacturer = string.Empty, board = string.Empty;
        try
        {
            using var cs = new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_ComputerSystem");
            using var csResults = cs.Get();
            foreach (ManagementObject o in csResults) using (o) { manufacturer = Convert.ToString(o["Manufacturer"])?.Trim() ?? string.Empty; break; }
            using var bb = new ManagementObjectSearcher("SELECT Product FROM Win32_BaseBoard");
            using var bbResults = bb.Get();
            foreach (ManagementObject o in bbResults) using (o) { board = Convert.ToString(o["Product"])?.Trim() ?? string.Empty; break; }
        }
        catch (Exception ex) { DebugLog.Write($"[TDP-LIMITES] Falha ao ler DMI: {ex.Message}"); }
        _dmi = (manufacturer, board);
        return _dmi.Value;
    }

    // --- Tomada ou bateria ---------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    /// <summary>
    /// Na tomada? Desconhecido (255) conta como BATERIA — é o teto menor, o
    /// lado seguro. Aparelho sem bateria conta como tomada.
    /// </summary>
    public static bool IsOnAcPower()
    {
        if (!OperatingSystem.IsWindows()) return true;
        try
        {
            if (!GetSystemPowerStatus(out var status)) return false;
            if ((status.BatteryFlag & 128) != 0) return true; // sem bateria
            return status.ACLineStatus == 1;
        }
        catch { return false; }
    }
}
