# Plano técnico: migração do controle de TDP para PawnIO

## Objetivo

Eliminar a dependência do driver vulnerável WinRing0/WinRing0x64.sys (CVE-2020-14979) sem perder o controle de TDP e sem introduzir custos para o projeto. O launcher deve continuar funcionando em dispositivos que não sejam AMD ou que não tenham PawnIO disponível.

## Auditoria do estado atual (master)

- O projeto inclui todo o conteúdo de `src/HandheldLauncher.App/Tools/**` no output por meio do `HandheldLauncher.App.csproj`.
- Essa pasta contém `WinRing0x64.sys`, `WinRing0x64.dll`, `inpoutx64.dll`, `libryzenadj.dll`, `ryzenadj.exe` e scripts antigos de instalação/serviço RyzenAdj.
- Portanto, os artefatos WinRing0 estão versionados e são copiados para o output do aplicativo, mesmo que nem todos sejam usados pelo fluxo C# atual.
- `WindowsHardwareService.SetTdpMode` tenta PawnIO e, se falhar, executa RyzenAdj. Logo, retirar apenas o arquivo .sys não elimina a dependência: o fallback também precisa ser removido ou substituído após validação.
- O runtime PawnIO ainda não é instalado pelo launcher. O usuário precisa instalar o driver oficial separadamente; o Launcher informa a ausência e não instala drivers silenciosamente.
- `RyzenSmuService` precisava alinhar a allowlist e a validação do codename com o CPUID realmente registrado no Legion Go. Os IDs de comando `0x14`, `0x15` e `0x16` são os usados pela tabela FT6/FP7/FP8 para Phoenix/Hawk Point; não são endereços de registradores.
- `PawnIOWrapper` implementa os IOCTLs documentados pelo próprio PawnIO. A implementação foi conferida contra `PawnIO/include/pawnio_um.h` e `PawnIOLib.cpp`: device type 41394, códigos 0x821/0x841, nome de função de 32 bytes e buffers de entrada/saída em `ULONG64`.
- O projeto não contém atualmente um fluxo de instalação/detecção do runtime PawnIO nem um módulo RyzenSMU validado incluído no pacote.

## Evidência adicional: comandos SMU para AMD Zen 4 mobile

A implementação pública atual do G-Helper em `app/Pawn/RyzenSmu.cs` identifica Hawk Point como parte da família Mobile e utiliza, para essa família, os comandos MP1 abaixo, em milliwatts:

- STAPM: comando SMU `0x14`
- Fast PPT: comando SMU `0x15`
- Slow PPT: comando SMU `0x16`

O módulo oficial `RyzenSMU.p` expõe `ioctl_send_smu_command` com exatamente 7 entradas (ID do comando + seis argumentos) e 6 saídas. A rotina de alto nível deve preparar os endereços de mailbox MP1, aguardar o mailbox ficar livre, enviar os argumentos e verificar o status retornado. Não é correto passar `0x14`, `0x15` e `0x16` como se fossem endereços de registradores nem enviar apenas dois valores.

O log real do Legion Go 8AHP2 reportou CPUID family 19h/model 75h (EAX 0x00A70F52). No release fixado PawnIO.Modules 0.2.11, o arquivo RyzenSMU.p mapeia CPUID 0x1974/0x1975 para CPU_Phoenix (enum 23) e CPUID 0x197C para CPU_HawkPoint (enum 30). Portanto, o codename interno retornado pelo módulo para o processador do teste é Phoenix, mesmo que a nomenclatura comercial/segmentação de mercado possa usar Hawk Point-U. O mesmo release atribui Phoenix e Hawk Point ao mesmo índice de mailbox (k_addridx 2) e à mesma família de operações da PM Table. A allowlist foi ampliada somente para family 19h/model 75h e 7Ch, com validação do codename específico esperado para cada CPUID. Referência primária: https://github.com/namazso/PawnIO.Modules/blob/0.2.11/RyzenSMU.p.

Referência de implementação para estudo (licença e atribuição devem ser preservadas): https://github.com/seerge/g-helper/blob/main/app/Pawn/RyzenSmu.cs

## Alterações já feitas nesta branch

- `PawnIOWrapper`: corrigida a transferência de propriedade do handle após `LoadModule`, evitando fechamento duplo; corrigidos os buffers de entrada/saída e a validação do número de bytes retornados. Os IOCTLs foram conferidos com os headers oficiais do PawnIO.
- `RyzenSmuService`: para os limites de potência Phoenix/Hawk Point, envia os comandos MP1 `0x14`, `0x15`, `0x16` usando os IOCTLs oficiais `ioctl_write_smu_register` e `ioctl_read_smu_register` nos endereços MP1 da família Mobile (`CMD=0x3B10528`, `RSP=0x3B10578`, `ARG0=0x3B10998`). Não usar `ioctl_send_smu_command` nesse caminho: o mailbox genérico selecionado pelo módulo para esse codename não é o mailbox MP1 usado por esses comandos. A validação exige status SMU `0x01` e read-back da PM Table. A CPU continua validada por CPUID 19h/75h (Phoenix) ou 19h/7Ch (Hawk Point), com codename conferido pelo módulo.
- Sincronização: a implementação tenta adquirir o mutex nomeado `Global\\Access_PCI`, conforme o aviso do módulo oficial, e falha de forma segura se ele não estiver disponível.
- `WindowsHardwareService`: usa exclusivamente PawnIO para TDP; o fallback RyzenAdj foi removido após `ryzenadj.exe` encerrar com `0xC0000005` no Legion Go 8AHP2. Os perfis foram ajustados para 15/20/25/30 W, dentro da faixa cTDP publicada pela Lenovo. Corrigida a assinatura COM do Core Audio e o namespace WMI de brilho.

