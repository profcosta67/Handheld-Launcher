import bank, importlib, sys, statistics as S
for modname in sys.argv[1:]:
    C = importlib.import_module(modname).C
    for noise in (0.06, 0.10):
        tot_ex = []; tot_st = []; tot_cpm = []; worst = []
        for name, scen, lim in bank.SCENARIOS:
            rs = [bank.run(C, scen, s, noise=noise) for s in range(30)]
            ex = S.mean(r['excess'] for r in rs); st = S.mean(r['starve'] for r in rs); cpm = S.mean(r['cpm'] for r in rs)
            mx = max(r['starve'] for r in rs)
            tot_ex.append(ex); tot_st.append(st); tot_cpm.append(cpm)
            bad = ex > lim[0] or st > lim[1] or cpm > lim[2]
            if bad: worst.append(f"{name}(sobra {ex:.1f}, fome {st:.1f}/{mx:.0f}, trocas {cpm:.2f})")
        print(f"{modname} ruido {noise:.2f}: sobra media {S.mean(tot_ex):.2f} W, fome media {S.mean(tot_st):.2f}%, trocas/min {S.mean(tot_cpm):.2f}; falhas {len(worst)}: {worst}")
