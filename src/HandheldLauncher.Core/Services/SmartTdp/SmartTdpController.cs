namespace HandheldLauncher.Core.Services.SmartTdp;

public enum SmartTdpChange { None, Up, Down }

/// <summary>
/// v0.27 — "cérebro" do TDP inteligente. Recebe o FPS medido a cada poucos
/// segundos e decide os watts: sobe rápido quando o jogo cai abaixo da meta,
/// desce devagar enquanto sobra. Quando uma descida causa queda, guarda esse
/// ponto como piso por alguns minutos — é assim que ele encontra o "teto que o
/// jogo precisa" sem ficar oscilando.
///
/// Classe pura (sem relógio nem hardware): o tempo entra por parâmetro, para
/// os testes simularem uma sessão inteira.
/// </summary>
public sealed class SmartTdpController
{
    // Folga maior que na 1ª versão (0,93 / 0,985): num jogo real o FPS oscila uns 5% em volta
    // da meta com qualquer potência, e isso travava a descida e disparava subidas à toa.
    public const double BelowRatio = 0.90;   // abaixo disto o jogo está engasgando
    public const double MeetRatio = 0.95;    // a partir disto a meta está cumprida
    public const double IgnoreBelowFps = 10; // carregamento/pausa: a leitura não vale
    public const int StepUpWatts = 3;
    public const int StepDownWatts = 1;
    public const double UpCooldownSeconds = 4;
    public const double DownAfterGoodSeconds = 12;
    // Enquanto nenhuma descida falhou ainda, desce mais depressa (2 W a cada 8 s): partindo de
    // 34 W num jogo que se contenta com 20 W, a 1 W / 12 s levava quase 3 minutos.
    public const int CoarseStepDownWatts = 2;
    public const double CoarseDownAfterGoodSeconds = 8;
    public const double FloorHoldSeconds = 180;
    public const double LearnedFloorHoldSeconds = 60;
    public const double SettleSeconds = 30;
    public const double MaxFloorHoldSeconds = 1800;
    public const double IgnoreLowFpsForSeconds = 20;
    public const int FutileClimbWatts = 6;
    public const double FutileGainRatio = 1.05;
    public const double FutileHoldSeconds = 120;
    // FPS bem acima da meta: há folga clara, desce depressa e o valor aprendido não segura.
    public const double SurplusRatio = 1.25;
    public const double ReturnAfterGoodSeconds = 8;
    public const int NearSamplesToNudge = 4;

    private readonly int _target;
    private readonly int _min;
    private readonly int _max;

    private int _belowCount;
    private double _goodSince;
    private double _lastChange;
    private int _floor;
    private double _floorUntil = double.NegativeInfinity;
    private bool _started;

    public SmartTdpController(int targetFps, int minWatts, int maxWatts, int startWatts, bool startIsLearned = false, bool coarseDescent = true)
    {
        _target = Math.Max(15, targetFps);
        _effectiveTarget = _target;
        _min = Math.Min(minWatts, maxWatts);
        _max = Math.Max(minWatts, maxWatts);
        Watts = Math.Clamp(startWatts, _min, _max);
        _floor = _min;
        _startIsLearned = startIsLearned;
        _coarse = coarseDescent;
    }

    private readonly bool _startIsLearned;
    private bool _coarse; // ainda não encontrou o ponto em que falta potência

    public int Watts { get; private set; }
    public int TargetFps => _target;

    // Reforço da volta ao jogo: por quanto tempo a meta tem de se manter para devolver
    // os watts de antes de sair.
    public const double ResumeBoostHoldSeconds = 8;

    private int? _resumeReturnTo;
    private double _resumeAt;

    // Subida por queda de FPS que não veio de um teste de descida (pausa, replay, cena
    // pesada passageira): quando a meta volta, retorna a este valor em vez de ficar preso lá
    // em cima.
    private int? _returnAfterClimb;
    private int? _provenWatts;   // último valor em que a meta se manteve por alguns segundos
    private bool _climbFromProbe; // a subida atual veio de um teste de descida que falhou
    private bool _lastDownWasReturn; // a última descida foi a volta de uma vez (não um passo de teste)
    private int _lastDownStep = StepDownWatts;
    private bool _probeWasReturn;
    private int _probeFrom;
    // Piso de antes de um "teste falhado", para desfazer se a queda não era culpa do teste.
    private (int Floor, double Until, bool Confirmed, double Hold, bool Learned)? _preProbeFloor;
    private double _futileFps;
    private bool _learnedCheckPending;
    private bool _floorIsLearned; // o piso atual é só o valor aprendido (não um teste desta sessão)
    public const double ProvenAfterSeconds = 6;

