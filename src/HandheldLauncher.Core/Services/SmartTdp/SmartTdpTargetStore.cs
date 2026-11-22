using System.Text.Json;

namespace HandheldLauncher.Core.Services.SmartTdp;

/// <summary>Meta de FPS escolhida para um jogo específico.</summary>
public sealed class SmartTdpGameTarget
{
    public int TargetFps { get; set; }
    public string Title { get; set; } = "";
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>
/// Meta de FPS por jogo do TDP inteligente. Fica num arquivo próprio (e não no
/// settings.json) porque a janela de Configurações grava o settings inteiro que abriu:
/// uma meta escolhida no Quick Settings com ela aberta seria desfeita ao salvar.
/// O Quick Settings e o serviço usam a mesma instância (<see cref="Shared"/>), então uma
/// escolha vale no ciclo seguinte sem reler o disco.
/// </summary>
public sealed class SmartTdpTargetStore
{
    public const int MinTargetFps = 20;
    public const int MaxTargetFps = 240;

    private static readonly Lazy<SmartTdpTargetStore> _shared = new(() => new SmartTdpTargetStore());
    public static SmartTdpTargetStore Shared => _shared.Value;

    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, SmartTdpGameTarget>? _entries;

    public SmartTdpTargetStore(string? pathOverride = null)
    {
        _path = pathOverride ?? Path.Combine(
            Path.GetDirectoryName(AppSettingsStore.DefaultPath) ?? ".", "smart-tdp-targets.json");
    }

    /// <summary>Meta própria do jogo, ou null se ele usa a meta padrão.</summary>
    public int? Get(string? gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId)) return null;
        lock (_gate)
        {
            return Load().TryGetValue(gameId, out var e) && e.TargetFps is >= MinTargetFps and <= MaxTargetFps
                ? e.TargetFps
                : null;
        }
    }

    /// <summary>Define a meta do jogo; null volta para a meta padrão.</summary>
    public bool Set(string gameId, string title, int? targetFps)
    {
        if (string.IsNullOrWhiteSpace(gameId)) return false;
        lock (_gate)
        {
            var all = Load();
            if (targetFps is { } fps)
                all[gameId] = new SmartTdpGameTarget
                {
                    TargetFps = Math.Clamp(fps, MinTargetFps, MaxTargetFps),
                    Title = title,
                    UpdatedUtc = DateTime.UtcNow,
                };
            else if (!all.Remove(gameId))
                return true;

            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, _path, overwrite: true);
                return true;
            }
            catch
            {
                // Sem disco a escolha vale até o launcher fechar (já está na memória).
                return false;
            }
        }
    }

    private Dictionary<string, SmartTdpGameTarget> Load()
    {
        if (_entries is not null) return _entries;
        try
        {
            if (File.Exists(_path))
                _entries = JsonSerializer.Deserialize<Dictionary<string, SmartTdpGameTarget>>(File.ReadAllText(_path));
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
            return new Dictionary<string, SmartTdpGameTarget>();
        }
        return _entries ??= new Dictionary<string, SmartTdpGameTarget>();
    }
}
