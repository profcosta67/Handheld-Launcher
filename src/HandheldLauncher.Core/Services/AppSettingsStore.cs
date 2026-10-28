using System.Text.Json;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HandheldLauncher", "settings.json");

    /// <summary>
    /// Loads settings.json, creating it with empty fields if it doesn't
    /// exist yet — so there's a discoverable file to hand-edit until a real
    /// settings screen exists.
    /// </summary>
    public static AppSettings Load(string? pathOverride = null)
    {
        var path = pathOverride ?? DefaultPath;

        if (!File.Exists(path))
        {
            var fresh = new AppSettings();
            Save(fresh, path);
            return fresh;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings, string? pathOverride = null)
    {
        var path = pathOverride ?? DefaultPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
