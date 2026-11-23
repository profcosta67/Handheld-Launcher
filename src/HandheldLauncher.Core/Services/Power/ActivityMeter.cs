using System.Collections.Concurrent;
using System.Diagnostics;

namespace HandheldLauncher.Core.Services.Power;

/// <summary>
/// Quanto cada função do launcher trabalha: quantas vezes acordou e quanto tempo levou.
/// Serve ao diagnóstico de bateria ("o launcher está gastando? com o quê?"). Custo próprio
/// desprezível: dois Stopwatch.GetTimestamp e dois Interlocked por medição.
/// </summary>
public static class ActivityMeter
{
    private sealed class Counter
    {
        public long Count;
        public long Ticks;
    }

    private static readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);

    /// <summary>Mede o bloco até o Dispose: <c>using var _ = ActivityMeter.Measure("sensores");</c></summary>
    public static Scope Measure(string name) => new(name, Stopwatch.GetTimestamp());

    public readonly struct Scope : IDisposable
    {
        private readonly string _name;
        private readonly long _start;

        internal Scope(string name, long start) { _name = name; _start = start; }

        public void Dispose()
        {
            if (_name is null) return;
            Add(_name, Stopwatch.GetTimestamp() - _start);
        }
    }

    /// <summary>Registra uma medição já feita (em ticks do Stopwatch).</summary>
    public static void Add(string name, long stopwatchTicks)
    {
        var c = _counters.GetOrAdd(name, _ => new Counter());
        Interlocked.Increment(ref c.Count);
        Interlocked.Add(ref c.Ticks, Math.Max(0, stopwatchTicks));
    }

    /// <summary>Lê e zera: quantas vezes e quantos milissegundos cada função gastou desde a última leitura.</summary>
    public static Dictionary<string, ActivityUsage> TakeSnapshot()
    {
        var result = new Dictionary<string, ActivityUsage>(StringComparer.Ordinal);
        foreach (var (name, c) in _counters)
        {
            var count = Interlocked.Exchange(ref c.Count, 0);
            var ticks = Interlocked.Exchange(ref c.Ticks, 0);
            if (count == 0) continue;
            result[name] = new ActivityUsage(count, ticks * 1000.0 / Stopwatch.Frequency);
        }
        return result;
    }
}

/// <summary>Uso de uma função num intervalo: vezes que acordou e milissegundos de trabalho.</summary>
public readonly record struct ActivityUsage(long Count, double Ms);
