using System.Text.Json;
using System.Text.RegularExpressions;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

/// <summary>Resumo de uma varredura de ports — alimenta o "Testar agora" da aba Emuladores.</summary>
public sealed record PortsScanReport(int Games, int FoldersScanned, int FoldersMissing, int FoldersWithoutExe,
    IReadOnlyList<string> RootsUsed, bool AutoDetected);

/// <summary>
/// Ports (pedido do usuário, 24/11: "uma pasta raiz (port) e dentro dela vários jogos com port,
/// queria que a busca por esses executáveis bem como seus jogos fossem detectados automático").
/// Port = jogo de console refeito para PC pela comunidade (Ship of Harkinian, sm64, Perfect Dark,
/// Sonic 3 A.I.R., OpenGOAL...) — cada um é um programa próprio, sem emulador.
///
/// <para><b>Organização esperada:</b> uma pasta raiz com <b>uma subpasta por jogo</b>:</para>
/// <code>
/// D:\Emuladores\Ports\
///   Ship of Harkinian\soh.exe
///   Super Mario 64\sm64.us.f3dex2e.exe
///   Perfect Dark\pd.x86_64.exe
/// </code>
///
/// <para><b>Qual .exe abre o jogo</b> (por subpasta, até 3 níveis abaixo):</para>
/// <list type="number">
/// <item><c>port.json</c> na subpasta, se existir — manda em tudo:
/// <c>{ "nome": "...", "exe": "bin\\jogo.exe", "args": "--fullscreen" }</c>.</item>
/// <item>Executável de port conhecido (tabela <see cref="KnownPorts"/>), que também dá o nome
/// oficial do jogo (melhor para achar capa).</item>
/// <item>O .exe com o nome mais parecido com a pasta; empate → o que está mais perto da raiz da
/// subpasta, depois o maior. Desinstaladores, instaladores, "crash handler", atualizadores e
/// ferramentas ficam de fora.</item>
/// </list>
///
/// <para><b>Onde procura a pasta raiz:</b> as pastas informadas em Configurações → Biblioteca →
/// Ports. Sem nenhuma informada, procura sozinho em cada disco: <c>\Ports</c>, <c>\Port</c>,
/// <c>\Emuladores\Ports</c>, <c>\Emuladores\Port</c>, <c>\Emulators\Ports</c>,
/// <c>\Games\Ports</c>, <c>\Jogos\Ports</c> e em <c>%USERPROFILE%\Ports</c> /
/// <c>%USERPROFILE%\Games\Ports</c>.</para>
///
/// <para>Nunca lança: pasta sumida, sem permissão ou exe estranho só resultam em menos jogos.</para>
/// </summary>
public sealed class PortsLibraryScanner : IStoreLibraryScanner
{
    public const string IdPrefix = "port_";
    public const string OverrideFileName = "port.json";
    private const int MaxDepth = 3;

