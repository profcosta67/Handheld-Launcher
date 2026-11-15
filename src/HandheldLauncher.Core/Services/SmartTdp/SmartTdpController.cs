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
    public const double SettleSeconds = 30;
    public const double MaxFloorHoldSeconds = 1800;
    public const double IgnoreLowFpsForSeconds = 20;
    public const int FutileClimbWatts = 6;
    public const double FutileGainRatio = 1.05;
    public const double FutileHoldSeconds = 300;
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
        _min = Math.Min(minWatts, maxWatts);
        _max = Math.Max(minWatts, maxWatts);
        Watts = Math.Clamp(startWatts, _min, _max);
        _floor = _min;
        _startIsLearned = startIsLearned;
        _coarse = !startIsLearned && coarseDescent;
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
        _lastChangeWasDown = false; // a volta não é uma descida de teste
        // Uma subida em andamento continua sendo medida a partir de onde começou: se ela se
        // mostrar inútil, volta para antes dela (e não para o valor com reforço).

        // Ainda na abertura do jogo (sem nenhuma leitura avaliada) não há o que reforçar; e
        // durante a espera por subida inútil, mais potência já se mostrou sem efeito. Um
        // reforço que ainda não foi devolvido continua pendente.
        if (!_started || nowSeconds < _futileUntil || boostWatts <= 0) return Watts;

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
    public int BaselineWatts => _resumeReturnTo ?? Watts;

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
    private double _climbFps;
    private int _climbFloor;
    private double _climbFloorUntil;
    private bool _climbFloorConfirmed;
    private bool _climbCoarse;
    private double _futileUntil = double.NegativeInfinity;
    private double _futileHold = FutileHoldSeconds;

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
                // Valor aprendido: não sai testando para baixo logo de cara.
                _floor = Watts;
                _floorUntil = nowSeconds + FloorHoldSeconds;
                _floorConfirmed = true;
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
                _belowCount = 0;
                _nearCount = 0;
                _goodSince = nowSeconds;
                return SmartTdpChange.None;
            }
        }
        else
        {
            _lowFpsSince = null;
        }

        var below = fps < _target * BelowRatio;
        var near = !below && fps < _target * MeetRatio;

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
            if (nowSeconds < _futileUntil) return SmartTdpChange.None;

            bool enough;
            if (below) { _belowCount++; _nearCount = 0; enough = _belowCount >= 2; }
            else { _nearCount++; _belowCount = 0; enough = _nearCount >= NearSamplesToNudge; }
            if (!enough || nowSeconds - _lastChange < UpCooldownSeconds) return SmartTdpChange.None;

            _belowCount = 0;
            _nearCount = 0;

            // Subiu bastante (ou bateu no teto) e o FPS não saiu do lugar: potência não é
            // o problema. Volta para onde estava e espera alguns minutos.
            if (_climbWatts is { } from && Watts > from
                && (Watts - from >= FutileClimbWatts || Watts >= _max)
                && fps < _climbFps * FutileGainRatio)
            {
                Watts = from;
                _resumeReturnTo = null;
                _returnAfterClimb = null;
                _floor = _climbFloor;
                _floorUntil = _climbFloorUntil;
                _floorConfirmed = _climbFloorConfirmed;
                _coarse = _climbCoarse; // a subida não adiantou: a descida rápida continua valendo
                _climbWatts = null;
                _futileUntil = nowSeconds + _futileHold;
                _futileHold = Math.Min(MaxFloorHoldSeconds, _futileHold * 2);
                _lastChange = nowSeconds;
                _lastChangeWasDown = false;
                return SmartTdpChange.Down;
            }

            if (Watts >= _max) return SmartTdpChange.None;

            if (_climbWatts is null)
            {
                // Com reforço pendente a subida conta a partir do valor de antes dele: se não
                // adiantar, volta para lá.
                _climbWatts = _resumeReturnTo ?? Watts;
                _climbFps = fps;
                _climbFloor = _floor;
                _climbFloorUntil = _floorUntil;
                _climbFloorConfirmed = _floorConfirmed;
                _climbCoarse = _coarse;
            }

            _resumeReturnTo = null; // faltou FPS mesmo com o reforço: ele fica, e sobe mais
            var afterProbe = _lastChangeWasDown && nowSeconds - _lastChange <= 30;
            if (afterProbe)
            {
                // Uma descida de teste falhou: este é o ponto em que falta potência. Só isto
                // vira piso. (Antes toda subida virava piso por minutos: uma pausa ou um
                // replay a 30 FPS prendia o TDP no teto com o jogo sobrando FPS.)
                _coarse = false;
                _returnAfterClimb = null;
                _climbFromProbe = true;
                RaiseFloor(Watts + 1, nowSeconds, confirmed: true);
            }
            else if (!_climbFromProbe && _climbWatts is { } climbBase && _provenWatts == climbBase)
            {
                // Só volta de uma vez para um valor que já tinha se mostrado suficiente.
                _returnAfterClimb ??= climbBase;
            }
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
        _futileHold = FutileHoldSeconds;
        if (nowSeconds - Math.Max(_goodSince, _lastChange) >= ProvenAfterSeconds) _provenWatts = Watts;

        var lowerBound = nowSeconds < _floorUntil ? Math.Max(_min, _floor) : _min;
        var downAfter = _coarse ? CoarseDownAfterGoodSeconds : DownAfterGoodSeconds;

        // A meta voltou depois de uma subida passageira: retorna de uma vez ao valor de antes.
        // Vale como teste: se faltar FPS logo em seguida, aí sim vira piso e sobe de novo.
        if (_returnAfterClimb is { } baseWatts)
        {
            if (nowSeconds - _goodSince < DownAfterGoodSeconds || nowSeconds - _lastChange < DownAfterGoodSeconds)
                return SmartTdpChange.None;
            _returnAfterClimb = null;
            var to = Math.Max(lowerBound, baseWatts);
            if (to < Watts)
            {
                Watts = to;
                _lastChange = nowSeconds;
                _lastChangeWasDown = true;
                IsSettled = false;
                return SmartTdpChange.Down;
            }
        }
        if (Watts > lowerBound
            && nowSeconds - _goodSince >= downAfter
            && nowSeconds - _lastChange >= downAfter)
        {
            Watts = Math.Max(lowerBound, Watts - (_coarse ? CoarseStepDownWatts : StepDownWatts));
            _lastChange = nowSeconds;
            _lastChangeWasDown = true;
            IsSettled = false;
            return SmartTdpChange.Down;
        }

        if (Watts <= lowerBound && nowSeconds - _lastChange >= SettleSeconds && (Watts == _min || _floorConfirmed))
            IsSettled = true;
        return SmartTdpChange.None;
    }
}