    /// <summary>
    /// Retomada do jogo após perder o foco (Quick Settings, outra janela): dá um reforço
    /// curto de potência enquanto o jogo volta a rodar. Se a meta se mantém por
    /// <see cref="ResumeBoostHoldSeconds"/>, volta direto aos watts de antes; se o FPS
    /// cair, o reforço fica (era preciso). Piso, aprendizado e a proteção contra subida
    /// sem ganho são mantidos — o jogo é o mesmo de antes da saída.
    /// </summary>
    public int PrepareForResume(double nowSeconds, int boostWatts)
    {
        _belowCount = 0;
        _nearCount = 0;
        _goodSince = nowSeconds;
        _lowFpsSince = null;
        _previousFps = null;        // a leitura de antes da pausa não conta para "queda brusca"
        _dropUntil = double.NegativeInfinity;
        _lastChangeWasDown = false; // a volta não é uma descida de teste
        // Uma subida em andamento continua sendo medida a partir de onde começou: se ela se
        // mostrar inútil, volta para antes dela (e não para o valor com reforço).

        // Ainda na abertura do jogo (sem nenhuma leitura avaliada) não há o que reforçar; e
        // durante a espera por subida inútil, mais potência já se mostrou sem efeito. Um
        // reforço que ainda não foi devolvido continua pendente.
        _atMaxSince = null;
        if (!_started || nowSeconds < _futileUntil || _unreachable || boostWatts <= 0) return Watts;

        // Voltas seguidas não acumulam: o reforço é sempre sobre o valor de antes da 1ª saída.
        var baseline = _resumeReturnTo ?? Watts;
        var boosted = Math.Min(_max, baseline + boostWatts);
        if (boosted <= Watts) return Watts;
        _resumeReturnTo = baseline;
        _resumeAt = nowSeconds;
        Watts = boosted;
        _lastChange = nowSeconds;
        IsSettled = false;
        return Watts;
    }

    /// <summary>Há um reforço de volta ao jogo ainda não devolvido.</summary>
    public bool ResumeBoostPending => _resumeReturnTo is not null;

    /// <summary>Watts sem o reforço da volta ao jogo (o que vale lembrar ou retomar).</summary>
    public int BaselineWatts => _resumeReturnTo ?? (Watts - ProbeExtraWatts);

    /// <summary>Watts a mais de um teste de +3 W em andamento (0 sem teste). A ventoinha não acompanha o teste.</summary>
    public int ProbeExtraWatts => _probeAt is null ? 0 : _probeDirection * StepUpWatts;

    /// <summary>
    /// Devolve o reforço sem esperar leituras (o jogo parou de entregar FPS). Retorna os
    /// watts de antes, ou null se não havia reforço.
    /// </summary>
    public int? DropResumeBoost(double nowSeconds)
    {
        if (_resumeReturnTo is not { } back) return null;
        _resumeReturnTo = null;
        if (back >= Watts) return null;
        Watts = back;
        _lastChange = nowSeconds;
        _lastChangeWasDown = false;
        return Watts;
    }

    /// <summary>true quando o valor atual se manteve sem quedas — vale a pena lembrar para a próxima vez.</summary>
    public bool IsSettled { get; private set; }

    private int _nearCount;
    private double _floorHold = FloorHoldSeconds;
    private bool _floorConfirmed;
    private bool _lastChangeWasDown;
    private double? _lowFpsSince;

    // Subida em andamento: de onde partiu e com que FPS — para perceber quando mais
    // potência não está ajudando (jogo limitado abaixo da meta, tela de carregamento,
    // cena presa na CPU) e voltar em vez de ir até o teto.
    private int? _climbWatts;
    private int _climbFloor;
    private double _climbFloorUntil;
    private bool _climbFloorConfirmed;
    private bool _climbFloorIsLearned;
    private bool _climbCoarse;
    private double _futileUntil = double.NegativeInfinity;
    private double _futileHold = FutileHoldSeconds;

    // Meta fora de alcance: mais potência não aumenta o FPS (jogo preso na GPU, limitado
    // abaixo da meta). Para de subir e fica no menor valor da subida que já dava esse FPS,
    // segurando o FPS possível; volta à meta real se o jogo mudar e tenta de novo de tempos
    // em tempos. Não desce abaixo desse ponto: numa pausa ou replay longo (FPS travado) isso
    // deixaria o jogo sem potência na volta.
    public const double UnreachableNoticeSeconds = 30; // um replay curto não é "fora de alcance"
    public const double UnreachableAtMaxSeconds = 20;
    public const double UnreachableMaxHoldSeconds = 240; // nova tentativa no máximo a cada 4 min
    private int _exitSamples;

