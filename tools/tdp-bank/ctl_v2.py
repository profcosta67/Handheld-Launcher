"""TDP inteligente v2 — regra unica: watt a mais so se comprar FPS."""
import math
NEG = float('-inf')

class C:
    Meet = 0.95          # meta cumprida a partir de 95%
    Below = 0.90         # so sobe abaixo de 90% (entre 90 e 95: fica onde esta, o ruido nao mexe)
    WallExit = 1.05      # sai da parede so com FPS 5% acima do patamar (oscilar na borda nao conta)
    Ignore = 10          # FPS abaixo disto = pausa/carregamento: nao vale
    Settle = 4           # s depois de trocar os watts: leitura ainda e do valor antigo
    UpCd = 4             # s minimos entre subidas
    DownAfter = 12       # s cumprindo a meta antes de descer 1 W
    CoarseDownAfter = 6  # com muita sobra desce mais rapido
    Surplus = 1.20       # "muita sobra"
    ReturnWindow = 10    # caiu logo depois de NOS descermos: volta na hora
    FloorHold = 180      # depois de uma descida que nao deu, nao tenta de novo por 3 min
    DropRatio = 0.65     # queda brusca (corte de cena, carregamento): espera antes de subir
    DropHold = 15
    Window = 240         # curva FPS x watts da sessao: ultimos 4 min
    SceneDev = 0.12      # 3 leituras 12% fora da curva = cena mudou
    GainNeed = 1.05      # +2 W ou mais precisa comprar 5% de FPS; senao e parede
    WallMeet = 0.95      # na parede: menor W que entrega 95% do maximo (o ruido da leitura e ~3%)
    ProbeEvery = 90      # na parede: testa +3 W depois de 90 s; cada teste que falha dobra a espera (max 5 min)
    ProbeMaxEvery = 300
    ProbeStep = 3
    ProbeKeep = 1.04     # +3 W que compram 4% de FPS (media das leituras) valem
    ProbeLen = 8
    ResumeHold = 8
    SettledAfter = 30

    def __init__(s, target, mn, mx, start, learned=False, coarse=True):
        s.target = max(15, target); s.min = min(mn, mx); s.max = max(mn, mx)
        s.W = max(s.min, min(s.max, start))
        s.samples = []; s.dev = 0
        s.lastChange = NEG; s.lastDir = 0; s.downFrom = None
        s.okSince = None; s.prevFps = None; s.dropUntil = NEG
        s.floorW = s.min; s.floorUntil = NEG
        s.wall = False; s.plateau = 0.0; s.wallSince = 0.0; s.nextProbe = NEG; s.probe = None
        s.resumeTo = None; s.resumeAt = 0.0
        s.stableSince = 0.0; s.settled = False
        s.goodW = None; s.goodAt = NEG          # ponto de jogo: menor W que cumpre a meta sem sobra
        s.floorCheck = None                     # volta depois de descida: confirma se a culpa era nossa
        s.above = 0; s.probeGap = C.ProbeEvery
        s.started = False

    # ------------------------------------------------ curva
    def curve(s, now):
        acc = {}
        for t, w, f in s.samples:
            if now - t <= C.Window: acc.setdefault(w, []).append(f)
        return {w: sum(v) / len(v) for w, v in acc.items() if len(v) >= 2}

    # ------------------------------------------------ compatibilidade (eff/unreach usados no app)
    @property
    def unreach(s): return s.wall
    @property
    def eff(s): return s.plateau if s.wall else float(s.target)

    def resume(s, now, boost):
        # reforco curto na volta do Quick Settings; voltas seguidas nao acumulam
        if not s.started or s.wall or boost <= 0: return s.W
        base = s.resumeTo if s.resumeTo is not None else s.W
        boosted = min(s.max, base + boost)
        if boosted <= s.W: return s.W
        s.resumeTo = base; s.resumeAt = now; s.W = boosted; s.lastChange = now; s.lastDir = 0
        s.okSince = None; s.prevFps = None; s.dropUntil = NEG; s.settled = False
        return s.W

    def _set(s, w, now, direction, came_from=None):
        w = max(s.min, min(s.max, w))
        if w == s.W: return 'None'
        s.W = w; s.lastChange = now; s.lastDir = direction; s.downFrom = came_from
        s.stableSince = now; s.settled = False
        return 'Up' if direction > 0 else 'Down'

    def _enter_wall(s, now, cur):
        c = s.curve(now)
        s.plateau = max(list(c.values()) + [cur])
        s.wall = True; s.wallSince = now; s.probeGap = C.ProbeEvery; s.nextProbe = now + s.probeGap
        knee = min((w for w, m in c.items() if m >= s.plateau * C.WallMeet), default=s.W)
        if knee < s.W: return s._set(knee, now, -1)
        return 'None'

    # ------------------------------------------------ leitura (a cada ~2 s, mediana de 3)
    def on(s, fps, now):
        if fps < C.Ignore:
            s.okSince = None; s.prevFps = None
            return 'None'
        s.started = True

        # volta do Quick Settings: se a meta se manteve durante o reforco, devolve os watts de antes;
        # se caiu, o reforco era preciso e fica
        if s.resumeTo is not None:
            if now - s.resumeAt < C.ResumeHold: return 'None'
            back = s.resumeTo; s.resumeTo = None
            if fps >= (s.plateau * C.WallMeet if s.wall else s.target * C.Meet):
                return s._set(back, now, -1)

        settled_reading = now - s.lastChange >= C.Settle
        if settled_reading:
            c = s.curve(now)
            m = c.get(s.W)
            if m is not None and abs(fps - m) > C.SceneDev * m: s.dev += 1
            else: s.dev = 0
            if s.dev >= 3:                       # cena mudou: o que se mediu antes nao vale
                s.samples = []; s.dev = 0
                if s.wall: s.wall = False
            s.samples.append((now, s.W, fps))
            s.samples = [x for x in s.samples if now - x[0] <= C.Window]

        prev = s.prevFps; s.prevFps = fps

        # teste de +3 W na parede
        if s.probe is not None:
            if settled_reading: s.probe['reads'].append(fps)
            if now - s.probe['t'] < C.ProbeLen: return 'None'
            base, frm, reads = s.probe['base'], s.probe['from'], s.probe['reads']; s.probe = None
            got = sum(reads) / len(reads) if reads else fps
            if got < base * C.ProbeKeep:
                s.probeGap = min(C.ProbeMaxEvery, s.probeGap * 2)
            s.nextProbe = now + s.probeGap
            if got >= base * C.ProbeKeep:
                s.wall = False                   # mais potencia voltou a comprar FPS
                return 'None'
            return s._set(frm, now, -1)

        # Na parede, FPS 5% acima do patamar por 3 leituras: a parede mudou de lugar (cena mais
        # leve). Esquece a curva e reavalia contra a meta de verdade.
        if s.wall and settled_reading and fps >= s.plateau * C.WallExit:
            s.above += 1
            if s.above >= 4 or fps >= s.target * C.Meet:
                s.wall = False; s.above = 0; s.samples = [x for x in s.samples if x[0] == now]
        else:
            s.above = 0

        goal = s.plateau * C.WallMeet if s.wall else s.target * C.Meet

        # confirmacao da volta: se voltar resolveu, a descida era o problema (piso); senao, era o jogo
        if s.floorCheck is not None and settled_reading:
            back, at = s.floorCheck; s.floorCheck = None
            if fps >= goal:
                s.floorW = back; s.floorUntil = now + C.FloorHold
            else:
                s.dropUntil = now + C.DropHold

        if fps >= goal:
            # ponto de jogo: so com os watts parados ha 12 s (descendo num menu nao conta)
            if not s.wall and fps < s.target * C.Meet * C.Surplus and now - s.lastChange >= C.DownAfter:
                s.goodW = s.W; s.goodAt = now
            if s.okSince is None: s.okSince = now
            if now - s.stableSince >= C.SettledAfter: s.settled = True
            ref = s.plateau if s.wall else s.target * C.Meet
            surplus = fps / ref
            hold = C.CoarseDownAfter if surplus >= C.Surplus else C.DownAfter
            if now - s.okSince >= hold and now - s.lastChange >= hold and s.W > s.min:
                step = 1
                if surplus >= C.Surplus: step = max(1, min(4, int((surplus - 1) * s.W * 0.5)))
                elif s.wall and not any(w < s.W for w in s.curve(now)): step = 2
                new = s.W - step
                if now < s.floorUntil: new = max(new, s.floorW)
                if new < s.W:
                    s.okSince = now
                    return s._set(new, now, -1, came_from=s.W)
            if s.wall and now >= s.nextProbe and s.W + C.ProbeStep <= s.max:
                base = s.curve(now).get(s.W, fps)   # media medida neste W (uma leitura so engana)
                s.probe = {'t': now, 'base': base, 'from': s.W, 'reads': []}
                return s._set(s.W + C.ProbeStep, now, +1)
            return 'None'

        # ---- abaixo da meta
        s.okSince = None; s.settled = False
        # caiu logo depois de NOS descermos: volta na hora e nao tenta de novo por um tempo
        if s.lastDir < 0 and s.downFrom is not None and now - s.lastChange <= C.ReturnWindow:
            # A nossa descida e culpada se, pela proporcao dos watts, ela podia levar abaixo da meta
            # (vsync 60->30 entra aqui), ou se a queda foi moderada. Uma queda pela metade depois de
            # tirar 1 W de um jogo com folga e o jogo (replay, corte de cena), nao a descida.
            crash = fps < s.target * C.DropRatio
            before = s.curve(now).get(s.downFrom, prev)   # media medida antes da descida (uma leitura engana)
            plausible = before is not None and before * s.W / s.downFrom < goal * 1.03
            if not crash or plausible:
                back = s.downFrom
                if s.goodW is not None and now - s.goodAt <= C.Window and not s.wall:
                    back = max(back, s.goodW)    # descemos num menu: volta direto ao ponto de jogo
                s.floorCheck = (back, now)       # o piso so vale se voltar resolver
                return s._set(back, now, +1)
        # queda brusca sem termos mexido: corte de cena/carregamento — espera. Mas, se estamos ABAIXO
        # do ponto de jogo conhecido (descemos num menu, por exemplo), volta direto para ele.
        if prev is not None and prev >= goal and fps < s.target * C.DropRatio:
            if s.goodW is not None and now - s.goodAt <= C.Window and s.W <= s.goodW - 2 and not s.wall:
                return s._set(s.goodW, now, +1)
            s.dropUntil = now + C.DropHold
        if now < s.dropUntil and fps < s.target * C.DropRatio: return 'None'

        if s.wall: return 'None'                 # parede: segura no joelho (a cena mudando reavalia)
        if fps >= s.target * C.Below: return 'None'   # faixa morta: ruido em volta da meta nao sobe
        if now - s.lastChange < C.UpCd + C.Settle: return 'None'

        c = s.curve(now)
        cur = c.get(s.W, fps)
        lower = [m for w, m in c.items() if w <= s.W - 2]
        if lower and max(lower) * C.GainNeed > cur:
            return s._enter_wall(now, cur)       # a ultima subida nao comprou FPS
        if s.W >= s.max:
            return s._enter_wall(now, cur)

        deficit = s.target / max(fps, 1)
        step = max(2, min(6, math.ceil((deficit - 1) * s.W * 0.8)))
        return s._set(s.W + step, now, +1)
