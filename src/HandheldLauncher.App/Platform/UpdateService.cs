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

    public UpdateService()
    {
        try
        {
            // Repositório público: sem token. 3º parâmetro = não considerar
            // pré-lançamentos.
            _manager = new UpdateManager(new GithubSource(RepoUrl, null, false));
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
        if (_manager is null || !IsInstalled) return null;
        if (_pending is not null) return PendingVersionText;

        try
        {
            var info = await _manager.CheckForUpdatesAsync();
            if (info is null) return null;

            await _manager.DownloadUpdatesAsync(info);
            _pending = info;
            return PendingVersionText;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Fecha o launcher, troca os arquivos pela versão nova e abre de
    /// novo. Quem chama é responsável por NÃO chamar com jogo rodando.
    /// </summary>
    public void ApplyAndRestart()
    {
        if (_manager is null || _pending is null) return;
        _manager.ApplyUpdatesAndRestart(_pending);
    }
}
