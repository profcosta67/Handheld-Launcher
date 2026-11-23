using System.Text.Json;

namespace HandheldLauncher.Core.Services.Power;

/// <summary>
/// Amostras do coletor de bateria em disco (JSONL), guardadas por <see cref="Keep"/>. As
/// amostras ficam num buffer e vão para o disco a cada poucos minutos — gravar a cada 30 s
/// acordaria o disco à toa, justamente numa ferramenta de economia de bateria.
/// </summary>
public sealed class PowerLogStore
{
    public static readonly TimeSpan Keep = TimeSpan.FromDays(3);
    private const int FlushEvery = 10;

    private static readonly Lazy<PowerLogStore> _shared = new(() => new PowerLogStore());
    public static PowerLogStore Shared => _shared.Value;

    private readonly string _path;
    private readonly object _gate = new();
    private readonly List<PowerSample> _pending = new();
    private bool _pruned;

    public PowerLogStore(string? pathOverride = null)
    {
        _path = pathOverride ?? Path.Combine(
            Path.GetDirectoryName(AppSettingsStore.DefaultPath) ?? ".", "battery", "amostras.jsonl");
    }

    public string FilePath => _path;

    public void Append(PowerSample sample)
    {
        lock (_gate)
        {
            _pending.Add(sample);
            if (_pending.Count >= FlushEvery) FlushUnlocked();
        }
    }

    /// <summary>Grava o que estiver no buffer (chamado também ao fechar o launcher).</summary>
    public void Flush()
    {
        lock (_gate) FlushUnlocked();
    }

    private void FlushUnlocked()
    {
        if (_pending.Count == 0) return;
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (!_pruned) { PruneUnlocked(DateTime.UtcNow); _pruned = true; }
            File.AppendAllLines(_path, _pending.Select(s => JsonSerializer.Serialize(s)));
            _pending.Clear();
        }
        catch
        {
            // melhor esforço; não deixa o buffer crescer sem limite se o disco falhar
            if (_pending.Count > 200) _pending.RemoveRange(0, _pending.Count - 200);
        }
    }

    /// <summary>Amostras desde <paramref name="sinceUtc"/>, em ordem, incluindo as do buffer.</summary>
    public List<PowerSample> Load(DateTime sinceUtc)
    {
        lock (_gate)
        {
            var list = new List<PowerSample>();
            try
            {
                if (File.Exists(_path))
                    foreach (var line in File.ReadLines(_path))
                    {
                        var s = TryParse(line);
                        if (s is not null && s.Utc >= sinceUtc) list.Add(s);
                    }
            }
            catch
            {
                // arquivo ilegível: segue só com o buffer
            }
            list.AddRange(_pending.Where(s => s.Utc >= sinceUtc));
            list.Sort((a, b) => a.Utc.CompareTo(b.Utc));
            return list;
        }
    }

    private void PruneUnlocked(DateTime utcNow)
    {
        if (!File.Exists(_path)) return;
        var cutoff = utcNow - Keep;
        var keep = File.ReadLines(_path).Where(l => TryParse(l) is { } s && s.Utc >= cutoff).ToList();
        var tmp = _path + ".tmp";
        File.WriteAllLines(tmp, keep);
        File.Move(tmp, _path, overwrite: true);
    }

    private static PowerSample? TryParse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try { return JsonSerializer.Deserialize<PowerSample>(line); }
        catch { return null; }
    }
}
