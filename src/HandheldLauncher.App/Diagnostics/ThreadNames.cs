using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace HandheldLauncher.App.Diagnostics;

/// <summary>
/// v0.28 (log de 24/11: o launcher usava ~5% do processador com jogo e as funções medidas somavam
/// bem menos): nome das threads do launcher pelo id do Windows, para o resumo [BATERIA] dizer
/// QUAL thread gasta. Threads sem nome aparecem pelo id (ex.: desenho da tela do Avalonia).
/// </summary>
public static class ThreadNames
{
    private static readonly ConcurrentDictionary<int, string> Names = new();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    /// <summary>Chame no começo da thread.</summary>
    public static void RegisterCurrent(string name)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { Names[(int)GetCurrentThreadId()] = name; } catch { }
    }

    public static string? Get(int osThreadId) => Names.TryGetValue(osThreadId, out var n) ? n : null;
}
