"""Banco fixo de cenarios do TDP inteligente (0.27 v2).
Cada cenario: jogo(t, W) -> FPS 'verdadeiro'; o controle ve FPS com ruido e mediana de 3 (como o app).
Metricas:
  avgW     watts medios
  ideal    watts medios do 'controle perfeito' (menor W que entrega o que da para entregar naquele instante)
  excess   avgW - ideal
  starve   % do tempo com FPS < 90% do alcancavel, tendo watts sobrando (o usuario perdendo FPS a toa)
  cpm      trocas de TDP por minuto
"""
import statistics as S, importlib, os, sys

class Lcg:
    """Gerador simples e deterministico — o MESMO no teste em C#, para os dois darem os mesmos numeros."""
    M = (1 << 64) - 1
    def __init__(self, seed): self.x = (seed * 0x9E3779B97F4A7C15 + 0x632BE59BD9B4E019) & Lcg.M
    def random(self):
        self.x = (self.x * 6364136223846793005 + 1442695040888963407) & Lcg.M
        return (self.x >> 11) * (1.0 / 9007199254740992.0)
    def uniform(self, a, b): return a + (b - a) * self.random()

MIN_W, MAX_W = 8, 35

def lerp_curve(points):
    pts = sorted(points)
    def f(W):
        if W <= pts[0][0]: return pts[0][1] * W / pts[0][0]
        for (w0, f0), (w1, f1) in zip(pts, pts[1:]):
            if W <= w1: return f0 + (f1 - f0) * (W - w0) / (w1 - w0)
        (w0, f0), (w1, f1) = pts[-2], pts[-1]
        return f1 + (f1 - f0) / (w1 - w0) * (W - w1) * 0.3
    return f

# ---------------------------------------------------------------- cenarios
def sc_light(rnd):
    f = lerp_curve([(10, 70), (20, 95), (30, 105)])
    return 60, (lambda t, W: f(W)), 20

def sc_medium(rnd):
    f = lerp_curve([(10, 35), (20, 62), (30, 72)])
    return 60, (lambda t, W: f(W)), 20

def sc_heavy_ok(rnd):
    f = lerp_curve([(10, 20), (20, 33), (30, 41), (35, 43)])
    return 40, (lambda t, W: f(W)), 20

def sc_gpu_wall(rnd):          # Days Gone: placa de video segura em 34
    f = lerp_curve([(10, 18), (20, 29), (26, 34), (35, 34.5)])
    return 40, (lambda t, W: min(34, f(W))), 20

def sc_just_above(rnd):        # meta 5% acima do teto
    f = lerp_curve([(10, 20), (20, 32), (26, 38), (35, 38.5)])
    return 40, (lambda t, W: min(38, f(W))), 20

def sc_cutscenes(rnd):         # EA FC: jogo leve, cortes de cena travados em 30
    f = lerp_curve([(10, 45), (15, 62), (20, 80), (30, 100)])
    cuts = []; t = 30
    while t < 4000:
        d = rnd.uniform(10, 20); cuts.append((t, t + d)); t += d + rnd.uniform(40, 90)
    def g(t, W):
        return 30 if any(a <= t < b for a, b in cuts) else f(W)
    return 60, g, 20

def sc_replays(rnd):           # replay curto a cada ~30 s
    f = lerp_curve([(10, 45), (15, 62), (20, 80), (30, 100)])
    def g(t, W):
        return 30 if (t % 30) < 8 and t > 30 else f(W)
    return 60, g, 20

def sc_menus(rnd):             # menu sem limite (FPS alto) por 30 s a cada 2 min
    f = lerp_curve([(10, 35), (20, 62), (30, 72)])
    def g(t, W):
        return f(W) * 2.2 if (t % 120) >= 90 else f(W)
    return 60, g, 20

def sc_vsync(rnd):             # vsync duplo: 60 ou 30
    f = lerp_curve([(10, 35), (20, 62), (30, 72)])
    return 60, (lambda t, W: 60 if f(W) >= 60 else 30), 20

