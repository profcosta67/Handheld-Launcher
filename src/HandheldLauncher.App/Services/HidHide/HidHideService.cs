#if WINDOWS
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks; // Adicionado para Task.Run
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Platform;
using HandheldLauncher.Core.Services;
using Nefarius.Drivers.HidHide;

namespace HandheldLauncher.Services.HidHide;

public sealed class HidHideService : IHidHideService, IDisposable
{
    private readonly IHidHideControlService? _hidHideControl;
    private bool _isInitialized;

    /// <summary>v0.24.1: só avisa UMA vez por sessão que o mascaramento está fora.</summary>
    private bool _warnedUnavailable;

    public string? ControllerInstanceId { get; private set; }

    /// <summary>Estado real do mascaramento (o driver aceitou a configuração?).</summary>
    public bool IsReady => _isInitialized;

    /// <summary>Explicação curta do estado — vai para o log e para a Saúde do aparelho.</summary>
    public string StatusDetail { get; private set; } = "ainda não inicializado";

    public HidHideService(IHidHideControlService? hidHideControl = null)
    {
        _hidHideControl = hidHideControl;
    }

    public bool IsAvailable
    {
        get
        {
            if (_hidHideControl is null) return false;
            try
            {
                return _hidHideControl.IsInstalled && _hidHideControl.IsOperational;
            }
            catch
            {
                return false;
            }
        }
    }

    public void Initialize()
    {
        if (_hidHideControl is null)
        {
            StatusDetail = "serviço do driver indisponível";
            DebugLog.Write("[HIDHIDE] Sem serviço de controlo do driver — mascaramento desligado.");
            Report();
            return;
        }

        if (!IsAvailable)
        {
            StatusDetail = "driver do HidHide ausente ou não operacional";
            DebugLog.Write("[HIDHIDE] Driver ausente/não operacional — mascaramento desligado.");
            Report();
            return;
        }

        // v0.24.1: antes, sem controle encontrado isto saía em SILÊNCIO
        // (ControllerInstanceId null e nenhuma linha no log), o que tornava
        // impossível distinguir "sem admin" de "sem controle" a ler o log.
        // v0.24.2: são TODOS os controles detetados (Lenovo + Xbox/DualSense…).
        var controllerIds = HidHideTask.FindControllerInstanceIds();
        ControllerInstanceId = controllerIds.FirstOrDefault();
        if (controllerIds.Count == 0)
        {
            StatusDetail = "nenhum controle reconhecido";
            DebugLog.Write("[HIDHIDE] Nenhum controle reconhecido — mascaramento desligado.");
            Report();
            return;
        }

        try
        {
            var added = 0;
            foreach (var id in controllerIds)
            {
                if (_hidHideControl.BlockedInstanceIds.Contains(id)) continue;
                _hidHideControl.AddBlockedInstanceId(id);
                added++;
            }

            // MODO DENY-LIST: o controle é escondido apenas de quem está na lista (o jogo)
            _hidHideControl.IsAppListInverted = true;
            _hidHideControl.IsActive = true;
            _isInitialized = true;

            StatusDetail = $"{controllerIds.Count} controle(s) na blocklist ({added} novo(s)); driver ativo";

            DebugLog.Write($"[HIDHIDE] Inicializado: ids={string.Join(" | ", controllerIds)}, novos={added}, ativo={ReadBack(() => _hidHideControl.IsActive)}, invertida={ReadBack(() => _hidHideControl.IsAppListInverted)}.");
            Report();
        }
        catch (Exception ex)
        {
            // v0.24: antes era um catch vazio — sem administrador a escrita no
            // driver falha e o mascaramento ficava silenciosamente desligado.
            _isInitialized = false;
            StatusDetail = ElevationService.IsElevated
                ? $"driver recusou a configuração: {ex.GetType().Name}"
                : "sem permissão (corre sem administrador)";

            DebugLog.Write($"[HIDHIDE] Sem permissão para configurar o driver (elevado={ElevationService.IsElevated}): {ex.GetType().Name}: {ex.Message}");
            Report();
        }
    }

    // ==========================================
    // B5: Bloqueio por Jogo (Deny-List)
    // ==========================================
    
    public void HideControllerForProcess(int processId)
    {
        if (processId <= 0)
        {
            DebugLog.Write($"[HIDHIDE] Ocultação ignorada: pid inválido ({processId}).");
            return;
        }

        if (!_isInitialized)
        {
            UseElevatedTask(hide: true, processId);
            return;
        }

        // Roda em background para NÃO travar a interface do Overlay
        Task.Run(() =>
        {
            try
            {
                var processPath = GetProcessPath(processId);
                if (string.IsNullOrEmpty(processPath))
                {
                    DebugLog.Write($"[HIDHIDE] Ocultação abortada: sem caminho para o pid {processId}.");
                    return;
                }

                _hidHideControl!.AddApplicationPath(processPath, throwIfInvalid: false);
                DebugLog.Write($"[HIDHIDE] Controle OCULTADO para o jogo: {processPath}");
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[HIDHIDE] Erro ao esconder controle: {ex.GetType().Name}: {ex.Message}");
            }
        });
    }

