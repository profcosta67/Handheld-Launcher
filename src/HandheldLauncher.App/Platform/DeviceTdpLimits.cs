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
    private static (string Manufacturer, string BoardProduct, string ProductVersion)? _dmi;

    /// <summary>Limites agora (tomada ou bateria). Nunca lança; no pior caso, fallback 30 W.</summary>
    public static TdpLimits Current() => For(IsOnAcPower());

    public static TdpLimits For(bool onAc)
    {
        lock (Sync)
        {
            if (Cache.TryGetValue(onAc, out var cached)) return cached;
            var (limits, cacheable) = Detect(onAc);
            // Revisão: só guarda resultado definitivo (detectou, ou aparelho
            // sabidamente sem limite conhecido). Falha passageira — WMI da
            // Lenovo ainda não pronta logo depois do boot — tenta de novo na
            // próxima aplicação em vez de ficar em 30 W a sessão inteira.
            if (cacheable) Cache[onAc] = limits;
            DebugLog.Write($"[TDP-LIMITES] {(onAc ? "Tomada" : "Bateria")}: SPL {limits.SplMin}–{limits.SplMax} W, SPPT até {limits.SpptMax} W, FPPT até {limits.FpptMax} W — fonte: {limits.Source}.");
            return limits;
        }
    }

    private static (TdpLimits Limits, bool Cacheable) Detect(bool onAc)
    {
        if (!OperatingSystem.IsWindows()) return (TdpLimits.Fallback(onAc), true);
        try
        {
            var dmi = ReadDmi();
            if (dmi is null) return (TdpLimits.Fallback(onAc), false); // DMI falhou: tenta de novo depois
            var (manufacturer, board, productVersion) = dmi.Value;

            // Só a família Legion Go (Go, Go 2, Go S): a interface da Lenovo
            // também existe em notebooks Legion, que não são o alvo aqui.
            // Mesmo critério do driver do Linux (DMI_PRODUCT_VERSION "Legion Go…").
            if (manufacturer.Contains("LENOVO", StringComparison.OrdinalIgnoreCase)
                && productVersion.Contains("Legion Go", StringComparison.OrdinalIgnoreCase))
            {
                var lenovo = TryReadLenovoFirmware(onAc);
                if (lenovo is not null) return (lenovo, true);
                return (TdpLimits.Fallback(onAc), false); // firmware não respondeu agora: não guarda
            }

            if (manufacturer.Contains("ASUS", StringComparison.OrdinalIgnoreCase))
            {
                // RC71L = ROG Ally (Z1/Z1 Extreme); RC72LA = ROG Ally X.
                if (board.StartsWith("RC71", StringComparison.OrdinalIgnoreCase)
                    || board.StartsWith("RC72", StringComparison.OrdinalIgnoreCase))
                {
                    var model = board.StartsWith("RC72", StringComparison.OrdinalIgnoreCase) ? "ROG Ally X" : "ROG Ally";
                    return (onAc
                        ? new TdpLimits(7, 30, 43, 53, onAc, $"tabela {model} ({board}), tomada")
                        : new TdpLimits(7, 25, 30, 35, onAc, $"tabela {model} ({board}), bateria"), true);
                }
            }

            DebugLog.Write($"[TDP-LIMITES] Aparelho sem limite conhecido (fabricante='{manufacturer}', placa='{board}', versão='{productVersion}') — usando 5–30 W.");
            return (TdpLimits.Fallback(onAc), true);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[TDP-LIMITES] Falha ao detectar limites: {ex.Message} — usando 5–30 W por agora.");
            return (TdpLimits.Fallback(onAc), false);
        }
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

    /// <summary>Fabricante, placa e versão do produto. Null se a leitura falhar (não guarda falha).</summary>
    private static (string Manufacturer, string BoardProduct, string ProductVersion)? ReadDmi()
    {
        if (_dmi is { } known) return known;
        string manufacturer = string.Empty, board = string.Empty, version = string.Empty;
        try
        {
            using var cs = new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_ComputerSystem");
            using var csResults = cs.Get();
            foreach (ManagementObject o in csResults) using (o) { manufacturer = Convert.ToString(o["Manufacturer"])?.Trim() ?? string.Empty; break; }
            using var bb = new ManagementObjectSearcher("SELECT Product FROM Win32_BaseBoard");
            using var bbResults = bb.Get();
            foreach (ManagementObject o in bbResults) using (o) { board = Convert.ToString(o["Product"])?.Trim() ?? string.Empty; break; }
            // Lenovo guarda o nome comercial aqui ("Legion Go 2 8AHP2", "Legion Go S 8ARP1").
            using var csp = new ManagementObjectSearcher("SELECT Version FROM Win32_ComputerSystemProduct");
            using var cspResults = csp.Get();
            foreach (ManagementObject o in cspResults) using (o) { version = Convert.ToString(o["Version"])?.Trim() ?? string.Empty; break; }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[TDP-LIMITES] Falha ao ler DMI: {ex.Message}");
            return null;
        }
        if (string.IsNullOrEmpty(manufacturer)) return null;
        _dmi = (manufacturer, board, version);
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
            // 128 = sem bateria (conta como tomada). 255 = DESCONHECIDO — também
            // tem o bit 128 ligado, então precisa ser excluído antes (revisão:
            // logo depois do boot o Ally na bateria recebia o teto da tomada).
            if (status.BatteryFlag != 255 && (status.BatteryFlag & 128) != 0) return true;
            return status.ACLineStatus == 1;
        }
        catch { return false; }
    }
}
