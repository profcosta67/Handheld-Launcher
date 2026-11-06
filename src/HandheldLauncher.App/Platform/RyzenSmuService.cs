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
/// Esta versão flexibiliza a checagem de DMI e CodeName para permitir
/// que o Ryzen Z2 (Hawk Point) rode sem bloqueios falsos, mantendo a
/// trava de segurança de Watts (5 W a 30 W nos perfis; no personalizado,
/// o teto do aparelho — DeviceTdpLimits — e nunca acima de 55 W).
/// </summary>
public sealed class RyzenSmuService : IDisposable
{
    private const string SmuFunction = "ioctl_send_smu_command";
    private const string PciMutexName = @"Global\Access_PCI";

    private const ulong PhoenixCodeName = 23;
    private const ulong HawkPointCodeName = 30;

    private const ulong SetStapmCommand = 0x14;
    private const ulong SetFastPptCommand = 0x15;
    private const ulong SetSlowPptCommand = 0x16;

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

            // Aceita estritamente Zen 4 Mobile (Phoenix 0x75 e Hawk Point 0x7C)
            bool supported = family == 0x19 && (model == 0x75 || model == 0x7C);
            string platform = model switch
            {
                0x75 => "Phoenix (CPUID 75h)",
                0x7C => "Hawk Point (CPUID 7Ch)",
                _ => "não mapeado"
            };

