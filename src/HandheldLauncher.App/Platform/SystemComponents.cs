using System.Diagnostics;
using System.Runtime.Versioning;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// 18/11: os componentes de fora que o launcher precisa. Numa instalação limpa (teste de 18/11)
/// o TDP não funcionou porque o driver do PawnIO não tinha sido instalado: o script de pós-
/// instalação do Velopack roda sem administrador e com limite de 30 s, então não chegava nele.
///
/// Aqui o PRÓPRIO launcher (que abre sempre como administrador) confere e instala o que falta:
/// <list type="bullet">
/// <item><b>PawnIO</b> — driver que lê e escreve os limites de energia: sem ele o TDP nunca é
/// aplicado. Instalador assinado vem no pacote (Tools\PawnIO_setup.exe).</item>
/// <item><b>HidHide</b> — esconde o controle do jogo (Quick Settings com jogo aberto). Instalador
/// vem no pacote (Tools\HidHide_*.exe).</item>
/// <item><b>RTSS (RivaTuner Statistics Server)</b> — mede o FPS e desenha o HUD; é o que o TDP
/// inteligente usa para saber se a meta de FPS foi atingida. Não pode ser redistribuído no nosso
/// pacote, então só é instalado quando o usuário pede, pelo winget (fonte oficial).</item>
/// </list>
/// Tudo em melhor esforço: cada passo vira linha no debug.log e nada aqui derruba o launcher.
/// </summary>
internal static class SystemComponents
{
    public enum Kind { PawnIo, HidHide, Rtss }

    public enum State { Installed, Missing, Unknown }

    public sealed record Status(Kind Kind, string Name, State State, string Detail, bool CanInstallHere);

    public const string RtssFullName = "RivaTuner Statistics Server (RTSS)";

    private static readonly string[] RtssPaths =
    {
        @"C:\Program Files (x86)\RivaTuner Statistics Server\RTSS.exe",
        @"C:\Program Files\RivaTuner Statistics Server\RTSS.exe",
    };

    private static string ToolsPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Tools", fileName);

    private static string? PawnIoInstaller()
    {
        var path = ToolsPath("PawnIO_setup.exe");
        return File.Exists(path) ? path : null;
    }

