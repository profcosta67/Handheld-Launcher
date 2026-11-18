# Controle do Legion Go: pedido HID "liga a saída XInput"

Arquivo: `src/HandheldLauncher.App/Input/LegionControllerHid.cs` (chamado por
`Diagnostics/ControllerDiagnostics.cs`).

## O problema

No Legion Go 2, às vezes o controle liga "mudo": aparece no Windows (XInput, slot 0), mas não
manda nenhum botão até o Legion Space abrir. Isso também acontece na tela do PIN. Testes de
17/11 mostraram que o launcher não é a causa: com o launcher desligado, o controle continua mudo.

## O que o launcher faz

O firmware do controle tem uma chave que liga e desliga a saída XInput física. Com ela desligada,
o controle continua conectado, mas não manda nada. O launcher manda o pedido "ligar", que é o
estado normal do controle:

- ao abrir (logo no começo, em thread de fundo);
- 3 s depois de o aparelho acordar da suspensão.

Ele não faz nada quando o Legion Space está aberto (o Legion Space cuida do controle) ou quando
`WakeControllerWithHidCommand` está `false` no `settings.json`. O plano B, que abre o Legion Space
ao iniciar (`WakeControllerWithLegionSpaceAtStartup`), fica desligado por padrão.

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

- [Handheld Companion](https://github.com/Valkirie/HandheldCompanion) — `Devices/Lenovo/LegionGoTablet.cs`
  (interface, PIDs e o pedido de XInput físico). Licença CC BY-NC-SA 4.0.
- [Handheld Daemon (hhd)](https://github.com/hhd-dev/hhd) — `src/hhd/device/legion_go/tablet/`
  (mesma interface e mesmo formato de relatório para outros pedidos). Licença LGPL-2.1.

Nenhum código desses projetos foi copiado. A implementação usa a API HID do Windows
(`SetupDi*`, `HidD_*`, `HidP_GetCaps`, `WriteFile` assíncrono) e foi escrita para o launcher, que é
GPL-3.0. Agradecimento aos dois projetos pelo trabalho de documentar o controle.

## Segurança

- Filtra por VID, PID e coleção antes de abrir para escrita.
- Cada escrita tem limite de 1 s, com cancelamento.
- Qualquer falha vira uma linha `[CONTROLE-HID]` no `debug.log` e o launcher segue normal.
