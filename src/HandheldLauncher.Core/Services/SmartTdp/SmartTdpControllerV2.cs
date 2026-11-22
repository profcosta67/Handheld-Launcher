namespace HandheldLauncher.Core.Services.SmartTdp;

/// <summary>
/// 0.27 v2 (22/11) — TDP inteligente reescrito em torno de UMA regra: <b>watt a mais só se
/// comprar FPS</b>. A meta de FPS escolhida manda; o controle procura o menor TDP que a entrega e
/// não gasta energia que não vira quadro.
///
/// Como decide, a cada leitura (~2 s, mediana de 3):
/// <list type="bullet">
/// <item><b>Cumprindo a meta</b> (≥ 95%): desce 1 W a cada 12 s; com muita sobra (≥ 20%) desce
/// mais e mais rápido, proporcional à sobra.</item>
/// <item><b>A nossa descida derrubou o FPS</b>: volta na hora e não tenta de novo por 3 min — mas
/// só se voltar resolver (se não resolver, a culpa era do jogo).</item>
/// <item><b>Queda brusca sem termos mexido</b> (corte de cena, replay, carregamento): espera 15 s
/// antes de subir. Exceção: se estamos abaixo do ponto em que o jogo rodava (descemos num menu),
/// volta direto para ele.</item>
/// <item><b>Abaixo de 90% da meta</b>: sobe proporcional ao que falta (2 a 6 W). Entre 90 e 95%
/// não mexe — é ruído.</item>
/// <item><b>Parede</b>: se a última subida não comprou nem 5% de FPS, o jogo está limitado (placa
/// de vídeo, CPU, cena travada). Volta para o "joelho" — o menor W que entrega 95% do máximo — e
/// fica lá. De tempos em tempos testa +3 W (90 s, dobrando a cada teste que falha, até 5 min); se
/// o FPS subir 5% acima do patamar, a parede mudou e tudo é reavaliado.</item>
/// </list>
///
/// A curva FPS×watts vem desta sessão (últimos 4 min); quando a cena muda (3 leituras 12% fora
/// da curva) ela é esquecida. Nada é ajustado por jogo.
///
/// Toda mudança aqui precisa passar no banco fixo de cenários
/// (<c>SmartTdpScenarioBankTests</c>): leve, médio, pesado, parede da placa, meta pouco acima do
/// teto, cortes de cena, replays, menus, vsync 60/30, área pesada, pausas, cenas alternadas,
/// limitador de FPS ligado e começo no teto. O mesmo banco existe em Python (ferramenta de ajuste)
/// e o teste confere que os dois dão exatamente os mesmos números.
///
/// Classe pura (sem relógio nem hardware): o tempo entra por parâmetro.
/// </summary>
public sealed class SmartTdpControllerV2 : ISmartTdpController
{
    public const double MeetRatio = 0.95;
    public const double BelowRatio = 0.90;
    public const double WallExitRatio = 1.05;
    public const double IgnoreBelowFps = 10;
    public const double SettleSeconds = 4;
    public const double UpCooldownSeconds = 4;
    public const double DownAfterSeconds = 12;
    public const double CoarseDownAfterSeconds = 6;
    public const double SurplusRatio = 1.20;
    public const double ReturnWindowSeconds = 10;
    public const double FloorHoldSeconds = 180;
    public const double DropRatio = 0.65;
    public const double DropHoldSeconds = 15;
    public const double CurveWindowSeconds = 240;
    public const double SceneChangeRatio = 0.12;
    public const double GainNeedRatio = 1.05;
    public const double WallMeetRatio = 0.95;
    public const double ProbeEverySeconds = 90;
    public const double ProbeMaxEverySeconds = 300;
    public const int ProbeStepWatts = 3;
    public const double ProbeKeepRatio = 1.04;
    public const double ProbeLengthSeconds = 8;
    public const double ResumeBoostHoldSeconds = 8;
    public const double SettledAfterSeconds = 30;

    private readonly int _target;
    private readonly int _min;
    private readonly int _max;

    private readonly List<(double T, int W, double Fps)> _samples = new();
    private int _sceneDev;
    private double _lastChange = double.NegativeInfinity;
    private int _lastDir;
    private int? _downFrom;
    private double? _okSince;
    private double? _prevFps;
    private double _dropUntil = double.NegativeInfinity;
    private int _floorW;
    private double _floorUntil = double.NegativeInfinity;
    private bool _wall;
    private double _plateau;
    private double _wallSince;
    private double _nextProbe = double.NegativeInfinity;
    private double _probeGap = ProbeEverySeconds;
    private (double T, double Base, int From, List<double> Reads, bool Heavier)? _probe;
    private readonly List<double> _wallLow = new();   // leituras abaixo do joelho, na parede
    private int? _resumeTo;
    private double _resumeAt;
    private double _stableSince;
    private int? _goodW;
    private double _goodAt = double.NegativeInfinity;
    private int? _floorCheck;
    private int _above;
    private bool _started;

