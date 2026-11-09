# Changelog

Mais recente primeiro. Cada versão tem notas completas em [`release-notes/`](release-notes/).
Betas não entram aqui: o que eles trazem aparece na versão estável que os junta.

## [0.25.0] — 2026-11-09
Instalador com **ecrã de boas-vindas** e escolha de pasta (`--instWelcome` + `--instLocation Either`); **TDP volta a funcionar** porque o instalador passa a instalar o **runtime do PawnIO** (faltava — só o módulo era copiado); **isolamento do controle** cobre todos os pads (Lenovo, Xbox USB/BLE, ROG Ally, DualSense…) e deixa de falhar com maiúsculas/minúsculas; **teclado do launcher em Avalonia** em todas as janelas com campos de texto; **congelamento opcional** do jogo no Quick Settings, com aviso de risco e consentimento. [Notas](release-notes/0.25.0.md)

## [0.24.0] — 2026-11-09
O launcher abre **sem UAC** (asInvoker) e eleva só o que precisa; Central de Controlo na tecla **Y** com Wi‑Fi real (sinal, conectar com senha) e Bluetooth clássico+BLE (descobrir, emparelhar, ligar/desligar); nova aba **Central de Saves** com o Syncthing embutido; parsers de rede movidos para o Core com testes. [Notas](release-notes/0.24.0.md)

## [0.18.4 → 0.23.4] — histórico recuperado
Estas versões saíram sem entrar aqui (só nas notas de cada release). Resumo, da mais antiga para a mais recente:

- **0.18.4 / 0.18.5 / 0.18.6** — correções do Quick Settings e estabilidade. [0.18.6](release-notes/0.18.6.md)
- **0.19.0** *(beta)* — "Cérebro dos Aparelhos": deteção do modelo (Legion Go 2 / Go S / ROG Ally) e limites de TDP por aparelho. [Notas](release-notes/0.19.0.md)
- **0.20.x** — Diário de Sessões (`sessions.jsonl`) e estabilidade do controle. [0.20.1](release-notes/0.20.1.md)
- **0.21.x** — conquistas Steam, tempo de jogo unificado, IGDB/metadados em PT-BR, redesign de Settings/Add Game/Game Hub. [0.21.1](release-notes/0.21.1.md)
- **0.22.3** *(beta)* — Windows Game Mode / OmniConsole (substituir o Xbox FSE) e arranque no boot. [Notas](release-notes/0.22.3-beta.1.md)
- **0.23.x** — fim do OmniConsole, hotfixes; 0.23.4 traz HidHide (segurança em jogos online) e a fase 1 do Syncthing. [0.23.4](release-notes/0.23.4.md)