def sc_heavy_area(rnd):        # trecho de 90 s que pede +7 W
    f = lerp_curve([(10, 35), (20, 62), (30, 72)])
    def g(t, W):
        return f(W - 7) if (t % 300) >= 200 and (t % 300) < 290 else f(W)
    return 60, g, 20

def sc_pauses(rnd):            # pausa/carregamento: FPS < 10 por 60 s a cada 4 min
    f = lerp_curve([(10, 35), (20, 62), (30, 72)])
    def g(t, W):
        return 5 if (t % 240) >= 180 else f(W)
    return 60, g, 20

def sc_scenes(rnd):            # Days Gone com cenas: teto 33 / 41 / 27
    scenes = []; t = 0
    while t < 4000:
        d = rnd.uniform(60, 240); r = rnd.random()
        cap = 33 if r < 0.65 else (41 if r < 0.85 else 27)
        scenes.append((t, t + d, cap)); t += d
    def g(t, W):
        cap = next(c for a, b, c in scenes if a <= t < b)
        k = 30 * W / 20 if W < 20 else 30 + 0.6 * (W - 20)
        return min(cap, k)
    return 35, g, 20

def sc_limiter(rnd):           # limitador de FPS na meta: o controle nao ve sobra
    f = lerp_curve([(10, 35), (20, 62), (30, 72)])
    return 60, (lambda t, W: min(60, f(W))), 20

def sc_start_high(rnd):        # comeca no teto num jogo leve
    f = lerp_curve([(10, 45), (15, 62), (20, 80), (30, 100)])
    return 60, (lambda t, W: f(W)), 35

def sc_gpu_wall_start_high(rnd):
    f = lerp_curve([(10, 18), (20, 29), (26, 34), (35, 34.5)])
    return 40, (lambda t, W: min(34, f(W))), 35

def sc_wall_heavier(rnd):      # parede da placa e, aos 600 s, a cena fica um pouco mais pesada
    f = lerp_curve([(10, 18), (20, 29), (26, 34), (35, 34.5)])
    return 40, (lambda t, W: min(34, f(W - 4) if t >= 600 else f(W))), 20

def sc_low_wall(rnd):   # parede abaixo de 65% da meta (meta 60, placa trava em ~35)
    f = lerp_curve([(8, 14), (14, 28), (18, 34), (35, 35)])
    return 60, (lambda t, W: f(W)), 20

def sc_unreachable_swings(rnd):  # EA FC meta 80: jogo para em ~68 a partir de ~25 W e oscila (camera, replay)
    f = lerp_curve([(10, 30), (18, 55), (24, 64), (35, 68)])
    dips = []; t = 40
    while t < 4000:
        d = rnd.uniform(6, 12); dips.append((t, t + d)); t += d + rnd.uniform(20, 40)
    def g(t, W):
        return f(W) * 0.68 if any(a <= t < b for a, b in dips) else f(W)
    return 80, g, 27

SCENARIOS = [
    # nome, fn, limites: (excess max W, starve max %, trocas/min max)
    ("leve",            sc_light,        (2.0, 3, 2.0)),
    ("medio",           sc_medium,       (2.5, 4, 2.0)),
    ("pesado-alcanca",  sc_heavy_ok,     (2.5, 5, 2.0)),
    ("teto-placa",      sc_gpu_wall,     (3.0, 5, 2.0)),
    ("meta-pouco-acima",sc_just_above,   (3.0, 5, 2.0)),
    ("cortes-de-cena",  sc_cutscenes,    (4.0, 5, 3.0)),
    ("replay-30s",      sc_replays,      (5.0, 6, 4.0)),
    ("menus",           sc_menus,        (3.0, 4, 3.0)),
    ("vsync-60-30",     sc_vsync,        (3.0, 6, 3.0)),
    ("area-pesada",     sc_heavy_area,   (3.0, 8, 3.0)),
    ("pausas",          sc_pauses,       (3.0, 4, 2.0)),
    ("cenas-alternadas",sc_scenes,       (4.0, 6, 3.0)),
    ("limitador",       sc_limiter,      (2.5, 4, 2.0)),
    ("comeca-no-teto",  sc_start_high,   (2.0, 3, 3.0)),
    ("teto-comeca-alto",sc_gpu_wall_start_high,(3.5, 5, 3.0)),
    ("parede-mais-pesada",sc_wall_heavier,(3.0, 5, 2.5)),
    ("inalcancavel-oscila",sc_unreachable_swings,(2.0, 5, 3.0)),
    ("parede-baixa",    sc_low_wall,     (3.0, 5, 2.0)),
]