    /// <summary>Executáveis de ports conhecidos (sem ".exe", minúsculo) → nome oficial do jogo.</summary>
    public static readonly IReadOnlyDictionary<string, string> KnownPorts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["soh"] = "The Legend of Zelda: Ocarina of Time",
        ["2ship"] = "The Legend of Zelda: Majora's Mask",
        ["zelda64recompiled"] = "The Legend of Zelda: Majora's Mask",
        ["zelda64recomp"] = "The Legend of Zelda: Majora's Mask",
        ["sm64"] = "Super Mario 64",
        ["sm64.us"] = "Super Mario 64",
        ["sm64.us.f3dex2e"] = "Super Mario 64",
        ["sm64.jp.f3dex2e"] = "Super Mario 64",
        ["sm64ex"] = "Super Mario 64",
        ["sm64coopdx"] = "Super Mario 64 (coop)",
        ["render96ex"] = "Super Mario 64",
        ["spaghettify"] = "Mario Kart 64",
        ["spaghettikart"] = "Mario Kart 64",
        ["starship"] = "Star Fox 64",
        ["pd"] = "Perfect Dark",
        ["pd.x86_64"] = "Perfect Dark",
        ["pd.i686"] = "Perfect Dark",
        ["banjorecompiled"] = "Banjo-Kazooie",
        ["dkr"] = "Diddy Kong Racing",
        ["sonic3air"] = "Sonic 3 & Knuckles",
        ["sonicmania"] = "Sonic Mania",
        ["rsdkv5"] = "Sonic Mania",
        ["rsdkv5u"] = "Sonic Mania",
        ["opengoal-launcher"] = "Jak and Daxter: The Precursor Legacy",
        ["re3"] = "Grand Theft Auto III",
        ["revc"] = "Grand Theft Auto: Vice City",
        ["openrct2"] = "RollerCoaster Tycoon 2",
        ["devilutionx"] = "Diablo",
        ["openmw-launcher"] = "The Elder Scrolls III: Morrowind",
        ["openmw"] = "The Elder Scrolls III: Morrowind",
        ["ioquake3"] = "Quake III Arena",
        ["yquake2"] = "Quake II",
        ["srb2win"] = "Sonic Robo Blast 2",
        ["srb2kart"] = "Sonic Robo Blast 2 Kart",
        ["ringracers"] = "Dr. Robotnik's Ring Racers",
    };

    /// <summary>Executável que nunca é o jogo.</summary>
    private static readonly Regex Excluded = new(
        @"^(unins.*|uninstall.*|setup.*|install.*|vc_?redist.*|vcredist.*|dxsetup|dxwebsetup|directx.*|dotnet.*|" +
        @".*crash.*|.*updater.*|update|.*(_|-)?debug|.*config(urator)?|.*editor|.*server|.*dedicated.*|" +
        @"7z.*|ffmpeg|ffprobe|python.*|pythonw|java.*|cmd|powershell|" +
        @"otrgui|otrgen|assetextractor|extract.*|.*extractor|.*assembler|.*compiler|.*packer|retarget.*|" +
        @"xenia.*|ryujinx|eden|yuzu|citra|cemu|dolphin|pcsx2.*|duckstation.*|rpcs3|ppsspp.*|retroarch|" +
        @"unitycrashhandler.*|crashpad_handler|nvngx.*|dxcompiler|notification_helper|quicksfv)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IReadOnlyList<string> _roots;
    private readonly Func<IEnumerable<string>> _autoRoots;

    public PortsLibraryScanner(IReadOnlyList<string>? roots, Func<IEnumerable<string>>? autoRootsOverride = null)
    {
        _roots = (roots ?? []).Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToList();
        _autoRoots = autoRootsOverride ?? DefaultAutoRoots;
    }

    public IReadOnlyList<GameEntry> ScanInstalledGames() => ScanInstalledGames(out _);

    public IReadOnlyList<GameEntry> ScanInstalledGames(out PortsScanReport report)
    {
        var games = new List<GameEntry>();
        var autoDetected = _roots.Count == 0;
        var roots = autoDetected ? SafeAutoRoots() : _roots.ToList();
        int scanned = 0, missing = 0, withoutExe = 0;
        var used = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (!SafeDirExists(root)) { if (!autoDetected) missing++; continue; }
            scanned++;
            used.Add(root);
            foreach (var gameDir in FindGameFolders(root))
            {
                try
                {
                    var entry = BuildEntry(gameDir);
                    if (entry is null) { withoutExe++; continue; }
                    if (seen.Add(entry.ExecutablePath!)) games.Add(entry);
                }
                catch
                {
                    // pula só esta subpasta
                }
            }
        }

        report = new PortsScanReport(games.Count, scanned, missing, withoutExe, used, autoDetected);
        return games;
    }

    /// <summary>Até quantos níveis de pastas de categoria (ex.: Ports\N64\Zelda) a busca desce.</summary>
    private const int MaxCategoryDepth = 3;

    /// <summary>Pastas que costumam guardar o .exe dentro de um jogo — não são jogos separados.</summary>
    private static readonly HashSet<string> BinaryFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "bin64", "binaries", "x64", "x86", "x86_64", "win64", "win32", "windows", "build", "release",
        "game", "app", "dist", "exe", "launcher", "runtime",
    };

    /// <summary>
    /// 24/11 (pedido do usuário: "habilita também para buscar subpastas"): as pastas de jogo dentro
    /// da raiz, aceitando pastas de categoria no meio (ex.: <c>C:\Ports\Nintendo 64\Ship of Harkinian</c>).
    /// <list type="bullet">
    /// <item>Pasta com .exe direto ou com <c>port.json</c> = um jogo.</item>
    /// <item>Pasta sem .exe direto: se as subpastas com .exe são só do tipo "bin", "x64", "build"…,
    /// ela é um jogo (o .exe está lá dentro); se não, é uma categoria e cada subpasta é olhada do
    /// mesmo jeito (até <see cref="MaxCategoryDepth"/> níveis).</item>
    /// </list>
    /// </summary>
    internal static IEnumerable<string> FindGameFolders(string root)
    {
        var result = new List<string>();
        foreach (var child in SafeSubdirs(root)) Classify(child, 1, result);
        return result;
    }

    private static void Classify(string dir, int level, List<string> into)
    {
        if (IsSkippedFolder(Path.GetFileName(dir))) return;
        if (File.Exists(Path.Combine(dir, OverrideFileName)) || HasDirectGameExe(dir)) { into.Add(dir); return; }

        var childrenWithExe = SafeSubdirs(dir).Where(c => !IsSkippedFolder(Path.GetFileName(c)) && HasExeWithin(c, MaxDepth)).ToList();
        if (childrenWithExe.Count == 0) { into.Add(dir); return; }   // sem .exe: conta como "subpasta sem .exe"
        if (level >= MaxCategoryDepth || childrenWithExe.All(c => BinaryFolderNames.Contains(Path.GetFileName(c))))
        {
            into.Add(dir);
            return;
        }
        foreach (var child in childrenWithExe) Classify(child, level + 1, into);
    }

    private static bool IsSkippedFolder(string name) =>
        name.StartsWith('.') || name.Equals("redist", StringComparison.OrdinalIgnoreCase)
        || name.Equals("_CommonRedist", StringComparison.OrdinalIgnoreCase)
        || name.Equals("tools", StringComparison.OrdinalIgnoreCase)
        || name.Equals("mods", StringComparison.OrdinalIgnoreCase);

    private static bool HasDirectGameExe(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*.exe").Any(f => !Excluded.IsMatch(Path.GetFileNameWithoutExtension(f))); }
        catch { return false; }
    }

    private static bool HasExeWithin(string dir, int depth)
    {
        if (HasDirectGameExe(dir) || File.Exists(Path.Combine(dir, OverrideFileName))) return true;
        if (depth <= 0) return false;
        return SafeSubdirs(dir).Any(c => !IsSkippedFolder(Path.GetFileName(c)) && HasExeWithin(c, depth - 1));
    }

    /// <summary>Monta o jogo de uma subpasta, ou null se não achar um executável.</summary>
    internal static GameEntry? BuildEntry(string gameDir)
    {
        var folderName = Path.GetFileName(gameDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var ov = ReadOverride(gameDir);

        string? exe = null;
        if (ov?.Exe is { Length: > 0 } rel)
        {
            var candidate = Path.GetFullPath(Path.Combine(gameDir, rel));
            if (File.Exists(candidate)) exe = candidate;
        }
        exe ??= PickExecutable(gameDir, folderName);
        if (exe is null) return null;

        var exeName = Path.GetFileNameWithoutExtension(exe);
        var title = ov?.Name is { Length: > 0 } n ? n
            : KnownPorts.TryGetValue(exeName, out var known) && known.Length > 0 ? known
            : CleanFolderName(folderName);

        return new GameEntry
        {
            Id = IdPrefix + SanitizeForId(exe),
            Title = title,
            Source = GameSource.Emulator,
            ExecutablePath = exe,
            LaunchArguments = string.IsNullOrWhiteSpace(ov?.Args) ? null : ov!.Args,
            WorkingDirectory = Path.GetDirectoryName(exe),
            InstallDirectory = gameDir,
        };
    }

    /// <summary>
    /// Escolhe o executável da subpasta. Pontuação: port conhecido (+100), nome parecido com a pasta
    /// (até +50), perto da raiz (−10 por nível), maior arquivo (desempate).
    /// </summary>
    internal static string? PickExecutable(string gameDir, string folderName)
    {
        var candidates = new List<(string Path, int Depth, long Size)>();
        Collect(gameDir, 0, candidates);
        if (candidates.Count == 0) return null;

        var folderWords = Words(folderName);
        return candidates
            .Select(c =>
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(c.Path);
                var score = 0.0;
                if (KnownPorts.ContainsKey(name)) score += 100;
                score += 50 * Similarity(folderWords, Words(name));
                score -= 10 * c.Depth;
                if (name.Contains("launcher", StringComparison.OrdinalIgnoreCase)) score -= 5;   // prefere o jogo ao lançador...
                return (c.Path, Score: score, c.Size);
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Size)
            .First().Path;
    }

    private static void Collect(string dir, int depth, List<(string, int, long)> into)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*.exe"))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                if (Excluded.IsMatch(name)) continue;
                long size = 0;
                try { size = new FileInfo(f).Length; } catch { }
                into.Add((f, depth, size));
            }
            if (depth >= MaxDepth) return;
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var n = Path.GetFileName(sub);
                // pastas que só têm ferramentas/dependências
                if (IsSkippedFolder(n)) continue;
                Collect(sub, depth + 1, into);
            }
        }
        catch
        {
            // sem permissão: segue com o que tiver
        }
    }

    private sealed class PortOverride
    {
        public string? Name { get; set; }
        public string? Exe { get; set; }
        public string? Args { get; set; }
    }

    private static PortOverride? ReadOverride(string gameDir)
    {
        try
        {
            var path = Path.Combine(gameDir, OverrideFileName);
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            string? Get(params string[] keys)
            {
                foreach (var p in doc.RootElement.EnumerateObject())
                    if (keys.Any(k => p.Name.Equals(k, StringComparison.OrdinalIgnoreCase)) && p.Value.ValueKind == JsonValueKind.String)
                        return p.Value.GetString();
                return null;
            }
            return new PortOverride { Name = Get("nome", "name", "titulo", "title"), Exe = Get("exe", "executavel", "executable"), Args = Get("args", "argumentos", "arguments") };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>"Super_Mario_64-PC-Port v1.2" → "Super Mario 64".</summary>
    internal static string CleanFolderName(string folder)
    {
        var s = folder.Replace('_', ' ');
        s = Regex.Replace(s, @"[\[(].*?[\])]", " ");                                   // (…) [...]
        s = Regex.Replace(s, @"\b(v|ver|version)\s*\d[\w.\-]*", " ", RegexOptions.IgnoreCase);
        s = s.Replace('-', ' ').Replace('.', ' ');
        s = Regex.Replace(s, @"\b(pc|port|win(dows)?|x64|x86_64|64bit|portable|recomp(iled)?)\b", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s*-\s*$", "");
        s = Regex.Replace(s, @"\s{2,}", " ").Trim(' ', '-');
        return s.Length > 0 ? s : folder;
    }

    private static List<string> Words(string s) =>
        Regex.Split(s.ToLowerInvariant(), @"[^a-z0-9]+").Where(t => t.Length > 0).ToList();

    /// <summary>
    /// Parecença de 0 a 1 entre o nome da pasta e o do exe: palavras em comum, um nome contido no
    /// outro ("Sonic3AIR" em "Sonic 3 AIR") ou sigla ("Ship of Harkinian" ~ "soh").
    /// </summary>
    internal static double Similarity(List<string> folder, List<string> exe)
    {
        if (folder.Count == 0 || exe.Count == 0) return 0;
        var f = folder.ToHashSet();
        var e = exe.ToHashSet();
        var jaccard = (double)f.Intersect(e).Count() / f.Union(e).Count();
        var fJoined = string.Concat(folder);
        var eJoined = string.Concat(exe);
        var contains = eJoined.Length >= 3 && (fJoined.Contains(eJoined) || eJoined.Contains(fJoined)) ? 0.7 : 0;
        var initials = string.Concat(folder.Select(t => t[0]));
        var acronym = eJoined.Length >= 2 && initials == eJoined ? 0.8 : 0;
        return Math.Max(jaccard, Math.Max(contains, acronym));
    }

    private static string SanitizeForId(string path) =>
        Regex.Replace(path.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');

    private List<string> SafeAutoRoots()
    {
        try { return _autoRoots().Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }
        catch { return new List<string>(); }
    }

    /// <summary>Lugares comuns para a pasta de ports, em cada disco fixo e na pasta do usuário.</summary>
    public static IEnumerable<string> DefaultAutoRoots()
    {
        var relative = new[] { "Ports", "Port", Path.Combine("Emuladores", "Ports"), Path.Combine("Emuladores", "Port"),
            Path.Combine("Emulators", "Ports"), Path.Combine("Games", "Ports"), Path.Combine("Jogos", "Ports") };
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); } catch { drives = []; }
        foreach (var d in drives)
        {
            bool ok;
            try { ok = d.DriveType == DriveType.Fixed && d.IsReady; } catch { ok = false; }
            if (!ok) continue;
            foreach (var r in relative) yield return Path.Combine(d.RootDirectory.FullName, r);
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            yield return Path.Combine(home, "Ports");
            yield return Path.Combine(home, "Games", "Ports");
        }
    }

    private static bool SafeDirExists(string path)
    {
        try { return Directory.Exists(path); } catch { return false; }
    }

    private static IEnumerable<string> SafeSubdirs(string root)
    {
        try { return Directory.GetDirectories(root); } catch { return []; }
    }
}