    /// <param name="startIsLearned">Aceito por compatibilidade com o controle antigo; aqui o começo
    /// aprendido é só o ponto de partida.</param>
    /// <param name="coarseDescent">Idem: a descida aqui é sempre proporcional à sobra.</param>
    public SmartTdpControllerV2(int targetFps, int minWatts, int maxWatts, int startWatts,
        bool startIsLearned = false, bool coarseDescent = true)
    {
        _target = Math.Max(15, targetFps);
        _min = Math.Min(minWatts, maxWatts);
        _max = Math.Max(minWatts, maxWatts);
        Watts = Math.Clamp(startWatts, _min, _max);
        _floorW = _min;
    }

    public int Watts { get; private set; }
    public int TargetFps => _target;
    public bool IsSettled { get; private set; }
    public bool TargetUnreachable => _wall;
    public int EffectiveTargetFps => _wall ? (int)Math.Round(_plateau) : _target;
    public double UnreachableSinceSeconds => _wallSince;
    public int ProbeExtraWatts => _probe is null ? 0 : ProbeStepWatts;
    public int BaselineWatts => _resumeTo ?? (Watts - ProbeExtraWatts);
    public bool ResumeBoostPending => _resumeTo is not null;

    /// <summary>Patamar de FPS da parede atual (0 fora da parede). Para o log.</summary>
    public double PlateauFps => _wall ? _plateau : 0;

    // ======================================================================= curva

    private Dictionary<int, double> Curve(double now)
    {
        var acc = new Dictionary<int, (double Sum, int N)>();
        foreach (var (t, w, f) in _samples)
        {
            if (now - t > CurveWindowSeconds) continue;
            acc[w] = acc.TryGetValue(w, out var a) ? (a.Sum + f, a.N + 1) : (f, 1);
        }
        var result = new Dictionary<int, double>();
        foreach (var (w, a) in acc)
            if (a.N >= 2) result[w] = a.Sum / a.N;
        return result;
    }

    // ======================================================================= volta do Quick Settings

    /// <summary>
    /// Reforço curto na volta ao jogo (Quick Settings, outra janela). Se a meta se mantém por
    /// <see cref="ResumeBoostHoldSeconds"/>, devolve os watts de antes; se o FPS cair, o reforço
    /// fica (era preciso). Voltas seguidas não acumulam.
    /// </summary>
    public int PrepareForResume(double nowSeconds, int boostWatts)
    {
        _okSince = null;      // o que veio antes da saída não conta na volta
        _prevFps = null;
        if (!_started || _wall || boostWatts <= 0) return Watts;
        var baseline = _resumeTo ?? Watts;
        var boosted = Math.Min(_max, baseline + boostWatts);
        if (boosted <= Watts) return Watts;
        _resumeTo = baseline;
        _resumeAt = nowSeconds;
        Watts = boosted;
        _lastChange = nowSeconds;
        _lastDir = 0;
        _okSince = null;
        _prevFps = null;
        _dropUntil = double.NegativeInfinity;
        IsSettled = false;
        _stableSince = nowSeconds;   // o reforço nunca vira "valor aprendido" na hora
        return Watts;
    }

    /// <summary>Devolve o reforço sem esperar leituras (o jogo parou de entregar FPS).</summary>
    public int? DropResumeBoost(double nowSeconds)
    {
        if (_resumeTo is not { } back) return null;
        _resumeTo = null;
        if (back >= Watts) return null;
        Watts = back;
        _lastChange = nowSeconds;
        _lastDir = 0;
        _downFrom = null;
        return Watts;
    }

    // ======================================================================= decisão

    private SmartTdpChange Set(int watts, double now, int direction, int? cameFrom = null)
    {
        watts = Math.Clamp(watts, _min, _max);
        if (watts == Watts) return SmartTdpChange.None;
        Watts = watts;
        _lastChange = now;
        _lastDir = direction;
        _downFrom = cameFrom;
        _stableSince = now;
        IsSettled = false;
        return direction > 0 ? SmartTdpChange.Up : SmartTdpChange.Down;
    }

    private SmartTdpChange EnterWall(double now, double current)
    {
        var curve = Curve(now);
        var plateau = current;
        foreach (var m in curve.Values) if (m > plateau) plateau = m;
        _plateau = plateau;
        _wall = true;
        _wallSince = now;
        _probeGap = ProbeEverySeconds;
        _nextProbe = now + _probeGap;
        _wallLow.Clear();

        var knee = Watts;
        var found = false;
        foreach (var (w, m) in curve)
            if (m >= _plateau * WallMeetRatio && (!found || w < knee)) { knee = w; found = true; }
        if (!found) knee = Watts;
        return knee < Watts ? Set(knee, now, -1) : SmartTdpChange.None;
    }