def ideal_watts(g, t, target):
    best = g(t, MAX_W)
    goal = min(target * 0.95, best * 0.97)
    for W in range(MIN_W, MAX_W + 1):
        if g(t, W) >= goal: return W
    return MAX_W

def run(C, scen, seed, dur=1500, noise=0.06):
    rnd = Lcg(seed * 7919 + 13)
    target, g, start = scen(rnd)
    try: c = C(target, MIN_W, MAX_W, start, capped=(scen is sc_limiter))
    except TypeError: c = C(target, MIN_W, MAX_W, start)
    rs = Lcg(seed)
    buf = []; ws = []; ideal = []; starve = 0; changes = 0; n = 0; fps_sum = 0
    t = 0.0
    while t < dur:
        true = g(t, c.W)
        seen = true * (1 + rs.uniform(-noise, noise))
        buf.append(seen); m = sorted(buf[-3:])[1] if len(buf) >= 3 else seen
        if t >= 20:  # aquecimento do jogo (o app tambem espera)
            r = c.on(m, t)
            if r != 'None': changes += 1
        if t >= 60 and g(t, MAX_W) >= 10:   # pausa/carregamento nao conta
            ws.append(c.W); ideal.append(ideal_watts(g, t, target)); n += 1
            reach = min(target, g(t, MAX_W))
            if true >= 10 and g(t, c.W) < 0.90 * reach and c.W < MAX_W: starve += 1
            fps_sum += true
        t += 2
    return dict(avgW=S.mean(ws), ideal=S.mean(ideal), excess=S.mean(ws) - S.mean(ideal),
                starve=100.0 * starve / n, cpm=changes / (dur / 60), fps=fps_sum / n)

def bank(C, seeds=12, verbose=True):
    fails = []; rows = []
    for name, scen, (mx_ex, mx_st, mx_cpm) in SCENARIOS:
        rs = [run(C, scen, s) for s in range(seeds)]
        ex = S.mean(r['excess'] for r in rs); st = S.mean(r['starve'] for r in rs)
        stmax = max(r['starve'] for r in rs); cpm = S.mean(r['cpm'] for r in rs)
        ok = ex <= mx_ex and st <= mx_st and cpm <= mx_cpm
        if not ok: fails.append(name)
        rows.append((name, S.mean(r['avgW'] for r in rs), S.mean(r['ideal'] for r in rs), ex, st, stmax, cpm, S.mean(r['fps'] for r in rs), ok))
    if verbose:
        print(f"{'cenario':18} {'avgW':>5} {'ideal':>5} {'sobra':>5} {'fome%':>5} {'fomeMx':>6} {'troc/m':>6} {'fps':>5}")
        for r in rows:
            print(f"{r[0]:18} {r[1]:5.1f} {r[2]:5.1f} {r[3]:5.1f} {r[4]:5.1f} {r[5]:6.1f} {r[6]:6.2f} {r[7]:5.1f} {'ok' if r[8] else 'FALHA'}")
        print(f"falhas: {len(fails)} {fails}")
    return fails, rows

if __name__ == '__main__':
    mod = importlib.import_module(sys.argv[1] if len(sys.argv) > 1 else 'ctl_v1')
    bank(mod.C)
