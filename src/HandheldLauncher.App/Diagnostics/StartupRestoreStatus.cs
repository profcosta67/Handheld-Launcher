using System.Collections.Concurrent;

namespace HandheldLauncher.App.Diagnostics;

/// <summary>
/// v0.16.2: resultado de cada item restaurado na abertura do launcher (TDP,
/// ventoinha, cor). Cada item registra uma linha "[RESTAURAÇÃO]" no
/// debug.log e fica guardado aqui pra ser lido pela futura verificação
/// "Restauração da última sessão" da Central de Ajuda.
/// </summary>
internal static class StartupRestoreStatus
{
    public sealed record Entry(string Item, string Detail, bool Ok, DateTime AtLocal);

    private static readonly ConcurrentDictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);

    public static void Report(string item, string detail, bool ok)
    {
        var entry = new Entry(item, detail, ok, DateTime.Now);
        Entries[item] = entry;
        DebugLog.Write($"[RESTAURAÇÃO] {item}: {(ok ? "ok" : "FALHOU")} — {detail}");
    }

    public static IReadOnlyCollection<Entry> Snapshot() => Entries.Values.ToList();
}
