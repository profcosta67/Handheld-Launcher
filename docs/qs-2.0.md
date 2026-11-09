# QS 2.0 — diagnóstico, inventário e arquitetura

> Documento de trabalho (09/11/2026). Substitui o "remendar" do Quick Settings
> por uma reconstrução com provas. Base: `debug.log` do aparelho (Legion Go 2,
> Far Cry 6, Xbox por BT) e o código atual (`Views/OverlayWindow*`).

## 1. Porque o QS atual falha (com prova)

| Sintoma | Prova no `debug.log` | Causa |
| --- | --- | --- |
| Controle não volta depois de fechar | `[OVERLAY-CLOSE] foco devolvido para 0x404C0 (jogo=True, foreground agora=0x0)` | O Windows **recusa** `SetForegroundWindow` vindo de um processo que não está em foreground |
| **Tela preta** ao abrir o QS com o jogo a correr | acontece "às vezes", com FC6 em fullscreen | O jogo perde o **fullscreen exclusivo** quando aparece uma janela por cima; o preto é a superfície **do jogo**, não do painel |
| Piscadas ao mexer | `enforcer` a cada 120 ms chamava `SetWindowPos` + `GuardPosition` | repaint constante (já passou a 250 ms) |
| Painel "pula" para o meio | `[OVERLAY] Janela movida por fora para 740,115 (esperado 1460,115)` | `GuardPosition` a corrigir movimento provocado por nós |

## 2. Inventário: o que é útil e o que "enche linguiça"

| Bloco atual | Serve para | Veredicto |
| --- | --- | --- |
| **TDP** — `U.Eco/Eco/Normal/Boost/Desemp/Pers` | controlo central do aparelho | **MANTER**, mas trocar 5 botões por **1 slider + presets curtos** (como o SteamOS) |
| **Ventoinha** — `Auto/Silenc/Normal/Desemp/Boost` | Legion/ROG: térmica | **MANTER** (específico de hardware; passa a `IFanService`) |
| **Resolução** — 6 botões | raro, e mudar resolução **em jogo** é uma armadilha | **ESCONDER** em "Avançado" |
| **Taxa (Hz)** — 4 botões | útil e barato | **MANTER** |
| **Modo de Cor** — `Padrão/Vivido/OLED/Noturno` | específico do Legion (API Windows) | **MOVER para Configurações** |
| **Lossless Scaling** — multiplicador | nicho Windows | **MOVER para Configurações** |
| **Print / Gravar** | captura de vídeo | **REMOVER** do QS (não é controlo de jogo) |
| **"Fechar Menu (X)"** no rodapé | redundante com **B** | **REMOVER** (fica o ✕ no cabeçalho) |
| **TxtScreenInfo** (`1920x1080 120Hz`) | contexto barato | **MANTER** |
| *(falta)* **Volume** | todos os QS têm | **ADICIONAR** |
| *(falta)* **Brilho** | todos os QS têm | **ADICIONAR** |
| *(falta)* **Wi‑Fi/BT** | já existe na Central de Controlo (Y) | **ATALHO** para ela, sem duplicar |
| *(falta)* **Perfil por jogo** | o diferencial | **ADICIONAR** (grava o que mudares no jogo atual) |

## 3. Referências (o que copiar e o que não)

| Plataforma | Como resolve | Lição para nós |
| --- | --- | --- |
| **SteamOS / Bazzite** (mesmo QS) | poucos grupos; **sliders** para volume, brilho, TDP e limite de FPS; perfil **por jogo**; overlay de desempenho em 4 níveis | poucos controlos, **uma grandeza por slider**, tudo persistido por jogo; nunca 5 botões para o TDP |
| **Bazzite** | igual ao SteamOS + plugins (Decky) | extensibilidade por plugins em vez de encher o painel |
| **WinHanced / Handheld Companion** | TDP + curva de ventoinha + remap/giroscópio + perfis por jogo + overlay de stats | o valor está em **perfis e remap**, não em duplicar atalhos do Windows |

> Nota de rigor: SteamOS/Bazzite são verificáveis (é o mesmo painel no Deck e no
> Bazzite). As funcionalidades do WinHanced variam por versão — **confirmar no
> app** antes de copiar algo.

## 4. Arquitetura QS 2.0 (já a pensar em Linux)

```
Views/Qs/               → só XAML + code-behind de UI (zero OperatingSystem.IsWindows)
ViewModels/QsViewModel  → estado, presets, perfis por jogo
Core/Services/Qs/*      → regras puras (clamp de TDP, presets, perfis) + testes
App/Platform/Qs/*       → implementação Windows (PawnIO, Lenovo WMI, DisplayColor)
App/Platform/Qs/Linux/* → fase 3 (hwmon/powerprofilesctl, sysfs, SDL)
```

Interfaces (o XAML só conhece estas):
`IPowerService` (TDP/limites — `TdpLimits` já está no Core), `IFanService` (Linux: `hwmon`),
`IDisplayService` (resolução/Hz/cor; Linux: `kscreen`/`wlr-randr`),
`IControllerIsolation` (HidHide hoje; Linux: `udev`/`uinput`),
`ISessionControl` (fechar/retomar o jogo; Linux: `SIGSTOP`), `IOverlayHost` (abrir/fechar + foco).

### Foco (o problema de fundo)

1. O painel **não** rouba foco ao jogo (`WS_EX_NOACTIVATE` mantém-se).
2. Ao fechar: `keybd_event(VK_MENU)` + `SetForegroundWindow` (já implementado) e, se
   falhar, pedir ao **GameInputHelper** (corre ao lado do jogo) `AllowSetForegroundWindow(pid)`.
3. Log obrigatório: `[QS] close alvo=0x…, foreground=0x… (ok|falhou)`.

### Tela preta

Hipótese principal: fullscreen **exclusivo** do jogo. Plano: registar `[QS] open` com o rect do
painel e o estado da janela do jogo; teste A/B com o jogo em **borderless** (se não houver preto,
causa confirmada); se confirmada, avisar no painel e evitar mexer na janela do jogo.

## 5. Fases

| Fase | Entrega | Prova |
| --- | --- | --- |
| **F1** | esqueleto + TDP (slider+presets) + **Volume/Brilho** + Hz + foco (ALT/helper) + log de rect; remover Print/Gravar/Fechar | `[QS] close … foreground=0x<hwnd do jogo>` em 5/5 tentativas |
| **F2** | **perfis por jogo** + mover Cor/Lossless para Configurações + resolução em "Avançado" | mudar TDP no FC6 → próximo lançamento aplica |
| **F3** | camada Linux (SDL gamepad, `hwmon`, `systemd --user`) sem tocar no XAML | build Linux + painel a abrir no Bazzite/Deck |
| **F4** | curva de ventoinha + overlay de desempenho (4 níveis) | leitura/escrita com prova no log |

## 6. Critérios de aceitação do QS 2.0

- [ ] Fechar o QS devolve o foco ao jogo **ou** ao launcher, sempre (log com `ok`).
- [ ] Zero reposicionamentos espontâneos em 60 s com o painel aberto.
- [ ] Sem piscadas (nenhum `SetWindowPos` periódico).
- [ ] Nenhum controlo do painel é Windows-only.
- [ ] Testado com jogo em fullscreen **e** em borderless (resultado documentado).

