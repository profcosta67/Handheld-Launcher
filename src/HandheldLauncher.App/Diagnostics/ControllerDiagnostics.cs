using System.Diagnostics;
using System.Text;
using HandheldLauncher.App.Input;

namespace HandheldLauncher.App.Diagnostics;

/// <summary>
/// v0.16.2 (diagnóstico): problema crônico do Legion Go 2 — às vezes o
/// controle embutido não é reconhecido e só volta depois de abrir o Legion
/// Space. Antes de escolher uma correção (abrir o Legion Space sozinho,
/// reiniciar o dispositivo, ou mandar o comando HID que o Legion Space
/// manda), precisamos comparar no debug.log um boot em que funcionou com um
/// em que não funcionou.
///
/// O que é registrado ("retrato" do controle):
/// <list type="bullet">
/// <item>Quais slots do XInput têm controle conectado.</item>
/// <item>Dispositivos da Lenovo (VID_17EF) que o Windows enxerga conectados,
/// e quais deles estão com problema (o "ponto de exclamação amarelo") — via
/// <c>pnputil</c> do próprio Windows, registrado como texto bruto (não
/// depende do idioma do sistema nem de API nativa não testada).</item>
/// <item>Processos com "legion" no nome (Legion Space e serviços).</item>
/// </list>
///
/// Quando: alguns segundos depois de abrir o launcher, e sempre que o
/// conjunto de controles conectados no XInput muda (com limite, pra não
/// encher o log). Tudo em thread de fundo, tudo em try/catch: diagnóstico
/// nunca pode ser o que quebra o launcher.
///
/// Pensado pra virar uma das verificações da futura Central de Ajuda.
/// </summary>
internal static class ControllerDiagnostics
{
    private const string LenovoVid = "VID_17EF";
    private static int _recoveryAttempted;
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MinGapBetweenSnapshots = TimeSpan.FromSeconds(30);
    private const int MaxSnapshotsPerSession = 20;

    private static int _started;

    public static void StartMonitoring()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (Interlocked.Exchange(ref _started, 1) == 1) return;