## [0.18.3] — em preparo (beta)
Busca por nome (sem acento) e filtro por letra (# A–Z) na aba Nuvem; mais fluidez (4 trabalhos pesados fora da thread da tela, capas da Nuvem em segundo plano); correções da revisão independente. [Notas](release-notes/0.18.3.md)

## [0.18.2] — 2026-11-05
Fim do "vaivém" de atualização entre beta e oficial (só instala versão mais nova que a instalada); aba Nuvem em grade vertical; fechar o Xbox Cloud Gaming pelo Quick Settings (L3+R3 → Y); Nuvem fora da busca automática do SteamGridDB; ícones em todas as abas. Inclui a 0.18.1. [Notas](release-notes/0.18.2.md)

## [0.18.1] — só beta
Aba Nuvem em grade vertical, fechar xCloud pelo Quick Settings, ícones nas abas. Saiu na estável junto com a 0.18.2. [Notas](release-notes/0.18.1.md)

## [0.18.0] — 2026-11-05
"Novidades 2.0": página do jogo refeita (tela cheia, toda pelo controle, conquistas, mídia, arquivos, funciona sem internet); Gerenciador de Armazenamento com mover jogo entre discos e limpar cache de shader; Saúde do Aparelho com alertas de temperatura, bateria e espaço; Guardião de suspensão; aba Nuvem com o catálogo do Xbox Cloud Gaming abrindo em tela cheia. [Notas](release-notes/0.18.0.md)

## [0.17.0] — 2026-11-05
Paletas novas (Meia-noite, Crepúsculo, Musgo, Névoa, Alto Contraste) e 9 fontes novas livres; sugestões de fonte por layout atualizadas; base das escalas de tipografia e borda (sem efeito visível ainda); ícones que não faziam sentido (🦆, 🩺 duplicado) corrigidos; botão "Reiniciar Launcher"; overlay que pulava pro centro com o Xbox Game Bar corrigido; barra de status (Wi-Fi/Bluetooth/TDP/bateria/relógio) passa a seguir o tema de cores. Inclui a 0.16.5. [Notas](release-notes/0.17.0.md)

## [0.16.5] — em preparo
Canal beta passa a receber também as versões oficiais (a mais recente vale); launcher que não abria após desligar/ligar com Inicialização Rápida; recuperação do controle do Legion tenta até 3 vezes; relatório de problema com o retrato do controle e eventos do Windows. [Notas](release-notes/0.16.5.md)

## [0.16.4] — 2026-11-05
7 estilos de som novos (CC0) e layout que aplica som e fonte combinando; volume dos sons corrigido (não tinha efeito) e cliques curtos que sumiam; tentativa de destravar o controle do Legion ao ligar abrindo o Legion Space (ainda em teste); opção de o Quick Settings pegar o foco; relatório de crash local; faxina final do plano da v0.16. [Notas](release-notes/0.16.4.md)

## [0.16.3] — 2026-11-05
Central de Ajuda (verificações, manual, reportar erro sem dados pessoais); L3+R3 em qualquer jogo pelo leitor HID; menu e Preferências reorganizados; controle só age com o launcher na frente; atualizações do Windows aparecem de novo; cor, TDP, Quick Settings e Lossless corrigidos. Inclui a 0.16.2, que saiu só como beta. [Notas](release-notes/0.16.3.md)

## [0.16.2] — só beta
Central de Ajuda, sistema de cor único e TDP na abertura com novas tentativas. Saiu na estável junto com a 0.16.3. [Notas](release-notes/0.16.2.md)

## [0.16.1] — 2026-11-04
Quick Settings abre com o jogo rodando de novo; fim do "segundo popup" ao abrir jogo; navegação presa e pisca do Quick Settings corrigidos; Configurações sempre salvam; Handheld em tela cheia como padrão. [Notas](release-notes/0.16.1.md)

## [0.16.0] — 2026-11
"Alicerce": TDP e ventoinha restaurados na abertura; L3+R3 consistente; aparelho volta a suspender; congelamento do jogo desligado por padrão; canal beta; versão num lugar só. [Notas](release-notes/0.16.0.md)

## [0.15.5]
Lossless Scaling: navegação pelo controle, tela cinza na primeira ativação e abertura mais rápida; scroll do Quick Settings. [Notas](release-notes/0.15.5.md)

## [0.15.4]
Detalhes da loja Steam sem chave; filtros novos no layout Moderno; seletor de capas largas corrigido. [Notas](release-notes/0.15.4.md)

## [0.15.3]
Abertura instantânea; fim dos downloads de capa em loop; busca que ignora acentos. [Notas](release-notes/0.15.3.md)

## [0.15.2]
"Patinho Turbo": abertura sem travar, troca de telas mais fluida e capas baixadas só quando faltam. [Notas](release-notes/0.15.2.md)

## [0.15.1]
Restaura o layout Moderno/BigScreen validado, a barra de conectividade e os atalhos. [Notas](release-notes/0.15.1.md)

## [0.15.0]
Novo tema BigScreen para navegar pelo controle; layouts Clássico e Clean melhorados. [Notas](release-notes/0.15.0.md)

## [0.14.9]
Travamento com PNG animado corrigido; FFmpeg incluído pra gravar tela; HUD mais compacto. [Notas](release-notes/0.14.9.md)
