using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.Core.Services;
using HandheldLauncher.Core.Services.SessionDiary;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// v0.31 — Diário de sessões: enquanto há um jogo aberto, lê a cada 5 s o consumo do
/// aparelho na bateria, a carga, o FPS (pelo RTSS, quando há) e o TDP em vigor, e ao fechar
/// o jogo grava o resumo em <see cref="SessionDiaryStore"/>. Só observa: não muda nada no
/// aparelho. Sem sensor ou sem RTSS o campo fica vazio — nada é inventado.
/// </summary>
internal sealed class SessionDiaryRecorder : IDisposable
{
    private const int SampleSeconds = 5;

    private readonly MainViewModel _viewModel;
    private readonly object _gate = new();
    private readonly Thread _thread;
    private volatile bool _stop;

    private SessionAccumulator? _session;
    private string? _gameId;
    private string _title = "";
    private DateTime _lastSampleUtc;
    private SystemMetrics? _metrics;
    private RtssSharedMemory? _rtss;

    public SessionDiaryRecorder(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        _thread = new Thread(Loop) { IsBackground = true, Name = "SessionDiary" };
        _thread.Start();
    }

    private void Loop()
    {
        while (!_stop)
        {
            try { lock (_gate) { if (!_stop) Tick(); } }
            catch (Exception ex) { DebugLog.Write($"[DIARIO] Erro no ciclo: {ex.GetType().Name}: {ex.Message}"); }
            for (var i = 0; i < SampleSeconds * 10 && !_stop; i++) Thread.Sleep(100);
        }
    }

    private void Tick()
    {
        var game = _viewModel.RunningGame;

        if (_session is not null && (game is null || game.Id != _gameId)) Finish();
        if (game is null) return;

        var now = DateTime.UtcNow;
        if (_session is null)
        {
            _gameId = game.Id;
            _title = game.Title;
            _session = new SessionAccumulator(game.Id, game.Title, now);
            _lastSampleUtc = now;
            _metrics ??= new SystemMetrics();
            if (OperatingSystem.IsWindows()) _rtss ??= new RtssSharedMemory();
            return; // a primeira leitura dos contadores não vale
        }

        var seconds = (now - _lastSampleUtc).TotalSeconds;
        _lastSampleUtc = now;

        MetricsSnapshot metrics;
        try { metrics = _metrics!.Read(); }
        catch (Exception ex) { DebugLog.Swallowed(ex); return; }

        double? fps = null;
        var pid = GameSessionInfo.DetectedGamePid;
        if (pid != 0 && _rtss is not null)
        {
            try
            {
                if (_rtss.ReadFrameStats(pid) is { } frames && frames.ProcessId == pid) fps = frames.Fps;
            }
            catch (Exception ex) { DebugLog.Swallowed(ex); }
        }

        // TDP em vigor: o do modo inteligente quando ele está no comando; senão, o perfil escolhido.
        int? tdp = null;
        var smartWatts = GameSessionInfo.SmartTdpWatts;
        var smart = smartWatts > 0;
        int? target = null;
        try
        {
            var settings = AppSettingsStore.Load();
            tdp = smart ? smartWatts : TdpProfiles.WattsFor(settings.TdpMode);
            if (smart)
                target = HandheldLauncher.Core.Services.SmartTdp.SmartTdpTargetStore.Shared.Get(game.Id) ?? settings.SmartTdpTargetFps;
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }

        _session.Add(seconds, metrics.OnAcPower, metrics.BatteryWatts, metrics.BatteryPercent, fps, tdp, smart, target);
    }

    private void Finish()
    {
        var session = _session;
        _session = null;
        _gameId = null;
        if (session is null) return;

        var record = session.Build(DateTime.UtcNow);
        if (!SessionDiaryStore.Shared.Append(record))
        {
            if (record.DurationSeconds >= SessionDiaryStore.MinSessionSeconds)
                DebugLog.Write($"[DIARIO] Não consegui gravar a sessão de '{_title}'.");
            return;
        }

        GameEnergyCache.Reload(); // a barra do jogo passa a mostrar a sessão nova

        var parts = new List<string> { $"{record.DurationSeconds / 60} min" };
        if (record.AvgBatteryWatts is { } watts) parts.Add($"{watts:0.0} W na bateria (pico {record.PeakBatteryWatts:0.0} W, {record.BatterySeconds / 60} min)");
        if (record.BatteryStartPercent is { } from && record.BatteryEndPercent is { } to) parts.Add($"bateria {from}% → {to}%");
        if (record.AvgFps is { } fps) parts.Add($"{fps:0} FPS (piores momentos {record.LowFps:0})");
        if (record.AvgTdpWatts is { } tdp) parts.Add($"TDP médio {tdp:0.0} W{(record.SmartTdp ? $" (Auto, meta {record.TargetFps})" : "")}");
        DebugLog.Write($"[DIARIO] Sessão de '{_title}': {string.Join(" · ", parts)}.");

        try
        {
            var summary = SessionDiaryStore.Shared.Summarize(record.GameId, null);
            if (summary is { LastAboveNormal: true })
                DebugLog.Write($"[DIARIO] Consumo acima do normal em '{_title}': {summary.LastWatts:0.0} W nesta sessão, contra {summary.TypicalWatts:0.0} W de costume.");
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
    }

    public void Dispose()
    {
        _stop = true;
        if (!_thread.Join(1500)) return; // ciclo preso numa leitura: não grava pela metade
        lock (_gate)
        {
            try { Finish(); } catch (Exception ex) { DebugLog.Swallowed(ex); }
            _metrics?.Dispose();
            _rtss?.Dispose();
        }
    }
}
