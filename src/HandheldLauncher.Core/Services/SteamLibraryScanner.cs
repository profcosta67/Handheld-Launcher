using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// Finds the local Steam install and reads its library folders + installed
/// app manifests directly from disk (no Steam Web API / no running Steam
/// required). This is the same approach Playnite, LaunchBox, etc. use.
///
/// We deliberately never shell out to Steam here — scanning is pure file
/// reads, launching later will go through "steam://rungameid/{appid}" so
/// Steam's own client handles DRM/overlay/updates correctly.
/// </summary>
public sealed class SteamLibraryScanner : ISteamLibraryScanner
{
    // Apps that show up in appmanifest_*.acf but aren't games a player would
    // want tiled in the library. Matched by name, case-insensitive substring.
    // Heuristic for v1 — revisit with an explicit appid denylist if this
    // over/under-filters on a real library.
    private static readonly string[] NonGameNameHints =
    {
        "steamworks common redistributables",
        "steam linux runtime",
        "proton ",
        "steam controller configs",
    };

    private readonly string? _steamRoot;

    public SteamLibraryScanner(string? steamRootOverride = null)
    {
        _steamRoot = steamRootOverride ?? FindSteamRoot();
    }

    public bool IsSteamInstalled => _steamRoot is not null;

    public IReadOnlyList<GameEntry> ScanInstalledGames()
    {
        var results = new List<GameEntry>();
        if (_steamRoot is null) return results;

        foreach (var libraryPath in GetLibraryFolders(_steamRoot))
        {
            var steamappsDir = Path.Combine(libraryPath, "steamapps");
            if (!Directory.Exists(steamappsDir)) continue;

            foreach (var manifestPath in Directory.EnumerateFiles(steamappsDir, "appmanifest_*.acf"))
            {
                var entry = TryParseManifest(manifestPath, libraryPath);
                if (entry is not null) results.Add(entry);
            }
        }

        return results;
    }

    private static GameEntry? TryParseManifest(string manifestPath, string libraryPath)
    {
        VdfNode root;
        try
        {
            root = VdfParser.ParseFile(manifestPath);
        }
        catch (IOException)
        {
            // Manifest can be mid-write if Steam is updating something; skip and
            // pick it up on the next scan rather than failing the whole pass.
            return null;
        }

        var appState = root["AppState"];
        if (appState is null) return null;

        var appIdText = appState.GetString("appid");
        var name = appState.GetString("name");
        var installDir = appState.GetString("installdir");

        if (string.IsNullOrWhiteSpace(appIdText) ||
            string.IsNullOrWhiteSpace(name) ||
            !uint.TryParse(appIdText, out var appId))
        {
            return null;
        }

        if (NonGameNameHints.Any(hint => name.Contains(hint, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var installPath = string.IsNullOrWhiteSpace(installDir)
            ? null
            : Path.Combine(libraryPath, "steamapps", "common", installDir);

        return new GameEntry
        {
            Id = $"steam_{appId}",
            Title = name,
            Source = GameSource.Steam,
            SteamAppId = appId,
            InstallDirectory = installPath,
        };
    }

    /// <summary>
    /// Reads steamapps/libraryfolders.vdf to get every drive/folder the user
    /// has added as a Steam library, including the default one under the
    /// Steam install itself.
    /// </summary>
    private static IEnumerable<string> GetLibraryFolders(string steamRoot)
    {
        yield return steamRoot;

        var libraryFoldersVdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFoldersVdf)) yield break;

        VdfNode root;
        try
        {
            root = VdfParser.ParseFile(libraryFoldersVdf);
        }
        catch (IOException)
        {
            yield break;
        }

        var libraryFolders = root["libraryfolders"];
        if (libraryFolders is null) yield break;

        foreach (var (_, entry) in libraryFolders.Children)
        {
            var path = entry.GetString("path");
            if (!string.IsNullOrWhiteSpace(path) &&
                !string.Equals(path, steamRoot, StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }
    }

    /// <summary>
    /// Checks the usual default install locations. We avoid touching the
    /// Windows registry here on purpose (it would need the extra
    /// Microsoft.Win32.Registry package) — if none of these hit, the caller
    /// should fall back to asking the user to point at their Steam folder.
    /// </summary>
    private static string? FindSteamRoot()
    {
        var candidates = new List<string>();

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(programFilesX86))
        {
            candidates.Add(Path.Combine(programFilesX86, "Steam"));
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "Steam"));
        }

        // Handhelds are frequently set up with Steam on a second drive to
        // save space on the OS drive — check the common drive letters too.
        foreach (var drive in new[] { "D", "E", "F" })
        {
            candidates.Add($@"{drive}:\SteamLibrary");
            candidates.Add($@"{drive}:\Steam");
        }

        return candidates.FirstOrDefault(c => File.Exists(Path.Combine(c, "steam.exe")));
    }
}
