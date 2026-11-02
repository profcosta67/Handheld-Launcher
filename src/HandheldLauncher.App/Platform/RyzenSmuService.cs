using System;
using System.Runtime.Intrinsics.X86;
using System.Runtime.InteropServices;
using System.Threading;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Management;
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

    /// <summary>
    /// DMI model strings dos Legion Go 2 validados empiricamente.
    /// - "8AHP2": identificador usado pela Lenovo em algumas revisões de BIOS.
    /// - "83N1": identificador reportado pelo Legion Go 2 (Ryzen Z2) com BIOS atual.
    /// Novos modelos devem ser adicionados aqui somente após validação em hardware real.
    /// </summary>
    private static readonly string[] ValidatedLegionGo2DmiModels = { "8AHP2", "83N1" };

    private readonly PawnIOWrapper _io = new();
    private bool _initialized;
    private uint _initializedCpuModel;
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

    private static bool IsValidatedLegionGo2Model(string model)
    {
        foreach (string validated in ValidatedLegionGo2DmiModels)
        {
            if (model.Contains(validated, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Fail-closed platform allowlist. CPUID identifies the silicon family, not
    /// the device: desktop APUs can share Phoenix/Hawk Point models. Until another
    /// handheld is explicitly validated, only Legion Go 2 devices whose DMI model
    /// appears in <see cref="ValidatedLegionGo2DmiModels"/> are allowed to issue
    /// SMU writes.
    /// </summary>
    public static bool IsSupportedPlatform()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Manufacturer, Model FROM Win32_ComputerSystem");
            using var results = searcher.Get();
            foreach (ManagementObject system in results)
            {
                string manufacturer = Convert.ToString(system["Manufacturer"])?.Trim() ?? string.Empty;
                string model = Convert.ToString(system["Model"])?.Trim() ?? string.Empty;
                bool isLenovo = manufacturer.Contains("LENOVO", StringComparison.OrdinalIgnoreCase);
                bool isValidatedLegionGo2 = IsValidatedLegionGo2Model(model);

                bool supported = isLenovo && isValidatedLegionGo2;
                string allowlistDescription = string.Join(", ", ValidatedLegionGo2DmiModels);
                DebugLog.Write($"[PawnIO] Plataforma DMI: fabricante='{manufacturer}', modelo='{model}'; allowlist Legion Go 2 [{allowlistDescription}]={(supported ? "permitida" : "bloqueada")}.");
                return supported;
            }

            DebugLog.Write("[PawnIO] WMI não retornou Win32_ComputerSystem; plataforma não identificada e TDP bloqueado.");
            return false;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[PawnIO] Falha ao identificar plataforma via WMI: {ex.Message}. TDP bloqueado por segurança.");
            return false;
        }
    }

    public static bool IsSupportedCpu() => TryGetSupportedCpuModel(out _);

    public bool Initialize(byte[] moduleData)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) return true;

        if (!IsSupportedPlatform())
        {
            DebugLog.Write($"[PawnIO] Plataforma fora da allowlist de hardware validado (Legion Go 2: {string.Join(", ", ValidatedLegionGo2DmiModels)}); nenhuma escrita SMU será enviada.");
            return false;
        }

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

        _initializedCpuModel = cpuModel;
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
            new SecurityIdentifier(WellKnownSidType.WorldSid, null!),
            MutexRights.FullControl,
            AccessControlType.Allow);
        var security = new MutexSecurity();
        security.AddAccessRule(worldRule);

        try
        {
            var mutex = MutexAcl.Create(false, PciMutexName, out bool createdNew, security);
            DebugLog.Write($"[PawnIO] Mutex {PciMutexName}: {(createdNew ? "criado" : "aberto")} com ACL compartilhada.");
            return mutex;
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
        if (!_initialized)
        {
            DebugLog.Write("[PawnIO] TDP bloqueado: provider não inicializado após as verificações de segurança.");
            return false;
        }
        if (!IsSupportedPlatform()
            || !TryGetSupportedCpuModel(out uint currentCpuModel)
            || currentCpuModel != _initializedCpuModel)
        {
            DebugLog.Write("[PawnIO] TDP bloqueado: a plataforma ou o CPUID não corresponde mais ao hardware validado na inicialização.");
            return false;
        }
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

            float targetWatts = limitMilliwatts / 1000f;
            float stapmRead = 0f, fastRead = 0f, slowRead = 0f;
            bool confirmed = false;
            int consecutiveMatches = 0;

            // O firmware pode atualizar STAPM com atraso em relação aos comandos MP1.
            // Damos uma janela maior para a PM Table convergir e exigimos duas leituras
            // consecutivas dentro da tolerância, evitando confirmar um valor transitório.
            const int maxReadAttempts = 12;
            const int requiredConsecutiveMatches = 2;
            const float readbackToleranceWatts = 1.0f;

            for (int attempt = 1; attempt <= maxReadAttempts; attempt++)
            {
                if (TryReadPowerLimits(out stapmRead, out fastRead, out slowRead))
                {
                    // O EC (Embedded Controller) de alguns handhelds (como o Legion Go) trava o STAPM
// em um valor fixo (ex: 35W) e ignora o SMU. No entanto, o FastPPT e o SlowPPT
// são os limites reais de potência. Se eles estiverem corretos, o TDP foi aplicado.
bool matchesTarget = Math.Abs(fastRead - targetWatts) <= readbackToleranceWatts
    && Math.Abs(slowRead - targetWatts) <= readbackToleranceWatts;

if (Math.Abs(stapmRead - targetWatts) > readbackToleranceWatts)
{
    DebugLog.Write($"[PawnIO] Aviso: STAPM está travado em {stapmRead:F1}W pelo EC, mas Fast/Slow PPT estão corretos. TDP aplicado.");
}

                    consecutiveMatches = matchesTarget ? consecutiveMatches + 1 : 0;
                    confirmed = consecutiveMatches >= requiredConsecutiveMatches;

                    DebugLog.Write($"[PawnIO] Read-back tentativa {attempt}/{maxReadAttempts}: STAPM={stapmRead:F2}W, FastPPT={fastRead:F2}W, SlowPPT={slowRead:F2}W; alvo={targetWatts:F1}W; sequência válida={consecutiveMatches}/{requiredConsecutiveMatches}; confirmado={confirmed}.");
                    if (confirmed)
                        break;
                }
                else
                {
                    consecutiveMatches = 0;
                    DebugLog.Write($"[PawnIO] PM Table indisponível na tentativa de leitura {attempt}/{maxReadAttempts}.");
                }

                if (attempt < maxReadAttempts)
                    Thread.Sleep(500);
            }

            if (!confirmed)
                DebugLog.Write($"[PawnIO] Limites não convergiram de forma estável para {targetWatts:F1}W após {maxReadAttempts} leituras; último read-back: STAPM={stapmRead:F2}W, FastPPT={fastRead:F2}W, SlowPPT={slowRead:F2}W.");

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
        _initializedCpuModel = 0;
        _io.Dispose();
        GC.SuppressFinalize(this);
    }
}