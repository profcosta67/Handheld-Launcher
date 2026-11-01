using System;
using System.Runtime.Intrinsics.X86;
using System.Runtime.InteropServices;
using System.Threading;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// TDP access through the official PawnIO RyzenSMU module.
/// This provider is limited to the two Family 19h models explicitly mapped by
/// PawnIO.Modules 0.2.11 for the Phoenix/Hawk Point mobile SMU path:
/// model 75h -> CPU_Phoenix, model 7Ch -> CPU_HawkPoint.
/// </summary>
public sealed class RyzenSmuService : IDisposable
{
    private const string SmuFunction = "ioctl_send_smu_command";
    private const string PciMutexName = @"Global\Access_PCI";

    // CodeName enum values from the pinned PawnIO.Modules 0.2.11 RyzenSMU.p:
    // CPU_Phoenix = 23, CPU_HawkPoint = 30.
    private const ulong PhoenixCodeName = 23;
    private const ulong HawkPointCodeName = 30;

    // MP1 commands for the FT6/FP7/FP8 mobile path used by Phoenix and Hawk Point.
    // Do NOT substitute 0x1A/0x1B/0x1C: those belong to the FT5/FP5/AM4 command table.
    private const ulong SetStapmCommand = 0x14;
    private const ulong SetFastPptCommand = 0x15;
    private const ulong SetSlowPptCommand = 0x16;

    private readonly PawnIOWrapper _io = new();
    private bool _initialized;
    private bool _disposed;

    public bool IsInitialized => _initialized;

    private static bool TryGetSupportedCpuModel(out uint model)
    {
        model = 0;
        if (!OperatingSystem.IsWindows() || !X86Base.IsSupported)
            return false;

        try
        {
            var (eax, _, _, _) = X86Base.CpuId(1, 0);
            uint signature = unchecked((uint)eax);
            uint baseFamily = (signature >> 8) & 0xF;
            uint extendedFamily = (signature >> 20) & 0xFF;
            uint baseModel = (signature >> 4) & 0xF;
            uint extendedModel = (signature >> 16) & 0xF;

            uint family = baseFamily == 0xF ? baseFamily + extendedFamily : baseFamily;
            model = (baseFamily == 0x6 || baseFamily == 0xF)
                ? (extendedModel << 4) | baseModel
                : baseModel;

            // The pinned PawnIO.Modules 0.2.11 RyzenSMU.p maps:
            // 0x1974/0x1975 -> CPU_Phoenix; 0x197C -> CPU_HawkPoint.
            bool supported = family == 0x19 && (model == 0x75 || model == 0x7C);
            string platform = model switch
            {
                0x75 => "Phoenix (PawnIO codename; CPUID model 75h)",
                0x7C => "Hawk Point (PawnIO codename; CPUID model 7Ch)",
                _ => "não mapeado"
            };

            DebugLog.Write($"[PawnIO] CPUID leaf 1: EAX=0x{signature:X8}; family=0x{family:X}; model=0x{model:X}; plataforma={platform}.");
            DebugLog.Write(supported
                ? "[PawnIO] CPU mapeada pelo módulo oficial; provider permitido para validação."
                : "[PawnIO] CPU fora da matriz Phoenix/Hawk Point validada; provider bloqueado.");
            return supported;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[PawnIO] Não foi possível consultar CPUID: {ex.Message}");
            return false;
        }
    }

    public static bool IsSupportedCpu() => TryGetSupportedCpuModel(out _);