    private static string? HidHideInstaller()
    {
        try
        {
            var tools = Path.Combine(AppContext.BaseDirectory, "Tools");
            if (!Directory.Exists(tools)) return null;
            // O nome traz a versão (HidHide_1.5.230_x64.exe) — procura pelo prefixo.
            return Directory.EnumerateFiles(tools, "HidHide_*.exe").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).LastOrDefault();
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); return null; }
    }

    // ===================================================================== estado

    public static IReadOnlyList<Status> CheckAll() => new[] { Check(Kind.PawnIo), Check(Kind.HidHide), Check(Kind.Rtss) };

    public static Status Check(Kind kind) => kind switch
    {
        Kind.PawnIo => CheckPawnIo(),
        Kind.HidHide => CheckHidHide(),
        _ => CheckRtss(),
    };

    private static Status CheckPawnIo()
    {
        const string name = "PawnIO (controle do TDP)";
        if (!OperatingSystem.IsWindows())
            return new Status(Kind.PawnIo, name, State.Unknown, "Só no Windows.", false);

        var query = RunCapture("sc.exe", "query PawnIO");
        var available = PawnIoInstaller() is not null;
        if (query is null)
            return new Status(Kind.PawnIo, name, State.Unknown, "Não foi possível consultar o serviço.", available);
        if (query.Value.ExitCode != 0)
            return new Status(Kind.PawnIo, name, State.Missing,
                available ? "Driver não instalado — o TDP não funciona sem ele." : "Driver não instalado e o instalador não veio no pacote (pawnio.eu).",
                available);

        // O texto do sc.exe vem traduzido e na página de código do console ("EM_EXECUÇÃO" chega
        // embaralhado), então o que vale é o número: STATE/ESTADO : 4 = rodando.
        var running = System.Text.RegularExpressions.Regex.IsMatch(query.Value.Output, @"^\s*(STATE|ESTADO)\s*:\s*4\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);
        return new Status(Kind.PawnIo, name, State.Installed,
            running ? "Instalado e rodando." : "Instalado, mas o driver está parado (reinicie o Windows).", available);
    }

    private static Status CheckHidHide()
    {
        const string name = "HidHide (isolar o controle)";
        if (!OperatingSystem.IsWindows())
            return new Status(Kind.HidHide, name, State.Unknown, "Só no Windows.", false);

        var available = HidHideInstaller() is not null;

        // Log do beta.74: HidHide instalado e funcionando (o launcher isolava o controle), mas a
        // chave do registro procurada antes não existe nas versões novas — o instalador rodava de
        // novo a cada abertura. O que vale é o driver: o serviço "HidHide" (sc query) ou a própria
        // biblioteca da Nefarius. O registro fica só como última tentativa.
        var query = RunCapture("sc.exe", "query HidHide");
        if (query is { ExitCode: 0 })
            return new Status(Kind.HidHide, name, State.Installed, "Instalado.", available);
#if WINDOWS
        try
        {
            if (new Nefarius.Drivers.HidHide.HidHideControlService().IsInstalled)
                return new Status(Kind.HidHide, name, State.Installed, "Instalado.", available);
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
#endif
        try
        {
            foreach (var path in new[] { @"SOFTWARE\Nefarius Software Solutions\HidHide", @"SOFTWARE\WOW6432Node\Nefarius Software Solutions\HidHide" })
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path);
                if (key is not null) return new Status(Kind.HidHide, name, State.Installed, "Instalado.", available);
            }
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }

        return new Status(Kind.HidHide, name, State.Missing,
            available ? "Não instalado — o Quick Settings com jogo aberto depende dele." : "Não instalado e o instalador não veio no pacote.",
            available);
    }

    private static Status CheckRtss()
    {
        var name = RtssFullName;
        if (!OperatingSystem.IsWindows())
            return new Status(Kind.Rtss, name, State.Unknown, "Só no Windows.", false);

        var found = RtssPaths.FirstOrDefault(File.Exists);
        if (found is not null) return new Status(Kind.Rtss, name, State.Installed, "Instalado.", true);

        var winget = HasWinget();
        return new Status(Kind.Rtss, name, State.Missing,
            winget
                ? "Não instalado — é ele que mede o FPS (HUD e TDP inteligente)."
                : "Não instalado, e o winget do Windows não está disponível para instalar automaticamente.",
            winget);
    }

    /// <summary>Pasta de instalação do RTSS, ou null se não estiver instalado.</summary>
    public static string? RtssDirectory()
    {
        var exe = RtssPaths.FirstOrDefault(File.Exists);
        return exe is null ? null : Path.GetDirectoryName(exe);
    }

    public static bool HasWinget()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
            return paths.Any(p =>
            {
                try { return !string.IsNullOrWhiteSpace(p) && File.Exists(Path.Combine(p, "winget.exe")); }
                catch { return false; }
            });
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); return false; }
    }

    // ===================================================================== instalação

    /// <summary>
    /// Instala o que falta e pode ser instalado daqui sem perguntar nada: PawnIO e HidHide (os
    /// instaladores vêm no pacote). O RTSS NÃO entra aqui — só com pedido do usuário.
    /// Devolve o texto para o log/aviso, ou null se não havia nada a fazer.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static string? InstallMissingBundled()
    {
        var done = new List<string>();
        foreach (var kind in new[] { Kind.PawnIo, Kind.HidHide })
        {
            var status = Check(kind);
            if (status.State != State.Missing || !status.CanInstallHere) continue;
            DebugLog.Write($"[COMPONENTES] {status.Name} não instalado — instalando agora.");
            if (Install(kind)) done.Add(status.Name);
        }
        return done.Count == 0 ? null : string.Join(", ", done);
    }

    /// <summary>Instala um componente. true = o instalador rodou e o componente agora aparece instalado.</summary>
    public static bool Install(Kind kind)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            switch (kind)
            {
                case Kind.PawnIo: return InstallPawnIo();
                case Kind.HidHide: return InstallHidHide();
                default: return InstallRtss();
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[COMPONENTES] Instalação de {kind} falhou: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool InstallPawnIo()
    {
        var installer = PawnIoInstaller();
        if (installer is null)
        {
            DebugLog.Write("[COMPONENTES] PawnIO: instalador não encontrado em Tools — veja https://pawnio.eu.");
            return false;
        }
        if (!ElevationService.IsElevated)
        {
            DebugLog.Write("[COMPONENTES] PawnIO: precisa de administrador — nada feito.");
            return false;
        }

        // Sintaxe oficial do PawnIO_setup.exe (o /S do NSIS é rejeitado).
        var exit = Run(installer, "-install -silent", 180);
        DebugLog.Write($"[COMPONENTES] PawnIO: instalador terminou com código {exit?.ToString() ?? "desconhecido"}.");

        // O driver é KERNEL_DRIVER com início manual: deixa em automático e tenta subir agora,
        // senão o TDP só funcionaria depois de reiniciar (mesmo ajuste do InstallTask.ps1).
        RunCapture("sc.exe", "config PawnIO start= auto");
        RunCapture("sc.exe", "start PawnIO", timeoutSeconds: 30);

        var after = CheckPawnIo();
        DebugLog.Write($"[COMPONENTES] PawnIO depois da instalação: {after.State} — {after.Detail}");
        return after.State == State.Installed;
    }

    [SupportedOSPlatform("windows")]
    private static bool InstallHidHide()
    {
        var installer = HidHideInstaller();
        if (installer is null)
        {
            DebugLog.Write("[COMPONENTES] HidHide: instalador não encontrado em Tools.");
            return false;
        }
        if (!ElevationService.IsElevated)
        {
            DebugLog.Write("[COMPONENTES] HidHide: precisa de administrador — nada feito.");
            return false;
        }

        var exit = Run(installer, "/exenoui /qn /norestart", 180);
        DebugLog.Write($"[COMPONENTES] HidHide: instalador terminou com código {exit?.ToString() ?? "desconhecido"}.");
        var after = CheckHidHide();
        DebugLog.Write($"[COMPONENTES] HidHide depois da instalação: {after.State} — {after.Detail} (o driver costuma pedir reinício do Windows).");
        return after.State == State.Installed;
    }

    [SupportedOSPlatform("windows")]
    private static bool InstallRtss()
    {
        if (!HasWinget())
        {
            DebugLog.Write("[COMPONENTES] RTSS: winget não disponível — baixe em https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/.");
            return false;
        }
        DebugLog.Write("[COMPONENTES] RTSS: instalando pelo winget (Guru3D.RTSS).");
        var exit = Run("winget.exe", "install --id Guru3D.RTSS --silent --accept-source-agreements --accept-package-agreements", 300);
        DebugLog.Write($"[COMPONENTES] RTSS: winget terminou com código {exit?.ToString() ?? "desconhecido"}.");
        var after = CheckRtss();
        DebugLog.Write($"[COMPONENTES] RTSS depois da instalação: {after.State} — {after.Detail}");
        return after.State == State.Installed;
    }

    // ===================================================================== processos

    private static int? Run(string fileName, string arguments, int timeoutSeconds)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(psi);
            if (process is null) return null;
            if (!process.WaitForExit(timeoutSeconds * 1000))
            {
                DebugLog.Write($"[COMPONENTES] {Path.GetFileName(fileName)} passou de {timeoutSeconds}s — seguindo sem esperar.");
                return null;
            }
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[COMPONENTES] Falha ao rodar {Path.GetFileName(fileName)}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Roda e devolve a saída, com limite de tempo de verdade. Antes era ReadToEnd() antes do
    /// WaitForExit: um comando que não termina (o "sc start" carrega driver) travava a thread para
    /// sempre, e o WaitForExit virava letra morta.
    /// </summary>
    private static (int ExitCode, string Output)? RunCapture(string fileName, string arguments, int timeoutSeconds = 20)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return null;

            var text = new System.Text.StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (text) text.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (text) text.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(timeoutSeconds * 1000))
            {
                DebugLog.Write($"[COMPONENTES] {Path.GetFileName(fileName)} {arguments} passou de {timeoutSeconds}s — encerrando.");
                try { process.Kill(entireProcessTree: true); } catch (Exception ex) { DebugLog.Swallowed(ex); }
                return null;
            }

            lock (text) return (process.ExitCode, text.ToString());
        }
        catch (Exception ex)
        {
            DebugLog.Swallowed(ex);
            return null;
        }
    }
}
