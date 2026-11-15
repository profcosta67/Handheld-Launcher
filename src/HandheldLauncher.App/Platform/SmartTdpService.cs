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
    // Sem valor aprendido, o modo inteligente parte de pelo menos isto (ou do perfil do
    // usuário, se for maior) e ajusta a partir daí. Partir de um Eco de 8 W deixava o jogo
    // travado por um bom tempo até a subida começar (testes de 14/11).
    private const int StartFloorWatts = 20;
    // Ao voltar do Quick Settings, dá uma margem curta (desfeita em ~8 s se a meta se
    // mantiver) para o jogo não engasgar enquanto volta a rodar.
    private const int ResumeBoostWatts = 6;
    private const double ResumeBoostNoFramesSeconds = 15;
    private const double ReassertSeconds = 30;
    private const double FanDownAfterSeconds = 30;
    private const double StatusLogSeconds = 10;

    /// <summary>
    /// Ligação com a ventoinha (preenchida pelo Quick Settings, roda na UI thread):
    /// nome da curva a aplicar, ou null para devolver a curva escolhida pelo usuário.
    /// </summary>
    public static Action<string?>? FanApplier { get; set; }

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
    private (string GameId, int Watts, string UserMode)? _resume;
    private bool _offWithGame;

    private sealed class Session
    {
        public required string GameId;
        public required string Title;
        public required string UserMode;
        public string UserModeAtStart = "";
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
        public int Min;
        public int Max;
        public int StartWatts;
        public int TargetFps;
        public bool StartLearned;
        public bool StartResumed;
        public string UserFan = "";
        public string? FanMode;      // curva posta pelo modo inteligente (null = ainda não mexeu)
        public int FanTouches;
        public bool FanOff;          // o usuário escolheu uma curva à mão: não mexemos mais
        public DateTime? FanLowerSince;
        public DateTime LastStatusLog = DateTime.MinValue;
        public double LastFps = double.NaN;
        public readonly double[] Recent = new double[3];
        public int RecentCount;
        public DateTime ResumedUtc;
        public int AltPid;
        public int AltRejectedPid;
        public bool OwnMeasured;
        public DateTime LastAssertUtc = DateTime.UtcNow;
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
            DebugLog.Write($"[TDP-AUTO] Modo inteligente {(_lastEnabled == true ? "LIGADO" : "desligado")} (meta padrão {DefaultTargetFps} FPS).");
        }

        var game = _viewModel.RunningGame;
        if (!_settings.SmartTdpEnabled || game is null)
        {
            // Desligado pelo Quick Settings ao escolher um perfil: o painel grava o "desligado"
            // antes de aplicar/gravar o perfil novo. Espera um ciclo e devolve o TDP que estiver
            // gravado nessa altura — nunca o do início da sessão por cima da escolha nova.
            if (!_settings.SmartTdpEnabled && game is not null && _session is { Suspended: false } live && (live.Applied || live.FanMode is not null))
            {
                if (!live.OffSeen) { live.OffSeen = true; return; }
                live.UserMode = string.IsNullOrWhiteSpace(_settings.TdpMode) ? live.UserMode : _settings.TdpMode;
            }

            if (game is null) _resume = null;
            _offWithGame = game is not null;
            EndSession(_settings.SmartTdpEnabled ? "jogo fechado" : "modo desligado");
            return;
        }

        if (_session is not null) _session.OffSeen = false; // ligado de novo: a próxima saída volta a esperar um ciclo

        if (_session is null || _session.GameId != game.Id)
        {
            EndSession("outro jogo");
            _settings = AppSettingsStore.Load();
            var userMode = string.IsNullOrWhiteSpace(_settings.TdpMode) ? "Normal" : _settings.TdpMode;
            var limits = DeviceTdpLimits.Current();
            var min = Math.Min(Math.Max(limits.SplMin, MinFloorWatts), limits.SplMax);
            var max = limits.SplMax;
            var target = TargetFor(game.Id);
            var learned = _store.Get(game.Id, target);
            int? resumed = learned is null && _resume is { } r && r.GameId == game.Id ? r.Watts : null;
            var userWatts = TdpProfiles.WattsFor(userMode) ?? TdpProfiles.DefaultCustomWatts;
            // Um perfil manual de 20 W ou mais é a informação mais recente que temos (o usuário
            // acabou de ver o jogo rodar com ele): parte dali, não do valor antigo da sessão.
            if (resumed is not null && userWatts >= StartFloorWatts && _resume is { } prev && prev.UserMode != userMode) resumed = null;
            var start = Math.Clamp(learned?.Watts ?? resumed ?? Math.Max(userWatts, StartFloorWatts), min, max);

            _session = new Session
            {
                GameId = game.Id,
                Title = game.Title,
                UserMode = userMode,
                UserModeAtStart = userMode,
                MidGame = _offWithGame, // o Auto foi ligado com este jogo já aberto
                Min = min,
                Max = max,
                StartWatts = start,
                TargetFps = target,
                StartLearned = learned is not null,
                StartResumed = resumed is not null,
                UserFan = _settings.FanMode ?? "",
                FanTouches = GameSessionInfo.FanTouches,
                SavedWatts = learned?.Watts ?? 0,
            };
            var origin = learned is not null ? "aprendido antes" : resumed is not null ? "retomado desta sessão de jogo" : "ponto de partida, aprendendo";
            DebugLog.Write($"[TDP-AUTO] Sessão iniciada: '{game.Title}', meta {target} FPS{(SmartTdpTargetStore.Shared.Get(game.Id) is null ? " (padrão)" : " (deste jogo)")}, janela {min}–{max} W, TDP do usuário = {userMode}, início em {start} W ({origin}).");

            // Aplica já, sem esperar o jogo aparecer: o carregamento não fica preso no perfil
            // baixo do usuário e o painel/HUD mostram na hora que o modo inteligente assumiu.
            if (Apply(_session, start, "início")) UpdateFan(_session, start, DateTime.UtcNow);
        }

        var s = _session;
        var sessionTarget = TargetFor(s.GameId);
        if (s.TargetFps != sessionTarget)
        {
            // Meta trocada no Quick Settings com o jogo aberto: recomeça o controle com a meta
            // nova, partindo do valor aprendido para ela (se houver) ou dos watts atuais.
            var previousTarget = s.TargetFps;
            s.TargetFps = sessionTarget;
            var learnedForTarget = _store.Get(s.GameId, sessionTarget);
            var current = s.Controller?.BaselineWatts ?? s.StartWatts;
            // Meta mais alta sem nada aprendido: parte de pelo menos 20 W, como no início do
            // jogo (o valor de uma meta baixa pode estar muito abaixo do que a nova pede).
            var from = learnedForTarget is not null
                ? Math.Clamp(learnedForTarget.Watts, s.Min, s.Max)
                : sessionTarget > previousTarget
                    ? Math.Clamp(Math.Max(current, StartFloorWatts), s.Min, s.Max)
                    : current;
            s.StartWatts = from;
            s.StartLearned = learnedForTarget is not null;
            s.StartResumed = false;
            s.SavedWatts = learnedForTarget?.Watts ?? 0; // o que havia antes valia para a meta antiga
            s.MidGame = true; // o jogo já está rodando: espera curta
            s.RecentCount = 0;
            if (s.Controller is not null)
            {
                s.Controller = new SmartTdpController(sessionTarget, s.Min, s.Max, from, startIsLearned: s.StartLearned);
                s.ControlStartedUtc = DateTime.UtcNow;
            }
            DebugLog.Write($"[TDP-AUTO] Meta alterada durante a sessão: {previousTarget} → {sessionTarget} FPS; recomeça em {from} W ({(s.StartLearned ? "aprendido para esta meta" : "watts atuais")}).");
            if (s.Applied && !s.Suspended) Apply(s, from, "meta alterada");
        }
        if (s.Suspended) return;

        var currentUserMode = string.IsNullOrWhiteSpace(_settings.TdpMode) ? "Normal" : _settings.TdpMode;
        if (currentUserMode != s.UserMode)
        {
            s.Suspended = true;
            GameSessionInfo.SmartTdpWatts = 0;
            // Garante que o que está no hardware é a escolha do usuário, não o nosso último valor.
            var ok = !s.Applied || _hardware.SetTdpMode(currentUserMode);
            Dispatcher.UIThread.Post(() => _viewModel.UpdateHudTdpStatus(currentUserMode));
            DebugLog.Write($"[TDP-AUTO] TDP trocado à mão ({s.UserMode} → {currentUserMode}); modo inteligente pausado até o jogo fechar (escolha do usuário {(ok ? "mantida" : "NÃO reaplicada")}).");
            return;
        }

        if (!s.FanOff && (GameSessionInfo.FanTouches != s.FanTouches || !string.Equals(_settings.FanMode ?? "", s.UserFan, StringComparison.Ordinal)))
        {
            s.FanOff = true;
            s.FanMode = null; // a curva agora é a que o usuário escolheu; nada a devolver
            DebugLog.Write($"[TDP-AUTO] Ventoinha trocada à mão ({s.UserFan} → {_settings.FanMode}); o modo inteligente deixa a ventoinha com o usuário até o jogo fechar.");
        }

        var gamePid = GameSessionInfo.DetectedGamePid;
        if (gamePid == 0) return; // a janela do jogo ainda não apareceu (lançador da loja, abertura)

        var foregroundPid = GetForegroundProcessId();
        if (foregroundPid != gamePid)
        {
            // Jogos com mais de um processo (lançador próprio, anti-cheat): a janela detectada
            // pode não ser a que desenha. Se o RTSS não mede o processo detectado mas mede o
            // que está na frente (e não é o próprio launcher), é por ele que se controla.
            // Só vale se o processo detectado NUNCA foi medido nesta sessão (senão é só o jogo
            // minimizado com outro programa na frente) e se o da frente é do mesmo jogo (mesma
            // pasta do executável). Um navegador ou a loja na frente nunca comanda o TDP.
            var measured = false;
            if (!s.OwnMeasured && foregroundPid != 0 && foregroundPid != Environment.ProcessId)
            {
                FrameStats? own, front;
                lock (_rtss)
                {
                    own = _rtss.ReadFrameStats(gamePid);
                    front = _rtss.ReadFrameStats(foregroundPid);
                }
                if (own is { } o && o.ProcessId == gamePid) s.OwnMeasured = true;
                else if (front is { } fr && fr.ProcessId == foregroundPid)
                {
                    if (s.AltPid != foregroundPid && s.AltRejectedPid != foregroundPid)
                    {
                        if (SameGameFolder(gamePid, foregroundPid))
                        {
                            s.AltPid = foregroundPid;
                            DebugLog.Write($"[TDP-AUTO] O processo detectado ({gamePid}) não é medido pelo RTSS; controlando pelo processo do jogo em primeiro plano ({foregroundPid}).");
                        }
                        else s.AltRejectedPid = foregroundPid;
                    }
                    measured = s.AltPid == foregroundPid;
                }
            }

            if (!measured)
            {
                if (!s.Away) DebugLog.Write($"[TDP-AUTO] Jogo fora do primeiro plano (pid {foregroundPid} na frente, jogo {gamePid}); controle em espera.");
                s.RecentCount = 0;
                s.Away = true; // Quick Settings, launcher ou outra janela na frente
                return;
            }
        }

        if (s.Away)
        {
            // De volta ao jogo: reafirma o valor do modo inteligente com um reforço curto
            // (o controlador devolve os watts de antes se a meta se mantiver ~8 s). Sem piso
            // fixo: um jogo leve que roda a 12 W não pula para 20 W a cada visita ao QS.
            s.Away = false;
            var resumeTime = (DateTime.UtcNow - s.ControlStartedUtc).TotalSeconds;
            var resumeWatts = s.Controller?.PrepareForResume(resumeTime, ResumeBoostWatts) ?? s.StartWatts;
            s.ResumedUtc = DateTime.UtcNow;
            if (s.Applied) Apply(s, resumeWatts, "de volta ao jogo");
            DebugLog.Write($"[TDP-AUTO] De volta ao jogo: {resumeWatts} W (reforço curto de até +{ResumeBoostWatts} W; volta ao valor anterior se a meta se mantiver).");
        }

        FrameStats? frames;
        lock (_rtss) frames = _rtss.ReadFrameStats(foregroundPid);
        if (frames is not { } f || f.ProcessId != foregroundPid)
        {
            s.RecentCount = 0;
            // Sem leituras depois da volta ao jogo, o reforço não seria devolvido nunca.
            if (s.Controller is { ResumeBoostPending: true } ctl && (DateTime.UtcNow - s.ResumedUtc).TotalSeconds >= ResumeBoostNoFramesSeconds)
            {
                var back = ctl.DropResumeBoost((DateTime.UtcNow - s.ControlStartedUtc).TotalSeconds);
                if (back is { } w && s.Applied) Apply(s, w, "reforço devolvido sem leitura de FPS");
            }
            if (!s.LoggedNoFrames)
            {
                s.LoggedNoFrames = true;
                DebugLog.Write($"[TDP-AUTO] Sem leitura de FPS do jogo ({_rtss.LastError ?? "RTSS não está medindo este processo"}); aguardando.");
            }
            return;
        }

        if (foregroundPid == gamePid) s.OwnMeasured = true;

        if (s.Controller is null)
        {
            s.Controller = new SmartTdpController(s.TargetFps, s.Min, s.Max, s.StartWatts, startIsLearned: s.StartLearned, coarseDescent: !s.StartResumed);
            s.ControlStartedUtc = DateTime.UtcNow;
            DebugLog.Write($"[TDP-AUTO] Jogo medindo FPS ({f.Fps:0}); controle começa em {s.Controller.Watts} W.");
            if (s.StartLearned && s.Applied) PerformanceHud.ShowNotice("Modo inteligente aplicado");
            return;
        }

        var elapsed = (DateTime.UtcNow - s.ControlStartedUtc).TotalSeconds; // conta da 1ª leitura do jogo
        if (elapsed < (s.MidGame ? MidGameWarmupSeconds : WarmupSeconds)) return; // abertura/carregamento do jogo

        // Mediana das 3 últimas leituras (~6 s): uma engasgada isolada não mexe no TDP.
        s.Recent[s.RecentCount++ % 3] = f.Fps;
        var fps = f.Fps;
        if (s.RecentCount >= 3)
        {
            double a = s.Recent[0], b = s.Recent[1], c = s.Recent[2];
            fps = Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));
        }

        var before = s.Controller.Watts;
        var change = s.Controller.OnSample(fps, elapsed);
        if (change != SmartTdpChange.None)
        {
            Apply(s, s.Controller.Watts, $"{(change == SmartTdpChange.Up ? "subiu" : "desceu")} de {before} W, FPS {fps:0}");
            s.LastAssertUtc = DateTime.UtcNow;
        }
        else if (s.Applied && (DateTime.UtcNow - s.LastAssertUtc).TotalSeconds >= ReassertSeconds)
        {
            // Outras partes do launcher reaplicam o perfil do usuário (volta da suspensão, troca
            // tomada/bateria). Reafirma o valor do modo inteligente de tempos em tempos.
            s.LastAssertUtc = DateTime.UtcNow;
            Apply(s, s.Controller.Watts, "reafirmado", quiet: true);
        }

        s.LastFps = f.Fps;
        var now = DateTime.UtcNow;
        UpdateFan(s, s.Controller.Watts, now);
        if ((now - s.LastStatusLog).TotalSeconds >= StatusLogSeconds)
        {
            s.LastStatusLog = now;
            var state = s.Controller.IsSettled ? "estável"
                : fps < s.TargetFps * SmartTdpController.BelowRatio ? (s.Controller.Watts >= s.Max ? "abaixo da meta mesmo no teto" : "abaixo da meta, subindo")
                : "na meta, testando para baixo";
            DebugLog.Write($"[TDP-AUTO] Estado: {s.Controller.Watts} W, FPS {fps:0} (meta {s.TargetFps}), ventoinha {s.FanMode ?? (s.FanOff ? "do usuário" : s.UserFan)} — {state}.");
        }
        if (s.Applied) GameSessionInfo.SmartTdpWatts = s.Controller.Watts;

        if (s.Controller.IsSettled && s.SavedWatts != s.Controller.Watts)
        {
            s.SavedWatts = s.Controller.Watts;
            _store.Set(s.GameId, s.Title, s.SavedWatts, s.TargetFps);
            DebugLog.Write($"[TDP-AUTO] Aprendido: '{s.Title}' fica bem com {s.SavedWatts} W a {s.TargetFps} FPS.");
        }
    }

    /// <summary>Meta das Configurações, para jogos sem meta própria.</summary>
    private int DefaultTargetFps => Math.Clamp(_settings.SmartTdpTargetFps <= 0 ? 60 : _settings.SmartTdpTargetFps,
        SmartTdpTargetStore.MinTargetFps, SmartTdpTargetStore.MaxTargetFps);

    /// <summary>Meta escolhida para o jogo no Quick Settings, ou a padrão.</summary>
    private int TargetFor(string gameId) => SmartTdpTargetStore.Shared.Get(gameId) ?? DefaultTargetFps;

    /// <summary>Os dois processos são do mesmo jogo (executáveis na mesma pasta ou em subpasta)?</summary>
    private static bool SameGameFolder(int gamePid, int otherPid)
    {
        try
        {
            using var a = System.Diagnostics.Process.GetProcessById(gamePid);
            using var b = System.Diagnostics.Process.GetProcessById(otherPid);
            var da = Path.GetDirectoryName(a.MainModule?.FileName);
            var db = Path.GetDirectoryName(b.MainModule?.FileName);
            if (string.IsNullOrEmpty(da) || string.IsNullOrEmpty(db)) return false;
            da = da.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            db = db.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return da.StartsWith(db, StringComparison.OrdinalIgnoreCase) || db.StartsWith(da, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false; // processo protegido (anti-cheat) ou já fechado: na dúvida, não usa
        }
    }

    private bool Apply(Session s, int watts, string why, bool quiet = false)
    {
        var mode = TdpProfiles.CustomMode(watts);
        bool ok;
        lock (WindowsHardwareService.TdpApplyLock) ok = _hardware.SetTdpMode(mode);
        if (ok)
        {
            s.Applied = true;
            GameSessionInfo.SmartTdpWatts = watts;
            s.Failures = 0;
            if (!quiet) DebugLog.Write($"[TDP-AUTO] {watts} W aplicado ({why}).");
            Dispatcher.UIThread.Post(() => _viewModel.UpdateHudTdpStatus(mode));
            return true;
        }

        s.Failures++;
        DebugLog.Write($"[TDP-AUTO] Falha ao aplicar {watts} W ({why}) — tentativa {s.Failures}.");
        if (s.Failures >= 3)
        {
            s.Suspended = true;
            GameSessionInfo.SmartTdpWatts = 0;
            DebugLog.Write("[TDP-AUTO] Três falhas seguidas; modo inteligente pausado até o jogo fechar.");
        }
        return false;
    }

    private static int FanRank(string? mode) => mode switch
    {
        "Silencioso" => 0,
        "Normal" => 1,
        "Desempenho" => 2,
        "Boost" => 3,
        _ => -1,
    };

    /// <summary>
    /// Ventoinha acompanha os watts do modo inteligente, pela mesma tabela dos perfis
    /// manuais. Sobe de curva na hora; só desce depois de 30 s na faixa de baixo, para não
    /// ficar trocando quando o TDP oscila 1 W em volta de um limite.
    /// </summary>
    private void UpdateFan(Session s, int watts, DateTime now)
    {
        if (s.FanOff || s.Suspended || FanApplier is null) return;
        if (FanRank(s.UserFan) < 0 && s.UserFan != "Automático" && s.UserFan != "") return; // curva personalizada do usuário: não mexe

        var wanted = TdpProfiles.LinkedFanMode(TdpProfiles.CustomMode(watts));
        var current = s.FanMode ?? s.UserFan;
        if (wanted == current) { s.FanLowerSince = null; return; }

        var up = FanRank(wanted) > FanRank(current);
        if (!up)
        {
            s.FanLowerSince ??= now;
            if ((now - s.FanLowerSince.Value).TotalSeconds < FanDownAfterSeconds) return;
        }

        s.FanLowerSince = null;
        s.FanMode = wanted;
        DebugLog.Write($"[TDP-AUTO] Ventoinha → {wanted} ({watts} W).");
        var applier = FanApplier;
        Dispatcher.UIThread.Post(() => { try { applier?.Invoke(wanted); } catch (Exception ex) { DebugLog.Write($"[TDP-AUTO] Ventoinha: {ex.Message}"); } });
    }

    private void EndSession(string why)
    {
        var s = _session;
        if (s is null) return;
        _session = null;
        GameSessionInfo.SmartTdpWatts = 0;

        if (s.FanMode is not null && !s.FanOff)
        {
            var applier = FanApplier;
            Dispatcher.UIThread.Post(() => { try { applier?.Invoke(null); } catch (Exception ex) { DebugLog.Write($"[TDP-AUTO] Ventoinha: {ex.Message}"); } });
        }

        if (why != "modo desligado") _resume = null;
        else if (s.Applied && !s.Suspended && s.Controller is not null && _viewModel.RunningGame?.Id == s.GameId)
            _resume = (s.GameId, s.Controller.BaselineWatts, s.UserModeAtStart);

        if (s.Applied && !s.Suspended)
        {
            bool ok;
            lock (WindowsHardwareService.TdpApplyLock) ok = _hardware.SetTdpMode(s.UserMode);
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
