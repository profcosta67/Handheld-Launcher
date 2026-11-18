# Controle do Legion Go: pedido HID "liga a saída XInput"

Arquivo: `src/HandheldLauncher.App/Input/LegionControllerHid.cs` (chamado por
`Diagnostics/ControllerDiagnostics.cs`).

## O problema

No Legion Go 2, às vezes o controle liga "mudo": aparece no Windows (XInput, slot 0), mas não
manda nenhum botão até o Legion Space abrir. Isso também acontece na tela do PIN. Testes de
17/11 mostraram que o launcher não é a causa: com o launcher desligado, o controle continua mudo.

## O que o launcher faz

O firmware do controle tem uma chave que liga e desliga a saída XInput física. Com ela desligada,
o controle continua conectado, mas não manda nada, que é exatamente o sintoma. A hipótese é que o
Legion Space liga essa chave ao abrir; o teste no aparelho confirma ou não. O launcher manda o
pedido "ligar", que é o estado normal do controle:

- ao abrir (logo no começo, em thread própria);
- 3 s depois de o aparelho acordar da suspensão (se o Windows avisar a volta; no Modern Standby
  confira no `debug.log` se aparece `[SUSPENSAO] PowerModeChanged recebido: Resume`).

Ele não faz nada quando o Legion Space está aberto (o Legion Space cuida do controle), quando há
um jogo aberto (para não mexer no controle no meio da partida) ou quando
`WakeControllerWithHidCommand` está `false` no `settings.json`. O plano B, que abre o Legion Space
ao iniciar (`OpenLegionSpaceAtStartup`), fica desligado por padrão.

Na tela do PIN logo depois de ligar, o launcher ainda não está rodando. Se o firmware guardar a
chave ligada entre um boot e outro, o pedido também ajuda ali; se não guardar, só ajuda depois do
login e na volta da suspensão.

## Fatos do protocolo

| Item | Valor |
| --- | --- |
| Fabricante (VID) | `0x17EF` (Lenovo) |
| Controles (PID) | firmware antigo `0x6182`–`0x6185`; firmware de 2025 `0x61EB`–`0x61EE` |
| Interface de configuração | coleção de fabricante, página `0xFFA0`, uso `0x0001` |
| Envio | relatório de saída (`WriteFile`), completado com zeros até o tamanho do relatório |
| Pedido "XInput físico" | `05 00 04 0F <alvo> <estado>` |
| Alvos | `00` conjunto, `03` controle esquerdo, `04` controle direito |
| Estado | `01` liga, `02` desliga |

O launcher só usa o pedido com estado `01` (liga). Ele não usa os pedidos de modo do controle,
giroscópio, RGB ou reset de fábrica.

## Origem e licença

Esses fatos vêm da leitura de dois projetos de código aberto:

- [Handheld Companion](https://github.com/Valkirie/HandheldCompanion), commit `ed6bc51`
  (07/11/2026) — `HandheldCompanion/Devices/Lenovo/LegionGoTablet.cs` (interface, PIDs e o pedido
  de XInput físico). Lá o pedido aparece como opção experimental e desligada, usada no sentido
  contrário (desligar, para esconder o controle físico). Licença CC BY-NC-SA 4.0.
- [Handheld Daemon (hhd)](https://github.com/hhd-dev/hhd), commit `dec62ef` (17/11/2026) —
  `src/hhd/device/legion_go/tablet/` (mesma interface e mesmo formato de relatório para outros
  pedidos; não usa o pedido de XInput físico). Licença LGPL-2.1 ou posterior.

Nenhum código desses projetos foi copiado. A implementação usa a API HID do Windows
(`SetupDi*`, `HidD_*`, `HidP_GetCaps`, `WriteFile` assíncrono) e foi escrita para o launcher, que é
GPL-3.0. Agradecimento aos dois projetos pelo trabalho de documentar o controle.

## Segurança

- Filtra por VID, PID e coleção antes de abrir para escrita.
- Cada escrita tem limite de 1 s, com cancelamento; se uma passar do limite, as outras não são
  mandadas.
- O log mostra qual interface recebeu o pedido (ex.: `mi_02&col01`). Os slots do XInput não
  mostram se o controle está mudo; para confirmar, aperte um botão.
- Qualquer falha vira uma linha `[CONTROLE-HID]` no `debug.log` e o launcher segue normal.
