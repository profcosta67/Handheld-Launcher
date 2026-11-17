using System.Text.Json;

namespace HandheldLauncher.Core.Services.SmartTdp;

/// <summary>O que o TDP inteligente aprendeu sobre um jogo.</summary>
public sealed class SmartTdpEntry
{
    public int Watts { get; set; }
    public int TargetFps { get; set; }
    public string Title { get; set; } = "";
    public DateTime UpdatedUtc { get; set; }
    /// <summary>Regra de aprendizado que gravou o valor. Valores de regras antigas são ignorados.</summary>
    public int Rev { get; set; }
}

/// <summary>
/// Memória por jogo do TDP inteligente, num arquivo próprio ao lado do
/// settings.json (assim gravar o aprendizado nunca pisa nas configurações).
/// </summary>
public sealed class SmartTdpStore
{
    private static readonly Lazy<SmartTdpStore> _shared = new(() => new SmartTdpStore());
    /// <summary>Instância única do launcher (o serviço e o Quick Settings veem os mesmos valores).</summary>
    public static SmartTdpStore Shared => _shared.Value;

    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, SmartTdpEntry>? _entries;

    public SmartTdpStore(string? pathOverride = null)
    {
        _path = pathOverride ?? Path.Combine(
            Path.GetDirectoryName(AppSettingsStore.DefaultPath) ?? ".", "smart-tdp.json");
    }

    // Um valor por jogo e por meta ("id@60"): trocar a meta no Quick Settings não apaga o
    // que foi aprendido para a outra. Arquivos antigos (chave só com o id) continuam lidos.
    public const int CurrentRev = 2;

    private static string KeyFor(string gameId, int targetFps) => $"{gameId}@{targetFps}";

    public SmartTdpEntry? Get(string gameId, int targetFps)
    {
        lock (_gate)
        {
            var all = Load();
            // Só vale o que foi aprendido pela regra atual: as betas até a 64 gravaram valores
            // altos demais (ex.: 34 W para 60 FPS num jogo que segura 60 com 20 W).
            return all.TryGetValue(KeyFor(gameId, targetFps), out var e) && e.Watts > 0 && e.Rev >= CurrentRev ? e : null;
        }
    }

    public void Set(string gameId, string title, int watts, int targetFps)
    {
        lock (_gate)
        {
            var all = Load();
            if (all.TryGetValue(gameId, out var legacy) && legacy.TargetFps == targetFps) all.Remove(gameId);
            all[KeyFor(gameId, targetFps)] = new SmartTdpEntry { Watts = watts, TargetFps = targetFps, Title = title, UpdatedUtc = DateTime.UtcNow, Rev = CurrentRev };
            Persist(all);
        }
    }

    /// <summary>
    /// Esquece o que foi aprendido: de um jogo (todas as metas) ou, com gameId nulo, de
    /// todos. Devolve quantos valores foram apagados.
    /// </summary>
    public int Clear(string? gameId)
    {
        lock (_gate)
        {
            var all = Load();
            var keys = string.IsNullOrWhiteSpace(gameId)
                ? all.Keys.ToList()
                : all.Keys.Where(k => k == gameId || k.StartsWith(gameId + "@", StringComparison.Ordinal)).ToList();
            foreach (var key in keys) all.Remove(key);
            if (keys.Count > 0) Persist(all);
            NoteReset(gameId);
            return keys.Count;
        }
    }

    // Quando o usuário mandou esquecer (por jogo; "*" = todos). O diário de sessões é
    // conferido contra isso: depois de um reset, sessões antigas não dão o ponto de partida.
    private string ResetsPath => Path.Combine(Path.GetDirectoryName(_path) ?? ".", "smart-tdp-resets.json");
    private const string AllGamesKey = "*";

    /// <summary>Último "Resetar aprendizado" que vale para o jogo (dele ou de todos). null = nunca.</summary>
    public DateTime? LastResetUtc(string gameId)
    {
        lock (_gate)
        {
            var resets = LoadResets();
            DateTime? own = resets.TryGetValue(gameId, out var g) ? g : null;
            DateTime? all = resets.TryGetValue(AllGamesKey, out var a) ? a : null;
            return own is null ? all : all is null ? own : (own > all ? own : all);
        }
    }

    private void NoteReset(string? gameId)
    {
        try
        {
            var resets = LoadResets();
            resets[string.IsNullOrWhiteSpace(gameId) ? AllGamesKey : gameId] = DateTime.UtcNow;
            var dir = Path.GetDirectoryName(ResetsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = ResetsPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(resets));
            File.Move(tmp, ResetsPath, overwrite: true);
        }
        catch
        {
            // melhor esforço
        }
    }

    private Dictionary<string, DateTime> LoadResets()
    {
        try
        {
            if (File.Exists(ResetsPath))
                return JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(ResetsPath)) ?? new();
        }
        catch
        {
            // arquivo estragado ou preso: trata como sem reset
        }
        return new Dictionary<string, DateTime>();
    }

    private void Persist(Dictionary<string, SmartTdpEntry> all)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _path, overwrite: true);
        }
        catch
        {
            // Aprendizado é melhor esforço: sem disco, vale só para esta sessão.
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
        catch (JsonException)
        {
            // Arquivo estragado: guarda uma cópia e recomeça (o próximo gravar não apaga nada útil).
            try { File.Move(_path, _path + ".bad", overwrite: true); } catch { }
            _entries = null;
        }
        catch
        {
            // Arquivo preso (antivírus, indexador): não guarda o vazio, tenta de novo na próxima.
            return new Dictionary<string, SmartTdpEntry>();
        }
        return _entries ??= new Dictionary<string, SmartTdpEntry>();
    }
}
