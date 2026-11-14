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

    private int _nearCount;
    private double _floorHold = FloorHoldSeconds;

    /// <summary>
    /// Marca o piso depois de uma descida que não deu certo. Se o mesmo ponto falhar de
    /// novo, o intervalo até a próxima tentativa dobra (3, 6, 12… até 30 min): o sistema
    /// continua se adaptando, mas para de incomodar quando já achou o valor do jogo.
    /// </summary>
    private void RaiseFloor(int floor, double nowSeconds)
    {
        floor = Math.Clamp(floor, _min, _max);
        _floorHold = floor == _floor ? Math.Min(MaxFloorHoldSeconds, _floorHold * 2) : FloorHoldSeconds;
        _floor = floor;
        _floorUntil = nowSeconds + _floorHold;
    }

    public int Watts { get; private set; }
    public int TargetFps => _target;

    /// <summary>true quando o valor atual se manteve sem quedas — vale a pena lembrar para a próxima vez.</summary>
    public bool IsSettled { get; private set; }

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
            }
        }

        if (double.IsNaN(fps) || fps < IgnoreBelowFps)
        {
            _belowCount = 0;
            _goodSince = nowSeconds;
            return SmartTdpChange.None;
        }

        if (fps < _target * BelowRatio)
        {
            _belowCount++;
            _goodSince = nowSeconds;
            IsSettled = false;
            if (_belowCount < 2 || nowSeconds - _lastChange < UpCooldownSeconds) return SmartTdpChange.None;

            _belowCount = 0;
            _nearCount = 0;
            RaiseFloor(Watts + 1, nowSeconds);
            if (Watts >= _max) return SmartTdpChange.None;

            Watts = Math.Min(_max, Watts + StepUpWatts);
            _lastChange = nowSeconds;
            return SmartTdpChange.Up;
        }

        _belowCount = 0;

        if (fps < _target * MeetRatio)
        {
            // Quase lá: não é hora de tirar potência. Se ficar "quase" por vários segundos,
            // falta um pouco — sobe 1 W e guarda este ponto como piso.
            _goodSince = nowSeconds;
            IsSettled = false;
            _nearCount++;
            if (_nearCount < NearSamplesToNudge || nowSeconds - _lastChange < UpCooldownSeconds || Watts >= _max)
                return SmartTdpChange.None;

            _nearCount = 0;
            RaiseFloor(Watts + 1, nowSeconds);
            Watts = Math.Min(_max, Watts + 1);
            _lastChange = nowSeconds;
            return SmartTdpChange.Up;
        }

        _nearCount = 0;

        var lowerBound = nowSeconds < _floorUntil ? Math.Max(_min, _floor) : _min;
        if (Watts > lowerBound
            && nowSeconds - _goodSince >= DownAfterGoodSeconds
            && nowSeconds - _lastChange >= DownAfterGoodSeconds)
        {
            Watts = Math.Max(lowerBound, Watts - StepDownWatts);
            _lastChange = nowSeconds;
            IsSettled = false;
            return SmartTdpChange.Down;
        }

        if (Watts <= lowerBound && nowSeconds - _lastChange >= SettleSeconds) IsSettled = true;
        return SmartTdpChange.None;
    }
}
