using System;
using System.Runtime.Intrinsics.X86;
using System.Threading;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// TDP access through the official PawnIO RyzenSMU module.
/// This provider is deliberately limited to AMD Hawk Point (family 19h, model 7Ch)
/// until other CPU families have separately validated command mappings.
/// </summary>
public sealed class RyzenSmuService : IDisposable
{
    private const string SmuFunction = "ioctl_send_smu_command";
    private const string PciMutexName = @"Global\Access_PCI";

    // RyzenSMU.p uses MP1 commands for Hawk Point / Mobile family.
    private const ulong HawkPointCodeName = 30; // CPU_HawkPoint in the official RyzenSMU.p enum\n    private const ulong SetStapmCommand = 0x14;
    private const ulong SetFastPptCommand = 0x15;
    private const ulong SetSlowPptCommand = 0x16;

    private readonly PawnIOWrapper _io = new();
    private bool _initialized;
    private bool _disposed;

    public bool IsInitialized => _initialized;

    public static bool IsSupportedCpu()
    {
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
            uint model = (baseFamily == 0x6 || baseFamily == 0xF)
                ? (extendedModel << 4) | baseModel
                : baseModel;

            // Ryzen Z2 (non-Extreme) is Hawk Point: family 19h, model 7Ch.
            return family == 0x19 && model == 0x7C;
        }
        catch
        {
            return false;
        }
    }

    public bool Initialize(byte[] moduleData)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) return true;

        // Never load or execute SMU commands on an unverified CPU family.
        if (!IsSupportedCpu())
        {
            DebugLog.Write("[PawnIO] CPU não identificado como AMD Hawk Point (19h/7Ch); TDP PawnIO desabilitado.");
            return false;
        }

        if (moduleData is null || moduleData.Length == 0)
        {
            DebugLog.Write("[PawnIO] Módulo RyzenSMU.bin ausente ou vazio.");
            return false;
        }

        var connect = _io.Connect();
        if (connect != PawnIOWrapper.ConnectResult.OK)
        {
            DebugLog.Write($"[PawnIO] Driver indisponível: {connect}.");
            return false;
        }

        if (!_io.LoadModule(moduleData))
        {
            DebugLog.Write("[PawnIO] O driver recusou o módulo RyzenSMU.");
            return false;
        }

        // The official module performs its own CPU/PCI validation during load.
        // Query its codename as a second guard before enabling any write command.
        ulong[] codeName = new ulong[1];
        if (!_io.Execute("ioctl_get_code_name", null, codeName) || codeName[0] != HawkPointCodeName)
        {
            DebugLog.Write($"[PawnIO] Codename retornado pelo módulo não é Hawk Point (valor={codeName[0]}). TDP desabilitado.");
            return false;
        }

        _initialized = true;
        DebugLog.Write("[PawnIO] Módulo RyzenSMU carregado e confirmou CPU_HawkPoint (código 30).");
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

            bool stapm = SendCommand(SetStapmCommand, limitMilliwatts);
            bool fast = SendCommand(SetFastPptCommand, limitMilliwatts);
            bool slow = SendCommand(SetSlowPptCommand, limitMilliwatts);

            DebugLog.Write($"[PawnIO] Limites SMU: STAPM={stapm}, FastPPT={fast}, SlowPPT={slow}, valor={limitMilliwatts}mW.");
            return stapm && fast && slow;
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