        var thread = new Thread(MonitorLoop)
        {
            IsBackground = true,
            Name = "ControllerDiagnostics",
            Priority = ThreadPriority.BelowNormal,
        };
        thread.Start();
    }

    private static void MonitorLoop()
    {
        try
        {
            Thread.Sleep(StartupDelay);
            var snapshots = 0;
            var lastSnapshot = DateTime.MinValue;

            var last = ConnectedSlots();
            LogSnapshot($"abertura do launcher (+{StartupDelay.TotalSeconds:0}s)", last);
            snapshots++;
            lastSnapshot = DateTime.UtcNow;

            // v0.16.3: bug crônico do Legion Go 2 — às vezes o controle embutido
            // não responde depois de ligar. Se ainda não há NENHUM controle no
            // XInput, tenta uma vez reiniciar o controle da Lenovo (como tirar e
            // pôr de volta). Só age quando não há controle nenhum: nada que
            // esteja funcionando é tocado.
            // Desligado na 0.16.3 estável: no boot ruim de 05/11 o controle
            // aparecia no slot 1 (não sumia), e o pnputil /restart-device
            // respondeu 3010 ("precisa reiniciar o sistema") — não resolve.
            // O que resolveu foi abrir o Legion Space. Próximo beta: detectar
            // "controle da Lenovo só no slot ≥ 1 ao ligar" e abrir o Legion
            // Space. TryRecoverLenovoController fica pro diagnóstico.
            if (last.Length > 0 && !last.Split(',').Contains("0"))
                DebugLog.Write($"[CONTROLE-DIAG] Atenção: controle só no slot [{last}], nada no slot 0 — padrão do boot ruim do Legion (controle não responde).");

            while (snapshots < MaxSnapshotsPerSession)
            {
                Thread.Sleep(PollInterval);
                var now = ConnectedSlots();
                if (now == last) continue;

                DebugLog.Write($"[CONTROLE-DIAG] Slots XInput mudaram: [{last}] → [{now}]");
                last = now;

                if (DateTime.UtcNow - lastSnapshot < MinGapBetweenSnapshots) continue;
                LogSnapshot("mudança nos controles conectados", now);
                snapshots++;
                lastSnapshot = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[CONTROLE-DIAG] Monitor parou: {ex.Message}");
        }
    }

    private static readonly System.Text.RegularExpressions.Regex LenovoUsbInstance =
        new(@"USB\\VID_17EF&PID_[0-9A-Fa-f]{4}[^\s|]*", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Reinicia o dispositivo USB do controle da Lenovo via pnputil (precisa do
    /// launcher rodando como administrador — a tarefa de inicialização do
    /// instalador já roda assim). Uma vez por sessão. Devolve os slots XInput
    /// depois da tentativa, ou null se não tentou.
    /// </summary>
    internal static string? TryRecoverLenovoController(bool manual = false)
    {
        // Automático: uma vez por sessão. Manual (menu Ajuda → Reiniciar
        // controle): sempre, pra quando o controle aparece no Windows mas não
        // responde — caso que a checagem automática não enxerga.
        if (!manual && Interlocked.Exchange(ref _recoveryAttempted, 1) == 1) return null;
        try
        {
            var blocks = LenovoDeviceBlocks("/enum-devices /connected");
            blocks.AddRange(LenovoDeviceBlocks("/enum-devices /problem"));

            var ids = new List<string>();
            foreach (var block in blocks)
                foreach (System.Text.RegularExpressions.Match m in LenovoUsbInstance.Matches(block))
                    if (!ids.Contains(m.Value, StringComparer.OrdinalIgnoreCase)) ids.Add(m.Value);

            // Preferência: o dispositivo USB "pai" (composto, sem &MI_ e sem &IG_)
            // reinicia todas as interfaces do controle de uma vez; senão, o
            // dispositivo compatível com XInput (&IG_).
            var target = ids.FirstOrDefault(i => !i.Contains("&MI_", StringComparison.OrdinalIgnoreCase) && !i.Contains("&IG_", StringComparison.OrdinalIgnoreCase))
                         ?? ids.FirstOrDefault(i => i.Contains("&IG_", StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                DebugLog.Write("[CONTROLE-RECUPERAR] Nenhum controle e nenhum dispositivo USB da Lenovo pra reiniciar — nada a fazer.");
                return null;
            }

            DebugLog.Write(manual
                ? $"[CONTROLE-RECUPERAR] Pedido manual — reiniciando o controle da Lenovo ({target})."
                : $"[CONTROLE-RECUPERAR] Nenhum controle no XInput — reiniciando o controle da Lenovo ({target}).");
            var psi = new ProcessStartInfo("pnputil.exe", $"/restart-device \"{target}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (var process = Process.Start(psi))
            {
                if (process is null) return null;
                var outTask = process.StandardOutput.ReadToEndAsync();
                var errTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(20_000))
                {
                    try { process.Kill(); } catch { }
                    DebugLog.Write("[CONTROLE-RECUPERAR] pnputil /restart-device passou de 20s — abandonado.");
                    return null;
                }
                var output = (outTask.Result + " " + errTask.Result).Replace("\r", " ").Replace("\n", " ").Trim();
                DebugLog.Write($"[CONTROLE-RECUPERAR] pnputil exit={process.ExitCode}: {output}");
            }

            for (var i = 0; i < 10; i++)
            {
                Thread.Sleep(1000);
                var slots = ConnectedSlots();
                if (slots.Length > 0 && !manual)
                {
                    DebugLog.Write($"[CONTROLE-RECUPERAR] Controle voltou após reiniciar (slots [{slots}]).");
                    return slots;
                }
            }
            var after = ConnectedSlots();
            DebugLog.Write(manual
                ? $"[CONTROLE-RECUPERAR] Reinício manual concluído (slots [{after}])."
                : "[CONTROLE-RECUPERAR] O controle não voltou em 10s após reiniciar.");
            return after;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[CONTROLE-RECUPERAR] Falhou: {ex.Message}");
            return null;
        }
    }

    internal static string ConnectedSlots()
    {
        var slots = new List<string>();
        for (uint slot = 0; slot < 4; slot++)
        {
            try
            {
                if (XInput.TryGetState(slot, out _)) slots.Add(slot.ToString());
            }
            catch
            {
                // Melhor esforço.
            }
        }
        return string.Join(",", slots);
    }

    private static void LogSnapshot(string reason, string xinputSlots)
    {
        try
        {
            DebugLog.Write($"[CONTROLE-DIAG] ===== Retrato do controle ({reason}) =====");
            DebugLog.Write($"[CONTROLE-DIAG] XInput — slots conectados: [{xinputSlots}]{(xinputSlots.Length == 0 ? " ← NENHUM controle no XInput" : "")}");
            DebugLog.Write($"[CONTROLE-DIAG] Processos 'legion': {DescribeLegionProcesses()}");

            foreach (var block in LenovoDeviceBlocks("/enum-devices /connected"))
                DebugLog.Write($"[CONTROLE-DIAG] Dispositivo Lenovo conectado: {block}");

            var problems = LenovoDeviceBlocks("/enum-devices /problem");
            if (problems.Count == 0)
                DebugLog.Write("[CONTROLE-DIAG] Nenhum dispositivo Lenovo com problema no Windows.");
            foreach (var block in problems)
                DebugLog.Write($"[CONTROLE-DIAG] Dispositivo Lenovo COM PROBLEMA: {block}");

            DebugLog.Write("[CONTROLE-DIAG] ===== fim do retrato =====");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[CONTROLE-DIAG] Falha ao tirar o retrato: {ex.Message}");
        }
    }

    internal static string DescribeLegionProcesses()
    {
        try
        {
            var names = Process.GetProcesses()
                .Select(p =>
                {
                    try { return p.ProcessName; }
                    catch { return null; }
                    finally { p.Dispose(); }
                })
                .Where(n => n is not null && n.Contains("legion", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return names.Count == 0 ? "nenhum" : string.Join(", ", names);
        }
        catch (Exception ex)
        {
            return $"(não consegui listar: {ex.Message})";
        }
    }

    /// <summary>
    /// Roda o pnputil e devolve só os blocos (um por dispositivo, separados
    /// por linha em branco) que mencionam o VID da Lenovo, achatados em uma
    /// linha cada. Exige Windows 10 2004+ (o /enum-devices); em versões
    /// antigas só registra a falha.
    /// </summary>
    internal static List<string> LenovoDeviceBlocks(string arguments)
    {
        var result = new List<string>();
        try
        {
            var psi = new ProcessStartInfo("pnputil.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return result;

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10_000))
            {
                try { process.Kill(); } catch { }
                DebugLog.Write($"[CONTROLE-DIAG] pnputil {arguments} passou de 10s — ignorado.");
                return result;
            }

            var output = stdoutTask.Result;
            foreach (var rawBlock in output.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            {
                if (!rawBlock.Contains(LenovoVid, StringComparison.OrdinalIgnoreCase)) continue;
                var flat = new StringBuilder();
                foreach (var line in rawBlock.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0) continue;
                    if (flat.Length > 0) flat.Append(" | ");
                    flat.Append(trimmed);
                }
                result.Add(flat.ToString());
            }

            var stderr = stderrTask.Result.Trim();
            if (process.ExitCode != 0 && result.Count == 0 && stderr.Length > 0)
                DebugLog.Write($"[CONTROLE-DIAG] pnputil {arguments} exit={process.ExitCode}: {stderr}");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[CONTROLE-DIAG] pnputil {arguments} falhou: {ex.Message}");
        }
        return result;
    }
}
