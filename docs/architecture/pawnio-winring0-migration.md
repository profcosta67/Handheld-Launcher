# Plano técnico: migração do controle de TDP para PawnIO

## Objetivo

Eliminar a dependência do driver vulnerável WinRing0/WinRing0x64.sys (CVE-2020-14979) sem perder o controle de TDP e sem introduzir custos para o projeto. O launcher deve continuar funcionando em dispositivos que não sejam AMD ou que não tenham PawnIO disponível.

## Auditoria do estado atual (master)

- O projeto inclui todo o conteúdo de `src/HandheldLauncher.App/Tools/**` no output por meio do `HandheldLauncher.App.csproj`.
- Essa pasta contém `WinRing0x64.sys`, `WinRing0x64.dll`, `inpoutx64.dll`, `libryzenadj.dll`, `ryzenadj.exe` e scripts antigos de instalação/serviço RyzenAdj.
- Portanto, os artefatos WinRing0 estão versionados e são copiados para o output do aplicativo, mesmo que nem todos sejam usados pelo fluxo C# atual.
- `WindowsHardwareService.SetTdpMode` tenta PawnIO e, se falhar, executa RyzenAdj. Logo, retirar apenas o arquivo .sys não elimina a dependência: o fallback também precisa ser removido ou substituído após validação.
- O caminho atual de RyzenAdj aponta para a raiz do diretório do aplicativo, enquanto o conteúdo da pasta Tools é copiado para o subdiretório `Tools`. Esse caminho precisa ser corrigido/validado durante a migração.
- `RyzenSmuService` contém endereços ilustrativos (0x14, 0x15 e 0x16) e não implementa corretamente a assinatura do comando `ioctl_send_smu_command` do módulo oficial RyzenSMU.
- `PawnIOWrapper` implementa IOCTLs diretamente e não verifica o tamanho de retorno; a interface oficial PawnIOLib deve ser preferida ou a implementação direta deve ser validada contra os headers oficiais.
- O projeto não contém atualmente um fluxo de instalação/detecção do runtime PawnIO nem um módulo RyzenSMU validado incluído no pacote.

## Evidência adicional: comandos SMU para AMD Zen 4 mobile

A implementação pública atual do G-Helper em `app/Pawn/RyzenSmu.cs` identifica Hawk Point como parte da família Mobile e utiliza, para essa família, os comandos MP1 abaixo, em milliwatts:

- STAPM: comando SMU `0x14`
- Fast PPT: comando SMU `0x15`
- Slow PPT: comando SMU `0x16`

O módulo oficial `RyzenSMU.p` expõe `ioctl_send_smu_command` com exatamente 7 entradas (ID do comando + seis argumentos) e 6 saídas. A rotina de alto nível deve preparar os endereços de mailbox MP1, aguardar o mailbox ficar livre, enviar os argumentos e verificar o status retornado. Não é correto passar `0x14`, `0x15` e `0x16` como se fossem endereços de registradores nem enviar apenas dois valores.

A correspondência do Ryzen Z2 não Extreme com Hawk Point precisa ser confirmada por CPUID/codename no próprio aparelho antes de habilitar esses comandos. O nome comercial Ryzen Z2, sozinho, não é critério suficiente.

Referência de implementação para estudo (licença e atribuição devem ser preservadas): https://github.com/seerge/g-helper/blob/main/app/Pawn/RyzenSmu.cs

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
- O runtime PawnIO oficial é detectado com tratamento claro para ausência, versão incompatível e acesso negado.
- O módulo RyzenSMU é obtido de uma origem oficial, com versão/hash registrados e licença respeitada.
- APU não suportada não recebe comandos SMU.
- Aplicar cada perfil confirma sucesso real; falha parcial não é reportada como sucesso.
- Compilação Windows Release e testes automatizados passam.
- TDP é validado em Legion Go 2 Ryzen Z2 e, depois, em outros modelos AMD suportados.
- Dispositivos sem suporte a PawnIO continuam abrindo e usando as demais funções do launcher.

## Referências técnicas

- PawnIO: https://pawnio.eu/
- PawnIO.Modules (módulo oficial RyzenSMU): https://github.com/namazso/PawnIO.Modules
- Documentação de integração: https://github.com/namazso/PawnIO.Modules/wiki/Using-PawnIO-Modules
- Microsoft Defender – WinRing0: https://support.microsoft.com/windows/microsoft-defender-antivirus-alert-vulnerabledriverwinntwinring0
- CVE-2020-14979: https://cve.mitre.org/cgi-bin/cvename.cgi?name=CVE-2020-14979
