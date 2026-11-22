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

    def __init__(s, target, mn, mx, start, learned=False, coarse=True, capped=False):
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
        s.wallLow = []
        s.judgeUntil = NEG                      # queda brusca logo apos descer na parede: julga a descida depois
        s.capped = capped                       # limite de FPS na meta ligado: a sobra nao aparece
        s.memKnee = None; s.memPlateau = 0.0    # parede ja medida nesta sessao

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
        s.okSince = None; s.prevFps = None       # o que veio antes da saida nao conta na volta
        if not s.started or s.wall or boost <= 0: return s.W
        base = s.resumeTo if s.resumeTo is not None else s.W
        boosted = min(s.max, base + boost)
        if boosted <= s.W: return s.W
        s.resumeTo = base; s.resumeAt = now; s.W = boosted; s.lastChange = now; s.lastDir = 0
        s.okSince = None; s.prevFps = None; s.dropUntil = NEG; s.settled = False
        s.stableSince = now                      # o reforco nunca vira "valor aprendido" na hora
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
        s.wallLow = []
        knee = min((w for w, m in c.items() if m >= s.plateau * C.WallMeet), default=s.W)
        s.memKnee = min(knee, s.W); s.memPlateau = s.plateau
        if knee < s.W: return s._set(knee, now, -1)
        return 'None'

    def _reenter_wall(s, now):
        # Saiu da parede so por oscilacao (FPS um pouco acima do patamar algumas vezes) e o jogo
        # continua sem chegar na meta: volta para a parede lembrada em vez de subir ate o teto.
        s.plateau = s.memPlateau
        s.wall = True; s.wallSince = now; s.probeGap = C.ProbeEvery
        s.nextProbe = now + s.probeGap
        s.wallLow = []
        return 'None'   # sem forcar descida: a parede desce sozinha se houver sobra

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
            # meta mantida: devolve. Queda brusca (tela de carregamento na volta): tambem devolve —
            # mais watts nao resolvem carregamento. So queda moderada mantem o reforco.
            if fps >= (s.plateau * C.WallMeet if s.wall else s.target * C.Meet) or fps < s.target * C.DropRatio:
                return s._set(back, now, -1)

        settled_reading = now - s.lastChange >= C.Settle
        if settled_reading:
            c = s.curve(now)
            m = c.get(s.W)
            # (queda brusca — corte de cena, camera, replay — nao conta: quem cuida dela e a espera
            # de 15 s; apagar a curva por ela fazia o controle esquecer a parede e subir de novo)
            # Na parede quem decide sao os dois caminhos proprios dela (FPS acima do patamar = cena
            # mais leve; abaixo do joelho = teste de +3 W): o detector de cena so vale fora dela.
            if (not s.wall and m is not None and abs(fps - m) > C.SceneDev * m
                    and fps >= s.target * C.DropRatio): s.dev += 1
            else: s.dev = 0
            if s.dev >= 3:                       # cena mudou: o que se mediu antes nao vale
                s.samples = []; s.dev = 0
                if s.wall: s.wall = False
                s.memKnee = None
                s.probe = None                   # teste de +3 W em andamento nao vale mais (fica nos watts atuais)
            s.samples.append((now, s.W, fps))
            s.samples = [x for x in s.samples if now - x[0] <= C.Window]

        prev = s.prevFps; s.prevFps = fps

        # teste de +3 W na parede
        if s.probe is not None:
            if settled_reading: s.probe['reads'].append(fps)
            if now - s.probe['t'] < C.ProbeLen: return 'None'
            base, frm, reads = s.probe['base'], s.probe['from'], s.probe['reads']
            s.probe_heavier = s.probe.get('heavier', False); s.probe_t = s.probe['t']; s.probe = None
            got = sum(reads) / len(reads) if reads else fps
            if got < base * C.ProbeKeep:
                s.probeGap = min(C.ProbeMaxEvery, s.probeGap * 2)
            s.nextProbe = now + s.probeGap
            if got >= base * C.ProbeKeep:
                s.wall = False                   # mais potencia voltou a comprar FPS
                if s.probe_heavier:
                    # a cena ficou mais pesada: a curva de antes da queda nao vale mais
                    s.samples = [x for x in s.samples if x[0] >= s.probe_t]   # so o que o teste mediu
                    s.memKnee = None
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
            s.wallLow = []; s.judgeUntil = NEG    # a leitura sem queda absolveu a descida
            # ponto de jogo: so com os watts parados ha 12 s (descendo num menu nao conta)
            if not s.wall and fps < s.target * C.Meet * C.Surplus and now - s.lastChange >= C.DownAfter:
                s.goodW = s.W; s.goodAt = now
            if s.okSince is None: s.okSince = now
            if now - s.stableSince >= C.SettledAfter: s.settled = True
            if not s.wall and fps >= s.target * C.Meet: s.memKnee = None   # a meta voltou a ser alcancavel
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
            # (nunca logo depois de uma descida: primeiro ela e julgada; um teste comecado ali
            # mede a base ja sem os watts e, ao terminar, volta para baixo sem conferir nada)
            if (s.wall and now >= s.nextProbe and s.W + C.ProbeStep <= s.max
                    and not (s.lastDir < 0 and now - s.lastChange <= C.ReturnWindow and fps < goal * 1.03)):
                base = s.curve(now).get(s.W, fps)   # media medida neste W (uma leitura so engana)
                s.probe = {'t': now, 'base': base, 'from': s.W, 'reads': []}
                return s._set(s.W + C.ProbeStep, now, +1)
            return 'None'

        # ---- abaixo da meta
        s.okSince = None; s.settled = False
        # caiu logo depois de NOS descermos: volta na hora e nao tenta de novo por um tempo
        if s.lastDir < 0 and s.downFrom is not None and (now - s.lastChange <= C.ReturnWindow or now <= s.judgeUntil):
            # A nossa descida e culpada se, pela proporcao dos watts, ela podia levar abaixo da meta
            # (vsync 60->30 entra aqui), ou se a queda foi moderada. Uma queda pela metade depois de
            # tirar 1 W de um jogo com folga e o jogo (replay, corte de cena), nao a descida.
            crash = fps < s.target * C.DropRatio
            before = s.curve(now).get(s.downFrom, prev)   # media medida antes da descida (uma leitura engana)
            plausible = before is not None and before * s.W / s.downFrom < goal * 1.03
            # Na parede a curva e plana (e isso que parede quer dizer): 1–2 W a menos nao derrubam o
            # FPS pela metade. Queda brusca ali e o jogo (camera, replay), nunca a descida.
            # na parede a queda e medida contra o patamar (meta muito acima dele faria toda leitura
            # parecer queda e a descida nunca seria julgada)
            if s.wall and fps < s.plateau * C.DropRatio:
                # nao culpa a descida pela queda, mas tambem nao a absolve: a primeira leitura sem
                # queda (ate 30 s) decide se os watts a menos fazem falta
                if now > s.judgeUntil: s.judgeUntil = now + 30   # queda longa nao adia para sempre
                return 'None'
            s.judgeUntil = NEG
            if not crash or plausible:
                back = s.downFrom
                if s.goodW is not None and now - s.goodAt <= C.Window and not s.wall:
                    back = max(back, s.goodW)    # descemos num menu: volta direto ao ponto de jogo
                s.floorCheck = (back, now)       # o piso so vale se voltar resolver
                return s._set(back, now, +1)
        # queda brusca sem termos mexido: corte de cena/carregamento — espera. Mas, se estamos ABAIXO
        # do ponto de jogo conhecido (descemos num menu, por exemplo), volta direto para ele.
        # ("estava bem" = acima de 90% da meta: o jogo pode estar parado na faixa morta)
        if prev is not None and prev >= goal * (C.Below / C.Meet) and fps < s.target * C.DropRatio:
            if s.goodW is not None and now - s.goodAt <= C.Window and s.W <= s.goodW - 2 and not s.wall:
                return s._set(s.goodW, now, +1)
            s.dropUntil = now + C.DropHold
        if now < s.dropUntil and fps < s.target * C.DropRatio: return 'None'

        if s.wall:
            # Abaixo do joelho por ~10 s sem queda brusca: a cena ficou mais pesada e o patamar
            # medido nao vale mais. Sai da parede, esquece a curva e reavalia contra a meta.
            # Abaixo do joelho por ~10 s: talvez a cena tenha ficado mais pesada (o patamar medido
            # nao vale mais), talvez seja so ruido. O teste de +3 W decide, usando como base a
            # media dessas leituras.
            if settled_reading:
                s.wallLow.append(fps)
                # (no maximo com a metade da espera dos testes normais: ruido no joelho nao vira
                # teste a cada 10 s)
                if len(s.wallLow) > 5: s.wallLow = s.wallLow[-5:]
                low = sum(s.wallLow) / len(s.wallLow)
                if (len(s.wallLow) >= 5 and low < s.plateau * C.WallMeet * 0.91
                        and s.W + C.ProbeStep <= s.max and now >= s.nextProbe - s.probeGap / 2):
                    base = low; s.wallLow = []
                    s.probe = {'t': now, 'base': base, 'from': s.W, 'reads': [], 'heavier': True}
                    return s._set(s.W + C.ProbeStep, now, +1)
            return 'None'
        if fps >= s.target * C.Below: return 'None'   # faixa morta: ruido em volta da meta nao sobe
        if now - s.lastChange < C.UpCd + C.Settle: return 'None'

        c = s.curve(now)
        cur = c.get(s.W, fps)
        lower = [m for w, m in c.items() if w <= s.W - 2]
        if lower and max(lower) > cur * (1 + C.SceneDev):
            # menos watts davam MAIS FPS: impossivel no mesmo trecho — a cena ficou pesada.
            # Nao e parede: esquece a curva antiga e sobe.
            # Espera mais uma leitura neste W antes de subir: assim, depois da subida, ja da para
            # ver se ela comprou FPS (corte de cena travado nao sobe duas vezes).
            s.samples = [x for x in s.samples if x[0] == now]
            s.memKnee = None
            return 'None'
        elif lower and max(lower) * C.GainNeed > cur:
            return s._enter_wall(now, cur)       # a ultima subida nao comprou FPS
        if s.W >= s.max:
            return s._enter_wall(now, cur)
        if s.memKnee is not None and s.W >= s.memKnee and fps <= s.memPlateau * C.WallExit:
            return s._reenter_wall(now)

        deficit = s.target / max(fps, 1)
        step = max(2, min(6, math.ceil((deficit - 1) * s.W * 0.8)))
        return s._set(s.W + step, now, +1)
