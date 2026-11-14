using System.Runtime.InteropServices;
using Avalonia.Threading;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;
using HandheldLauncher.Core.Services.SmartTdp;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// v0.27 — TDP inteligente. Com um jogo aberto, lê o FPS medido pelo RTSS e
/// ajusta os watts sozinho para manter a meta sem gastar bateria à toa; o
/// valor em que o jogo estabiliza é lembrado e aplicado na próxima vez.
///
/// Regras de convivência:
/// - Desligado por padrão (Configurações → Geral).
/// - Não grava o TDP nas configurações: ao fechar o jogo, volta o modo que o
///   usuário tinha escolhido.
/// - Se o usuário trocar o TDP à mão durante o jogo, o modo inteligente sai
///   da frente até o jogo fechar.
/// - Só usa leituras do jogo em primeiro plano (com o Quick Settings ou outra
///   janela na frente, espera).
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class SmartTdpService : IDisposable
{
    private const int TickSeconds = 2;
    private const double WarmupSeconds = 15;
    private const int MinFloorWatts = 8;

    private readonly MainViewModel _viewModel;
    private readonly RtssSharedMemory _rtss = new();
    private readonly WindowsHardwareService _hardware = new();
    private readonly SmartTdpStore _store = new();
    private readonly Thread _thread;
    private volatile bool _stop;

    private AppSettings _settings = new();
    private int _settingsAge = int.MaxValue;

    private Session? _session;

    private sealed class Session
    {
        public required string GameId;
        public required string Title;
        public required string UserMode;
        public DateTime StartedUtc = DateTime.UtcNow;
        public SmartTdpController? Controller;
        public bool Applied;
        public bool Suspended;
        public int SavedWatts;
        public int Failures;
        public bool LoggedNoFrames;
        public bool Away;
    }

    public SmartTdpService(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        _thread = new Thread(Run) { IsBackground = true, Name = "SmartTdp" };
        _thread.Start();
    }

    private void Run()
    {
        Thread.Sleep(TimeSpan.FromSeconds(8));
        while (!_stop)
        {
            try { Tick(); }
            catch (Exception ex) { DebugLog.Write($"[TDP-AUTO] Erro no ciclo: {ex.GetType().Name}: {ex.Message}"); }
            Thread.Sleep(TimeSpan.FromSeconds(TickSeconds));
        }
    }

    private void Tick()
    {
        if (++_settingsAge >= 3)
        {
            _settings = AppSettingsStore.Load();
            _settingsAge = 0;
        }

        var game = _viewModel.RunningGame;
        if (!_settings.SmartTdpEnabled || game is null)
        {
            EndSession(_settings.SmartTdpEnabled ? "jogo fechado" : "modo desligado");
            return;
        }

        if (_session is null || _session.GameId != game.Id)
        {
            EndSession("outro jogo");
            _session = new Session
            {
                GameId = game.Id,
                Title = game.Title,
                UserMode = string.IsNullOrWhiteSpace(_settings.TdpMode) ? "Normal" : _settings.TdpMode,
            };
            DebugLog.Write($"[TDP-AUTO] Sessão iniciada: '{game.Title}', meta {TargetFps} FPS, TDP do usuário = {_session.UserMode}.");
        }

        var s = _session;
        if (s.Suspended) return;

        var currentUserMode = string.IsNullOrWhiteSpace(_settings.TdpMode) ? "Normal" : _settings.TdpMode;
        if (currentUserMode != s.UserMode)
        {
            s.Suspended = true;
            DebugLog.Write($"[TDP-AUTO] TDP trocado à mão ({s.UserMode} → {currentUserMode}); modo inteligente pausado até o jogo fechar.");
            return;
        }

        var foregroundPid = GetForegroundProcessId();
        if (foregroundPid == 0 || foregroundPid == Environment.ProcessId)
        {
            s.Away = true; // Quick Settings ou o próprio launcher na frente
            return;
        }

        if (s.Away)
        {
            // O Quick Settings reaplica o TDP salvo ao abrir; de volta ao jogo, o valor do
            // modo inteligente é reafirmado.
            s.Away = false;
            if (s.Applied && s.Controller is not null) Apply(s, s.Controller.Watts, "de volta ao jogo");
        }

        FrameStats? frames;
        lock (_rtss) frames = _rtss.ReadFrameStats(foregroundPid);
        if (frames is not { } f || f.ProcessId != foregroundPid)
        {
            if (!s.LoggedNoFrames)
            {
                s.LoggedNoFrames = true;
                DebugLog.Write($"[TDP-AUTO] Sem leitura de FPS do jogo ({_rtss.LastError ?? "RTSS não está medindo este processo"}); aguardando.");
            }
            return;
        }

        if (s.Controller is null)
        {
            var limits = DeviceTdpLimits.Current();
            var min = Math.Min(Math.Max(limits.SplMin, MinFloorWatts), limits.SplMax);
            var learned = _store.Get(s.GameId, TargetFps);
            var start = learned?.Watts ?? TdpProfiles.WattsFor(s.UserMode) ?? TdpProfiles.DefaultCustomWatts;
            s.Controller = new SmartTdpController(TargetFps, min, limits.SplMax, start, startIsLearned: learned is not null);
            DebugLog.Write($"[TDP-AUTO] Janela {min}–{limits.SplMax} W; início em {s.Controller.Watts} W ({(learned is not null ? "aprendido antes" : "TDP atual, aprendendo")}).");

            if (learned is not null)
            {
                s.SavedWatts = learned.Watts;
                if (Apply(s, s.Controller.Watts, "valor aprendido"))
                    PerformanceHud.ShowNotice("Modo inteligente aplicado");
            }
            return;
        }

        var elapsed = (DateTime.UtcNow - s.StartedUtc).TotalSeconds;
        if (elapsed < WarmupSeconds) return; // abertura/carregamento do jogo

        var before = s.Controller.Watts;
        var change = s.Controller.OnSample(f.Fps, elapsed);
        if (change != SmartTdpChange.None)
            Apply(s, s.Controller.Watts, $"{(change == SmartTdpChange.Up ? "subiu" : "desceu")} de {before} W, FPS {f.Fps:0}");

        if (s.Controller.IsSettled && s.SavedWatts != s.Controller.Watts)
        {
            s.SavedWatts = s.Controller.Watts;
            _store.Set(s.GameId, s.Title, s.SavedWatts, TargetFps);
            DebugLog.Write($"[TDP-AUTO] Aprendido: '{s.Title}' fica bem com {s.SavedWatts} W a {TargetFps} FPS.");
        }
    }

    private int TargetFps => Math.Clamp(_settings.SmartTdpTargetFps <= 0 ? 60 : _settings.SmartTdpTargetFps, 20, 240);

    private bool Apply(Session s, int watts, string why)
    {
        var mode = TdpProfiles.CustomMode(watts);
        var ok = _hardware.SetTdpMode(mode);
        if (ok)
        {
            s.Applied = true;
            s.Failures = 0;
            DebugLog.Write($"[TDP-AUTO] {watts} W aplicado ({why}).");
            Dispatcher.UIThread.Post(() => _viewModel.UpdateHudTdpStatus(mode));
            return true;
        }

        s.Failures++;
        DebugLog.Write($"[TDP-AUTO] Falha ao aplicar {watts} W ({why}) — tentativa {s.Failures}.");
        if (s.Failures >= 3)
        {
            s.Suspended = true;
            DebugLog.Write("[TDP-AUTO] Três falhas seguidas; modo inteligente pausado até o jogo fechar.");
        }
        return false;
    }

    private void EndSession(string why)
    {
        var s = _session;
        if (s is null) return;
        _session = null;

        if (s.Applied && !s.Suspended)
        {
            var ok = _hardware.SetTdpMode(s.UserMode);
            DebugLog.Write($"[TDP-AUTO] Sessão encerrada ({why}); TDP do usuário {(ok ? "restaurado" : "NÃO restaurado")}: {s.UserMode}.");
            var mode = s.UserMode;
            Dispatcher.UIThread.Post(() => _viewModel.UpdateHudTdpStatus(mode));
        }
        else
        {
            DebugLog.Write($"[TDP-AUTO] Sessão encerrada ({why}); nada a restaurar.");
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static int GetForegroundProcessId()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return 0;
            GetWindowThreadProcessId(hwnd, out var pid);
            return (int)pid;
        }
        catch { return 0; }
    }

    public void Dispose()
    {
        _stop = true;
        try { EndSession("launcher fechando"); } catch { /* melhor esforço */ }
        _rtss.Dispose();
    }
}
