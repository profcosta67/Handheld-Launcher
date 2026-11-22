import bank, ctl_v2
C = ctl_v2.C
for name, scen, _ in bank.SCENARIOS:
    for seed in (0, 5):
        rnd = bank.Lcg(seed * 7919 + 13); target, g, start = scen(rnd)
        c = C(target, bank.MIN_W, bank.MAX_W, start, capped=(scen is bank.sc_limiter))
        rs = bank.Lcg(seed); buf = []; t = 0.0; ws = 0; ch = 0
        while t < 1500:
            true = g(t, c.W); seen = true * (1 + rs.uniform(-0.06, 0.06)); buf.append(seen)
            m = sorted(buf[-3:])[1] if len(buf) >= 3 else seen
            if t >= 20 and c.on(m, t) != 'None': ch += 1
            ws += c.W; t += 2
        print(f'    [InlineData("{name}", {seed}, {ws}, {ch})]')