    public SmartTdpChange OnSample(double fps, double nowSeconds)
    {
        var now = nowSeconds;
        if (fps < IgnoreBelowFps)
        {
            _okSince = null;
            _prevFps = null;
            return SmartTdpChange.None;
        }
        _started = true;

        if (_resumeTo is { } resumeBack)
        {
            if (now - _resumeAt < ResumeBoostHoldSeconds) return SmartTdpChange.None;
            _resumeTo = null;
            if (fps >= (_wall ? _plateau * WallMeetRatio : _target * MeetRatio))
                return Set(resumeBack, now, -1);
        }

        var settledReading = now - _lastChange >= SettleSeconds;
        if (settledReading)
        {
            var c = Curve(now);
            if (c.TryGetValue(Watts, out var m) && Math.Abs(fps - m) > SceneChangeRatio * m) _sceneDev++;
            else _sceneDev = 0;
            if (_sceneDev >= 3)
            {
                // a cena mudou: o que se mediu antes não vale para agora
                _samples.Clear();
                _sceneDev = 0;
                if (_wall) _wall = false;
                _probe = null;   // teste de +3 W em andamento não vale mais (fica nos watts atuais)
            }
            _samples.Add((now, Watts, fps));
            _samples.RemoveAll(x => now - x.T > CurveWindowSeconds);
        }

        var prev = _prevFps;
        _prevFps = fps;

        // teste de +3 W na parede
        if (_probe is { } probe)
        {
            if (settledReading) probe.Reads.Add(fps);
            if (now - probe.T < ProbeLengthSeconds) return SmartTdpChange.None;
            _probe = null;
            var got = fps;
            if (probe.Reads.Count > 0)
            {
                var sum = 0.0;
                foreach (var r in probe.Reads) sum += r;
                got = sum / probe.Reads.Count;
            }
            if (got < probe.Base * ProbeKeepRatio)
                _probeGap = Math.Min(ProbeMaxEverySeconds, _probeGap * 2);
            _nextProbe = now + _probeGap;
            if (got >= probe.Base * ProbeKeepRatio)
            {
                _wall = false;              // mais potência voltou a comprar FPS
                if (probe.Heavier)
                {
                    // a cena ficou mais pesada: a curva de antes da queda não vale mais
                    var since = probe.T - 12;
                    _samples.RemoveAll(x => x.T < since);
                }
                return SmartTdpChange.None;
            }
            return Set(probe.From, now, -1);
        }

        // Na parede, FPS 5% acima do patamar por 4 leituras: a parede mudou de lugar.
        if (_wall && settledReading && fps >= _plateau * WallExitRatio)
        {
            _above++;
            if (_above >= 4 || fps >= _target * MeetRatio)
            {
                _wall = false;
                _above = 0;
                _samples.RemoveAll(x => x.T != now);
            }
        }
        else
        {
            _above = 0;
        }

        var goal = _wall ? _plateau * WallMeetRatio : _target * MeetRatio;

        // confirmação da volta: se voltar resolveu, a descida era o problema (piso); senão, era o jogo
        if (_floorCheck is { } checkBack && settledReading)
        {
            _floorCheck = null;
            if (fps >= goal)
            {
                _floorW = checkBack;
                _floorUntil = now + FloorHoldSeconds;
            }
            else
            {
                _dropUntil = now + DropHoldSeconds;
            }
        }

        if (fps >= goal)
        {
            _wallLow.Clear();
            // ponto de jogo: só com os watts parados há 12 s (descendo num menu não conta)
            if (!_wall && fps < _target * MeetRatio * SurplusRatio && now - _lastChange >= DownAfterSeconds)
            {
                _goodW = Watts;
                _goodAt = now;
            }
            _okSince ??= now;
            if (now - _stableSince >= SettledAfterSeconds) IsSettled = true;

            var reference = _wall ? _plateau : _target * MeetRatio;
            var surplus = fps / reference;
            var hold = surplus >= SurplusRatio ? CoarseDownAfterSeconds : DownAfterSeconds;
            if (now - _okSince.Value >= hold && now - _lastChange >= hold && Watts > _min)
            {
                var step = 1;
                if (surplus >= SurplusRatio)
                {
                    step = Math.Max(1, Math.Min(4, (int)((surplus - 1) * Watts * 0.5)));
                }
                else if (_wall)
                {
                    var anyLower = false;
                    foreach (var w in Curve(now).Keys) if (w < Watts) { anyLower = true; break; }
                    if (!anyLower) step = 2;
                }
                var next = Watts - step;
                if (now < _floorUntil) next = Math.Max(next, _floorW);
                if (next < Watts)
                {
                    _okSince = now;
                    return Set(next, now, -1, cameFrom: Watts);
                }
            }
            if (_wall && now >= _nextProbe && Watts + ProbeStepWatts <= _max)
            {
                var probeBase = Curve(now).TryGetValue(Watts, out var bm) ? bm : fps;
                _probe = (now, probeBase, Watts, new List<double>(), false);
                return Set(Watts + ProbeStepWatts, now, +1);
            }
            return SmartTdpChange.None;
        }

        // ---- abaixo da meta
        _okSince = null;
        IsSettled = false;

        // caiu logo depois de NÓS descermos: volta na hora
        if (_lastDir < 0 && _downFrom is { } downFrom && now - _lastChange <= ReturnWindowSeconds)
        {
            // A descida é culpada se, pela proporção dos watts, podia levar abaixo da meta (vsync
            // 60→30 entra aqui), ou se a queda foi moderada. Queda pela metade depois de tirar 1 W
            // de um jogo com folga é o jogo (replay, corte de cena), não a descida.
            var crash = fps < _target * DropRatio;
            double? before = Curve(now).TryGetValue(downFrom, out var bf) ? bf : prev;
            var plausible = before is { } b && b * Watts / downFrom < goal * 1.03;
            if (!crash || plausible)
            {
                var back = downFrom;
                if (_goodW is { } gw && now - _goodAt <= CurveWindowSeconds && !_wall)
                    back = Math.Max(back, gw);     // descemos num menu: volta direto ao ponto de jogo
                _floorCheck = back;                // o piso só vale se voltar resolver
                return Set(back, now, +1);
            }
        }

        // queda brusca sem termos mexido: corte de cena/carregamento — espera. "Estava bem" = acima
        // de 90% da meta (o jogo pode estar parado na faixa morta, entre 90 e 95%).
        if (prev is { } p && p >= goal * (BelowRatio / MeetRatio) && fps < _target * DropRatio)
        {
            if (_goodW is { } gw && now - _goodAt <= CurveWindowSeconds && Watts <= gw - 2 && !_wall)
                return Set(gw, now, +1);
            _dropUntil = now + DropHoldSeconds;
        }
        if (now < _dropUntil && fps < _target * DropRatio) return SmartTdpChange.None;

        if (_wall)
        {
            // Abaixo do joelho por ~10 s, bem abaixo (média < ~86% do patamar): talvez a cena tenha
            // ficado mais pesada e o patamar medido não vale mais. O teste de +3 W decide, com a
            // média dessas leituras como base. No máximo com metade da espera dos testes normais.
            if (settledReading)
            {
                _wallLow.Add(fps);
                if (_wallLow.Count > 5) _wallLow.RemoveRange(0, _wallLow.Count - 5);
                var sumLow = 0.0;
                foreach (var v in _wallLow) sumLow += v;
                var low = sumLow / _wallLow.Count;
                if (_wallLow.Count >= 5 && low < _plateau * WallMeetRatio * 0.91
                    && Watts + ProbeStepWatts <= _max && now >= _nextProbe - _probeGap / 2)
                {
                    _wallLow.Clear();
                    _probe = (now, low, Watts, new List<double>(), true);
                    return Set(Watts + ProbeStepWatts, now, +1);
                }
            }
            return SmartTdpChange.None;                           // segura no joelho
        }
        if (fps >= _target * BelowRatio) return SmartTdpChange.None; // faixa morta: ruído não sobe
        if (now - _lastChange < UpCooldownSeconds + SettleSeconds) return SmartTdpChange.None;

        var curve = Curve(now);
        var current = curve.TryGetValue(Watts, out var cm) ? cm : fps;
        var hasLower = false;
        var maxLower = double.NegativeInfinity;
        foreach (var (w, m) in curve)
            if (w <= Watts - 2) { hasLower = true; if (m > maxLower) maxLower = m; }
        if (hasLower && maxLower > current * (1 + SceneChangeRatio))
        {
            // menos watts davam MAIS FPS: impossível no mesmo trecho — a cena ficou pesada. Não é
            // parede: esquece a curva antiga. Espera mais uma leitura neste W antes de subir: assim,
            // depois da subida, já dá para ver se ela comprou FPS (corte de cena travado não sobe
            // duas vezes).
            _samples.RemoveAll(x => x.T != now);
            return SmartTdpChange.None;
        }
        else if (hasLower && maxLower * GainNeedRatio > current)
        {
            return EnterWall(now, current);            // a última subida não comprou FPS
        }
        if (Watts >= _max)
            return EnterWall(now, current);

        var deficit = _target / Math.Max(fps, 1);
        var climb = Math.Max(2, Math.Min(6, (int)Math.Ceiling((deficit - 1) * Watts * 0.8)));
        return Set(Watts + climb, now, +1);
    }
}