    public bool Initialize(byte[] moduleData)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) return true;

        // Never load or execute SMU commands on an unverified CPU family/model.
        if (!TryGetSupportedCpuModel(out uint cpuModel))
        {
            DebugLog.Write("[PawnIO] CPU fora da matriz Phoenix/Hawk Point (19h/75h ou 19h/7Ch); TDP PawnIO desabilitado.");
            return false;
        }

        ulong expectedCodeName = cpuModel switch
        {
            0x75 => PhoenixCodeName,
            0x7C => HawkPointCodeName,
            _ => ulong.MaxValue
        };

        if (moduleData is null || moduleData.Length == 0)
        {
            DebugLog.Write("[PawnIO] Módulo RyzenSMU.bin ausente ou vazio.");
            return false;
        }

        var connect = _io.Connect();
        if (connect != PawnIOWrapper.ConnectResult.OK)
        {
            DebugLog.Write(connect switch
            {
                PawnIOWrapper.ConnectResult.NotInstalled =>
                    "[PawnIO] Driver não encontrado. Instale o runtime oficial em https://pawnio.eu/ e reinicie o Windows; o Launcher não instala drivers silenciosamente.",
                PawnIOWrapper.ConnectResult.AccessDenied =>
                    "[PawnIO] Acesso negado ao driver. Abra o Launcher com elevação e confira a instalação oficial do PawnIO.",
                _ => "[PawnIO] Não foi possível abrir o dispositivo do driver: " + connect + "."
            });
            return false;
        }

        if (!_io.LoadModule(moduleData))
        {
            DebugLog.Write("[PawnIO] O driver recusou o módulo RyzenSMU.");
            return false;
        }

        // The official module performs its own CPU/PCI validation during load.
        // Query its codename and require the exact value expected for this CPUID.
        ulong[] codeName = new ulong[1];
        if (!_io.Execute("ioctl_get_code_name", null, codeName) || codeName[0] != expectedCodeName)
        {
            string expectedName = cpuModel == 0x75 ? "CPU_Phoenix (23)" : "CPU_HawkPoint (30)";
            DebugLog.Write($"[PawnIO] Codename do módulo incompatível: recebido={codeName[0]}, esperado={expectedName}. TDP desabilitado.");
            return false;
        }

        _initialized = true;
        DebugLog.Write(cpuModel == 0x75
            ? "[PawnIO] Módulo RyzenSMU carregado e confirmou CPU_Phoenix (CPUID 19h/75h, enum 23)."
            : "[PawnIO] Módulo RyzenSMU carregado e confirmou CPU_HawkPoint (CPUID 19h/7Ch, enum 30).");
        return true;
    }

    /// <summary>
    /// Applies STAPM, Fast PPT and Slow PPT in milliwatts.
    /// The official module contract is 7 input values and 6 output values:
    /// [commandId, arg0..arg5] -> [result0..result5].
    /// </summary>
    public bool SetPowerLimits(uint limitMilliwatts)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized) return false;
        if (limitMilliwatts < 5000 || limitMilliwatts > 30000)
        {
            DebugLog.Write($"[PawnIO] Limite TDP fora da faixa permitida: {limitMilliwatts}mW.");
            return false;
        }

        Mutex? pciMutex = null;
        bool acquired = false;
        try
        {
            // RyzenSMU.p explicitly requires the shared Access_PCI mutant.
            pciMutex = Mutex.OpenExisting(PciMutexName);
            acquired = pciMutex.WaitOne(TimeSpan.FromSeconds(5));
            if (!acquired)
            {
                DebugLog.Write("[PawnIO] Timeout aguardando mutex Access_PCI.");
                return false;
            }

            bool stapmCommand = SendCommand(SetStapmCommand, limitMilliwatts);
            bool fastCommand = SendCommand(SetFastPptCommand, limitMilliwatts);
            bool slowCommand = SendCommand(SetSlowPptCommand, limitMilliwatts);

            DebugLog.Write($"[PawnIO] Comandos SMU: STAPM={stapmCommand}, FastPPT={fastCommand}, SlowPPT={slowCommand}, alvo={limitMilliwatts}mW.");
            if (!stapmCommand || !fastCommand || !slowCommand)
                return false;

            if (!TryReadPowerLimits(out float stapmRead, out float fastRead, out float slowRead))
            {
                DebugLog.Write("[PawnIO] Os comandos foram aceitos, mas não foi possível ler os limites de volta pela PM Table.");
                return false;
            }

            float targetWatts = limitMilliwatts / 1000f;
            bool confirmed = Math.Abs(stapmRead - targetWatts) <= 1.0f
                && Math.Abs(fastRead - targetWatts) <= 1.0f
                && Math.Abs(slowRead - targetWatts) <= 1.0f;

            DebugLog.Write($"[PawnIO] Read-back PM Table: STAPM={stapmRead:F1}W, FastPPT={fastRead:F1}W, SlowPPT={slowRead:F1}W; alvo={targetWatts:F1}W; confirmado={confirmed}.");
            return confirmed;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // Some PawnIO module/driver builds do not expose the named mutant to user mode.
            // Do not issue unsynchronized PCI/SMU writes in that case.
            DebugLog.Write("[PawnIO] Mutex Access_PCI não encontrado; comandos SMU cancelados por segurança.");
            return false;
        }
        catch (AbandonedMutexException)
        {
            // WaitOne grants ownership even when it reports an abandoned mutex.
            acquired = true;
            DebugLog.Write("[PawnIO] Mutex Access_PCI abandonado; comandos SMU cancelados nesta tentativa.");
            return false;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[PawnIO] Falha ao aplicar TDP: {ex.Message}");
            return false;
        }
        finally
        {
            if (acquired)
            {
                try { pciMutex?.ReleaseMutex(); }
                catch (ApplicationException) { }
            }
            pciMutex?.Dispose();
        }
    }

    // Called while Access_PCI is held. PM Table values are reported in watts;
    // STAPM/Fast/Slow are float entries 0, 2 and 4 in the 64-QWORD table window.
    private bool TryReadPowerLimits(out float stapmWatts, out float fastWatts, out float slowWatts)
    {
        stapmWatts = fastWatts = slowWatts = 0f;

        ulong[] resolved = new ulong[2];
        if (!_io.Execute("ioctl_resolve_pm_table", null, resolved))
            return false;

        // First update may return DEVICE_BUSY while the SMU finishes a previous
        // transfer. This follows the official G-Helper/PawnIO retry sequence.
        _io.Execute("ioctl_update_pm_table", null, null);
        Thread.Sleep(100);
        if (!_io.Execute("ioctl_update_pm_table", null, null))
            return false;
        Thread.Sleep(200);

        ulong[] words = new ulong[64];
        if (!_io.Execute("ioctl_read_pm_table", null, words))
            return false;

        Span<float> values = MemoryMarshal.Cast<ulong, float>(words);
        if (values.Length < 5)
            return false;

        stapmWatts = values[0];
        fastWatts = values[2];
        slowWatts = values[4];

        return float.IsFinite(stapmWatts) && float.IsFinite(fastWatts)
            && float.IsFinite(slowWatts) && stapmWatts > 0f
            && fastWatts > 0f && slowWatts > 0f;
    }

    private bool SendCommand(ulong commandId, uint valueMilliwatts)
    {
        // ioctl_send_smu_command is defined with exactly 7 inputs and 6 outputs.
        ulong[] input = { commandId, valueMilliwatts, 0, 0, 0, 0, 0 };
        ulong[] output = new ulong[6];
        return _io.Execute(SmuFunction, input, output);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _initialized = false;
        _io.Dispose();
        GC.SuppressFinalize(this);
    }
}
