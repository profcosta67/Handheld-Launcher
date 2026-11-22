import sys, importlib, random, bank
mod = importlib.import_module(sys.argv[1]); name = sys.argv[2]; seed = int(sys.argv[3]) if len(sys.argv) > 3 else 0
t0 = float(sys.argv[4]) if len(sys.argv) > 4 else 0; t1 = float(sys.argv[5]) if len(sys.argv) > 5 else 400
scen = next(f for n, f, _ in bank.SCENARIOS if n == name)
rnd = bank.Lcg(seed * 7919 + 13); target, g, start = scen(rnd)
try: c = mod.C(target, bank.MIN_W, bank.MAX_W, start, capped=(scen is bank.sc_limiter))
except TypeError: c = mod.C(target, bank.MIN_W, bank.MAX_W, start)
rs = bank.Lcg(seed); buf = []; t = 0.0
while t < t1:
    true = g(t, c.W); seen = true * (1 + rs.uniform(-0.06, 0.06)); buf.append(seen)
    m = sorted(buf[-3:])[1] if len(buf) >= 3 else seen
    w = c.W; r = c.on(m, t) if t >= 20 else 'None'
    if t >= t0 and (r != 'None'): print(f"t={t:6.0f} W {w:2}->{c.W:2} {r:4} fps={m:5.1f} true={true:5.1f} wall={c.wall} plat={c.plateau:.1f}" if hasattr(c,'wall') else f"t={t:6.0f} W {w}->{c.W} {r} fps={m:.1f}")
    t += 2
