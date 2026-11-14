using System.Text.Json;

namespace HandheldLauncher.Core.Services.SmartTdp;

/// <summary>O que o TDP inteligente aprendeu sobre um jogo.</summary>
public sealed class SmartTdpEntry
{
    public int Watts { get; set; }
    public int TargetFps { get; set; }
    public string Title { get; set; } = "";
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>
/// Memória por jogo do TDP inteligente, num arquivo próprio ao lado do
/// settings.json (assim gravar o aprendizado nunca pisa nas configurações).
/// </summary>
public sealed class SmartTdpStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, SmartTdpEntry>? _entries;

    public SmartTdpStore(string? pathOverride = null)
    {
        _path = pathOverride ?? Path.Combine(
            Path.GetDirectoryName(AppSettingsStore.DefaultPath) ?? ".", "smart-tdp.json");
    }

    public SmartTdpEntry? Get(string gameId, int targetFps)
    {
        lock (_gate)
        {
            return Load().TryGetValue(gameId, out var e) && e.TargetFps == targetFps && e.Watts > 0 ? e : null;
        }
    }

    public void Set(string gameId, string title, int watts, int targetFps)
    {
        lock (_gate)
        {
            var all = Load();
            all[gameId] = new SmartTdpEntry { Watts = watts, TargetFps = targetFps, Title = title, UpdatedUtc = DateTime.UtcNow };
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_path, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // Aprendizado é melhor esforço: sem disco, vale só para esta sessão.
            }
        }
    }

    private Dictionary<string, SmartTdpEntry> Load()
    {
        if (_entries is not null) return _entries;
        try
        {
            if (File.Exists(_path))
                _entries = JsonSerializer.Deserialize<Dictionary<string, SmartTdpEntry>>(File.ReadAllText(_path));
        }
        catch
        {
            _entries = null;
        }
        return _entries ??= new Dictionary<string, SmartTdpEntry>();
    }
}