    public void RestoreControllerForProcess(int processId)
    {
        if (processId <= 0)
        {
            DebugLog.Write($"[HIDHIDE] Restauro ignorado: pid inválido ({processId}).");
            return;
        }

        if (!_isInitialized)
        {
            UseElevatedTask(hide: false, processId);
            return;
        }

        Task.Run(() =>
        {
            try
            {
                var processPath = GetProcessPath(processId);
                if (string.IsNullOrEmpty(processPath))
                {
                    DebugLog.Write($"[HIDHIDE] Restauro abortado: sem caminho para o pid {processId}.");
                    return;
                }

                _hidHideControl!.RemoveApplicationPath(processPath);
                DebugLog.Write($"[HIDHIDE] Controle RESTAURADO para o jogo: {processPath}");
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[HIDHIDE] Erro ao restaurar controle: {ex.GetType().Name}: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// v0.24.1 — caminho de recurso quando o driver não aceitou a configuração
    /// (quase sempre por falta de administrador). Só corre se o utilizador ligou
    /// "Pedir permissão para isolar" nas Configurações: um UAC a meio de um jogo
    /// não pode aparecer sem ele ter pedido. Quem não liga fica com uma linha no
    /// log (uma por sessão) e nada muda no aparelho.
    /// </summary>
    private void UseElevatedTask(bool hide, int processId)
    {
        if (!AppSettingsStore.Load().HidHideElevateOnDemand)
        {
            if (!_warnedUnavailable)
            {
                _warnedUnavailable = true;
                DebugLog.Write(
                    "[HIDHIDE] Mascaramento indisponível nesta sessão (" + StatusDetail + "). " +
                    "Abre o launcher pela tarefa de boot, usa \"Reiniciar como administrador\" nas Configurações, " +
                    "ou liga \"Pedir permissão para isolar\" para delegar ao processo elevado.");
            }
            return;
        }

        var task = hide ? "hidhide-hide" : "hidhide-restore";
        Task.Run(() =>
        {
            DebugLog.Write($"[HIDHIDE] A delegar '{task}' (pid={processId}) ao processo elevado — o UAC vai aparecer uma vez.");
            var ok = ElevationService.RunElevated(task, "--pid", processId.ToString());
            DebugLog.Write(ok
                ? $"[HIDHIDE] Tarefa elevada '{task}' concluída (pid={processId})."
                : $"[HIDHIDE] Tarefa elevada '{task}' NÃO concluída (pid={processId}) — UAC cancelado ou falhou.");
        });
    }

    public void ForceRestartController()
    {
        if (!OperatingSystem.IsWindows() || !ElevationService.IsElevated)
        {
            DebugLog.Write("[HIDHIDE] Reinício do gamepad ignorado: sem administrador.");
            return;
        }

        // Um reinício por vez: abrir e fechar o QS rápido não pode rodar dois pnputil juntos.
        if (System.Threading.Interlocked.CompareExchange(ref _restartInFlight, 1, 0) != 0)
        {
            DebugLog.Write("[HIDHIDE] Reinício do gamepad já em andamento — ignorado.");
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                // Só os gamepads (IG_00 / SYNTHETICGAMEPAD): reiniciar teclado,
                // rato ou touchpad da handheld seria pior do que o problema.
                var ids = HidHideTask.FindControllerInstanceIds()
                    .Where(id => id.Contains("IG_00", StringComparison.OrdinalIgnoreCase)
                              || id.Contains("SYNTHETICGAMEPAD", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var id in ids)
                {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo("pnputil.exe", $"/restart-device \"{id}\"")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                        };
                        using var proc = System.Diagnostics.Process.Start(psi);
                        if (proc is null) continue;
                        if (!proc.WaitForExit(5000))
                        {
                            try { proc.Kill(entireProcessTree: true); } catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); }
                        }
                        DebugLog.Write($"[HIDHIDE] Gamepad re-enumerado ({id}): exit={proc.ExitCode}.");
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Write($"[HIDHIDE] Falha ao re-enumerar {id}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[HIDHIDE] ForceRestartController falhou: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _restartInFlight, 0);
            }
        });
    }

    private int _restartInFlight;

    public void Dispose()
    {
        if (_hidHideControl != null && IsAvailable && _isInitialized)
        {
            try
            {
                _hidHideControl.IsActive = false;
                StatusDetail = "driver desativado na saída (segurança)";
                DebugLog.Write("[HIDHIDE] Mascaramento desativado no Dispose (segurança).");
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[HIDHIDE] Falha ao desativar o mascaramento no Dispose: {ex.Message}");
            }

            _isInitialized = false;
        }
    }

    // ==========================================
    // Helpers (Síncronos, rodam no boot)
    // ==========================================

    /// <summary>Põe o estado na "Restauração da última sessão" (Central de Ajuda).</summary>
    private void Report()
        => StartupRestoreStatus.Report("Controle isolado (HidHide)", StatusDetail, _isInitialized);

    /// <summary>Leitura tolerante a falhas do estado do driver (nunca derruba o arranque).</summary>
    private static string ReadBack<T>(Func<T> read)
    {
        try { return read()?.ToString() ?? "null"; }
        catch (Exception ex)
        {
            DebugLog.Swallowed(ex);
            return "erro";
        }
    }

    private string? GetProcessPath(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.MainModule?.FileName;
        }
        catch { return null; }
    }
}
#endif
