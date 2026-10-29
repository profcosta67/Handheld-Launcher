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
/// <summary>
/// Resumo de uma varredura do Eden (v0.9.17) — alimenta o botão "Testar
/// agora" da aba Emuladores. <paramref name="UsedEdenConfigFallback"/>
/// diz que NENHUMA pasta foi informada e o scanner caiu na config do
/// próprio Eden, que é justamente o caso em que o resultado costuma
/// surpreender (achar pouco, ou achar coisa de outra pasta).
/// </summary>
public sealed record EdenScanReport(
    int Games,
    int SkippedAddOns,
    int FoldersScanned,
    int FoldersMissing,
    bool UsedEdenConfigFallback,
    bool EdenExecutableFound);

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

    public IReadOnlyList<GameEntry> ScanInstalledGames() => ScanInstalledGames(out _);

    /// <summary>
    /// Mesma varredura, devolvendo também um resumo do que aconteceu —
    /// usado pelo botão "Testar agora" da aba Emuladores (v0.9.17), pra o
    /// usuário ver na hora quantos jogos apareceram, quantos arquivos
    /// foram ignorados por serem DLC/update e se alguma pasta configurada
    /// não existe. Antes disso, a única forma de saber se a configuração
    /// tinha funcionado era salvar, reiniciar o launcher e olhar a
    /// biblioteca — o que custou uma rodada inteira de diagnóstico às
    /// cegas pro usuário.
    /// </summary>
    public IReadOnlyList<GameEntry> ScanInstalledGames(out EdenScanReport report)
    {
        var games = new List<GameEntry>();
        var skippedAddOns = 0;
        var foldersScanned = 0;
        var foldersMissing = 0;

        // Sem o caminho do eden.exe (nem detectado nem configurado), não
        // tem como abrir nenhum jogo que a gente ache — nem vale escanear.
        if (string.IsNullOrWhiteSpace(_edenExecutablePath) || !File.Exists(_edenExecutablePath))
        {
            report = new EdenScanReport(0, 0, 0, 0, false, EdenExecutableFound: false);
            return games;
        }

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedFallback = _romFolders.All(string.IsNullOrWhiteSpace);

        foreach (var (dir, deepScan) in ResolveGameDirs())
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                foldersMissing++;
                continue;
            }

            foldersScanned++;

            List<string> files;
            try
            {
                // ToList dentro do try de propósito: EnumerateFiles é
                // preguiçoso, então sem materializar aqui as exceções de
                // acesso estourariam só no foreach abaixo, FORA destes
                // catch (e aí subiriam pro chamador em vez de só pular
                // esta pasta).
                files = Directory.EnumerateFiles(dir, "*.*", deepScan ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .Where(f => RomExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                    .ToList();
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

                    // DLC e update são .nsp igual ao jogo base, mas não
                    // abrem como jogo nenhum — ver LooksLikeAddOnOrUpdate.
                    if (LooksLikeAddOnOrUpdate(title))
                    {
                        skippedAddOns++;
                        continue;
                    }

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

        report = new EdenScanReport(games.Count, skippedAddOns, foldersScanned, foldersMissing, usedFallback, EdenExecutableFound: true);
        return games;
    }

    /// <summary>
    /// v0.9.17, BUG REPORTADO: o primeiro jogo que apareceu na biblioteca
    /// do usuário era uma DLC ("ele detectou apenas uma room que na vdd eh
    /// uma dlc nsp do digimon time strange"). DLC e update são arquivos
    /// .nsp iguais ao jogo base, mas não abrem como jogo nenhum — poluem a
    /// biblioteca com entradas que só dão erro se clicadas.
    ///
    /// <para>
    /// Critério 1, title ID (o confiável): dumps de Switch costumam trazer
    /// o title ID de 16 dígitos hex entre colchetes no nome do arquivo, e
    /// a convenção da Nintendo é fixa — jogo base termina em <c>000</c>,
    /// update termina em <c>800</c>, e DLC termina em qualquer outro
    /// valor. Então, quando o nome tem title ID, ele decide sozinho.
    /// Exemplos reais da pasta do usuário:
    /// <c>[0100F43008C44800]</c> → update;
    /// <c>[010062E01FE0D00A]</c> e <c>[0100F43008C45002]</c> → DLC.
    /// </para>
    ///
    /// <para>
    /// Critério 2, nome (pros arquivos sem title ID): "dlc" como palavra,
    /// "update"/"upd" como palavra, ou prefixo "v-" (padrão de arquivo de
    /// atualização na pasta do usuário:
    /// <c>v-digimon_story_time_stranger_v196608.nsp</c>). Mantém, de
    /// propósito, nomes sem marcação nenhuma —
    /// <c>Pokemon NSP.nsp</c>, <c>Pokemon Legends Arceus-002.nsp</c> e
    /// <c>Digimon Story Time Stranger Switch NSP Base Game.nsp</c> todos
    /// passam.
    /// </para>
    ///
    /// <para>
    /// Se algum jogo base for filtrado por engano aqui, o usuário ainda
    /// pode adicioná-lo pela tela "Adicionar jogo" à mão (foi assim que
    /// ele testou o Pokémon antes desta correção) — e o contador do botão
    /// "Testar agora" mostra quantos arquivos foram ignorados, pra o
    /// número não ser invisível.
    /// </para>
    /// </summary>
    private static bool LooksLikeAddOnOrUpdate(string fileName)
    {
        foreach (var token in BracketTokens(fileName))
        {
            if (token.Length == 16 && token.All(Uri.IsHexDigit))
            {
                return !token.EndsWith("000", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (fileName.StartsWith("v-", StringComparison.OrdinalIgnoreCase)) return true;

        return ContainsWord(fileName, "dlc")
            || ContainsWord(fileName, "update")
            || ContainsWord(fileName, "upd");
    }

    /// <summary>Conteúdo de cada [...] do nome, na ordem em que aparecem.</summary>
    private static IEnumerable<string> BracketTokens(string fileName)
    {
        var start = -1;
        for (var i = 0; i < fileName.Length; i++)
        {
            if (fileName[i] == '[')
            {
                start = i + 1;
            }
            else if (fileName[i] == ']' && start >= 0 && i > start)
            {
                yield return fileName[start..i];
                start = -1;
            }
        }
    }

    /// <summary>
    /// "dlc" como palavra isolada — exige que os vizinhos não sejam letra,
    /// pra não filtrar um jogo cujo nome só contenha essas letras por
    /// acidente no meio de outra palavra.
    /// </summary>
    private static bool ContainsWord(string text, string word)
    {
        var from = 0;
        while (from <= text.Length - word.Length)
        {
            var at = text.IndexOf(word, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return false;

            var beforeOk = at == 0 || !char.IsLetter(text[at - 1]);
            var afterIndex = at + word.Length;
            var afterOk = afterIndex >= text.Length || !char.IsLetter(text[afterIndex]);

            if (beforeOk && afterOk) return true;
            from = at + 1;
        }

        return false;
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
