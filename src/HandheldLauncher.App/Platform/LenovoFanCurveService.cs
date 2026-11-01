using System.Management;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Lenovo Legion Go Fan Method WMI interface (GUID 92549549-4BDE-4F06-AC04-CE8BF898DBAA).
/// Only enables writes on the explicitly supported Legion Go 8AHP2 model.
/// The firmware table is always read before a complete-table write.
/// </summary>
public sealed class LenovoFanCurveService : IDisposable
{
    private const string Namespace = @"root\WMI";
    private const string Query = "SELECT * FROM LENOVO_FAN_METHOD";
    private const int PointCount = 10;
    private static readonly int[] Temperatures = [10,20,30,40,50,60,70,80,90,100];
    private readonly object _sync = new();
    private int[]? _original;
    private bool _hasWrittenCurve;
    private bool _disposed;

    public bool IsSupportedModel
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\CIMV2", "SELECT Manufacturer FROM Win32_ComputerSystem");
                foreach (ManagementObject item in searcher.Get())
                {
                    using (item)
                    {
                        var manufacturer = item["Manufacturer"]?.ToString() ?? string.Empty;
                        return manufacturer.Contains("Lenovo", StringComparison.OrdinalIgnoreCase)
                            && GetProductVersion()?.Equals("Legion Go 8AHP2", StringComparison.OrdinalIgnoreCase) == true;
                    }
                }
            }
            catch (Exception ex) { DebugLog.Write($"[FAN] Falha ao validar DMI: {ex.Message}"); }
            return false;
        }
    }

    public static string? GetProductVersion()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2", "SELECT Version FROM Win32_ComputerSystemProduct");
            foreach (ManagementObject item in searcher.Get())
            {
                try { return item["Version"]?.ToString(); }
                finally { item.Dispose(); }
            }
        }
        catch (Exception ex) { DebugLog.Write($"[FAN] Não foi possível identificar o modelo: {ex.Message}"); }
        return null;
    }

    public bool TryRead(out int[] curve, out string message)
    {
        lock (_sync)
        {
            curve = [];
            if (!IsSupportedModel) { message = "Disponível somente no Legion Go 2 (8AHP2)."; return false; }
            try
            {
                var result = InvokeGet();
                if (result is null) { message = "A interface Fan Method não está disponível."; return false; }
                curve = result;
                _original ??= (int[])result.Clone();
                message = "Curva original lida com sucesso.";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Falha ao ler a curva: {ex.GetBaseException().Message}";
                DebugLog.Write("[FAN] " + message);
                return false;
            }
        }
    }

    public bool TryApplyPreset(string mode, out string message)
    {
        lock (_sync)
        {
            if (!IsSupportedModel) { message = "Modelo não suportado."; return false; }
            try
            {
                var current = InvokeGet();
                if (current is null) { message = "Fan Method indisponível."; return false; }
                _original ??= (int[])current.Clone();
                var target = BuildPreset(mode, current);
                if (!InvokeSet(target))
                {
                    try { InvokeSet(current); } catch { }
                    message = "O firmware recusou a curva; tabela anterior reenviada.";
                    return false;
                }
                var verify = InvokeGet();
                if (verify is null || !verify.SequenceEqual(target))
                {
                    // Revert to the exact table read immediately before this write.
                    InvokeSet(current);
                    message = "A leitura de confirmação divergiu; curva anterior restaurada.";
                    DebugLog.Write("[FAN] Readback divergente; curva anterior restaurada.");
                    return false;
                }
                _hasWrittenCurve = true;
                message = $"Curva {mode} aplicada e confirmada pelo firmware.";
                DebugLog.Write($"[FAN] Perfil {mode} aplicado; pontos: {string.Join(",", verify)}.");
                return true;
            }
            catch (Exception ex)
            {
                message = $"Falha ao aplicar curva: {ex.GetBaseException().Message}";
                DebugLog.Write("[FAN] " + message);
                return false;
            }
        }
    }

    public void RestoreOriginal()
    {
        lock (_sync)
        {
            if (_disposed || !_hasWrittenCurve || _original is null || !IsSupportedModel) return;
            try
            {
                var current = InvokeGet();
                if (current is null) return;
                if (current.SequenceEqual(_original))
                {
                    _original = null;
                    _hasWrittenCurve = false;
                    return;
                }
                if (InvokeSet(_original))
                {
                    var verify = InvokeGet();
                    if (verify is not null && verify.SequenceEqual(_original))
                    {
                        DebugLog.Write("[FAN] Curva original restaurada e confirmada.");
                        _original = null;
                        _hasWrittenCurve = false;
                    }
                    else DebugLog.Write("[FAN] Restauração solicitada, mas leitura de confirmação divergiu; backup mantido.");
                }
                else DebugLog.Write("[FAN] Firmware recusou a restauração da curva original; backup mantido.");
            }
            catch (Exception ex) { DebugLog.Write($"[FAN] Falha ao restaurar curva original: {ex.GetBaseException().Message}; backup mantido."); }
        }
    }

    private int[]? InvokeGet()
    {
        using var searcher = new ManagementObjectSearcher(Namespace, Query);
        foreach (ManagementObject item in searcher.Get())
        {
            using (item)
            using (var input = item.GetMethodParameters("Fan_Get_Table"))
            {
                input["FanID"] = (byte)1;
                input["SensorID"] = (byte)1;
                using var output = item.InvokeMethod("Fan_Get_Table", input, null);
                if (output?["FanTable"] is not uint[] data || data.Length < 22)
                    throw new InvalidDataException("Resposta Fan_Get_Table inválida (esperados 22 DWORDs).");
                int speedCount = checked((int)data[0]);
                int tempCount = checked((int)data[11]);
                if (speedCount != PointCount || tempCount != PointCount)
                    throw new InvalidDataException($"Tabela inesperada: {speedCount} pontos de velocidade e {tempCount} temperaturas.");
                var speeds = new int[PointCount];
                for (int i = 0; i < PointCount; i++)
                {
                    if (data[1 + i] > 115)
                        throw new InvalidDataException($"Valor de controle fora da faixa validada no Legion Go: {data[1+i]}.");
                    if (data[12 + i] != Temperatures[i])
                        throw new InvalidDataException("A tabela de temperaturas não corresponde aos dez pontos conhecidos.");
                    speeds[i] = (int)data[1 + i];
                }
                return speeds;
            }
        }
        return null;
    }

    private bool InvokeSet(int[] speeds)
    {
        if (speeds.Length != PointCount || speeds.Any(v => v is < 0 or > 115))
            throw new ArgumentOutOfRangeException(nameof(speeds), "A curva deve conter dez valores entre 0 e 115.");
        using var searcher = new ManagementObjectSearcher(Namespace, Query);
        foreach (ManagementObject item in searcher.Get())
        {
            using (item)
            using (var input = item.GetMethodParameters("Fan_Set_Table"))
            {
                var payload = new byte[64];
                payload[0] = 0xff;
                payload[1] = 0x01;
                WriteUInt32(payload, 2, PointCount);
                for (int i = 0; i < PointCount; i++) WriteUInt16(payload, 6 + i * 2, speeds[i]);
                payload[26] = 1; // temperature type
                WriteUInt32(payload, 27, PointCount);
                for (int i = 0; i < PointCount; i++) WriteUInt16(payload, 31 + i * 2, Temperatures[i]);
                payload[51] = 0x5a;
                WriteUInt16(payload, 53, 100);
                input["FanTable"] = payload;
                using var output = item.InvokeMethod("Fan_Set_Table", input, null);
                if (output is null) return false;
                if (output.Properties["ReturnValue"]?.Value is not null
                    && Convert.ToUInt32(output["ReturnValue"]) != 0)
                    throw new InvalidOperationException($"Fan_Set_Table retornou código {output["ReturnValue"]}.");
                return true;
            }
        }
        return false;
    }

    private static int[] BuildPreset(string mode, int[] current)
    {
        // Conservative initial curves, expressed in Lenovo's firmware control units.
        // These are deliberately capped at 100 until RPM/thermal response is measured.
        int[] values = mode switch
        {
            "Silencioso" => [0,0,0,0,8,15,25,38,55,70],
            "Normal" => [0,0,0,5,15,28,42,58,75,90],
            "Desempenho" => [0,0,5,15,30,48,65,80,92,100],
            "Boost" => [0,5,15,30,50,70,85,95,100,100],
            _ => throw new ArgumentException("O modo Automático não grava uma curva.", nameof(mode))
        };
        // Never return a less aggressive curve than the current firmware's safety curve.
        for (int i = 0; i < PointCount; i++)
        {
            values[i] = Math.Max(values[i], current[i]);
            if (i > 0) values[i] = Math.Max(values[i], values[i - 1]);
        }
        return values;
    }

    private static void WriteUInt16(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value & 0xff);
        target[offset + 1] = (byte)((value >> 8) & 0xff);
    }

    private static void WriteUInt32(byte[] target, int offset, int value)
    {
        for (int i = 0; i < 4; i++) target[offset + i] = (byte)((value >> (8 * i)) & 0xff);
    }

    public void Dispose()
    {
        if (_disposed) return;
        RestoreOriginal();
        _disposed = true;
    }
}