    /// <summary>Instante (no relógio do controle) em que a meta passou a estar fora de alcance.</summary>
    public double UnreachableSinceSeconds => _unreachableSince;
    private double _effectiveTarget;
    private bool _unreachable;
    private double _unreachableUntil = double.NegativeInfinity;
    private double _unreachableSince;
    private double _plateauFps;
    private double _climbBestFps;
    private double? _atMaxSince;
    private readonly List<(int Watts, double Fps)> _climbHistory = new();

    // Curva desta sessão: FPS medido em cada potência nos últimos minutos. Serve para não subir
    // de novo quando já se viu que mais watts não dão mais FPS (jogo preso na placa de vídeo) e
    // para voltar ao menor valor que já dava esse FPS. Só vale para a cena atual: se o FPS na
    // mesma potência muda muito, a curva é apagada.
    public const double CurveWindowSeconds = 240;
    public const int CurveMinSamples = 3;
    public const double CurveSettleSeconds = 4;     // leituras logo depois de trocar os watts ainda são do valor antigo
    private const int CurveMaxPerWatt = 12;
    public const double SceneChangeRatio = 0.12;
    public const double KneeRatio = 0.95;
    // Sair de "fora de alcance" exige o FPS bem acima do patamar, não só encostar na meta:
    // num jogo que oscila em volta de 33 com meta 35, a oscilação fazia sair e subir de novo.
    public const double UnreachableExitAbovePlateau = 1.05;
    // Fora de alcance: de minuto em minuto um teste curto de +3 W. Se o FPS subir de verdade
    // (12%), a cena mudou e o controle volta a buscar a meta.
    public const double ProbeEverySeconds = 60;
    public const double ProbeGainRatio = 1.12;
    private readonly Dictionary<int, Queue<(double At, double Fps)>> _curve = new();
    private int _sceneChangeCount;
    private double? _probeAt;
    private double _probeBase;
    private readonly List<double> _probeSamples = new();
    private double _lastProbe = double.NegativeInfinity;
    private int _aboveCount;
    private int _probeDirection = 1;              // +1 teste de +3 W; -1 teste de -3 W
    private double _downProbeBlockedUntil = double.NegativeInfinity;
    public const double DownProbeKeepRatio = 0.96;
    public const double DownProbeBlockSeconds = 180;
    public const double SuddenDropRatio = 0.65;
    public const double SuddenDropHoldSeconds = 15;
    private double? _previousFps;
    private double _previousAt = double.NegativeInfinity;
    private double _dropUntil = double.NegativeInfinity;

    private void NoteCurve(double fps, double nowSeconds)
    {
        if (fps < IgnoreBelowFps || nowSeconds - _lastChange < CurveSettleSeconds) return;
        var (mean, n) = CurveAverage(Watts - 1, Watts + 1, nowSeconds);
        _sceneChangeCount = n >= CurveMinSamples && mean is { } m && Math.Abs(fps - m) > SceneChangeRatio * m ? _sceneChangeCount + 1 : 0;
        if (_sceneChangeCount >= 3)
        {
            // A cena mudou: o que se mediu antes não vale para agora.
            _curve.Clear();
            _sceneChangeCount = 0;
            if (_unreachable) LeaveUnreachable();
        }
        if (!_curve.TryGetValue(Watts, out var queue)) _curve[Watts] = queue = new Queue<(double, double)>();
        queue.Enqueue((nowSeconds, fps));
        while (queue.Count > 0 && nowSeconds - queue.Peek().At > CurveWindowSeconds) queue.Dequeue();
        if (queue.Count > CurveMaxPerWatt) queue.Dequeue();
    }

    private (double? Mean, int Count) CurveAverage(int fromWatts, int toWatts, double nowSeconds)
    {
        double sum = 0;
        var count = 0;
        foreach (var (watts, queue) in _curve)
        {
            if (watts < fromWatts || watts > toWatts) continue;
            foreach (var (at, value) in queue)
            {
                if (nowSeconds - at > CurveWindowSeconds) continue;
                sum += value;
                count++;
            }
        }
        return count == 0 ? (null, 0) : (sum / count, count);
    }

    /// <summary>Menor potência medida que já dava (quase) o FPS do patamar.</summary>
    private int? Knee(double plateauFps, double nowSeconds)
    {
        foreach (var watts in _curve.Keys.OrderBy(w => w))
        {
            var (mean, n) = CurveAverage(watts, watts + 1, nowSeconds);
            if (n >= CurveMinSamples && mean >= plateauFps * KneeRatio) return watts;
        }
        return null;
    }

