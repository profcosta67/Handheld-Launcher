using HandheldLauncher.App.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Atualização automática via Velopack + GitHub Releases. Fluxo inteiro:
/// <list type="number">
/// <item>A cada tag "vX.Y.Z" enviada pro GitHub, o workflow release.yml
/// compila, empacota com o `vpk` e publica uma Release com os arquivos
/// que o Velopack entende.</item>
/// <item>O launcher (instalado pelo Setup.exe da Release) pergunta ao
/// GitHub "tem versão mais nova?", baixa em segundo plano (só o que mudou,
/// quando possível) e avisa "Atualização pronta".</item>
/// <item>A troca só acontece quando o usuário manda — nunca no meio de
/// um jogo.</item>
/// </list>
/// Fora de uma instalação Velopack de verdade (rodando via `dotnet run`,
/// ou instalado pelo Install-Launcher.ps1 antigo) <see cref="IsInstalled"/>
/// é false e todo o resto vira no-op silencioso: nunca quebra a abertura
/// do launcher.
/// </summary>
public sealed class UpdateService
{
    public const string RepoUrl = "https://github.com/profcosta67/Handheld-Launcher";

    private readonly UpdateManager? _manager;
    private UpdateInfo? _pending;

    /// <summary>
    /// v0.16: <paramref name="betaChannelOptIn"/> vem de
    /// AppSettings.BetaChannelOptIn (default false — ver comentário lá).
    /// Canal estável (default): mesmo comportamento de sempre, byte a byte
    /// — GithubSource com prerelease=false, sem UpdateOptions, então o
    /// GitHub nem entrega um Release "beta" marcado como pré-lançamento
    /// pra essa instância, e o Velopack só olha releases.win.json.
    /// Canal beta (opt-in explícito): prerelease=true no GithubSource (só
    /// assim o pré-lançamento aparece pra essa instância) +
    /// ExplicitChannel="beta" (só assim ele procura releases.beta.json em
    /// vez do canal padrão do build atual) + AllowVersionDowngrade=true
    /// (recomendação oficial da Velopack pra quando o usuário desativar o
    /// beta depois e precisar "descer" pra uma estável mais antiga que a
    /// beta que tinha instalado — ver docs.velopack.io/integrating/switching-channels).
    /// </summary>
    public UpdateService(bool betaChannelOptIn = false)
    {
        IsBetaChannel = betaChannelOptIn;
        try
        {
            if (betaChannelOptIn)
            {
                var options = new UpdateOptions { ExplicitChannel = "beta", AllowVersionDowngrade = true };
                _manager = new UpdateManager(new GithubSource(RepoUrl, null, prerelease: true), options);
            }
            else
            {
                // Repositório público: sem token. 3º parâmetro = não considerar
                // pré-lançamentos.
                _manager = new UpdateManager(new GithubSource(RepoUrl, null, false));
            }
        }
        catch
        {
            _manager = null;
        }
    }

    /// <summary>True só se este processo foi instalado pelo Setup.exe do Velopack.</summary>
    public bool IsInstalled
    {
        get
        {
            try { return _manager?.IsInstalled ?? false; }
            catch { return false; }
        }
    }

    /// <summary>Versão atual ("1.2.3") ou "dev" fora de uma instalação Velopack.</summary>
    public string CurrentVersionText
    {
        get
        {
            try { return _manager?.CurrentVersion?.ToString() ?? "dev"; }
            catch { return "dev"; }
        }
    }

    /// <summary>Já baixou uma versão nova e só falta reiniciar.</summary>
    public bool IsUpdateReady => _pending is not null;

    /// <summary>Versão baixada e pronta pra aplicar (ex: "1.2.4"), ou null.</summary>
    public string? PendingVersionText
    {
        get
        {
            try { return _pending?.TargetFullRelease.Version.ToString(); }
            catch { return null; }
        }
    }

