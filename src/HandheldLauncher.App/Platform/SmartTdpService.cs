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
    private const double MidGameWarmupSeconds = 4;
    private const int MinFloorWatts = 8;

    private readonly MainViewModel _viewModel;
    private readonly RtssSharedMemory _rtss = new();
    private readonly WindowsHardwareService _hardware = new();
    private readonly SmartTdpStore _store = new();
    private readonly Thread _thread;
    private volatile bool _stop;
    private readonly object _gate = new();

    private AppSettings _settings = new();
    // Começa "vencido" para ler as configurações já no 1º ciclo. (Na beta.57 isto era
    // int.MaxValue: o ++ estourava para negativo e as configurações nunca eram lidas —
    // por isso o modo inteligente não fez nada no primeiro teste.)
    private int _settingsAge = 1000;

    private Session? _session;
    private bool? _lastEnabled;

    // Último valor do modo inteligente neste jogo, enquanto ele continuar aberto. Desligar e
    // religar o Auto no meio do jogo retoma daqui em vez de recomeçar do perfil manual
    // (na beta.58 recomeçava dos 8 W do Eco e demorava a subir).
    private (string GameId, int Watts)? _resume;

    private sealed class Session
    {
        public required string GameId;
        public required string Title;
        public required string UserMode;
        public bool OffSeen;
        public bool MidGame; // ligado com o jogo já rodando: sem a espera de abertura
        public DateTime ControlStartedUtc = DateTime.UtcNow;
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
            try { lock (_gate) { if (!_stop) Tick(); } }
            catch (Exception ex) { DebugLog.Write($"[TDP-AUTO] Erro no ciclo: {ex.GetType().Name}: {ex.Message}"); }
            Thread.Sleep(TimeSpan.FromSeconds(TickSeconds));
        }
    }

    private void Tick()
    {
        // Com sessão aberta as configurações são relidas a cada ciclo: uma troca de TDP feita
        // no Quick Settings tem de ser vista antes de qualquer gravação nossa (revisão 14/11).
        if (_session is not null || ++_settingsAge >= 3)
        {
            _settings = AppSettingsStore.Load();
            _settingsAge = 0;
        }

        if (_settings.SmartTdpEnabled != _lastEnabled)
        {
            _lastEnabled = _settings.SmartTdpEnabled;
            DebugLog.Write($"[TDP-AUTO] Modo inteligente {(_lastEnabled == true ? "LIGADO" : "desligado")} (meta {TargetFps} FPS).");
        }

        var game = _viewModel.RunningGame;
        if (!_settings.SmartTdpEnabled || game is null)
        {
            // Desligado pelo Quick Settings ao escolher um perfil: o painel grava o "desligado"
            // antes de aplicar/gravar o perfil novo. Espera um ciclo e devolve o TDP que estiver
            // gravado nessa altura — nunca o do início da sessão por cima da escolha nova.
            if (!_settings.SmartTdpEnabled && game is not null && _session is { Applied: true, Suspended: false } live)
            {
                if (!live.OffSeen) { live.OffSeen = true; return; }
                live.UserMode = string.IsNullOrWhiteSpace(_settings.TdpMode) ? live.UserMode : _settings.TdpMode;
            }

            if (game is null) _resume = null;
            EndSession(_settings.SmartTdpEnabled ? "jogo fechado" : "modo desligado");
            return;
        }

        if (_session is null || _session.GameId != game.Id)
        {
            EndSession("outro jogo");
            _settings = AppSettingsStore.Load();
            _session = new Session
            {
                GameId = game.Id,
                Title = game.Title,
                UserMode = string.IsNullOrWhiteSpace(_settings.TdpMode) ? "Normal" : _settings.TdpMode,
                MidGame = GameSessionInfo.DetectedGamePid != 0,
            };
            DebugLog.Write($"[TDP-AUTO] Sessão iniciada: '{game.Title}', meta {TargetFps} FPS, TDP do usuário = {_session.UserMode}.");
        }

        var s = _session;
        if (s.Suspended) return;

        var currentUserMode = string.IsNullOrWhiteSpace(_settings.TdpMode) ? "Normal" : _settings.TdpMode;
        if (currentUserMode != s.UserMode)
        {
            s.Suspended = true;
            // Garante que o que está no hardware é a escolha do usuário, não o nosso último valor.
            var ok = !s.Applied || _hardware.SetTdpMode(currentUserMode);
            Dispatcher.UIThread.Post(() => _viewModel.UpdateHudTdpStatus(currentUserMode));
            DebugLog.Write($"[TDP-AUTO] TDP trocado à mão ({s.UserMode} → {currentUserMode}); modo inteligente pausado até o jogo fechar (escolha do usuário {(ok ? "mantida" : "NÃO reaplicada")}).");
            return;
        }

        var gamePid = GameSessionInfo.DetectedGamePid;
        if (gamePid == 0) return; // a janela do jogo ainda não apareceu (lançador da loja, abertura)

        var foregroundPid = GetForegroundProcessId();
        if (foregroundPid != gamePid)
        {
            s.Away = true; // Quick Settings, launcher ou outra janela na frente
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
            int? resumed = learned is null && _resume is { } r && r.GameId == s.GameId ? r.Watts : null;
            var start = learned?.Watts ?? resumed ?? TdpProfiles.WattsFor(s.UserMode) ?? TdpProfiles.DefaultCustomWatts;
            s.Controller = new SmartTdpController(TargetFps, min, limits.SplMax, start, startIsLearned: learned is not null);
            s.ControlStartedUtc = DateTime.UtcNow;
            DebugLog.Write($"[TDP-AUTO] Janela {min}–{limits.SplMax} W; início em {s.Controller.Watts} W ({(learned is not null ? "aprendido antes" : resumed is not null ? "retomado desta sessão de jogo" : "TDP atual, aprendendo")}).");

            if (resumed is not null) Apply(s, s.Controller.Watts, "retomando o modo inteligente");

            if (learned is not null)
            {
                s.SavedWatts = learned.Watts;
                if (Apply(s, s.Controller.Watts, "valor aprendido"))
                    PerformanceHud.ShowNotice("Modo inteligente aplicado");
            }
            return;
        }

        var elapsed = (DateTime.UtcNow - s.ControlStartedUtc).TotalSeconds; // conta da 1ª leitura do jogo
        if (elapsed < (s.MidGame ? MidGameWarmupSeconds : WarmupSeconds)) return; // abertura/carregamento do jogo

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

        if (why != "modo desligado") _resume = null;
        else if (s.Applied && s.Controller is not null) _resume = (s.GameId, s.Controller.Watts);

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
        lock (_gate)
        {
            try { EndSession("launcher fechando"); } catch { /* melhor esforço */ }
            _rtss.Dispose();
        }
    }
}
