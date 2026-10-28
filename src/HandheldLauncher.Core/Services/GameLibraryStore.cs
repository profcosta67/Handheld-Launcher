using System.Text.Json;
using System.Text.Json.Serialization;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// JSON-on-disk implementation of IGameLibraryStore. Lives at
/// %AppData%\HandheldLauncher\library.json by default. Kept deliberately
/// dumb (read-whole-file / write-whole-file) — this file will realistically
/// hold a few hundred entries at most, no need for a real database.
/// </summary>
public sealed class GameLibraryStore : IGameLibraryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private LibraryFile? _cache;

    public GameLibraryStore(string? filePathOverride = null)
    {
        _filePath = filePathOverride ?? GetDefaultFilePath();
    }

    public static string GetDefaultFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "HandheldLauncher", "library.json");
    }

    public IReadOnlyList<GameEntry> LoadManualGames() => Load().ManualGames;

    public void AddManualGame(GameEntry game)
    {
        var data = Load();
        data.ManualGames.RemoveAll(g => g.Id == game.Id);
        data.ManualGames.Add(game);
        Save(data);
    }

    public void RemoveManualGame(string id)
    {
        var data = Load();
        data.ManualGames.RemoveAll(g => g.Id == id);
        Save(data);
    }

    public IReadOnlyDictionary<string, GameMetadataOverride> LoadOverrides() => Load().Overrides;

    public void SaveOverride(string gameId, GameMetadataOverride overrideData)
    {
        var data = Load();
        data.Overrides[gameId] = overrideData;
        Save(data);
    }

    private LibraryFile Load()
    {
        if (_cache is not null) return _cache;

        if (!File.Exists(_filePath))
        {
            _cache = new LibraryFile();
            return _cache;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            _cache = JsonSerializer.Deserialize<LibraryFile>(json, JsonOptions) ?? new LibraryFile();
        }
        catch (JsonException)
        {
            // Corrupt file shouldn't crash the launcher — start fresh rather
            // than refusing to boot. The bad file is left on disk for inspection.
            _cache = new LibraryFile();
        }

        return _cache;
    }

    private void Save(LibraryFile data)
    {
        _cache = data;

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(data, JsonOptions);

        // Write to a temp file then move, so a crash mid-write never leaves
        // library.json truncated/corrupt.
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private sealed class LibraryFile
    {
        public List<GameEntry> ManualGames { get; set; } = new();
        public Dictionary<string, GameMetadataOverride> Overrides { get; set; } = new();
    }
}