    /// <summary>A meta não é alcançável agora: o controle segura <see cref="EffectiveTargetFps"/>.</summary>
    public bool TargetUnreachable => _unreachable;

    /// <summary>FPS que o controle está segurando (a meta, ou o que o jogo consegue quando ela é inalcançável).</summary>
    public int EffectiveTargetFps => (int)Math.Round(_effectiveTarget);

    private void LeaveUnreachable()
    {
        _unreachable = false;
        _effectiveTarget = _target;
        _futileUntil = double.NegativeInfinity;
        _exitSamples = 0;
        _probeAt = null;
    }

    private void MarkUnreachable(double fps, double nowSeconds) => MarkUnreachable(Math.Max(_climbBestFps, fps), fps, nowSeconds);

    private void MarkUnreachable(double plateauFps, double fps, double nowSeconds)
    {
        _unreachable = true;
        _unreachableUntil = nowSeconds + _futileHold;
        _unreachableSince = nowSeconds;
        _plateauFps = plateauFps;
        _effectiveTarget = Math.Max(15.0, Math.Min(_target, _plateauFps * 0.97));
        _futileFps = fps;
        _futileUntil = nowSeconds + _futileHold;
        _futileHold = Math.Min(UnreachableMaxHoldSeconds, _futileHold * 2);
        _atMaxSince = null;
        _exitSamples = 0;
        IsSettled = false;
    }

    /// <summary>
    /// Marca o piso depois de uma falta de potência. Se o mesmo ponto falhar de novo, o
    /// intervalo até a próxima tentativa dobra (3, 6, 12… até 30 min). O piso só é
    /// "confirmado" (vale como aprendizado) quando veio de uma descida de teste que não
    /// deu certo — não de uma subida por causa de uma cena pesada ou de um carregamento.
    /// </summary>
    private void RaiseFloor(int floor, double nowSeconds, bool confirmed)
    {
        floor = Math.Clamp(floor, _min, _max);
        var same = floor == _floor;
        _floorHold = same ? Math.Min(MaxFloorHoldSeconds, _floorHold * 2) : FloorHoldSeconds;
        _floorConfirmed = confirmed || (same && _floorConfirmed);
        _floor = floor;
        _floorUntil = nowSeconds + _floorHold;
        _floorIsLearned = false;
    }

