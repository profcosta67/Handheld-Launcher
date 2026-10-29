using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// Integração com o emulador Eden (sucessor comunitário do Yuzu, roda
/// jogos de Nintendo Switch — pedido do usuário: "tipo o Pokémon"), item
/// antigo do roteiro ("só anotado, pedido explícito de não codar ainda")
/// retomado na v0.9.15.
///
/// <para>
/// <b>O que foi pesquisado (sem conseguir testar aqui — nenhum Switch/Eden
/// de verdade neste sandbox) antes de escrever isto:</b>
/// </para>
/// <list type="bullet">
/// <item>Eden é um fork direto do Yuzu (mesmo frontend Qt, mesmo binário
/// de config — a documentação oficial do Eden confirma que no Windows os
/// dados/config ficam em <c>%AppData%\Roaming</c>, igual o Yuzu original
/// ficava em <c>%AppData%\yuzu</c>). Por analogia direta, o arquivo é
/// <c>%AppData%\eden\config\qt-config.ini</c>.</item>
/// <item>O Yuzu/Eden guarda as pastas de jogo configuradas (Emulação →
/// Configurar → Geral → "Diretórios de jogos") na seção <c>[UI]</c> desse
/// INI, com chaves no padrão de array do Qt (<c>beginWriteArray</c>):
/// <c>Paths\gamedirs\size</c> (quantas), <c>Paths\gamedirs\1\path</c>,
/// <c>Paths\gamedirs\1\deep_scan</c>, <c>Paths\gamedirs\2\path</c>... —
/// confirmado por múltiplas fontes da comunidade Yuzu/LaunchBox, mas
/// **nunca visto num arquivo de verdade por quem escreveu isto**: se o
/// Eden tiver mudado esse formato, esta leitura simplesmente não acha
/// nada (ver <see cref="TryParseGameDirs"/> — nunca derruba o app).</item>
/// <item>Lançar um jogo específico pelo frontend Qt (não o `eden-cmd`
/// separado): <c>eden.exe -g "caminho\do\jogo.nsp"</c> (opcionalmente
/// <c>-f</c> pra abrir em tela cheia, o que faz sentido pro uso em
/// handheld) — confirmado contra um PR do próprio Yuzu no GitHub e um
/// relato de quem testou no LaunchBox ("yuzu.exe -f -g "path_to_game""
/// funcionou). Eden mantém o mesmo frontend/parser de linha de comando.</item>
/// </list>
/// <para>
/// Como não tem como escanear Switch/eden.exe de verdade aqui, esta
/// classe é EXPERIMENTAL por natureza (mesmo espírito do
/// <see cref="EaLibraryScanner"/>): qualquer coisa que não bater com o
/// esperado é ignorada (lista vazia), nunca derruba o app nem os outros
/// scanners — ver AppSettings.EdenExecutablePath pra por que precisa
/// configurar o caminho do `eden.exe` à mão por enquanto.
/// </para>
/// </summary>
public sealed class EdenLibraryScanner : IStoreLibraryScanner
{
    private static readonly string[] RomExtensions = [".nsp", ".xci"];

    private readonly string? _edenExecutablePath;
    private readonly string _configPathOverride;

    public EdenLibraryScanner(string? edenExecutablePath, string? configPathOverride = null)
    {
        _edenExecutablePath = edenExecutablePath;
        _configPathOverride = configPathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "eden", "config", "qt-config.ini");
    }

    public IReadOnlyList<GameEntry> ScanInstalledGames()
    {
        var games = new List<GameEntry>();

        // Sem o caminho do eden.exe configurado (AppSettings.EdenExecutablePath,
        // hand-edit só por enquanto), não tem como abrir nenhum jogo que a
        // gente ache — nem vale a pena escanear. Ver comentário da classe.
        if (string.IsNullOrWhiteSpace(_edenExecutablePath) || !File.Exists(_edenExecutablePath))
        {
            return games;
        }

        if (!File.Exists(_configPathOverride)) return games;

        List<(string Path, bool DeepScan)> gameDirs;
        try
        {
            gameDirs = TryParseGameDirs(_configPathOverride);
        }
        catch
        {
            return games;
        }

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (dir, deepScan) in gameDirs)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.*", deepScan ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .Where(f => RomExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var romPath in files)
            {
                try
                {
                    if (!seenPaths.Add(romPath)) continue;

                    var title = Path.GetFileNameWithoutExtension(romPath);
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    games.Add(new GameEntry
                    {
                        Id = $"eden_{SanitizeForId(romPath)}",
                        Title = title,
                        Source = GameSource.Emulator,
                        ExecutablePath = _edenExecutablePath,
                        // v0.9.15: -f abre em tela cheia (faz sentido no
                        // handheld), -g passa o jogo — ver comentário da
                        // classe pra onde essa sintaxe foi confirmada.
                        LaunchArguments = $"-f -g \"{romPath}\"",
                        InstallDirectory = Path.GetDirectoryName(romPath),
                    });
                }
                catch
                {
                    // Pula só esse arquivo.
                }
            }
        }

        return games;
    }

    /// <summary>
    /// Lê a seção [UI] do qt-config.ini procurando o array
    /// "Paths\gamedirs" (ver comentário da classe pro formato esperado).
    /// Nunca lança — qualquer formato inesperado só resulta em lista
    /// vazia (o scanner então não acha nada, mas não derruba nada).
    /// </summary>
    private static List<(string Path, bool DeepScan)> TryParseGameDirs(string configPath)
    {
        var result = new List<(string Path, bool DeepScan)>();

        var ui = new Dictionary<string, string>(StringComparer.Ordinal);
        string? currentSection = null;
        foreach (var rawLine in File.ReadAllLines(configPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                continue;
            }

            if (!string.Equals(currentSection, "UI", StringComparison.Ordinal)) continue;

            var eq = line.IndexOf('=');
            if (eq < 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            ui[key] = value;
        }

        // "size" é o jeito normal do Qt guardar quantos itens tem no
        // array, mas se não achar (formato diferente do esperado), tenta
        // mesmo assim subindo o índice até faltar "...\N\path" — mais
        // robusto contra uma variação de formato que não previ.
        var hasSize = ui.TryGetValue(@"Paths\gamedirs\size", out var sizeText) && int.TryParse(sizeText, out var size);
        var maxIndex = hasSize ? size : 64; // limite de segurança pro fallback sem "size"

        for (var i = 1; i <= maxIndex; i++)
        {
            if (!ui.TryGetValue($@"Paths\gamedirs\{i}\path", out var path))
            {
                if (!hasSize) break; // sem "size": parou de achar = acabou a lista
                continue; // com "size" declarado: índice pode ter buraco, continua até maxIndex
            }

            var deepScan = ui.TryGetValue($@"Paths\gamedirs\{i}\deep_scan", out var deepText)
                && string.Equals(deepText, "true", StringComparison.OrdinalIgnoreCase);

            result.Add((path, deepScan));
        }

        return result;
    }

    /// <summary>Mesmo esquema do XboxFolderScanner.SanitizeForId — determinístico, nunca usa GetHashCode (randomizado por processo).</summary>
    private static string SanitizeForId(string path)
    {
        var chars = path.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars);
    }
}
