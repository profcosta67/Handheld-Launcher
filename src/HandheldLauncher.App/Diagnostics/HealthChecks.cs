using System.Reflection;
using HandheldLauncher.App.Input;
using HandheldLauncher.App.Platform;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Diagnostics;

/// <summary>
/// v0.16.2 — Central de Ajuda, etapa 1: verificações de saúde do launcher.
/// Cada verificação devolve um status (ok / informação / atenção / problema),
/// um resumo do que foi encontrado e, quando há o que fazer, uma dica.
///
/// Regras: só LEITURA (nenhuma verificação muda nada no aparelho), cada uma
/// isolada em try/catch (uma falhar não derruba as outras), e tudo roda fora
/// da thread da interface (pnputil e schtasks levam alguns segundos).
/// O resultado também vai pro debug.log, pra aparecer nos relatórios.
/// </summary>
internal static class HealthChecks
{
    public enum Status { Ok, Info, Warning, Error }

    public sealed record Result(string Title, Status Status, string Summary, string? Hint = null, string? ArticleId = null);

    public static Task<IReadOnlyList<Result>> RunAllAsync() => Task.Run<IReadOnlyList<Result>>(() =>
    {
        // Cada verificação aponta pro artigo do manual (HelpArticles) com a solução.
        var checks = new (string Title, string ArticleId, Func<Result> Run)[]
        {
            ("Controle", "controle-legion", CheckController),
            ("Quick Settings com jogo aberto (L3+R3)", "qs-em-jogo", CheckGameInputHelper),
            ("Restauração da última sessão", "tdp-boot", CheckStartupRestore),
            ("Atualizações", "atualizacoes-nao-aparece", CheckUpdates),
            ("Modo de uso", "modo-uso", CheckUsageMode),
            ("Lossless Scaling", "lossless", CheckLossless),
            ("Capas dos jogos (SteamGridDB)", "capas", CheckSteamGridDb),
            ("Conta Steam", "conta-steam", CheckSteamAccount),
            ("Emulador Eden", "eden", CheckEden),
        };

        var results = new List<Result>();
        foreach (var (title, articleId, run) in checks)
        {
            Result result;
            try { result = run(); }
            catch (Exception ex) { result = new Result(title, Status.Warning, $"Não foi possível verificar ({ex.Message})."); }
            results.Add(result.ArticleId is null ? result with { ArticleId = articleId } : result);
        }

        try
        {
            DebugLog.Write("[AJUDA] ===== Central de Ajuda — verificações =====");
            foreach (var r in results)
                DebugLog.Write($"[AJUDA] {Label(r.Status)} {r.Title}: {r.Summary}");
        }
        catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }

        return results;
    });

    public static string Icon(Status status) => status switch
    {
        Status.Ok => "✅",
        Status.Info => "ℹ️",
        Status.Warning => "⚠️",
        _ => "❌",
    };

    private static string Label(Status status) => status switch
    {
        Status.Ok => "[ok]",
        Status.Info => "[info]",
        Status.Warning => "[atenção]",
        _ => "[problema]",
    };

    // ---------------------------------------------------------------------

    private static Result CheckController()
    {
        const string title = "Controle";
        if (!OperatingSystem.IsWindows()) return new Result(title, Status.Info, "Verificação disponível só no Windows.");

        var slots = ControllerDiagnostics.ConnectedSlots();
        var problems = ControllerDiagnostics.LenovoDeviceBlocks("/enum-devices /problem");
        var legion = ControllerDiagnostics.DescribeLegionProcesses();

        if (slots.Length == 0)
        {
            return new Result(title, Status.Error,
                $"Nenhum controle visto pelo Windows (XInput). Processos Legion: {legion}.",
                "No Legion Go 2, abrir o Legion Space costuma fazer o controle voltar. Desencaixar e encaixar os controles também pode ajudar. Se acontecer de novo, anote o horário — o debug.log registra um retrato do controle pra investigação.");
        }

        if (problems.Count > 0)
        {
            return new Result(title, Status.Warning,
                $"Controle conectado (slot {slots}), mas o Windows aponta {problems.Count} dispositivo(s) da Lenovo com problema.",
                "Pode ser um driver com ponto de exclamação no Gerenciador de Dispositivos. Se o controle falhar em algum jogo, reinicie o aparelho; persistindo, reporte o erro com o log.");
        }

        return new Result(title, Status.Ok, $"Controle conectado (slot {slots}).");
    }

    private static Result CheckGameInputHelper()
    {
        const string title = "Quick Settings com jogo aberto (L3+R3)";
        const string knownIssue = "Desde a 0.16.3 o launcher também lê o controle direto (HID), o que resolveu os casos conhecidos (Days Gone, Pokémon pelo Eden). Se ainda falhar em algum jogo, use Ctrl+Alt+O e mande o log.";

        if (GameInputHelper.IsHelperRunning)
            return new Result(title, Status.Ok, "Detector do jogo ativo agora (há um jogo rodando).", knownIssue);

        // 0xFFFFFFFF (-1) = encerrado pelo próprio launcher ao fechar o jogo; 0 = saída normal.
        if (GameInputHelper.LastExitCode is { } code && code != 0 && code != -1)
        {
            return new Result(title, Status.Warning,
                $"O detector do último jogo encerrou com erro (código 0x{code:X8}, {GameInputHelper.LastExitAtLocal:HH:mm}).",
                knownIssue);
        }

        return new Result(title, Status.Info, "Detector liga sozinho quando um jogo abre.", knownIssue);
    }

    private static Result CheckStartupRestore()
    {
        const string title = "Restauração da última sessão";
        var entries = StartupRestoreStatus.Snapshot();
        if (entries.Count == 0)
            return new Result(title, Status.Info, "Nada registrado ainda nesta sessão (TDP, ventoinha e cor são restaurados logo após abrir).");

        var summary = string.Join(" · ", entries.OrderBy(e => e.Item).Select(e => $"{e.Item}: {e.Detail}"));
        var failed = entries.Where(e => !e.Ok).Select(e => e.Item).ToList();
        if (failed.Count == 0) return new Result(title, Status.Ok, summary);

        return new Result(title, Status.Warning, summary,
            $"Não restaurou: {string.Join(", ", failed)}. Abra o Quick Settings e escolha de novo. No TDP, o firmware do aparelho às vezes sobrescreve os limites logo após ligar — o launcher já tenta de novo algumas vezes sozinho.");
    }

    private static Result CheckUpdates()
    {
        const string title = "Atualizações";
        var version = CurrentVersion();
        var channel = UpdateService.IsBetaChannel ? "beta" : "estável";
        var last = UpdateService.LastCheck;

        if (last is null)
            return new Result(title, Status.Info, $"Versão {version}, canal {channel}. Primeira verificação acontece alguns segundos após abrir o launcher.");

        var when = last.AtLocal.ToString("HH:mm");
        if (!last.Ok)
        {
            return new Result(title, Status.Warning, $"Versão {version}, canal {channel}. Última verificação ({when}): {last.Result}.",
                "Confira a internet. Se persistir, o GitHub pode estar limitando as consultas — tente mais tarde pelo menu Menu → Atualizações → Verificar launcher.");
        }

        var hint = UpdateService.IsBetaChannel
            ? "No canal beta você recebe versões de teste. Pra voltar ao estável, desligue \"Receber atualizações beta\" em Configurações → Atualizações, salve e reinicie o launcher."
            : null;
        return new Result(title, Status.Ok, $"Versão {version}, canal {channel}. Última verificação ({when}): {last.Result}.", hint);
    }

    private static Result CheckUsageMode()
    {
        const string title = "Modo de uso";
        var settings = AppSettingsStore.Load();
        var mode = settings.KioskMode ? "Handheld (tela cheia)" : "Uso em PC (Desktop)";
        var task = WatchdogTask.Exists();

        if (task is null) return new Result(title, Status.Info, $"{mode}. Não foi possível conferir a abertura automática.");

        if (settings.AutoRestart && task == false)
        {
            return new Result(title, Status.Warning, $"{mode}, mas a reabertura automática do modo Handheld (tarefa \"HandheldLauncherWatchdog\") não está registrada no Windows.",
                "Abra Configurações → Geral, escolha Handheld de novo e salve — isso registra a reabertura automática.");
        }

        if (!settings.AutoRestart && task == true)
        {
            return new Result(title, Status.Warning, $"{mode}, mas a reabertura automática do modo Handheld (tarefa \"HandheldLauncherWatchdog\") ainda está registrada no Windows.",
                "Abra Configurações → Geral, escolha o modo desejado e salve — isso acerta a reabertura automática.");
        }

        // O instalador (InstallTask.ps1) registra uma segunda abertura no login,
        // independente do modo escolhido nas Configurações.
        var installerTask = WatchdogTask.Exists(WatchdogTask.InstallerTaskName) == true;

        if (!settings.KioskMode && installerTask)
        {
            return new Result(title, Status.Info, $"{mode}, mas o launcher ainda abre sozinho no login (abertura registrada pelo instalador).",
                "O instalador registra uma abertura automática que vale nos dois modos. Se não quiser que ele abra no login, desative \"HandheldLauncher_Startup\" no Agendador de Tarefas do Windows.");
        }

        var autoText = task == true || installerTask ? "abre sozinho no login" : "não abre sozinho";
        return new Result(title, Status.Ok, $"{mode}, {autoText}.");
    }

    private static Result CheckLossless()
    {
        const string title = "Lossless Scaling";
        var path = LosslessScalingService.GetExecutablePath();
        if (string.IsNullOrEmpty(path))
        {
            return new Result(title, Status.Info, "Não encontrado (opcional).",
                "O Lossless Scaling é vendido na Steam. Depois de instalado, o launcher encontra sozinho na pasta padrão, ou você indica o caminho em Configurações → Lossless.");
        }

        var running = LosslessScalingService.IsLosslessRunning();
        return new Result(title, Status.Ok, running ? "Instalado e rodando." : "Instalado (abre quando você ativa pelo Quick Settings).");
    }

    private static Result CheckSteamGridDb()
    {
        const string title = "Capas dos jogos (SteamGridDB)";
        var settings = AppSettingsStore.Load();
        if (string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey))
        {
            return new Result(title, Status.Warning, "Chave da API não configurada — capas não são baixadas automaticamente.",
                "Crie uma conta grátis em steamgriddb.com, gere a chave em Preferências → API e cole em Configurações → Contas.");
        }
        return new Result(title, Status.Ok, "Chave configurada.");
    }

    private static Result CheckSteamAccount()
    {
        const string title = "Conta Steam";
        var settings = AppSettingsStore.Load();
        var hasKey = !string.IsNullOrWhiteSpace(settings.SteamWebApiKey);
        var hasId = !string.IsNullOrWhiteSpace(settings.SteamId64);
        if (hasKey && hasId) return new Result(title, Status.Ok, "Chave da Web API e SteamID configurados.");

        var missing = !hasKey && !hasId ? "chave da Web API e SteamID" : !hasKey ? "chave da Web API" : "SteamID";
        return new Result(title, Status.Info, $"Falta {missing} (opcional).",
            "Sem isso os jogos instalados continuam aparecendo, mas a aba \"Não instalados\", downloads e conquistas não funcionam. Configure em Configurações → Contas.");
    }

    private static Result CheckEden()
    {
        const string title = "Emulador Eden";
        var settings = AppSettingsStore.Load();
        if (string.IsNullOrWhiteSpace(settings.EdenExecutablePath))
            return new Result(title, Status.Info, "Não configurado (opcional).");

        if (!File.Exists(settings.EdenExecutablePath))
        {
            return new Result(title, Status.Error, "O executável do Eden configurado não existe mais.",
                $"Caminho salvo: {settings.EdenExecutablePath}. Atualize em Configurações → Biblioteca (o Eden pode ter sido movido ou atualizado pra outra pasta).");
        }

        var folders = settings.EdenRomFolders ?? new List<string>();
        var missing = folders.Where(f => !string.IsNullOrWhiteSpace(f) && !Directory.Exists(f)).ToList();
        if (folders.Count == 0)
        {
            return new Result(title, Status.Warning, "Eden encontrado, mas nenhuma pasta de ROMs configurada.",
                "Adicione a pasta dos jogos em Configurações → Biblioteca.");
        }
        if (missing.Count > 0)
        {
            return new Result(title, Status.Warning, $"Eden encontrado, mas {missing.Count} pasta(s) de ROMs não existe(m): {string.Join("; ", missing)}.",
                "Se for um cartão de memória ou disco externo, confira se está conectado.");
        }

        return new Result(title, Status.Ok, $"Eden encontrado, {folders.Count} pasta(s) de ROMs.");
    }

    private static string CurrentVersion()
    {
        try
        {
            var info = Assembly.GetEntryAssembly()?
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .OfType<AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion ?? "dev";
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }
        catch
        {
            return "dev";
        }
    }
}