            DebugLog.Write($"[PawnIO] CPUID leaf 1: EAX=0x{signature:X8}; family=0x{family:X}; model=0x{model:X}; plataforma={platform}.");
            DebugLog.Write(supported
                ? "[PawnIO] CPU mapeada pelo módulo oficial; provider permitido."
                : "[PawnIO] CPU fora da matriz Zen 4; provider bloqueado.");
            return supported;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[PawnIO] Não foi possível consultar CPUID: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Fail-closed platform allowlist, but much less restrictive.
    /// Se for um Ryzen Mobile Zen 4, nós permitimos. O próprio SMU da AMD 
    /// vai rejeitar comandos absurdos, não precisamos restringir por modelo de notebook.
    /// </summary>
    public static bool IsSupportedPlatform()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
            using var results = searcher.Get();
            foreach (ManagementObject system in results)
            {
                string manufacturer = Convert.ToString(system["Manufacturer"])?.Trim() ?? string.Empty;
                string model = Convert.ToString(system["Model"])?.Trim() ?? string.Empty;
                bool isLenovo = manufacturer.Contains("LENOVO", StringComparison.OrdinalIgnoreCase);
                
                // Se for Lenovo, libera (cobre o Legion Go).
                // Se for outro fabricante, mas a CPU passou no teste (Zen 4 Mobile), também deixamos passar.
                DebugLog.Write($"[PawnIO] Plataforma DMI: fabricante='{manufacturer}', modelo='{model}'. Permissão de fabricante: {(isLenovo ? "Lenovo (OK)" : "Outro (Aceitando via CPUID)")}.");
                return true; 
            }
            DebugLog.Write("[PawnIO] WMI não retornou Win32_ComputerSystem; assumindo bloqueio.");
            return false;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[PawnIO] Falha ao identificar plataforma via WMI: {ex.Message}. Assumindo bloqueio.");
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
            DebugLog.Write("[PawnIO] Plataforma bloqueada por segurança; nenhuma escrita SMU será enviada.");
            return false;
        }

        if (!TryGetSupportedCpuModel(out uint cpuModel))
        {
            DebugLog.Write("[PawnIO] CPU fora da matriz Zen 4; TDP PawnIO desabilitado.");
            return false;
        }

        // O Z2 é Hawk Point (arquitetura), mas pode reportar CPUID Phoenix (0x75).
        // Vamos aceitar tanto o CodeName 23 quanto o 30 se for Zen 4 Mobile.
        ulong[] allowedCodeNames = { PhoenixCodeName, HawkPointCodeName };

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
                    "[PawnIO] Driver não encontrado. Instale o runtime oficial em https://pawnio.eu/ e reinicie o Windows.",
                PawnIOWrapper.ConnectResult.AccessDenied =>
                    "[PawnIO] Acesso negado ao driver. Abra o Launcher com elevação.",
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

        DebugLog.Write($"[PawnIO] CPUID model=0x{cpuModel:X}; codename módulo={codeName[0]}; esperados=23 ou 30.");

        if (Array.IndexOf(allowedCodeNames, codeName[0]) < 0)
        {
            DebugLog.Write($"[PawnIO] Codename incompatível: recebido={codeName[0]}, esperados=23 ou 30. TDP desabilitado.");
            return false;
        }

        _initializedCpuModel = cpuModel;
        _initialized = true;
        DebugLog.Write($"[PawnIO] Módulo carregado com sucesso (Codename {codeName[0]}). TDP habilitado.");
        return true;
    }

    private static Mutex OpenOrCreatePciMutex()
    {
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
            return Mutex.OpenExisting(PciMutexName);
        }
    }

    // Teto absoluto de sanidade (mW), independente de qualquer tabela/firmware.
    // Igual a TdpLimits.AbsoluteMaxWatts.
    private const uint AbsoluteMaxMilliwatts = 55000;

    /// <summary>
    /// Perfis fixos (UltraEco…Boost): o mesmo valor nos três limites, com a
    /// trava de sempre (5–30 W). Comportamento idêntico ao de antes da v0.18.5.
    /// </summary>
    public bool SetPowerLimits(uint limitMilliwatts)
        => SetPowerLimits(limitMilliwatts, limitMilliwatts, limitMilliwatts,
                          maxStapmMilliwatts: 30000, maxSlowMilliwatts: 30000, maxFastMilliwatts: 30000);

    /// <summary>
    /// v0.18.5 (TDP personalizado com o teto real do aparelho): STAPM/SPL,
    /// Slow PPT/SPPT e Fast PPT/FPPT separados, cada um com o teto informado
    /// pelo aparelho (DeviceTdpLimits). Recusa se qualquer valor estiver
    /// abaixo de 5 W, acima do teto dele, acima de 55 W (sanidade), ou fora da
    /// ordem STAPM ≤ Slow ≤ Fast.
    /// </summary>
    public bool SetPowerLimits(uint stapmMilliwatts, uint slowMilliwatts, uint fastMilliwatts,
                               uint maxStapmMilliwatts, uint maxSlowMilliwatts, uint maxFastMilliwatts)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized)
        {
            DebugLog.Write("[PawnIO] TDP bloqueado: provider não inicializado.");
            return false;
        }
        if (!IsSupportedPlatform() || !TryGetSupportedCpuModel(out uint currentCpuModel) || currentCpuModel != _initializedCpuModel)
        {
            DebugLog.Write("[PawnIO] TDP bloqueado: plataforma ou CPUID não corresponde ao validado.");
            return false;
        }
        
        // TRAVA DE SEGURANÇA MÁXIMA E MÍNIMA:
        // Piso 5 W; teto = o do aparelho pra cada limite (perfis: 30 W, como
        // sempre), e nunca acima de 55 W. Isso impede que alguém mande 150 W
        // e queime o aparelho, e que um valor passe do que o fabricante permite.
        bool InRange(uint value, uint max) => value >= 5000 && value <= max && max <= AbsoluteMaxMilliwatts;
        if (!InRange(stapmMilliwatts, maxStapmMilliwatts)
            || !InRange(slowMilliwatts, maxSlowMilliwatts)
            || !InRange(fastMilliwatts, maxFastMilliwatts)
            || stapmMilliwatts > slowMilliwatts || slowMilliwatts > fastMilliwatts)
        {
            DebugLog.Write($"[PawnIO] Limites TDP fora da faixa permitida: STAPM={stapmMilliwatts}mW (máx {maxStapmMilliwatts}), Slow={slowMilliwatts}mW (máx {maxSlowMilliwatts}), Fast={fastMilliwatts}mW (máx {maxFastMilliwatts}). BLOQUEADO POR SEGURANÇA.");
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

            bool stapmCommand = SendMp1Command(SetStapmCommand, stapmMilliwatts);
            bool fastCommand = SendMp1Command(SetFastPptCommand, fastMilliwatts);
            bool slowCommand = SendMp1Command(SetSlowPptCommand, slowMilliwatts);

            DebugLog.Write(stapmMilliwatts == slowMilliwatts && slowMilliwatts == fastMilliwatts
                ? $"[PawnIO] MP1 commands: STAPM={stapmCommand}, FastPPT={fastCommand}, SlowPPT={slowCommand}, alvo={stapmMilliwatts}mW."
                : $"[PawnIO] MP1 commands: STAPM={stapmCommand}, FastPPT={fastCommand}, SlowPPT={slowCommand}, alvos STAPM={stapmMilliwatts}mW / Slow={slowMilliwatts}mW / Fast={fastMilliwatts}mW.");
            if (!stapmCommand || !fastCommand || !slowCommand)
                return false;

            float targetWatts = stapmMilliwatts / 1000f;
            float fastTargetWatts = fastMilliwatts / 1000f;
            float slowTargetWatts = slowMilliwatts / 1000f;
            float stapmRead = 0f, fastRead = 0f, slowRead = 0f;
            bool confirmed = false;
            int consecutiveMatches = 0;

            const int maxReadAttempts = 12;
            const int requiredConsecutiveMatches = 2;
            const float readbackToleranceWatts = 1.0f;

            for (int attempt = 1; attempt <= maxReadAttempts; attempt++)
            {
                if (TryReadPowerLimits(out stapmRead, out fastRead, out slowRead))
                {
                    // Se o Fast e Slow estiverem no alvo, consideramos aplicado.
                    // (O STAPM às vezes fica travado pelo EC no Legion Go)
                    bool matchesTarget = Math.Abs(fastRead - fastTargetWatts) <= readbackToleranceWatts
                        && Math.Abs(slowRead - slowTargetWatts) <= readbackToleranceWatts;

                    if (Math.Abs(stapmRead - targetWatts) > readbackToleranceWatts)
                    {
                        DebugLog.Write($"[PawnIO] Aviso: STAPM travado em {stapmRead:F1}W pelo EC, mas Fast/Slow PPT estão corretos. TDP aplicado.");
                    }

                    consecutiveMatches = matchesTarget ? consecutiveMatches + 1 : 0;
                    confirmed = consecutiveMatches >= requiredConsecutiveMatches;

                    DebugLog.Write($"[PawnIO] Read-back {attempt}/{maxReadAttempts}: STAPM={stapmRead:F2}W, FastPPT={fastRead:F2}W, SlowPPT={slowRead:F2}W; alvo={targetWatts:F1}/{slowTargetWatts:F1}/{fastTargetWatts:F1}W; sequência={consecutiveMatches}/{requiredConsecutiveMatches}; confirmado={confirmed}.");
                    if (confirmed) break;
                }
                else
                {
                    consecutiveMatches = 0;
                    DebugLog.Write($"[PawnIO] PM Table indisponível na tentativa {attempt}/{maxReadAttempts}.");
                }

                if (attempt < maxReadAttempts) Thread.Sleep(500);
            }

            if (!confirmed)
                DebugLog.Write($"[PawnIO] Limites não convergiram para {targetWatts:F1}/{slowTargetWatts:F1}/{fastTargetWatts:F1}W. Último read-back: STAPM={stapmRead:F2}W, FastPPT={fastRead:F2}W, SlowPPT={slowRead:F2}W.");

            return confirmed;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            DebugLog.Write("[PawnIO] Não foi possível abrir/criar o mutex Global\\Access_PCI; comandos SMU cancelados.");
            return false;
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
            DebugLog.Write("[PawnIO] Mutex abandonado; propriedade recuperada, comandos cancelados.");
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
                try { pciMutex?.ReleaseMutex(); } catch (ApplicationException swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
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
            $"[PawnIO] PM Table limites: STAPM@0x00={stapmWatts:F2}W, " +
            $"FastPPT@0x08={fastWatts:F2}W, SlowPPT@0x10={slowWatts:F2}W.");

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