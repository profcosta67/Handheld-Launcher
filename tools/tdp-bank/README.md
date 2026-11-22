# Banco fixo de cenários do TDP inteligente

Ferramenta de ajuste do controle do TDP inteligente (`SmartTdpControllerV2`). É a cópia em Python
do teste `SmartTdpScenarioBankTests` (C#): mesmo gerador de números (`Lcg`), mesmas contas, mesmos
resultados — o teste `PortMatchesPython` confere isso no build.

- `bank.py` — os 15 cenários e as métricas. `python3 bank.py ctl_v2` mostra a tabela.
- `ctl_v2.py` — o controle atual (espelho exato do C#).
- `ctl_v1.py` — o controle das betas 66–73, para comparar.
- `robust.py` — 30 sementes e ruído de 6% e 10%: `python3 robust.py ctl_v1 ctl_v2`.
- `trace.py` — cada troca de watts de um cenário: `python3 trace.py ctl_v2 cortes-de-cena 0 0 400`.

Métricas: **sobra** = watts médios acima do "controle perfeito" (menor W que entrega o que dá para
entregar naquele instante); **fome** = % do tempo com FPS abaixo de 90% do alcançável tendo watts
sobrando; **trocas/min**. Cada cenário tem limites; o v2 precisa passar em todos.

Ao mudar o controle: ajuste aqui primeiro, depois porte para o C# e atualize as impressões
digitais de `PortMatchesPython`.
