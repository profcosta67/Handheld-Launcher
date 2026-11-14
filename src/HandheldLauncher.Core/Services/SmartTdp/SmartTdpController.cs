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
    public const double BelowRatio = 0.93;   // abaixo disto o jogo está engasgando
    public const double MeetRatio = 0.985;   // a partir disto a meta está cumprida
    public const double IgnoreBelowFps = 10; // carregamento/pausa: a leitura não vale
    public const int StepUpWatts = 3;
    public const int StepDownWatts = 1;
    public const double UpCooldownSeconds = 4;
    public const double DownAfterGoodSeconds = 12;
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

    public SmartTdpController(int targetFps, int minWatts, int maxWatts, int startWatts, bool startIsLearned = false)
    {
        _target = Math.Max(15, targetFps);
        _min = Math.Min(minWatts, maxWatts);
        _max = Math.Max(minWatts, maxWatts);
        Watts = Math.Clamp(startWatts, _min, _max);
        _floor = _min;
        _startIsLearned = startIsLearned;
    }

    private readonly bool _startIsLearned;

    public int Watts { get; private set; }
    public int TargetFps => _target;

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
                _floor = _climbFloor;
                _floorUntil = _climbFloorUntil;
                _floorConfirmed = _climbFloorConfirmed;
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
                _climbWatts = Watts;
                _climbFps = fps;
                _climbFloor = _floor;
                _climbFloorUntil = _floorUntil;
                _climbFloorConfirmed = _floorConfirmed;
            }

            var afterProbe = _lastChangeWasDown && nowSeconds - _lastChange <= 30;
            RaiseFloor(Watts + 1, nowSeconds, afterProbe);
            Watts = Math.Min(_max, Watts + (below ? StepUpWatts : 1));
            _lastChange = nowSeconds;
            _lastChangeWasDown = false;
            return SmartTdpChange.Up;
        }

        // Meta cumprida.
        _belowCount = 0;
        _nearCount = 0;
        _climbWatts = null;
        _futileHold = FutileHoldSeconds;

        var lowerBound = nowSeconds < _floorUntil ? Math.Max(_min, _floor) : _min;
        if (Watts > lowerBound
            && nowSeconds - _goodSince >= DownAfterGoodSeconds
            && nowSeconds - _lastChange >= DownAfterGoodSeconds)
        {
            Watts = Math.Max(lowerBound, Watts - StepDownWatts);
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
