using System;
using System.Runtime.Intrinsics.X86;
using System.Runtime.InteropServices;
using System.Threading;
using System.Security.AccessControl;
using System.Security.Principal;
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
    // PawnIO documents the kernel mutant as \BaseNamedObjects\Access_PCI.
    // On Windows/.NET, the corresponding cross-session name is Global\Access_PCI.
    private const string PciMutexName = @"Global\Access_PCI";

    private const ulong PhoenixCodeName = 23;
    private const ulong HawkPointCodeName = 30;

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

        ulong[] codeName = new ulong[1];
        if (!_io.Execute("ioctl_get_code_name", null, codeName))
        {
            DebugLog.Write("[PawnIO] Falha ao consultar o codename retornado pelo módulo RyzenSMU.");
            return false;
        }

        string expectedName = cpuModel == 0x75 ? "CPU_Phoenix (23)" : "CPU_HawkPoint (30)";
        DebugLog.Write($"[PawnIO] Identificação validada: CPUID model=0x{cpuModel:X}; codename módulo={codeName[0]}; esperado={expectedName}.");

        if (codeName[0] != expectedCodeName)
        {
            DebugLog.Write($"[PawnIO] Codename incompatível: recebido={codeName[0]}, esperado={expectedCodeName}. TDP desabilitado.");
            return false;
        }

        _initialized = true;
        DebugLog.Write(cpuModel == 0x75
            ? "[PawnIO] Módulo RyzenSMU carregado e confirmou CPU_Phoenix (CPUID 19h/75h, enum 23)."
            : "[PawnIO] Módulo RyzenSMU carregado e confirmou CPU_HawkPoint (CPUID 19h/7Ch, enum 30).");
        return true;
    }

    private static Mutex OpenOrCreatePciMutex()
    {
        // Match LibreHardwareMonitor's shared PCI lock convention: Global namespace
        // and an ACL that permits hardware-monitoring tools to coordinate with us.
        var worldRule = new MutexAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            MutexRights.FullControl,
            AccessControlType.Allow);
        var security = new MutexSecurity();
        security.AddAccessRule(worldRule);

        try
        {
            return MutexAcl.Create(false, PciMutexName, out _, security);
        }
        catch (UnauthorizedAccessException)
        {
            // An existing mutex may have a stricter ACL; open it with current rights.
            return Mutex.OpenExisting(PciMutexName);
        }
    }

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
            pciMutex = OpenOrCreatePciMutex();
            acquired = pciMutex.WaitOne(TimeSpan.FromSeconds(5));
            if (!acquired)
            {
                DebugLog.Write("[PawnIO] Timeout aguardando mutex Global\\Access_PCI.");
                return false;
            }

            bool stapmCommand = SendMp1Command(SetStapmCommand, limitMilliwatts);
            bool fastCommand = SendMp1Command(SetFastPptCommand, limitMilliwatts);
            bool slowCommand = SendMp1Command(SetSlowPptCommand, limitMilliwatts);

            DebugLog.Write($"[PawnIO] MP1 commands: STAPM={stapmCommand}, FastPPT={fastCommand}, SlowPPT={slowCommand}, alvo={limitMilliwatts}mW.");
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
            DebugLog.Write("[PawnIO] Não foi possível abrir/criar o mutex Global\\Access_PCI; comandos SMU cancelados por segurança.");
            return false;
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
            DebugLog.Write("[PawnIO] Mutex Global\\Access_PCI abandonado; propriedade recuperada, mas comandos cancelados nesta tentativa.");
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

    private bool TryReadPowerLimits(out float stapmWatts, out float fastWatts, out float slowWatts)
    {
        stapmWatts = fastWatts = slowWatts = 0f;

        ulong[] resolved = new ulong[2];
        if (!_io.Execute("ioctl_resolve_pm_table", null, resolved) || resolved[1] == 0)
            return false;
        uint tableVersion = (uint)resolved[0];
        DebugLog.Write($"[PawnIO] PM Table: versão=0x{tableVersion:X6}, base=0x{resolved[1]:X}.");

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
        DebugLog.Write(
            $"[PawnIO] PM Table dump (float indexes): [0]={values[0]:F2}, [1]={values[1]:F2}, " +
            $"[2]={values[2]:F2}, [3]={values[3]:F2}, [4]={values[4]:F2}, [5]={values[5]:F2}, " +
            $"[6]={values[6]:F2}, [7]={values[7]:F2}, [8]={values[8]:F2}, [9]={values[9]:F2}, " +
            $"[10]={values[10]:F2}, [11]={values[11]:F2}, [12]={values[12]:F2}, [13]={values[13]:F2}, " +
            $"[14]={values[14]:F2}, [15]={values[15]:F2}.");
        DebugLog.Write(
            $"[PawnIO] PM Table limites interpretados: STAPM@0x00={stapmWatts:F2}W, " +
            $"FastPPT@0x08={fastWatts:F2}W, SlowPPT@0x10={slowWatts:F2}W; " +
            $"tableVersion=0x{tableVersion:X6}, tableBase=0x{resolved[1]:X}.");

        return float.IsFinite(stapmWatts) && float.IsFinite(fastWatts)
            && float.IsFinite(slowWatts) && stapmWatts > 0f
            && fastWatts > 0f && slowWatts > 0f;
    }

    private bool SendMp1Command(ulong commandId, uint valueMilliwatts)
    {
        const ulong commandAddress = 0x03B10528;
        const ulong responseAddress = 0x03B10578;
        const ulong argumentAddress = 0x03B10998;

        if (!WaitForMp1Mailbox(responseAddress))
        {
            DebugLog.Write($"[PawnIO] MP1 mailbox ocupado antes do comando 0x{commandId:X2}.");
            return false;
        }

        if (!_io.Execute("ioctl_write_smu_register", new[] { responseAddress, 0UL }, null))
            return false;

        for (int i = 0; i < 6; i++)
        {
            ulong value = i == 0 ? valueMilliwatts : 0UL;
            if (!_io.Execute("ioctl_write_smu_register",
                new[] { argumentAddress + (ulong)(i * 4), value }, null))
                return false;
        }

        if (!_io.Execute("ioctl_write_smu_register",
            new[] { commandAddress, commandId }, null))
            return false;

        var timer = System.Diagnostics.Stopwatch.StartNew();
        ulong status = 0;
        while (timer.ElapsedMilliseconds < 200)
        {
            ulong[] result = new ulong[1];
            if (!_io.Execute("ioctl_read_smu_register", new[] { responseAddress }, result))
                return false;
            status = result[0] & 0xFF;
            if (status != 0)
                break;
            Thread.Sleep(1);
        }

        if (status != 0x01)
        {
            DebugLog.Write($"[PawnIO] MP1 comando 0x{commandId:X2} retornou status 0x{status:X2}.");
            return false;
        }

        return true;
    }

    private bool WaitForMp1Mailbox(ulong responseAddress)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 200)
        {
            ulong[] result = new ulong[1];
            if (!_io.Execute("ioctl_read_smu_register", new[] { responseAddress }, result))
                return false;
            if ((result[0] & 0xFF) != 0)
                return true;
            Thread.Sleep(1);
        }
        return false;
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