## Correção aplicada para CPUID 19h/75h

- O log do equipamento reporta CPUID model 75h. O PawnIO.Modules 0.2.11 reconhece esse identificador como `CPU_Phoenix` (codename enum 23), não como `CPU_HawkPoint`.
- O CPUID model 7Ch continua reconhecido como `CPU_HawkPoint` (codename enum 30).
- A allowlist agora aceita somente esses dois modelos dentro da family 19h e exige o codename exato retornado pelo módulo para cada um.
- Os IDs de comando continuam `0x14` (STAPM), `0x15` (Fast PPT) e `0x16` (Slow PPT), enviados pelo mailbox MP1 Mobile (`0x3B10528/0x3B10578/0x3B10998`). O mailbox genérico usado por `ioctl_send_smu_command` não deve ser confundido com MP1. Não usar `0x1A`, `0x1B` e `0x1C` neste caminho: esses IDs pertencem a outra tabela de plataformas.

## Estado atual e pendências de validação

- O módulo oficial não deve ser compilado localmente para uso com a edição oficial assinada do driver: o driver valida a assinatura do blob. A branch agora prepara `RyzenSMU.bin` diretamente do release oficial `PawnIO.Modules 0.2.11`, por meio de `scripts/Prepare-PawnIO.ps1`, e valida o SHA-256 do ZIP antes de extrair o arquivo. O digest fixado é `43608cb89bc84247fef1368a139013f7d043e17db6d6c8dfc9b46bf0905a81f4`. Os workflows de CI e release executam o script após o publish Windows; o módulo é colocado em `Tools/RyzenSMU.bin` no pacote Windows, sem ser incluído no pacote Linux. A origem é o release oficial: https://github.com/namazso/PawnIO.Modules/releases/tag/0.2.11.
- O runtime PawnIO ainda não é instalado pelo launcher. O usuário precisa instalar o driver oficial separadamente; uma futura integração deve detectar ausência, explicar o motivo, abrir a página oficial e solicitar consentimento/elevation antes de qualquer instalação. Nunca instalar driver de kernel silenciosamente. A documentação oficial recomenda obter o runtime em https://pawnio.eu/.
- A comunicação SMU exige validação em hardware real. Os comandos de potência são enviados pelo mailbox MP1, com polling explícito do status SMU, e o código consulta a PM Table após os três comandos. Só reporta sucesso quando STAPM/Fast/Slow retornam dentro de 1 W do alvo; isso ainda precisa ser confirmado no Legion Go 8AHP2.
- O fallback RyzenAdj/WinRing0 e os binários/scripts legados foram removidos desta branch. Se PawnIO estiver ausente ou a CPU não estiver na matriz validada, TDP fica indisponível com diagnóstico explícito; volume, brilho e demais funções continuam independentes.

## Regras para a migração

1. Não chamar `ioctl_send_smu_command` com nomes de limites ou endereços inventados. O módulo oficial define uma chamada com 7 valores de entrada (ID do comando + seis argumentos) e seis valores de saída.
2. Não presumir que os limites STAPM / fast / slow são endereços SMU. Identificar os comandos e estruturas corretos por geração de APU e firmware.
3. Detectar fabricante, família/modelo/stepping e capacidade do módulo antes de habilitar TDP.
4. Manter os perfis Eco/Normal/Desempenho/Boost apenas quando o provider confirmar suporte e os limites forem validados para o dispositivo.
5. Não usar RyzenAdj/WinRing0 como fallback silencioso na versão final. Se PawnIO não estiver disponível, informar que o ajuste de TDP não é suportado naquele dispositivo, mantendo as demais funções do launcher.
6. Não exigir instalação de PawnIO em dispositivos que não utilizem o provider AMD/PawnIO.
7. Não alterar a versão estável nem publicar uma release até passar compilação, testes automatizados e validação em hardware real.

## Critérios de aceite

- Nenhum arquivo `WinRing0*.sys` ou `WinRing0*.dll` é distribuído pelo pacote final.
- Nenhum script ou executável RyzenAdj é invocado pelo launcher final.
- O runtime PawnIO oficial é detectado com tratamento claro para ausência, versão incompatível e acesso negado; a instalação é sempre explícita e consentida pelo usuário.
- O módulo RyzenSMU é obtido do release oficial 0.2.11 com o hash SHA-256 do arquivo ZIP validado, assinatura original preservada e licença LGPL-2.1-or-later informada.
- APU não suportada não recebe comandos SMU.
- Aplicar cada perfil exige sucesso dos três IOCTLs e read-back da PM Table dentro de 1 W do alvo; falha parcial ou ausência de leitura não é reportada como sucesso.
- Compilação Windows Release e testes automatizados passam.
- TDP é validado no Legion Go 8AHP2 Ryzen Z2 (CPUID 19h/75h) e, depois, em outros modelos AMD suportados.
- Dispositivos sem suporte a PawnIO continuam abrindo e usando as demais funções do launcher.

## Referências técnicas

- PawnIO: https://pawnio.eu/
- PawnIO.Modules (módulo oficial RyzenSMU): https://github.com/namazso/PawnIO.Modules
- Documentação de integração: https://github.com/namazso/PawnIO.Modules/wiki/Using-PawnIO-Modules
- Microsoft Defender – WinRing0: https://support.microsoft.com/windows/microsoft-defender-antivirus-alert-vulnerabledriverwinntwinring0
- CVE-2020-14979: https://cve.mitre.org/cgi-bin/cvename.cgi?name=CVE-2020-14979
