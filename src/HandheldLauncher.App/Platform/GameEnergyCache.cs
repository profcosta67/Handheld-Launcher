using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.Core.Services.SessionDiary;

namespace HandheldLauncher.App.Platform;

/// <summary>O que a barra do jogo selecionado mostra sobre energia (v0.31).</summary>
public sealed record GameEnergyInfo(string? GuideText, string? LastSessionText, EnergyGuide? Guide);

/// <summary>
/// Resumo do diário por jogo, guardado em memória para a tela não ler o disco a cada troca
/// de jogo. Carrega em segundo plano; quem mostra reage ao <see cref="Changed"/>.
/// </summary>
public static class GameEnergyCache
{
    private static readonly object Gate = new();
    private static Dictionary<string, GameEnergyInfo>? _byGame;
    private static int _loading;
    private static int _dirty;

    /// <summary>Disparado (fora da thread da tela) quando o resumo foi carregado ou refeito.</summary>
    public static event Action? Changed;

    /// <summary>null = sem dados deste jogo, ou o resumo ainda está carregando.</summary>
    public static GameEnergyInfo? Get(string gameId)
    {
        lock (Gate)
        {
            if (_byGame is not null) return _byGame.GetValueOrDefault(gameId);
        }
        if (Volatile.Read(ref _loading) == 0) Reload();
        return null;
    }

    /// <summary>Refaz o resumo (ex.: uma sessão acabou de ser gravada).</summary>
    public static void Reload()
    {
        Interlocked.Exchange(ref _dirty, 1);
        if (Interlocked.Exchange(ref _loading, 1) == 1) return; // a carga em curso refaz no fim
        Interlocked.Exchange(ref _dirty, 0);
        _ = Task.Run(() =>
        {
            try
            {
                var built = Build(SessionDiaryStore.Shared.Load(), MaxDeviceWatts());
                lock (Gate) _byGame = built;
            }
            catch (Exception ex)
            {
                DebugLog.Swallowed(ex);
                lock (Gate) _byGame ??= new Dictionary<string, GameEnergyInfo>();
            }
            finally
            {
                Interlocked.Exchange(ref _loading, 0);
            }
            try { Changed?.Invoke(); }
            catch (Exception ex) { DebugLog.Swallowed(ex); }
            // Pediram de novo enquanto carregava (ex.: uma sessão acabou de ser gravada): refaz.
            if (Interlocked.CompareExchange(ref _dirty, 0, 0) == 1) Reload();
        });
    }

    /// <summary>Teto de TDP do aparelho (na tomada, que é o mais alto). 0 = desconhecido.</summary>
    public static int MaxDeviceWatts()
    {
        try { return OperatingSystem.IsWindows() ? DeviceTdpLimits.For(true).SplMax : 0; }
        catch (Exception ex) { DebugLog.Swallowed(ex); return 0; }
    }

    private static Dictionary<string, GameEnergyInfo> Build(IReadOnlyList<SessionRecord> sessions, int maxWatts)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        var result = new Dictionary<string, GameEnergyInfo>();
        foreach (var group in sessions.GroupBy(s => s.GameId))
        {
            var list = group.OrderBy(s => s.StartedUtc).ToList();
            var guide = EnergyGuide.Build(list, maxWatts);

            // Última sessão que mediu alguma coisa: consumo na bateria e/ou FPS.
            string? lastText = null;
            var last = list.LastOrDefault(s =>
                s.AvgFps is > 0 || (s.AvgBatteryWatts is > 0 && s.BatterySeconds >= SessionDiaryStore.MinBatterySecondsForEnergy));
            if (last is not null)
            {
                var parts = new List<string>();
                if (last.AvgBatteryWatts is > 0 && last.BatterySeconds >= SessionDiaryStore.MinBatterySecondsForEnergy)
                    parts.Add(last.AvgBatteryWatts.Value.ToString("0.0", culture) + " W");
                if (last.AvgFps is > 0) parts.Add(last.AvgFps.Value.ToString("0", culture) + " FPS");
                lastText = string.Join(" · ", parts);
            }

            if (guide is null && lastText is null) continue;
            var guideText = guide is { HasAnyMeasurement: true } ? guide.ShortText() : null;
            if (guideText is null && lastText is null) continue;
            result[group.Key] = new GameEnergyInfo(guideText, lastText, guide);
        }
        return result;
    }
}