    /// <summary>
    /// "O que há de novo" da versão baixada, em markdown simples — vem do
    /// mesmo release-notes/X.Y.Z.md que o workflow já escreve pra cada
    /// versão, agora também embutido no pacote Velopack (ver
    /// release.yml, passo "Empacotar" → --releaseNotes). Null/vazio numa
    /// versão sem notas (ou empacotada antes desta leva, v0.9.6) — a
    /// UpdateWindow trata isso mostrando só o número da versão.
    /// </summary>
    public string? PendingReleaseNotesMarkdown
    {
        get
        {
            try { return _pending?.TargetFullRelease.NotesMarkdown; }
            catch { return null; }
        }
    }

    /// <summary>
    /// Pergunta ao GitHub se tem versão nova e, se tiver, baixa. Devolve a
    /// versão nova baixada ("1.2.4") ou null se já está atualizado, não é
    /// uma instalação Velopack, ou deu qualquer erro (sem internet, API do
    /// GitHub fora do ar, limite de requisições...) — erro de atualização
    /// nunca pode incomodar quem só quer jogar, então só vira null.
    /// </summary>
    public async Task<string?> CheckAndDownloadAsync(CancellationToken cancellationToken = default)
    {
        if (_manager is null || !IsInstalled)
        {
            RecordCheck("não verificado — launcher não está numa instalação com atualização automática", ok: true);
            return null;
        }
        if (_pending is not null) return PendingVersionText;

        try
        {
            var info = await _manager.CheckForUpdatesAsync();
            if (info is null)
            {
                RecordCheck("nenhuma versão nova", ok: true);
                return null;
            }

            await _manager.DownloadUpdatesAsync(info);
            _pending = info;
            RecordCheck($"versão {PendingVersionText} baixada, pronta pra instalar", ok: true);
            return PendingVersionText;
        }
        catch (Exception ex)
        {
            // v0.16.2: continua sem incomodar quem só quer jogar, mas agora
            // deixa rastro — antes a falha sumia sem log nenhum.
            DebugLog.Write($"[UPDATE] Falha ao verificar/baixar atualização: {ex.GetType().Name}: {ex.Message}");
            RecordCheck($"falhou ({ex.Message})", ok: false);
            return null;
        }
    }

    /// <summary>v0.16.2: canal com que o serviço foi montado na abertura.</summary>
    public static bool IsBetaChannel { get; private set; }

    /// <summary>v0.16.2: resultado da última verificação, pra Central de Ajuda.</summary>
    public static LastCheckInfo? LastCheck { get; private set; }

    public sealed record LastCheckInfo(DateTime AtLocal, string Result, bool Ok);

    private static void RecordCheck(string result, bool ok)
    {
        LastCheck = new LastCheckInfo(DateTime.Now, result, ok);
        DebugLog.Write($"[UPDATE] Verificação: {result}");
    }

    /// <summary>
    /// Fecha o launcher, troca os arquivos pela versão nova e abre de
    /// novo. Quem chama é responsável por NÃO chamar com jogo rodando.
    ///
    /// v0.9.8: usava <c>ApplyUpdatesAndRestart</c>, que mostra a UI nativa
    /// do próprio Velopack (sem NENHUMA opção documentada de cor/tema)
    /// durante a troca de arquivos — exatamente a "telinha estilo
    /// Windows" que o usuário reportou aparecer DEPOIS da nossa
    /// UpdateWindow (que é temática). Não tem como pintar essa parte, mas
    /// dá pra não mostrar ela: <c>WaitExitThenApplyUpdates</c> tem um
    /// parâmetro <c>silent</c> justamente pra isso (doc oficial: "whether
    /// Velopack should show a progress window / dialogs during the
    /// updates or not"). Diferença importante: esse método NÃO fecha o
    /// launcher por conta própria como o antigo fazia — a doc da Velopack
    /// é explícita ("you should then clean up any state and exit your
    /// app"), por isso o <c>Environment.Exit</c> aqui.
    /// </summary>
    public void ApplyAndRestart()
    {
        if (_manager is null || _pending is null) return;
        _manager.WaitExitThenApplyUpdates(_pending, silent: true, restart: true);
        Environment.Exit(0);
    }
}
