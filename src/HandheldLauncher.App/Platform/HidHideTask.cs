using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.Core.Services;
using Nefarius.Drivers.HidHide;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Executa o mascaramento do controle (HidHide) numa instância ELEVADA do
/// launcher — as tarefas <c>--elevated-task hidhide-hide</c> e
/// <c>--elevated-task hidhide-restore</c> (ver <see cref="ElevationService"/>).
///
/// <para>v0.24.1: sem administrador, o <c>HidHideService</c> não consegue
/// escrever no driver — o <c>Initialize()</c> falhava e a ocultação ficava
/// desligada em silêncio. A escrita direta continua a ser o caminho normal
/// (quando o launcher já está elevado, sem UAC nenhum); este runner existe
/// para o caso "modo padrão", e só é chamado quando o utilizador liga
/// <c>HidHideElevateOnDemand</c> nas Configurações — nunca sozinho, para não
/// aparecer um UAC a meio de um jogo.</para>
/// </summary>
internal static class HidHideTask
{
    /// <summary>
    /// Esconde (ou devolve) o controle ao processo do jogo. Devolve 0 quando o
    /// driver ficou no estado pedido; qualquer outro código é falha (o
    /// chamador registra no debug.log e não inventa estado).
    /// </summary>
    public static int Apply(bool hide, int processId)
    {
        if (!OperatingSystem.IsWindows())
        {
            DebugLog.Write("[HIDHIDE-TASK] Ignorado: não é Windows.");
            return 1;
        }

        try
        {
            if (processId <= 0)
            {
                DebugLog.Write($"[HIDHIDE-TASK] pid inválido ({processId}) — nada a fazer.");
                return 1;
            }

            var control = new HidHideControlService();

            if (!control.IsInstalled)
            {
                DebugLog.Write("[HIDHIDE-TASK] Driver do HidHide não está instalado.");
                return 1;
            }

            if (!control.IsOperational)
            {
                DebugLog.Write("[HIDHIDE-TASK] Driver instalado mas não operacional (reboot pendente?).");
                return 1;
            }

            // Estado base do mascaramento: o(s) controle(s) entram na blocklist
            // uma única vez, a lista de aplicações fica em modo deny e o driver
            // tem de estar ativo. Idempotente de propósito — este runner pode
            // ser chamado a cada abertura do overlay.
            var instanceIds = FindControllerInstanceIds();
            if (instanceIds.Count == 0)
            {
                DebugLog.Write("[HIDHIDE-TASK] Nenhum controle encontrado — nada foi escondido.");
                return 1;
            }

            foreach (var instanceId in instanceIds)
            {
                if (control.BlockedInstanceIds.Contains(instanceId)) continue;
                control.AddBlockedInstanceId(instanceId);
                DebugLog.Write($"[HIDHIDE-TASK] Bloqueado no driver: {instanceId}");
            }

            control.IsAppListInverted = true;
            control.IsActive = true;

            // v0.24.1: ler de volta o que o driver aceitou. Sem isto, "a escrita
            // não deu exceção" era confundido com "o driver está a mascarar".
            DebugLog.Write(
                $"[HIDHIDE-TASK] Estado do driver: ativo={SafeRead(() => control.IsActive)}, " +
                $"invertida={SafeRead(() => control.IsAppListInverted)}, " +
                $"bloqueados={SafeRead(() => control.BlockedInstanceIds.Count())}");

            var path = GetProcessPath(processId);
            if (string.IsNullOrEmpty(path))
            {
                DebugLog.Write($"[HIDHIDE-TASK] Sem caminho para o pid {processId} — nada foi {(hide ? "escondido" : "restaurado")}.");
                return 1;
            }

            if (hide)
            {
                control.AddApplicationPath(path, throwIfInvalid: false);
                DebugLog.Write($"[HIDHIDE-TASK] Controle OCULTADO para o jogo: {path}");
            }
            else
            {
                control.RemoveApplicationPath(path);
                DebugLog.Write($"[HIDHIDE-TASK] Controle RESTAURADO para o jogo: {path}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[HIDHIDE-TASK] Falhou (hide={hide}, pid={processId}): {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// DeviceInstanceIds de TODOS os controles detetados (Lenovo primeiro, depois
    /// Xbox/DualSense/etc.). A escolha em si vive no Core
    /// (<see cref="HidHideDeviceSelector"/>) para poder ser testada.
    /// </summary>
    public static IReadOnlyList<string> FindControllerInstanceIds()
    {
        var devices = new List<HidDeviceInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Name FROM Win32_PnPEntity WHERE PNPClass = 'HIDClass'");

            foreach (var device in searcher.Get())
            {
                devices.Add(new HidDeviceInfo(
                    device["DeviceID"]?.ToString() ?? string.Empty,
                    device["Name"]?.ToString()));
            }
        }
        catch (Exception ex)
        {
            DebugLog.Swallowed(ex);
            return [];
        }

        var ids = HidHideDeviceSelector.SelectControllerIds(devices);
        if (ids.Count == 0)
        {
            // v0.24.1: sem esta linha, "sem controle" e "sem permissão" eram
            // indistinguíveis a ler o debug.log.
            var candidates = HidHideDeviceSelector.VendorCandidates(devices.Select(d => d.DeviceId));
            DebugLog.Write(
                $"[HIDHIDE] Nenhum controle reconhecido entre {devices.Count} dispositivo(s) HID" +
                (candidates.Count == 0 ? "." : $"; {HidHideDeviceSelector.LegionVendorId}: " + string.Join(" | ", candidates)));
        }
        else
        {
            DebugLog.Write($"[HIDHIDE] Controles detetados ({ids.Count}): {string.Join(" | ", ids)}");
        }

        return ids;
    }

    private static string? GetProcessPath(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.MainModule?.FileName;
        }
        catch (Exception ex)
        {
            DebugLog.Swallowed(ex);
            return null;
        }
    }

    /// <summary>Leitura tolerante a falhas (o driver pode recusar leituras a meio de uma escrita).</summary>
    private static T? SafeRead<T>(Func<T> read)
    {
        try { return read(); }
        catch (Exception ex)
        {
            DebugLog.Swallowed(ex);
            return default;
        }
    }
}
