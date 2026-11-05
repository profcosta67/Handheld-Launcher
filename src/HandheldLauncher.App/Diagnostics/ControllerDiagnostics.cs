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

    // v0.16.5: o auto-wake do Legion Space passa a ser repetido enquanto o
    // controle seguir travado (até MaxWakeAttempts vezes, com intervalo).
    private const int MaxWakeAttempts = 3;
    private static readonly TimeSpan WakeRetryGap = TimeSpan.FromSeconds(75);
    private static int _wakeAttempts;
    private static DateTime _lastWakeAtUtc = DateTime.MinValue;
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
            if (NeedsWake(last))
            {
                DebugLog.Write($"[CONTROLE-DIAG] Atenção: controle só no slot [{last}], nada no slot 0 — padrão do boot ruim do Legion (controle não responde).");
                // v0.16.4: no teste de 05/11 o que destravou foi abrir o Legion
                // Space. Só no Legion (dispositivo da Lenovo presente) e só se
                // ele ainda não estiver aberto.
                last = TryWakeControllerWithLegionSpace(last) ?? last;
            }
            else
            {
                // v0.16.5: antes, quando nada precisava ser feito, o log ficava
                // mudo — impossível distinguir "não precisou" de "não rodou".
                DebugLog.Write(last.Length == 0
                    ? "[CONTROLE-DIAG] Nenhum controle no XInput na abertura — a recuperação automática só age quando o controle aparece fora do slot 0."
                    : $"[CONTROLE-DIAG] Slot 0 presente (slots [{last}]) — controle normal, nada a recuperar.");
            }

            while (snapshots < MaxSnapshotsPerSession)
            {
                Thread.Sleep(PollInterval);
                var now = ConnectedSlots();

                // v0.16.5: o travamento pode aparecer DEPOIS dos 8 s iniciais, ou a
                // primeira tentativa pode não ter bastado — tenta de novo, com
                // intervalo e limite.
                if (NeedsWake(now) && _wakeAttempts < MaxWakeAttempts && DateTime.UtcNow - _lastWakeAtUtc >= WakeRetryGap)
                {
                    DebugLog.Write($"[CONTROLE-RECUPERAR] Controle ainda só no slot [{now}] — nova tentativa ({_wakeAttempts + 1}/{MaxWakeAttempts}).");
                    now = TryWakeControllerWithLegionSpace(now) ?? now;
                }

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

    /// <summary>Há controle no XInput, mas nenhum no slot 0 (o padrão do boot ruim do Legion).</summary>
    private static bool NeedsWake(string slots) => slots.Length > 0 && !slots.Split(',').Contains("0");

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
                    try { process.Kill(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
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

    /// <summary>
    /// v0.16.4: abre o Legion Space (que manda o comando que destrava o
    /// controle) quando o controle da Lenovo liga travado. Devolve os slots
    /// depois da tentativa, ou null se não tentou.
    /// </summary>
    internal static string? TryWakeControllerWithLegionSpace(string slotsBefore)
    {
        _wakeAttempts++;
        _lastWakeAtUtc = DateTime.UtcNow;
        try
        {
            if (LenovoDeviceBlocks("/enum-devices /connected").Count == 0)
            {
                DebugLog.Write("[CONTROLE-RECUPERAR] Nenhum dispositivo da Lenovo — não é o caso do Legion, nada a fazer.");
                return null;
            }
            if (Process.GetProcessesByName("LegionSpace").Length > 0)
            {
                DebugLog.Write($"[CONTROLE-RECUPERAR] O Legion Space já está aberto — não abro de novo (controle só no slot [{slotsBefore}]).");
                return null;
            }

            var appId = FindLegionSpaceAppId();
            if (appId is null)
            {
                DebugLog.Write("[CONTROLE-RECUPERAR] Não achei o Legion Space instalado (menu Iniciar).");
                return null;
            }

            DebugLog.Write($"[CONTROLE-RECUPERAR] Controle travado no slot [{slotsBefore}] — abrindo o Legion Space ({appId}).");
            // O explorer abre o app com as permissões do usuário, mesmo com o
            // launcher rodando como administrador.
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{appId}") { UseShellExecute = false })?.Dispose();

            for (var i = 0; i < 30; i++)
            {
                Thread.Sleep(1000);
                var slots = ConnectedSlots();
                if (slots.Split(',').Contains("0"))
                {
                    DebugLog.Write($"[CONTROLE-RECUPERAR] Controle destravado {i + 1}s depois de abrir o Legion Space (slots [{slots}]).");
                    LogWindowsDeviceEvents("depois de abrir o Legion Space", 3);
                    return slots;
                }
            }
            var after = ConnectedSlots();
            DebugLog.Write($"[CONTROLE-RECUPERAR] Abri o Legion Space, mas o controle continua só em [{after}] depois de 30s.");
            LogWindowsDeviceEvents("depois de abrir o Legion Space", 3);
            return after;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[CONTROLE-RECUPERAR] Falha ao abrir o Legion Space: {ex.Message}");
            return null;
        }
    }

    /// <summary>ID do app "Legion Space" no menu Iniciar (Get-StartApps), ou null.</summary>
    private static string? FindLegionSpaceAppId()
    {
        var psi = new ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -Command \"Get-StartApps | Where-Object { $_.Name -like '*Legion Space*' } | Select-Object -First 1 -ExpandProperty AppID\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(psi);
        if (process is null) return null;
        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(15_000))
        {
            try { process.Kill(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
            return null;
        }
        var id = output.Result.Trim();
        return id.Length == 0 ? null : id;
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

            LogWindowsDeviceEvents("últimos 30 min", 30);
            DebugLog.Write("[CONTROLE-DIAG] ===== fim do retrato =====");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[CONTROLE-DIAG] Falha ao tirar o retrato: {ex.Message}");
        }
    }

    private static void LogWindowsDeviceEvents(string label, int minutes)
    {
        try
        {
            var lines = WindowsDeviceEvents(minutes);
            if (lines.Count == 0)
            {
                DebugLog.Write($"[CONTROLE-EVENTOS] ({label}) nenhum evento do Windows sobre o dispositivo da Lenovo.");
                return;
            }
            foreach (var line in lines) DebugLog.Write($"[CONTROLE-EVENTOS] ({label}) {line}");
        }
        catch (Exception ex)
        {
            DebugLog.Swallowed(ex);
        }
    }

    /// <summary>
    /// Eventos recentes do Windows que citam o dispositivo da Lenovo (VID_17EF):
    /// Kernel-PnP (configuração e gerenciamento) e DriverFrameworks-UserMode.
    /// Serve pra comparar o que muda quando o Legion Space destrava o controle.
    /// Até 60 linhas; vazio se não houver (ou se o log do Windows estiver desligado).
    /// </summary>
    internal static List<string> WindowsDeviceEvents(int minutes)
    {
        var lines = new List<string>();
        if (!OperatingSystem.IsWindows()) return lines;
        try
        {
            const string template = """
                $since = (Get-Date).AddMinutes(-__MIN__)
                foreach ($log in 'Microsoft-Windows-Kernel-PnP/Configuration','Microsoft-Windows-Kernel-PnP/Device Management','Microsoft-Windows-DriverFrameworks-UserMode/Operational') {
                  $cfg = Get-WinEvent -ListLog $log -ErrorAction SilentlyContinue
                  if (-not $cfg) { "$log: não existe neste Windows"; continue }
                  if (-not $cfg.IsEnabled) { "$log: desligado no Windows"; continue }
                  Get-WinEvent -FilterHashtable @{LogName=$log; StartTime=$since} -ErrorAction SilentlyContinue |
                    Where-Object { $_.Message -match 'VID_17EF' } |
                    Sort-Object TimeCreated | Select-Object -First 25 |
                    ForEach-Object { '{0:HH:mm:ss} {1} id={2} {3}' -f $_.TimeCreated, ($log -replace 'Microsoft-Windows-',''), $_.Id, (($_.Message -replace '\s+',' ').Trim()) }
                }
                """;
            var script = template.Replace("__MIN__", minutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -EncodedCommand {encoded}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using var process = Process.Start(psi);
            if (process is null) return lines;
            var stdout = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(25_000))
            {
                try { process.Kill(); } catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); }
                lines.Add("(consulta aos eventos do Windows passou de 25s — ignorada)");
                return lines;
            }
            foreach (var raw in stdout.Result.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                lines.Add(line.Length > 320 ? line[..320] + "…" : line);
                if (lines.Count >= 60) break;
            }
        }
        catch (Exception ex)
        {
            lines.Add($"(não consegui consultar os eventos do Windows: {ex.Message})");
        }
        return lines;
    }

    /// <summary>
    /// Retrato do controle AGORA, em texto, pro relatório de problema: slots do
    /// XInput, processos da Lenovo, dispositivos conectados/com problema e
    /// eventos recentes do Windows. Se o relatório for gerado enquanto o
    /// controle está travado (antes de abrir o Legion Space), este é o retrato
    /// do estado ruim.
    /// </summary>
    internal static string DescribeNow()
    {
        var sb = new StringBuilder();
        try
        {
            sb.AppendLine($"Slots do XInput com controle: [{ConnectedSlots()}]");
            sb.AppendLine($"Processos 'legion': {DescribeLegionProcesses()}");
            foreach (var block in LenovoDeviceBlocks("/enum-devices /connected"))
                sb.AppendLine($"Conectado: {block}");
            foreach (var block in LenovoDeviceBlocks("/enum-devices /problem"))
                sb.AppendLine($"COM PROBLEMA: {block}");
            sb.AppendLine("Eventos do Windows sobre o dispositivo da Lenovo (últimos 30 min):");
            var events = WindowsDeviceEvents(30);
            if (events.Count == 0) sb.AppendLine("  (nenhum)");
            foreach (var line in events) sb.AppendLine("  " + line);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"(falha ao montar o retrato: {ex.Message})");
        }
        return sb.ToString();
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
                try { process.Kill(); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
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
