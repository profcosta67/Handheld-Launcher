using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// Integração com o emulador Eden (sucessor comunitário do Yuzu, roda
/// jogos de Nintendo Switch — pedido do usuário: "tipo o Pokémon"), item
/// antigo do roteiro retomado na v0.9.15 e reformulado na v0.9.16.
///
/// <para>
/// <b>Como acha os jogos (v0.9.16, duas fontes, nesta ordem):</b>
/// </para>
/// <list type="number">
/// <item><b>Pastas ditas pelo usuário</b> (AppSettings.EdenRomFolders, aba
/// "Emuladores" de Configurações) — fonte PRIMÁRIA, escaneadas
/// recursivamente. Pedido do próprio usuário ("será que não seria viável
/// indicar um campo de diretorios para room do eden?") e hoje o caminho
/// principal justamente porque não depende de adivinhar nada.</item>
/// <item><b>Config do próprio Eden</b> (<c>%AppData%\eden\config\qt-config.ini</c>,
/// seção <c>[UI]</c>, array <c>Paths\gamedirs</c>) — só FALLBACK, quando a
/// lista acima está vazia. Era a única fonte na v0.9.15, e isso era um
/// problema: esse formato foi DEDUZIDO por analogia com o Yuzu e nunca
/// foi visto num arquivo de verdade por quem escreveu isto. Se o Eden
/// mudou o formato, este fallback simplesmente não acha nada — e aí a
/// pasta explícita do item 1 resolve.</item>
/// </list>
///
/// <para>
/// <b>Como abre o jogo:</b> <c>eden.exe -f -g "caminho\do\jogo.nsp"</c>
/// (<c>-f</c> tela cheia, que faz sentido no handheld; <c>-g</c> o jogo).
/// Essa sintaxe foi pesquisada na v0.9.15 contra um PR do próprio Yuzu no
/// GitHub + um relato de quem testou no LaunchBox, e <b>CONFIRMADA pelo
/// usuário na v0.9.16</b> contra um atalho real da área de trabalho dele:
/// <c>"C:\Eden Emu\eden.exe" -f -g "D:/Roms\Switch\Pokemon NSP.nsp"</c>
/// (note que ali as barras estão até misturadas e funciona — o Qt não é
/// chato com separador, então não precisa normalizar caminho aqui).
/// </para>
///
/// <para>
/// O caminho do <c>eden.exe</c> vem de AppSettings.EdenExecutablePath —
/// detectado sozinho no primeiro boot quando possível (ver
/// <see cref="EdenInstallationDetector"/>) ou apontado na mão na aba
/// Emuladores. Sem ele, este scanner devolve lista vazia: não adianta
/// listar um jogo que não teria como abrir.
/// </para>
///
/// <para>
/// Nunca lança: qualquer coisa fora do esperado (pasta que sumiu, disco
/// sem permissão, config em formato diferente) só resulta em menos jogos
/// na lista, nunca em erro que derrube o app ou os outros scanners.
/// </para>
/// </summary>
public sealed class EdenLibraryScanner : IStoreLibraryScanner
{
    /// <summary>
    /// .nsp/.xci são os formatos que o usuário usa de fato (o atalho dele
    /// aponta pra um .nsp). .nsz/.xcz são os mesmos dois comprimidos,
    /// comuns em quem guarda ROM em disco pequeno de handheld — se o Eden
    /// instalado não abrir esses, é só tirar daqui.
    /// </summary>
    private static readonly string[] RomExtensions = [".nsp", ".xci", ".nsz", ".xcz"];

    private readonly string? _edenExecutablePath;
    private readonly IReadOnlyList<string> _romFolders;
    private readonly string _configPathOverride;

    public EdenLibraryScanner(string? edenExecutablePath, IReadOnlyList<string>? romFolders = null, string? configPathOverride = null)
    {
        _edenExecutablePath = edenExecutablePath;
        _romFolders = romFolders ?? [];
        _configPathOverride = configPathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "eden", "config", "qt-config.ini");
    }

    public IReadOnlyList<GameEntry> ScanInstalledGames()
    {
        var games = new List<GameEntry>();

        // Sem o caminho do eden.exe (nem detectado nem configurado), não
        // tem como abrir nenhum jogo que a gente ache — nem vale escanear.
        if (string.IsNullOrWhiteSpace(_edenExecutablePath) || !File.Exists(_edenExecutablePath))
        {
            return games;
        }

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (dir, deepScan) in ResolveGameDirs())
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
    /// Pastas ditas pelo usuário primeiro (sempre recursivas: quem aponta
    /// "D:\Roms\Switch" espera que subpasta por console/região também
    /// conte). Só se não tiver nenhuma é que tenta ler a config do Eden,
    /// onde o `deep_scan` de cada pasta é respeitado como está lá.
    /// </summary>
    private List<(string Path, bool DeepScan)> ResolveGameDirs()
    {
        var configured = _romFolders
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => (Path: f.Trim(), DeepScan: true))
            .ToList();

        if (configured.Count > 0) return configured;

        try
        {
            if (!File.Exists(_configPathOverride)) return new List<(string Path, bool DeepScan)>();
            return TryParseGameDirs(_configPathOverride);
        }
        catch
        {
            return new List<(string Path, bool DeepScan)>();
        }
    }

    /// <summary>
    /// Lê a seção [UI] do qt-config.ini procurando o array
    /// "Paths\gamedirs" (ver comentário da classe pro formato esperado e
    /// pro aviso de que ele nunca foi verificado num arquivo real).
    /// Nunca lança — qualquer formato inesperado só resulta em lista
    /// vazia.
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
        var size = 0; // inicializado explicitamente: sem isso o compilador não
                      // sabe que "size" está sempre atribuído quando hasSize é
                      // true, por causa do curto-circuito do "&&" abaixo.
        var hasSize = ui.TryGetValue(@"Paths\gamedirs\size", out var sizeText) && int.TryParse(sizeText, out size);
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
