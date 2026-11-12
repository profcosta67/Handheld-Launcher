# Proposta — Ajudante privilegiado (fim do "iniciar com admin")

> Status: **aprovada (12/11/2026)** — implementação depois da 0.26, primeiro no
> Windows (etapas 2 e 3), depois no Linux (etapa 4, marco 0.27). Até lá o
> launcher continua abrindo sempre como administrador. Nada disto está implementado.
> Nenhuma função existente é removida: o caminho atual (abrir elevado pela
> tarefa de logon) continua funcionando até o novo estar validado no aparelho.

## Onde estamos

O jeito de lidar com administrador mudou três vezes em poucos dias:

| Versão | Como abre | O que precisa de admin |
| --- | --- | --- |
| 0.24 | Sem UAC (`asInvoker`) | UAC por ação (`--elevated-task`) |
| 0.25.5 | Sem UAC + aviso "Reiniciar como administrador" | Tarefas elevadas `tdp-apply`/`fan-apply` |
| 0.25.7 → atual | **Sempre admin** (`requireAdministrator`) | Tudo roda elevado; tarefa `HandheldLauncher_Startup` no logon (`/RL HIGHEST`) |

Abrir sempre elevado resolveu o TDP/ventoinha falhando em silêncio, mas tem
custos conhecidos no Windows:

- **Tudo o que o launcher abre herda o admin**: jogos, Steam, Edge (Nuvem).
  Jogos e lojas não foram feitos para rodar elevados.
- **UIPI**: programas sem admin (Steam Input, teclado virtual, Lossless
  Scaling, ferramentas de captura) ficam limitados ao mandar entrada ou agir
  numa janela elevada.
- **Abrir fora da tarefa de logon** (atalho, reinício após atualização do
  Velopack, modo Xbox) pede UAC ou falha.
- O mesmo problema volta no Linux de outra forma: `ryzenadj` e afins exigem
  root, e a 0.27 do roadmap pede "nenhuma dependência silenciosa de permissão
  administrativa".

## Proposta

Separar em **dois processos**:

1. **Launcher** — volta a abrir **sem admin** (`asInvoker`). Interface,
   biblioteca, jogos, Nuvem, QS: tudo como usuário normal.
2. **Ajudante privilegiado** (o mesmo `HandheldLauncher.exe` com
   `--privileged-helper`) — pequeno, sem janela, só executa uma **lista
   fechada** de ações de hardware:
   - TDP (PawnIO/RyzenSMU), curva de ventoinha (Lenovo), HidHide
     (esconder/restaurar/reiniciar controle), drivers sob pedido.
   - Nada de "executar comando qualquer": cada ação tem nome e parâmetros
     validados (ex.: TDP só dentro do teto lido do aparelho).

Conversa entre os dois por **named pipe local** com ACL só para o usuário
atual. O launcher pede ("tdp 18"), o ajudante responde "ok"/"erro + motivo".

### Como o ajudante sobe sem UAC

- **Windows:** a tarefa agendada que já existe (logon, "executar com
  privilégios mais altos") passa a abrir o **ajudante** em vez do launcher. A
  instalação pede admin uma vez (como hoje); depois disso, zero UAC.
- **Linux/Bazzite:** um serviço `systemd` de sistema (ou regra polkit) com a
  mesma lista fechada de ações. Mesmo protocolo, mesmo código de validação.

### Se o ajudante não estiver lá

O launcher continua funcionando; as ações de hardware aparecem como
"indisponível — ajudante não está rodando" (com o botão de reparar), em vez
de falhar em silêncio. Mesma regra do roadmap: função não validada não finge
que funciona.

## Plano em etapas (cada uma com beta próprio e teste no Go 2)

1. **Diagnóstico** (já na 0.25.9): o log registra se o launcher está elevado
   e quem o abriu (`[STARTUP] Abertura … aberto por=…`). Serve para medir o
   problema antes de mexer.
2. **Ajudante + pipe, em paralelo**: o ajudante sobe junto, mas o launcher
   continua elevado. As ações passam a ir pelo pipe, com fallback para o
   caminho atual. Validar TDP/ventoinha/HidHide no Go 2 e num Ally.
3. **Launcher sem admin**: só depois da etapa 2 validada, o manifesto volta a
   `asInvoker`. A tarefa de logon abre o ajudante; o launcher abre normal.
4. **Linux**: o mesmo ajudante como serviço, dentro do marco 0.27.

## Decisões que precisam do Marcelo

- Aprovar a direção (dois processos, lista fechada de ações).
- Ordem: fazer isto **antes** do marco visual 0.26 terminar, ou logo depois?
- Ações extras para o ajudante além de TDP/ventoinha/HidHide/drivers?