    public SmartTdpChange OnSample(double fps, double nowSeconds)
    {
        if (!_started)
        {
            _started = true;
            _goodSince = nowSeconds;
            _lastChange = nowSeconds;
            if (_startIsLearned)
            {
                // Valor aprendido: segura por um minuto e depois confere para baixo (o valor
                // pode ter ficado alto; se estiver certo, o teste falha uma vez e ele volta).
                _floor = Watts;
                _floorUntil = nowSeconds + LearnedFloorHoldSeconds;
                _floorConfirmed = true;
                _floorIsLearned = true;
                _learnedCheckPending = true;
            }
        }

        if (double.IsNaN(fps)) return SmartTdpChange.None;

        if (fps < IgnoreBelowFps)
        {
            // Carregamento/pausa: ignora por um tempo. Se durar, é o jogo que está sem
            // potência (ex.: começou em 8 W) e passa a contar como "abaixo da meta".
            _lowFpsSince ??= nowSeconds;
            if (nowSeconds - _lowFpsSince.Value < IgnoreLowFpsForSeconds)
            {
                _atMaxSince = null;
                _belowCount = 0;
                _nearCount = 0;
                _goodSince = nowSeconds;
                return SmartTdpChange.None;
            }
        }
        else
        {
            _lowFpsSince = null;

            // Queda brusca: de dentro da meta para menos de 65% dela, de uma leitura para a outra.
            // É corte de cena, replay ou menu travado em 30 (EA FC a cada lance parado), não falta
            // de potência: espera até 15 s antes de subir. Falta de potência não derruba o FPS assim.
            var previous = nowSeconds - _previousAt <= 6 ? _previousFps : null; // só de uma leitura para a seguinte
            _previousFps = fps;
            _previousAt = nowSeconds;
            if (previous is { } prev && prev >= _effectiveTarget * MeetRatio && fps < _effectiveTarget * SuddenDropRatio
                && _dropUntil < nowSeconds)
                _dropUntil = nowSeconds + SuddenDropHoldSeconds;
            if (nowSeconds < _dropUntil)
            {
                if (fps < _effectiveTarget * SuddenDropRatio)
                {
                    _belowCount = 0;
                    _nearCount = 0;
                    return SmartTdpChange.None;
                }
                _dropUntil = double.NegativeInfinity;
            }

            NoteCurve(fps, nowSeconds);
        }

        // Sai do modo "meta fora de alcance" quando a meta real volta a ser cumprida, quando o
        // FPS sobe bem acima do patamar (a cena mudou) ou quando passa o prazo de nova tentativa.
        // (Duas leituras seguidas: uma só, por oscilação, não conta.)
        if (_unreachable)
        {
            var changed = (fps >= _target * MeetRatio && fps >= _plateauFps * UnreachableExitAbovePlateau) || fps > _plateauFps * 1.20;
            _exitSamples = changed ? _exitSamples + 1 : 0;
            // (Com um teste de +3 W em andamento, a nova tentativa espera o teste terminar e devolver os watts.)
            if (_exitSamples >= 2 || (nowSeconds >= _unreachableUntil && _probeAt is null))
            {
                if (_exitSamples >= 2) _futileHold = FutileHoldSeconds;
                else _curve.Clear(); // nova tentativa: mede de novo em vez de confiar no que já se viu
                LeaveUnreachable();
            }
        }

        // Fora de alcance: teste curto de +3 W de minuto em minuto (ou logo, se o FPS subiu acima
        // do patamar). Se o FPS subir de verdade, a cena mudou e o controle volta a buscar a meta;
        // se não, devolve os 3 W.
        if (_unreachable && fps >= IgnoreBelowFps)
        {
            if (_probeAt is { } probeAt)
            {
                if (nowSeconds - probeAt >= CurveSettleSeconds) _probeSamples.Add(fps);
                if (_probeSamples.Count < 3) return SmartTdpChange.None;
                _probeSamples.Sort();
                var gain = _probeBase > 0 ? _probeSamples[1] / _probeBase : 1;
                _probeAt = null;
                _lastProbe = nowSeconds;
                if (_probeDirection < 0)
                {
                    // Teste para baixo: o FPS ficou (quase) igual com 3 W a menos? Fica e testa de
                    // novo em 20 s. Se caiu, devolve os 3 W e não tenta descer por 3 minutos.
                    if (gain >= DownProbeKeepRatio)
                    {
                        _lastProbe = nowSeconds - ProbeEverySeconds + 20;
                        return SmartTdpChange.None;
                    }
                    _downProbeBlockedUntil = nowSeconds + DownProbeBlockSeconds;
                    Watts = Math.Min(_max, Watts + StepUpWatts);
                    _lastChange = nowSeconds;
                    _lastChangeWasDown = false;
                    return SmartTdpChange.Up;
                }
                if (gain >= ProbeGainRatio)
                {
                    LeaveUnreachable();
                    _curve.Clear();
                }
                else
                {
                    Watts = Math.Max(_min, Watts - StepUpWatts);
                    _lastChange = nowSeconds;
                    _lastChangeWasDown = false;
                    return SmartTdpChange.Down;
                }
            }
            else
            {
                _aboveCount = fps >= _plateauFps * UnreachableExitAbovePlateau ? _aboveCount + 1 : 0;
            }

            if (_unreachable && _probeAt is null
                && (nowSeconds - _lastProbe >= ProbeEverySeconds || (_aboveCount >= 3 && nowSeconds - _lastProbe >= 10))
                && nowSeconds - _lastChange >= 10 && _resumeReturnTo is null
                && CurveAverage(Watts, Watts, nowSeconds) is { Count: >= 2, Mean: { } probeBase })
            {
                // Para baixo só até o último valor que já segurou a meta: num menu ou pausa com o FPS
                // travado, descer sem esse limite deixaria o jogo sem potência na volta.
                var down = _provenWatts is { } proven && Watts - StepUpWatts >= proven
                    && nowSeconds >= _downProbeBlockedUntil && _aboveCount < 3;
                if (down || Watts + StepUpWatts <= _max)
                {
                    _probeAt = nowSeconds;
                    _probeBase = probeBase;
                    _probeSamples.Clear();
                    _probeDirection = down ? -1 : 1;
                    Watts += down ? -StepUpWatts : StepUpWatts;
                    _lastChange = nowSeconds;
                    _lastChangeWasDown = false;
                    return down ? SmartTdpChange.Down : SmartTdpChange.Up;
                }
            }
        }

        var below = fps < _effectiveTarget * BelowRatio;
        var near = !below && fps < _effectiveTarget * MeetRatio;

        // Reforço da volta ao jogo: passado o prazo sem faltar FPS de verdade, devolve.
        // (Conta pelo relógio: uma leitura "quase na meta" não segura o reforço.)
        if (_resumeReturnTo is { } back && !below && nowSeconds - _resumeAt >= ResumeBoostHoldSeconds)
        {
            _resumeReturnTo = null;
            if (back < Watts)
            {
                Watts = back;
                _lastChange = nowSeconds;
                _lastChangeWasDown = false; // devolução do reforço, não teste
                return SmartTdpChange.Down;
            }
        }

        if (below || near)
        {
            _goodSince = nowSeconds;
            IsSettled = false;
            if (nowSeconds < _futileUntil)
            {
                // A espera vale enquanto o FPS continua no mesmo patamar em que a subida não
                // adiantou. Se mudou (a pausa acabou e agora falta potência de verdade), reage.
                // Exceção: logo depois de uma descida de teste a queda pode ser culpa dela (jogos
                // com vsync caem para metade do FPS) — aí não espera.
                var justProbed = _lastChangeWasDown && nowSeconds - _lastChange <= 30;
                if (!justProbed && Math.Abs(fps - _futileFps) <= 0.10 * _futileFps) return SmartTdpChange.None;
                _futileUntil = double.NegativeInfinity;
            }

            bool enough;
            if (below) { _belowCount++; _nearCount = 0; enough = _belowCount >= 2; }
            else { _nearCount++; _belowCount = 0; enough = _nearCount >= NearSamplesToNudge; }
            if (_climbWatts is not null) _climbBestFps = Math.Max(_climbBestFps, fps);
            if (!enough || nowSeconds - _lastChange < UpCooldownSeconds) return SmartTdpChange.None;

            // Caiu abaixo até do FPS possível: a situação mudou, volta ao controle normal.
            if (_unreachable && below) LeaveUnreachable();

            _belowCount = 0;
            _nearCount = 0;

            // A curva desta sessão já mostra que mais potência não dá mais FPS (a partir de 3 W
            // acima, o FPS não passa de 5% a mais que o de agora, e não chega à meta): não sobe;
            // fica no menor valor que já dava esse FPS. Era o caso do Days Gone com meta 35: o
            // FPS oscilava 31–34 com qualquer potência entre 25 e 35 W, e cada oscilação subia.
            if (Watts < _max)
            {
                var (current, currentCount) = CurveAverage(Watts - 1, Watts + 1, nowSeconds);
                var (higher, higherCount) = CurveAverage(Watts + StepUpWatts, _max, nowSeconds);
                if (currentCount >= CurveMinSamples && higherCount >= CurveMinSamples && current is { } cur && higher is { } high
                    && high < Math.Max(cur, fps) * FutileGainRatio && high < _target * MeetRatio
                    // Watts mais altos com FPS bem menor que agora são de outra cena (pausa, replay):
                    // não servem de prova de que subir não adianta.
                    && high >= Math.Max(cur, fps) * (1 - SceneChangeRatio))
                {
                    var plateau = Math.Max(cur, high);
                    var knee = Knee(plateau, nowSeconds);
                    var baseline = _resumeReturnTo;
                    _resumeReturnTo = null;
                    _returnAfterClimb = null;
                    if (_unreachable) return SmartTdpChange.None; // já segurando: não estica o prazo da nova tentativa
                    _climbWatts = null;
                    MarkUnreachable(plateau, fps, nowSeconds);
                    _lastProbe = nowSeconds;
                    // Sem joelho medido, ao menos devolve o reforço da volta ao jogo.
                    if ((knee ?? baseline) is { } k && k < Watts)
                    {
                        Watts = Math.Max(_min, k);
                        _lastChange = nowSeconds;
                        _lastChangeWasDown = false;
                        return SmartTdpChange.Down;
                    }
                    return SmartTdpChange.None;
                }
            }

            // Subida que não adianta: compara com o FPS de 6 W atrás nesta mesma subida (ou
            // com o início, se bateu no teto). Sem ganho de 5%, potência não é o problema:
            // volta ao menor valor da subida que já dava este FPS e segura o FPS possível.
            (int Watts, double Fps)? reference = null;
            if (_climbWatts is { } from && Watts > from)
            {
                foreach (var point in _climbHistory)
                    if (point.Watts <= Watts - FutileClimbWatts && (reference is null || point.Watts > reference.Value.Watts))
                        reference = point;
                if (reference is null && Watts >= _max && _climbHistory.Count > 0) reference = _climbHistory[0];

                if (reference is { } r && fps < r.Fps * FutileGainRatio)
                {
                    var knee = Watts;
                    foreach (var point in _climbHistory)
                        if (point.Fps >= fps * 0.95 && point.Watts < knee) knee = point.Watts;
                    var revertTo = Math.Max(from, knee);

                    if (_returnAfterClimb is { } beforeProbe && beforeProbe > revertTo)
                    {
                        // A subida começou num teste de descida que falhou: o nível testado já
                        // se mostrou baixo, volta para o de antes do teste.
                        revertTo = Math.Min(Watts, beforeProbe);
                    }
                    else if (revertTo == from)
                    {
                        _floor = _climbFloor;
                        _floorUntil = _climbFloorUntil;
                        _floorConfirmed = _climbFloorConfirmed;
                        _floorIsLearned = _climbFloorIsLearned;
                    }
                    // A curva da sessão pode mostrar um valor ainda menor que já dava este FPS.
                    // Nunca abaixo do último valor que já segurou a meta: num replay a ~28 FPS qualquer
                    // potência "dá o patamar", e voltar até lá deixaria o jogo sem potência depois.
                    if (Knee(Math.Max(_climbBestFps, fps), nowSeconds) is { } curveKnee && curveKnee < revertTo)
                        revertTo = Math.Max(_min, Math.Min(revertTo, Math.Max(curveKnee, _provenWatts ?? _min)));
                    Watts = revertTo;
                    _resumeReturnTo = null;
                    _returnAfterClimb = null;
                    _coarse = _climbCoarse; // a subida não adiantou: a descida rápida continua valendo
                    _climbWatts = null;
                    MarkUnreachable(fps, nowSeconds);
                    _lastChange = nowSeconds;
                    _lastChangeWasDown = false;
                    return SmartTdpChange.Down;
                }
            }

            if (Watts >= _max)
            {
                // No teto sem alcançar a meta e sem subida para comparar (ex.: começou em 35 W):
                // depois de 20 s assim, a meta está fora de alcance.
                if (_atMaxSince is null) { _atMaxSince = nowSeconds; _climbBestFps = fps; }
                _climbBestFps = Math.Max(_climbBestFps, fps);
                if (nowSeconds - _atMaxSince.Value >= UnreachableAtMaxSeconds)
                {
                    _climbWatts = null;
                    MarkUnreachable(fps, nowSeconds);
                }
                return SmartTdpChange.None;
            }

            if (_climbWatts is null)
            {
                // Com reforço pendente a subida conta a partir do valor de antes dele: se não
                // adiantar, volta para lá.
                _climbWatts = _resumeReturnTo ?? Watts;
                _climbBestFps = fps;
                _climbHistory.Clear();
                _climbFloor = _floor;
                _climbFloorUntil = _floorUntil;
                _climbFloorConfirmed = _floorConfirmed;
                _climbFloorIsLearned = _floorIsLearned;
                _climbCoarse = _coarse;
            }

            _resumeReturnTo = null; // faltou FPS mesmo com o reforço: ele fica, e sobe mais
            // Subida logo depois da volta de uma vez (não de um passo de teste): não foi o teste que
            // falhou, foi a cena que pesou de novo (EA FC: lance parado a cada 30–60 s). Sem isso a
            // volta rápida se perdia e o TDP descia de 1 em 1 W por minutos.
            var afterProbe = _lastChangeWasDown && nowSeconds - _lastChange <= 30 && !_lastDownWasReturn;
            if (afterProbe)
            {
                // Uma descida de teste falhou: este é o ponto em que falta potência. Só isto
                // vira piso. (Antes toda subida virava piso por minutos: uma pausa ou um
                // replay a 30 FPS prendia o TDP no teto com o jogo sobrando FPS.)
                _coarse = false;
                _climbFromProbe = true;
                _probeWasReturn = _lastDownWasReturn;
                _preProbeFloor = (_floor, _floorUntil, _floorConfirmed, _floorHold, _floorIsLearned);
                _probeFrom = Watts + (_probeWasReturn ? 1 : _lastDownStep);
                RaiseFloor(Watts + 1, nowSeconds, confirmed: true);
                // Quando a meta voltar, retorna ao valor de antes do teste (que servia) em vez
                // de ficar onde a subida parar. Se o que falhou foi a própria volta, não repete.
                _returnAfterClimb = _probeWasReturn ? null : Math.Min(_max, _probeFrom);
            }
            else if (_climbFromProbe && below && !_probeWasReturn && Watts >= _floor && _preProbeFloor is { } pre)
            {
                // Já voltou ao valor de antes do teste e continua faltando FPS: a queda não foi
                // culpa do teste (pausa, replay). Desfaz o piso e trata como subida passageira.
                (_floor, _floorUntil, _floorConfirmed, _floorHold, _floorIsLearned) = pre;
                _preProbeFloor = null;
                _climbFromProbe = false;
            }
            else if (!_climbFromProbe && _climbWatts is { } climbBase && _provenWatts == climbBase)
            {
                // Só volta de uma vez para um valor que já tinha se mostrado suficiente.
                _returnAfterClimb ??= climbBase;
            }
            _climbHistory.Add((Watts, fps));
            Watts = Math.Min(_max, Watts + (below ? StepUpWatts : 1));
            _lastChange = nowSeconds;
            _lastChangeWasDown = false;
            return SmartTdpChange.Up;
        }

        // Meta cumprida.
        _belowCount = 0;
        _nearCount = 0;
        _climbWatts = null;
        _climbFromProbe = false;
        _atMaxSince = null;
        if (!_unreachable) _futileHold = FutileHoldSeconds;
        if (nowSeconds - Math.Max(_goodSince, _lastChange) >= ProvenAfterSeconds) _provenWatts = Watts;
        if (nowSeconds - _goodSince >= DownAfterGoodSeconds) _futileUntil = double.NegativeInfinity;

        // Folga clara (FPS 25% acima da meta): o valor aprendido não segura a descida — pode
        // ter ficado alto demais numa sessão anterior — e os passos são os rápidos.
        var surplus = fps >= _effectiveTarget * SurplusRatio;
        var floorActive = nowSeconds < _floorUntil && !(surplus && _floorIsLearned);
        var lowerBound = floorActive ? Math.Max(_min, _floor) : _min;
        var fast = _coarse || surplus;
        var downAfter = fast ? CoarseDownAfterGoodSeconds : DownAfterGoodSeconds;

        // A meta voltou depois de uma subida passageira: retorna de uma vez ao valor de antes.
        // Vale como teste: se faltar FPS logo em seguida, aí sim vira piso e sobe de novo.
        // Meta fora de alcance: segura onde está (ver comentário de UnreachableNoticeSeconds).
        if (_unreachable) return SmartTdpChange.None;

        if (_returnAfterClimb is { } baseWatts)
        {
            if (nowSeconds - _goodSince < ReturnAfterGoodSeconds || nowSeconds - _lastChange < ReturnAfterGoodSeconds)
                return SmartTdpChange.None;
            _returnAfterClimb = null;
            var to = Math.Max(lowerBound, baseWatts);
            if (to < Watts)
            {
                Watts = to;
                _lastChange = nowSeconds;
                _lastChangeWasDown = true;
                _lastDownWasReturn = true;
                IsSettled = false;
                return SmartTdpChange.Down;
            }
        }
        if (Watts > lowerBound
            && nowSeconds - _goodSince >= downAfter
            && nowSeconds - _lastChange >= downAfter)
        {
            var beforeStep = Watts;
            // Perto de um piso confirmado nesta sessão o passo é de 1 W: o de 2 W pulava o nível
            // que já falhou e repetia o mesmo teste a cada expiração do piso.
            // A primeira conferência de um valor aprendido também é de 1 W: se ele estiver certo,
            // a queda é uma só e pequena.
            var bigStep = fast && !_learnedCheckPending
                          && !(!_coarse && _floorConfirmed && !_floorIsLearned && Watts - CoarseStepDownWatts < _floor);
            _learnedCheckPending = false;
            Watts = Math.Max(lowerBound, Watts - (bigStep ? CoarseStepDownWatts : StepDownWatts));
            _lastDownStep = beforeStep - Watts;
            _lastDownWasReturn = false;
            _lastChange = nowSeconds;
            _lastChangeWasDown = true;
            IsSettled = false;
            return SmartTdpChange.Down;
        }

        if (_unreachable)
        {
            // Não se aprende um valor enquanto a meta não é a real.
        }
        else if (Watts <= lowerBound && nowSeconds - _lastChange >= SettleSeconds && (Watts == _min || _floorConfirmed))
            IsSettled = true;
        // Jogos com pausas/replays frequentes nunca passam 30 s sem mexer: também vale como
        // acomodado estar no piso confirmado por um teste desta sessão, com a meta cumprida.
        else if (Watts <= lowerBound && _floorConfirmed && !_floorIsLearned && nowSeconds < _floorUntil
                 && nowSeconds - Math.Max(_goodSince, _lastChange) >= DownAfterGoodSeconds)
            IsSettled = true;
        return SmartTdpChange.None;
    }
}
