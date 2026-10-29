# Handheld Launcher

**Status: Beta v0.2 (em andamento)** — v0.1 fechou com o essencial confirmado
no Legion Go 2 de verdade (biblioteca, navegação por controle, seletor de
capas/banners, modo kiosk, instalador, Modo Xbox via OmniConsole). v0.2 ataca
o que apareceu testando v0.1 no handheld: janela saindo de foco, não reabrir
depois de fechada, barra de baixo poluída, API key manual em toda instalação.
Ver "Status atual" e "⚠️ Build ainda não totalmente verificado aqui" abaixo
pra saber exatamente o que já foi confirmado funcionando no Legion/ROG Ally de
verdade e o que ainda está esperando o primeiro teste real.

Launcher "bigscreen" próprio para Windows, feito pra rodar no **Legion Go 2** e no
**ROG Ally** — sem depender de nenhum launcher já existente. Boot direto em tela
cheia, biblioteca unificada (Steam + jogos adicionados manualmente), capas via
SteamGridDB, navegação por controle.

> O nome "HandheldLauncher" é só um placeholder de desenvolvimento — troca
> quando quiser (é um find-and-replace no `.sln`, nos `.csproj` e no
> namespace `HandheldLauncher.*`).

## Aviso legal, privacidade e segurança

> Texto informativo, escrito sem assessoria jurídica — não substitui
> orientação de um advogado.

- **Projeto independente.** Sem vínculo, patrocínio ou aprovação de Valve
  (Steam), Epic Games, Microsoft (Xbox/Windows), Lenovo (Legion), ASUS (ROG),
  SteamGridDB ou Guru3D (RTSS). Nomes e marcas citados pertencem aos seus
  donos e aparecem só pra identificar compatibilidade.
- **Fornecido "como está", sem garantias** de funcionamento, e sem
  responsabilidade por perda de dados, problemas na conta de terceiros ou no
  aparelho. Use por sua conta e risco. O instalador mexe em itens do Windows
  (tarefa agendada, chaves de registro do Xbox Game Bar, inicialização) — tudo
  documentado aqui e reversível pelo desinstalador.
- **Não coleta dados.** Sem telemetria, sem conta própria, sem servidor. Tudo
  fica no aparelho (`%AppData%\HandheldLauncher\`). O launcher só se conecta
  a: SteamGridDB (capas, com a SUA chave), API oficial da Steam (lista de jogos
  da conta, com a SUA chave) e GitHub (verificar/baixar atualizações).
- **Contas.** O launcher **nunca pede, lê ou guarda senha ou token de login**
  de nenhuma plataforma. Da Steam ele lê só o SteamID64 (número público do
  perfil) e o apelido, do arquivo que o próprio cliente Steam mantém no PC.
  Cada usuário usa a própria chave da Steam Web API e do SteamGridDB, sujeito
  aos termos de uso desses serviços.
- **Chaves protegidas.** Desde a v0.9.0 as chaves de API ficam no
  `settings.json` criptografadas com o DPAPI do Windows (só o mesmo usuário do
  Windows consegue ler; copiar o arquivo pra outro PC não revela nada).
  `debug.log` não contém chaves e é limitado a ~1 MB.
- **Conteúdo de terceiros.** Capas/ícones vêm do SteamGridDB sob demanda, por
  usuário, e ficam só em cache local — o projeto não redistribui arte.
- **Atualizações.** Baixadas por HTTPS das Releases deste repositório. O
  instalador não tem assinatura digital paga (por isso o aviso do SmartScreen).
  **Segurança do mantenedor:** como todo aparelho instalado aceita o que for
  publicado aqui, a conta GitHub `profcosta67` precisa de **verificação em duas
  etapas (2FA)** ligada.
- **Licença: GPL-3.0** (arquivo `LICENSE`). Qualquer um pode usar, estudar e
  modificar, mas quem distribuir uma versão modificada precisa manter os
  créditos e liberar o código sob a mesma licença — ninguém pode transformar
  o launcher num produto fechado. A própria GPL já traz a cláusula de "sem
  garantia / sem responsabilidade" (seções 15 e 16).

## Roteiro planejado (próximas levas)

Depois do primeiro teste real completo (ver "Meus parabéns" — biblioteca,
menu, Downloads e RTSS todos confirmados funcionando), o usuário pediu pra
planejar as próximas levas antes de sair codando tudo de uma vez. Ordem
combinada (ele não tinha preferência forte, então ficou a ordem que faz
mais sentido de risco crescente):

0. ~~Organização da interface (menu em subpastas, Power, Downloads com
   velocidade, capas cinza, ícone no seletor)~~ — **v0.5.0**, ver seção própria.
   **Próximos lotes combinados**: ~~v0.6.0 visual~~ (feito, ver "Aparência");
   depois HUD 2 — ~~mini gráfico de frametime~~ (feito na v0.9.5) e
   temperatura (pesquisado na v0.9.5, ver "Temperatura no HUD — pesquisado,
   ainda sem caminho bom", ainda pendente de decisão); mais pra frente:
   login Steam, pasta de instalação. "Modo Xbox/'Sair' no Legion" foi
   tentado (esconder a janela + minimizar as outras, item "Ir para a
   área de trabalho" no menu de Energia) e **removido na v0.9.9** — não
   achava o modo Desktop de verdade, só minimizava, e sair do Modo Xbox
   de fato não tem um jeito documentado de fazer só por fora (sem vir de
   uma API do próprio Windows/fabricante).
1. ~~Reorganizar as abas da biblioteca~~ — **feito nesta leva**, ver
   "Status atual" mais abaixo pros detalhes (Favoritos/Todos/Oficiais/Não
   Steam/Não instalados). **Nunca testado num build real.**
2. ~~Atalho de Downloads na barra de cima~~ — **feito nesta leva**, ver
   "Status atual" mais abaixo. **Nunca testado num build real.**
2b. ~~Atualização automática~~ — **feita e confirmada no aparelho** (v0.3.1 →
   v0.3.2), ver "Instalação e atualização automática" mais abaixo.
3. ~~Overlay de desempenho estilo MangoHud~~ — **feito na v0.4.0**, ver
   "HUD de desempenho" abaixo. Nota original do planejamento:
   **Overlay de performance mais completo** (presets Resumido/Médio/Completo
   em vez de só FPS) — decisão do usuário: **overlay próprio do launcher**,
   não RTSS + Afterburner. Nota técnica importante pra quando for
   implementar: o problema difícil que o RTSS resolve é o *hook* de
   apresentação de frame (DX9-12/Vulkan/OpenGL) pra medir FPS de dentro do
   processo do jogo — escrever esse hook do zero é um projeto gigante e
   arriscado por si só (injeção de DLL, compatibilidade por API gráfica). O
   caminho sensato é **continuar usando o RTSS só pra resolver esse problema
   difícil** (ele já expõe FPS/frametime via shared-memory, tem wrapper
   .NET conhecido — `RTSSSharedMemoryNET`) e construir o resto por cima:
   uma janela transparente sempre-no-topo QUE A GENTE desenha (CPU/GPU/RAM/
   temperatura via `LibreHardwareMonitorLib`, bateria via API do Windows),
   com os 3 presets controlados na nossa tela de Configurações. Isso dá o
   controle visual total que foi pedido, sem reinventar a parte mais
   arriscada (a medição do FPS em si).
4. ~~Temas + customização visual~~ — **feito na v0.6.0** (+ layout vertical).
   Nota original: **Temas + customização visual** (dark/claro/azul/vermelho, efeitos de
   transição entre telas, tamanho de ícone configurável, zoom ao passar o
   mouse/focar um ícone) — deixado por último de propósito porque mexe em
   praticamente todo `Theme.axaml`/XAML existente; faz mais sentido depois
   que a estrutura das abas (item 1) já estiver estável, pra não re-estilizar
   duas vezes a mesma coisa.

5. **Nova rodada de ideias combinada com o usuário (pós v0.9.9)**, ordem de
   risco/esforço crescente: ~~som de interação pelo controle~~ (**feito na
   v0.9.10**, correção de bug + 3 pacotes extras na **v0.9.11**, ver seções
   próprias) → ~~tela de informações do jogo~~, estilo Decky Loader (horas
   jogadas e conquistas via Steam Web API — **feito na v0.9.11**, ver seção
   própria; classificação/resumo de loja ficou pra uma fase 2, via API
   não-oficial da loja da Steam, já que a API "de conta" não tem esses
   dados) → tema inspirado no Nintendo Switch: só a disposição
   limpa/minimalista dos itens, **sem** logos/fontes/ícones oficiais (evita
   problema de marca registrada) — **parcial na v0.9.13** (paleta de cor
   cinza-grafite + vermelho e cantos mais retos, ver seção própria), **mas
   não é o que foi pedido**: usuário esclareceu depois que a ideia era
   mudar a DISPOSIÇÃO de verdade (menu embaixo, capas minimalistas,
   configurações em cima, fundo claro só com banner + nome — um esqueleto
   visual diferente, não cor em cima do mesmo esqueleto de sempre). O
   projeto de verdade (junto de temas parecidos inspirados em SteamOS e
   Xbox Series) foi **adiado de propósito pelo próprio usuário** — ver
   item 6 abaixo.

6. **Mais ideias do usuário (pós v0.9.10/v0.9.11), só anotadas por enquanto
   — pedido explícito de não codar/testar ainda**:

   **Ordem combinada com o usuário (pós v0.9.14)**: ~~(a) ajustes nas abas
   de Configurações~~ + ~~(b) desinstalar jogo do Xbox/Game Pass de
   verdade + integração com o Eden~~ — **feitos na v0.9.15** (ver bullets
   próprias abaixo e a seção de changelog) → **subir update, testar**
   (próximo passo) → (c) temas de LAYOUT de verdade (SteamOS/Switch/Xbox
   Series) + só então trocar o "Switch" atual (cor) pelo tema real →
   **subir update, testar** → (d) revisão ampla de qualidade de código (a
   mais abrangente, pedida no início desta sessão, que até agora só teve
   uma auditoria focada nas áreas tocadas recentemente).

   - ~~**Desinstalar jogo do Xbox app/Game Pass de verdade.**~~ — **feito
     na v0.9.15**: "Excluir jogo" (menu de contexto) pra um jogo escaneado
     de `XboxScanFolders` agora acha o pacote MSIX/UWP de verdade via
     PowerShell (`Get-AppxPackage`, comparando `InstallLocation` com a
     pasta do jogo) e abre a página OFICIAL desse app em Configurações do
     Windows (`ms-settings:appsfeatures-app?<PackageFamilyName>` —
     documentado pela própria Microsoft), onde o usuário clica em
     "Desinstalar". Mesmo princípio da Steam: o launcher nunca apaga nada
     sozinho. Se não achar o pacote (PowerShell indisponível, instalação
     não-MSIX, nome não bater), cai pra abrir a lista geral de Apps em vez
     de falhar silenciosamente — ver `GameLauncher.OpenWindowsAppUninstaller`.
     **Nunca testado contra uma instalação real** (sem Xbox app/Game Pass
     neste sandbox pra validar).
   - ~~**Integração com o emulador Eden**~~ (sucessor comunitário do Yuzu,
     roda jogos de Switch — "tipo o Pokémon") — **feito na v0.9.15** via
     `EdenLibraryScanner` (mesmo padrão dos scanners de Epic/GOG/Ubisoft/
     EA), lendo as pastas de jogo já configuradas DENTRO do próprio Eden
     (`%AppData%\eden\config\qt-config.ini`, seção `[UI]`, array
     `Paths\gamedirs`) — pesquisado contra documentação do Eden + relatos
     da comunidade Yuzu/LaunchBox antes de escrever (ver comentário da
     classe pras fontes). Precisa configurar `AppSettings.EdenExecutablePath`
     à mão por enquanto (hand-edit só, igual `XboxScanFolders`/
     `SteamGridDbApiKey` quando foram criados — sem UI de configuração
     ainda), já que o Eden não tem um "protocolo" de URI pra abrir um jogo
     específico (quem abre é o `eden.exe -f -g "caminho\do\jogo"`, sintaxe
     confirmada contra um PR do próprio Yuzu no GitHub + um relato de quem
     testou no LaunchBox). **Nunca testado contra uma instalação real do
     Eden** (sem Switch/Eden neste sandbox pra validar) — se o formato do
     `qt-config.ini` tiver mudado, o scanner simplesmente não acha nada
     (nunca derruba o app).
   - ~~**Modos de otimização de imagem/cor**~~ (Vívido, OLED, Noturno,
     Confortável) — **feito na v0.9.13** via `SetDeviceGammaRamp`, ver
     seção própria. Marcado como experimental na própria tela: é uma API
     que reage diferente dependendo do driver/GPU e não dá pra confirmar
     daqui que não vai bagunçar o HDR/perfil de cor das telas OLED do
     Legion Go 2/ROG Ally — vale testar com calma antes de adotar como
     uso diário.
   - ~~**Volume dos sons de interação um pouco alto**~~ — **feito na
     v0.9.13**: slider próprio em Configurações → Aparência + padrão
     reduzido de 100% pra 70%, ver seção própria.
   - **Temas de LAYOUT de verdade** (SteamOS-style, Switch-style, Xbox
     Series-style) — pedido original, esclarecido depois de um equívoco meu
     na v0.9.13 (saiu só uma paleta de cor nova pro "tema Switch", quando a
     ideia era mudar a disposição inteira: posição do menu, grade vs.
     fileira, tratamento de fundo). Visão do usuário: um tema "SteamOS"
     (referência enviada); um "Switch" com menu embaixo, capas
     minimalistas, configurações em cima, fundo claro só com banner + nome
     do jogo; um "Xbox Series" (referência enviada); e por aí vai — cada um
     um ESQUELETO visual diferente, não só cor/raio em cima do esqueleto de
     hoje (`ThemeManager` hoje só troca `Palette`: cor + `TileRadius`/
     `BarRadius`, nunca a posição/forma dos elementos). Entraria um nível
     acima do `ThemeManager` atual — provavelmente trocar o layout raiz da
     tela principal por tema, não só `Brush`/`CornerRadius`. **Adiado de
     propósito pelo próprio usuário**: "Prefiro finalizar o que temos por
     agora e aguardar quando terminar esses blocos de ideia no nosso tema,
     lançar o tema propriamente dito." Fica pra quando o resto desta fila
     estiver fechado, como um projeto dedicado.

     **Escopo combinado com o usuário (pós v0.9.15, só perguntas — ainda
     sem codar)**:
     - Mudança **profunda**: cada tema de layout pode ter telas
       completamente diferentes entre si (não só reposicionar os mesmos
       elementos) — e atinge **tudo**: grade de jogos, menu global, tela
       de Configurações e o HUD de desempenho, não só a tela principal.
     - Referência pro tema Switch (que será o primeiro a sair):
       **tela HOME do console** — fileira horizontal de ícones de jogos,
       navegação horizontal (bate com a visão já anotada acima: menu
       embaixo, capas minimalistas, configurações em cima, fundo claro só
       com banner + nome do jogo).
     - Troca de tema de layout **pode pedir reiniciar o launcher** (não
       precisa trocar ao vivo como hoje acontece com cor/paleta).
     - Nome do ComboBox de tema **"Switch" atual (só cor)** vai ser
       renomeado pra algo genérico quando este projeto entrar em pauta —
       "Switch" fica reservado pro tema de LAYOUT de verdade.
     - **Ordem de entrega**: em levas, um tema de layout por vez — começa
       pelo **Switch-style** (não SteamOS/Xbox Series, que ficam pra
       depois).
     - Ainda **sem pedido explícito pra começar a codar** — usuário
       combinou de parar exatamente neste ponto (perguntas de escopo)
       nesta sessão.
   - ~~**Ajustes nas abas de Configurações (pós v0.9.14)**~~ — **feito na
     v0.9.15**: ordem trocada pra Aparência/Diretórios/Modo de uso/Contas
     e Chaves (Contas foi pro final, era a primeira); ComboBox de
     Aparência ganharam `Width="440"` fixa + `HorizontalAlignment="Center"`
     (eram `Stretch`, indo até perto da barra de rolagem — pedido do
     usuário pra centralizar/diminuir, todas do mesmo tamanho); e o bloco
     de versão/"Verificar atualizações" saiu de dentro da área rolável das
     abas, agora mora fixo no rodapé (acima dos botões Teclado/Fechar/
     Salvar), nunca mais "dentro" de nenhuma categoria.

## Status atual

Construído até agora:

- **`HandheldLauncher.Core`** — biblioteca sem UI, sem dependências externas
  além da base class library do .NET:
  - `VdfParser` — parser próprio do formato VDF (KeyValues) que o Steam usa
    em `libraryfolders.vdf` e `appmanifest_*.acf`.
  - `SteamLibraryScanner` — encontra o Steam instalado e lê todos os jogos
    instalados direto do disco (sem precisar do Steam rodando).
  - `GameLibraryStore` — persiste jogos adicionados manualmente e overrides
    (capa customizada, coleção, tempo jogado) em
    `%AppData%\HandheldLauncher\library.json`.
  - `LibraryService` — junta scanner(s) + store num único catálogo que a UI
    consome.
  - `GameLauncher` — inicia jogos (via `steam://rungameid/` pros jogos Steam,
    processo direto pros manuais), rastreia quando fecham, e fecha à força
    via `CloseGame()` (processo próprio pros manuais; sweep best-effort por
    `InstallDirectory` pros Steam, já que não possuímos o processo real).
  - `SteamGridDbClient` / `ArtworkService` — busca a melhor capa ("grid"),
    **o banner largo ("hero") e também o "logo"** (overlay transparente do
    título/wordmark — o "CURRENT LOGO" que aparece só sobre a tile larga do
    último jogo aberto, ver bullet própria mais abaixo) no SteamGridDB (por
    AppId pros jogos Steam, por nome pros manuais), baixa e cacheia em
    `%AppData%\HandheldLauncher\artwork\`, persiste os três caminhos como
    override. **Agora com seletor manual pros três** ("Escolher
    capa"/"Escolher banner"/"Escolher logo" no menu de contexto — ver bullet
    própria mais abaixo): `GetLogoCandidatesForSteamAppAsync`/
    `GetLogoCandidatesForTitleAsync` (novo nesta leva) espelham exatamente os
    métodos que já existiam pro grid/hero, só muda o endpoint
    (`/logos/steam/{id}`/`/logos/game/{id}`).
  - `AppSettingsStore` — lê/cria `%AppData%\HandheldLauncher\settings.json`
    (SteamGridDB key, Steam Web API key, SteamID64, pastas extras do Xbox
    app). **Agora tem tela de configurações de verdade** — ver "Tela de
    Configurações" mais abaixo.
- **`HandheldLauncher.App`** — UI em Avalonia:
  - **Banner HERO no topo**: alimenta AS DUAS coisas ao mesmo tempo: o
    banner cru (sem blur) lá no topo, seguindo o jogo focado, E o fundo
    desfocado/esticado da janela inteira. **Mudou de novo nesta leva**: a
    barra translúcida escura que ficava por baixo (com logo/título/minutos
    jogados dentro) foi REMOVIDA a pedido do usuário ("retirar essa linha
    escura do hero") — agora é só o CURRENT LOGO puro, **no canto SUPERIOR
    esquerdo** (antes era inferior) e bem maior (64px de altura, contra os
    22/32px das tiles da fileira — pedido explícito: "jogar a logo para o
    canto esquerdo superior e aumentar ainda mais o tamanho"). Sem fundo
    nenhum atrás dele, sem fallback de texto quando não tem logo ainda (só
    não aparece nada ali — o nome do jogo já aparece normalmente na caption
    de cada tile, logo abaixo). **Escolha de design minha, não pedida
    explicitamente**: os minutos jogados que viviam dentro dessa barra
    foram removidos do hero (não foram realocados pra outro lugar) — se
    quiser esse dado visível de novo, me avisa onde prefere. **Nunca
    testado num build real com esse layout.**
  - Na fileira, o **último jogo aberto** (não o que está só focado/com o
    cursor em cima — o que foi realmente *lançado* por último) aparece
    largo/deitado — a **WIDE CAPSULE**. **Mudou nesta leva (urgente,
    reportado pelo usuário)**: usava a MESMA arte de capa/grid portrait
    das tiles normais (`GridImagePath`) só renderizada mais larga, o que
    esticava/distorcia a imagem vertical dentro da caixa horizontal.
    Agora usa um asset totalmente INDEPENDENTE — `GameEntry.
    WideGridImagePath`, uma CAPA (não o hero/banner) só que já em formato
    landscape de verdade, buscada no SteamGridDB com `dimensions=
    460x215,920x430` em vez do `600x900` portrait normal. Nunca cai de
    volta pra `GridImagePath` esticado quando ainda não tem — mostra o
    placeholder vazio igual qualquer outra tile sem arte, de propósito
    (pra nunca mais mostrar o visual distorcido que estava sendo
    corrigido). Novo item "Escolher capa larga" no menu de contexto (ver
    bullet própria abaixo) escolhe manualmente. Só troca de lugar/tamanho
    quando você abre outro jogo de verdade; navegar com ← → não mexe
    nisso. Ao abrir um jogo ele vira o primeiro da fileira (empurra o
    resto). **Nunca testado num build real.**
  - Navegação por **controle via XInput** (dpad/analógico esquerdo, A
    confirma, B fecha o jogo) — testado e funcionando com um controle Xbox.
    Teclado (← → Enter Esc) continua funcionando como fallback de dev.
  - **Menu de contexto (Start no controle / Espaço no teclado)** sobre o
    jogo focado — nesta leva o menu deixou de ser uma lista FIXA de itens
    e passou a ser reconstruído do zero a cada abertura
    (`MainViewModel.BuildContextMenu`, mesmo padrão já usado pelo menu
    geral/Power), porque os itens de exclusão abaixo dependem de qual
    jogo está em foco. Itens de base, sempre presentes: "Escolher capa",
    "Escolher capa larga" (ver WIDE CAPSULE acima), "Escolher banner",
    "Escolher logo", "Favoritar"/"Desfavoritar" e "Abrir jogo". ↑↓ navega
    entre as opções, A/Enter confirma, B/Esc/Start fecha sem fazer nada.
    O texto do item "Favoritar" troca pra "Desfavoritar" dependendo se o
    jogo em foco já está marcado — toggla `GameEntry.IsFavorite`
    (persistido como override, sobrevive a re-scan) e acende uma
    estrelinha (★) no canto da tile na fileira.
    **Novo nesta leva — exclusão, pedido explícito do usuário ("apertando
    start em cima do ícone")**: um item extra aparece dependendo da
    origem do jogo —
      - **Steam**: "Excluir jogo" abre o desinstalador da própria Steam
        (`steam://uninstall/{appid}`, `GameLauncher.OpenSteamUninstaller`)
        — a Steam mesma mostra a confirmação dela e cuida de apagar os
        arquivos; o launcher nunca toca nada no disco.
      - **Não-Steam adicionado à mão** (via "Adicionar jogo"): "Excluir
        atalho" chama `ILibraryService.RemoveManualGame` — só tira da
        nossa lista, não apaga nenhum arquivo do jogo em si.
      - **Não-Steam escaneado automaticamente** (pastas do
        `XboxScanFolders`, ver bullet própria mais abaixo) **não** ganha
        item de exclusão ainda — reaparece no próximo scan de qualquer
        forma; pra tirar um desses da lista, por enquanto é preciso
        editar `XboxScanFolders` no `settings.json`.
      Escolher qualquer um dos dois itens de exclusão **não executa na
      hora** — troca o menu por uma confirmação de 2 opções
      ("Cancelar"/"Sim, excluir...", cursor sempre começando em
      "Cancelar") antes de executar de verdade. **Ainda não testado num
      build real — nem os itens de exclusão, nem a confirmação.**
  - **Abas de filtro**, logo abaixo do banner HERO e acima da fileira:
    **"Favoritos", "Todos", "Oficiais", "Não Steam", "Não instalados"**
    (reorganizado numa leva posterior a pedido do usuário: "dividir as
    subabas, fav, ALL, oficiais, não Steam, não instalados" — era
    "Favoritos/Todos/Steam/Xbox/Não Steam"). "Oficiais" junta Steam E
    Xbox na mesma aba (`GameSource.Steam || GameSource.XboxApp` — decisão
    explícita do usuário, substitui as duas abas separadas de antes);
    "Não Steam" virou o complemento exato disso (nem Steam nem Xbox) em
    vez de "qualquer coisa que não seja Steam" como era antes (quando
    isso incluía Xbox também). Clicar numa aba não re-escaneia nada, só
    filtra a lista já carregada na hora (`MainViewModel.SetFilter`/
    `RebuildGamesFromLibrary`). Layout é um `Grid` de 5 colunas IGUAIS
    ocupando a largura inteira — pedido do usuário: "centralize... fica
    com todos espaçamentos iguais e ocupe do início ao fim da tela".
    **Acesso por controle**: LB/RB ciclam entre as abas nessa mesma ordem
    visual (`GamepadAction.PreviousFilter`/`NextFilter`, teclado Q/E como
    fallback de dev). "Xbox" (dentro de "Oficiais") filtra por
    `GameSource.XboxApp`, que tem DUAS formas de preencher: marcar o
    checkbox "É um jogo do Xbox app / Game Pass" no diálogo "Adicionar
    jogo" (manual), ou configurar pastas em `AppSettings.XboxScanFolders`
    (ver bullet própria mais abaixo) — a detecção automática de verdade
    continua não existindo (precisaria de APIs de UWP que este projeto
    não referencia), isso é um atalho de pasta, não um scanner real do
    Xbox app. **Importante**: "Atualizar Capas Automático" continua
    sempre olhando a biblioteca INTEIRA, nunca só a aba ativa — corrigido
    de propósito pra não virar um bug silencioso. **Nunca testado num
    build real.**
  - **Aba "Não instalados" (nova numa leva posterior)** — pedido do
    usuário: "queria uma aba de não instalados". Mostra, como tiles
    normais na mesma fileira, os jogos que a conta Steam do usuário TEM
    mas que ainda não estão instalados nesta máquina — a MESMA lista que
    já alimentava o painel de Downloads (`OwnedNotInstalledGames`, via
    Steam Web API), só que agora também exposta aqui. Cada tile ganha um
    badge "⇩ Instalar" no canto (oposto da estrelinha de favorito) e
    confirmar/clicar NELA instala (`steam://install/{appid}`) em vez de
    tentar abrir — nunca baixa nada diretamente, quem baixa é a própria
    Steam. Capa buscada sob demanda (SteamGridDB, só a capa — essas tiles
    nunca viram a WIDE CAPSULE "último jogo aberto") usando o MESMO Id
    (`steam_{appid}`) que o jogo vai ter quando for instalado de
    verdade, então a capa já aparece pronta depois, sem buscar de novo
    (`ILibraryService.BuildEntryForOwnedGame`). Sem credencial da Steam
    Web API configurada, sem jogo faltando instalar, carregando ou com
    erro de rede, a aba mostra uma mensagem explicando em vez de ficar
    em branco. Reconsulta a Steam Web API toda vez que a aba é
    selecionada (mesma cadência do painel de Downloads). **Nunca testado
    num build real.**
  - **"Editor de onde procurar" jogos Xbox/Game Pass — `XboxScanFolders`
    (novo nesta leva)**: pedido do usuário depois que a detecção
    automática não achou nada. Novo campo `AppSettings.XboxScanFolders`
    (lista de pastas, hand-edit do `settings.json`, SEM tela de
    configuração — mesmo padrão da API key do SteamGridDB) alimenta um
    `XboxFolderScanner` novo: pra cada pasta configurada, toda SUBPASTA
    IMEDIATA dentro dela vira um jogo (`GameSource.XboxApp`), e o
    executável escolhido é o MAIOR `.exe` encontrado lá dentro
    (recursivo) — heurística simples ("o jogo de verdade quase sempre é
    bem maior que qualquer launcher/suporte junto"), não um scanner real
    de UWP/Xbox (precisaria de APIs que este projeto não referencia).
    Exemplo de uso — pelo que o usuário relatou ("tem jogo tanto no
    c:/Xbox quanto no d:/Xbox... que seria no HD e no sdcard"), adicionar
    as duas como pastas de busca:
    ```json
    { "XboxScanFolders": ["C:\\Xbox", "D:\\Xbox"] }
    ```
    **Atenção**: o usuário mesmo não tinha certeza se `C:\Xbox`/`D:\Xbox`
    são exatamente a raiz certa ("não sei se é essa a raiz mesmo") — vale
    confirmar no Explorador de Arquivos antes (a pasta certa é aquela cuja
    SUBPASTA IMEDIATA de cada jogo contém o `.exe` dele; se `C:\Xbox` abrir
    direto numa pasta de UM jogo só, em vez de uma pasta com VÁRIAS
    subpastas de jogos dentro, o campo deveria apontar pra pasta MÃE dela,
    não pra ela mesma). Lista aceita quantas pastas quiser, uma por HD/
    cartão SD.
    **Importante**: isso é um CAMPO pra editar no JSON, não um editor
    visual dentro do app — se o pedido era por uma tela de configuração
    de verdade, isso ainda está pendente (ver "Ainda não feito"). Jogos
    encontrados assim não ganham item "Excluir atalho" no menu de
    contexto (ver bullet do menu de contexto acima) — reaparecem no
    próximo scan de qualquer forma, então remover um específico por
    enquanto é editar `XboxScanFolders` na mão. **Nunca testado num
    build real.**
  - **Painel de Downloads — "gerenciador de download" (novo nesta
    leva)**: item "Downloads" no menu geral (View/Back ou engrenagem)
    abre um painel com duas seções. 1) **Baixando agora**: downloads/
    atualizações da Steam em andamento NESTE MOMENTO, lidos direto do
    manifesto `appmanifest_*.acf` de cada jogo (compara `BytesDownloaded`
    vs. `BytesToDownload` — sinal estável, ao contrário do bitmask
    `StateFlags` que não é documentado) — atualiza sozinho a cada 2s
    enquanto o painel estiver aberto. 2) **Na sua conta, não instalado**:
    busca TODA a biblioteca Steam da conta via Steam Web API e mostra só
    quem ainda não está instalado aqui, com um botão "Instalar" que abre
    `steam://install/{appid}` (a própria Steam baixa — este launcher nunca
    baixa nada diretamente, mesmo padrão do "Excluir jogo"). Precisa de
    `SteamWebApiKey` + `SteamId64` configurados no `settings.json` (ver
    seção própria abaixo); sem isso, a segunda seção mostra uma mensagem
    explicando o que falta, mas a primeira (baixando agora) funciona
    independente disso.
    **Decisões de escopo tomadas sem perguntar de volta**:
    - **Só Steam.** Epic/Xbox app não têm nenhum arquivo local confiável
      de progresso de download (o truque do `.acf` é específico da Steam)
      — "baixando agora" de Epic/Xbox **não existe** nesta versão, mesmo o
      usuário tendo pedido os três. "Na conta, não instalado" também é só
      Steam — na pergunta de esclarecimento o usuário só marcou a opção
      Steam, e Epic não tem API pública pra isso sem OAuth completo.
    - **Botão "Instalar" é mouse/touch-only** — diferente do resto do app
      (que é 100% navegável por controle). O controle só FECHA o painel
      (Back/View/Menu); navegar a lista e clicar "Instalar" precisa de
      mouse/toque. Por isso o painel também tem um botão ✕ na tela, pra
      não depender só do controle.
    **Nunca testado num build real** — nem a leitura do `.acf` em downloads
    de verdade, nem a chamada pra Steam Web API, nem o painel em si.
  - **Atalho de Downloads na barra de cima (novo numa leva posterior)** —
    pedido do usuário: "colocar um atalho lá em cima igual a Steam".
    Ícone (⇩) perto da bateria, mesmo painel/toggle do item "Downloads" do
    menu geral — só um caminho mais rápido pra mouse/touch. Ganha um
    pontinho no canto quando `HasActiveDownloads` é true (algo baixando
    de verdade agora), pra dar uma pista visual sem precisar abrir o
    painel. **Confirmado funcionando no build real** pelo usuário.
    **Ajustado na leva seguinte** (pedido: "não consigo acessar o ícone
    pelo controle" + "algo que indica visual o percentual do download"):
    - **Botão Y do controle** abre/fecha o painel de Downloads de
      qualquer lugar da tela principal (`GamepadAction.Downloads`; tecla
      D como fallback de teclado) — botão dedicado em vez de um modelo de
      foco "subir até a barra de cima", que num handheld seria mais lento.
      Um "Y" pequeno aparece do lado do ícone pra ficar descobrível.
      Instalar pelo controle continua sendo pela aba "Não instalados" (a
      lista do painel em si segue mouse/touch-only).
    - **Percentual + mini barra de progresso** do lado do ícone quando
      tem algo baixando (o download que está DE FATO em andamento — a
      Steam baixa um por vez, o resto fica na fila; o tooltip diz o nome
      do jogo e quantos estão na fila). Pra isso o "baixando agora" agora
      é relido também com o painel FECHADO, a cada ~6s — e **pausa
      sozinho enquanto um jogo está rodando**, pra não mexer em disco no
      meio da jogatina.
    **Ajustes da leva seguinte nunca testados num build real.**
  - **Overlay de FPS/performance via RTSS (novo nesta leva)** — pedido
    como "aquele mangohud... sem usar aquele nativo do legion". Em vez de
    construir um overlay do zero (exigiria hookar `Present()` de DirectX/
    Vulkan/OpenGL via injeção de DLL — risco de ban por anti-cheat e
    impossível de testar neste sandbox), o launcher integra com o RTSS
    (RivaTuner Statistics Server, grátis, já maduro e aceito pela maioria
    dos anti-cheats — mesma base do MSI Afterburner): detecta se está
    instalado (caminho fixo padrão), e se estiver instalado mas não
    rodando, abre ele sozinho ao iniciar o launcher (minimizado na
    bandeja, sem interromper nada). O item "Overlay FPS (RTSS)" no menu
    geral mostra o status (rodando / iniciar / baixar) e, se não estiver
    instalado, abre a página de download no navegador. A CONFIGURAÇÃO do
    overlay em si (o que mostrar na tela, posição, etc.) continua sendo
    feita dentro do próprio RTSS — isso não é replicado aqui. **Nunca
    testado num build real** — nem a detecção, nem o auto-start, nem se o
    RTSS de fato aparece sobre um jogo rodando pelo launcher.
  - **Seletor de capa/capa larga/banner/logo** ("Escolher capa"/"Escolher
    capa larga"/"Escolher banner"/"Escolher logo" acima): abre uma grade
    com até 24 opções estáticas vindas do SteamGridDB (ordenadas por
    pontuação, sem NSFW/humor, sem animadas — ver seção própria mais
    abaixo) pra aquele jogo específico. ← → navegam dentro da fileira, ↑ ↓
    pulam uma fileira inteira (grade de verdade, 5 colunas), A/Enter
    aplica a opção destacada na hora (capa, capa larga, banner ou logo do
    jogo, conforme qual dos quatro você abriu), B/Esc cancela sem mudar
    nada. Enquanto carrega mostra "Buscando
    opções..."; se não achar nenhuma, mostra "Nenhuma opção estática
    encontrada". Substitui o antigo botão único "Buscar capa no
    SteamGridDB" (que só re-buscava a imagem de maior pontuação, sem
    escolha) — esse foi removido, o seletor faz tudo que ele fazia e mais.
    **Confirmado funcionando** pelo usuário pra capa/banner (abre, carrega
    candidatas reais, aplica na hora, D-pad/analógico navegando certinho
    por fileira, banner grande de cima atualizando na hora). O terceiro
    tipo, **logo, é novo nesta leva e reusa exatamente o mesmo código**
    (`MainViewModel`'s antigo campo `_coverPickerIsHero` virou um enum de 3
    valores `ArtworkPickerKind{Grid,Hero,Logo}`, `ArtworkService.
    ApplyCandidateAsync` ganhou o mesmo tratamento) — **ainda não testado**
    especificamente pro logo. **Pendente de teste**: a grade ainda não
    acompanha a seleção com scroll automático quando ela desce (ou sobe)
    além da área visível — corrigido nesta leva, ver "Build ainda não
    totalmente verificado".
  - **Botão "Adicionar jogo"** na barra de baixo abre um diálogo (nome,
    caminho do executável com seletor de arquivo, argumentos opcionais,
    pasta de trabalho opcional) e cadastra um jogo não-Steam na
    biblioteca. Testado, abre certinho.
  - **Menu geral (View/Back no controle / Tab no teclado / ⚙ no topo
    pra mouse/touch — ver bullet da engrenagem abaixo)**: mesmo padrão
    visual do menu de contexto, com "Fechar: <jogo>" (só com jogo
    rodando), "Atualizar Capas Automático" (renomeado de "Buscar
    capas" — só com API key configurada), "Adicionar jogo", "Atualizar
    Biblioteca" (renomeado de "Atualizar"), **"Minimizar" e "Power"
    (novos nesta leva)**. "Sair" **saiu** daqui — mudou pro menu Power
    (ver bullet própria abaixo). "Minimizar"/"Power" entram aqui
    especificamente porque o usuário reportou não conseguir alcançar os
    ícones de cima/baixo (engrenagem/minimizar/power) apertando o
    direcional no controle — como esse menu já é 100% navegável por
    gamepad (View/Back abre de qualquer lugar), repetir as duas ações
    aqui dá acesso total sem precisar inventar um novo modelo de foco
    subindo/descendo pela tela. **Nunca testado num build real com esse
    formato.**
  - **Ícone de engrenagem (⚙)** — antes um placeholder desabilitado; abre
    o **mesmo** menu geral do botão View/Back (`OnGearMenuClick` só chama
    `vm.ToggleGlobalMenu()`, não é um menu separado) — assim mouse/touch
    chega exatamente nas mesmas opções que o controle, sem duplicar nada.
    Movido pro canto inferior direito numa leva posterior (junto de
    Minimizar/Power) — ver "Status atual" mais abaixo. O item
    "Configurações" desse menu agora abre uma tela de configurações de
    verdade (ver "Tela de Configurações" mais abaixo).
  - **Modo kiosk**: `AppSettings.KioskMode` (novo campo em `settings.json`,
    desligado por padrão). Quando ligado, `App.axaml.cs` abre a janela já
    em `WindowState.FullScreen` + `SystemDecorations.None` em vez da
    janela normal redimensionável de dev. De propósito **não** troca o
    shell do Windows — Alt+Tab, Alt+F4 e a área de trabalho por baixo
    continuam funcionando normal; fechar o app sempre volta pro desktop
    comum. Quem liga essa flag é o instalador (ver seção própria mais
    abaixo), não é pra editar essa parte do `settings.json` na mão.
    **Confirmado funcionando** — abre fullscreen de verdade, sem borda,
    num PC Windows normal. Em modo kiosk a janela também vira
    `Topmost = true` — achado testando no Legion Go 2 de verdade: Legion
    Space/Xbox/Steam abrem no boot e a nossa janela ficava escondida
    atrás deles; `Topmost` mantém ela sempre por cima, sem bloquear
    Alt+Tab (dá pra trocar de janela na mão, só não deixa OUTRA janela
    cobrir a nossa sem querer). **Confirmado funcionando no modo desktop
    normal do Legion Go 2** (boot direto no Windows, sem o Modo Xbox); no
    boot pelo Modo Xbox (ver seção "Instalar no Legion Go 2 / ROG Ally")
    a Legion Space ainda voltava pra frente mesmo com `Topmost = true` —
    ver bullet do "enforcer" abaixo pra tentativa de corrigir isso.
  - **"Enforcer" de Topmost (tentativa, ainda sem confirmação)**: achado
    no Legion Go 2 — no boot pelo Modo Xbox, um `Topmost = true` aplicado
    uma vez só na abertura não bastava: a Legion Space continuava
    aparecendo na frente depois. Hipótese: o Windows só garante que o
    Topmost "mais recente" fica na frente entre vários Topmost ao mesmo
    tempo, e a Legion Space parece se reafirmar depois de nós. Agora
    `MainWindow` reaplica `Topmost` (desliga/liga + `Activate()`) a cada 2
    segundos enquanto o modo kiosk estiver ligado (`StartTopmostEnforcer`,
    iniciado a partir do evento `Opened`), pra "ganhar de volta" a frente
    mesmo se a Legion Space ganhar uma rodada. É só uma mitigação, não
    garantia — se o shell do Modo Xbox roda numa camada composta acima do
    Topmost normal do Win32, isso pode não ser suficiente, e a correção de
    verdade teria que vir do lado do OmniConsole. **Ainda não confirmado
    se resolve no handheld real.**
  - **Reabrir sozinho se for fechado**: achado no Legion Go 2 — fechar o
    app (Alt+F4 ou crash) deixava ele fechado de vez, sem reabrir. Agora
    o instalador registra uma **Tarefa Agendada do Windows**
    (`HandheldLauncherWatchdog`) que checa a cada 1 minuto e reabre o app
    se não estiver rodando — sobrevive a Alt+F4 e a crash. Pra isso não
    virar uma prisão, **qualquer um dos lugares com "Sair" (menu geral,
    menu do ícone da bandeja — ver bullet abaixo) desativa esse watchdog
    antes de fechar** (`MainWindow.axaml.cs`/`App.axaml.cs`), então um
    "Sair" deliberado continua fechando de vez; só o Alt+F4/crash é que
    volta sozinho. Quem não quiser esse comportamento usa a flag
    `-NoWatchdog` do instalador (ver seção própria mais abaixo) pra
    voltar ao atalho simples antigo, que não reabre.
  - **Minimizar pra bandeja (novo nesta leva)**: achado no Legion Go 2 —
    o watchdog acima faz sentido pra Alt+F4/crash, mas reabria o app todo
    mundo-abre-sozinho até quando você só queria usar o desktop normal
    por um momento ("fica abrindo toda hora"). Agora o botão "Minimizar"
    na barra de baixo chama `Hide()` em vez de `Close()` — o processo
    continua rodando (então o watchdog nunca tem motivo pra reabrir nada,
    já está "rodando" do ponto de vista dele) e a janela só fica
    escondida. Pra voltar: um **ícone de verdade na bandeja do Windows**
    (clica ou usa o menu dele — "Abrir"/"Sair") **e** o atalho de teclado
    **Ctrl+Alt+H** de qualquer lugar (via `RegisterHotKey`, funciona
    mesmo com a janela escondida/sem foco — diferente de um KeyDown
    normal). Ícone gerado em `Assets/app-icon.ico` (tema "HL" nas cores
    do app). **Nunca testado num build real.**
  - **Botão "Power" (⏻ — antes era o texto "Power", virou ícone nesta
    leva, igual a engrenagem já é só ⚙)**: abre um mini-menu com agora
    **4 opções** — "Desligar tela" (`SC_MONITORPOWER` via
    `WM_SYSCOMMAND`, um toque clássico do Win32 pra forçar a tela a
    apagar sem afetar sleep/hibernar — qualquer tecla/botão do controle
    acorda de volta, isso é comportamento normal do Windows), "Reiniciar
    o Legion Go" e "Desligar o Legion Go" (`shutdown.exe /r` e `/s`,
    ambos imediatos), e **"Sair" (movido pra aqui nesta leva — antes
    vivia no menu geral/engrenagem)**, que faz mais sentido ao lado de
    reiniciar/desligar o aparelho. Esse menu também é alcançável de
    dentro do menu geral agora (ver bullet acima), não só pelo botão/ícone
    da barra de baixo. **Atenção, risco conhecido**: nenhuma das opções
    de reiniciar/desligar tem uma confirmação antes de executar — um
    toque errado já dispara na hora, podendo perder progresso de um jogo
    aberto. Fica como próximo passo decidir se entra uma etapa de
    confirmação (ex: "tem certeza?") antes dessas duas. **Nunca testado
    num build real.**
  - **Dica de controles removida**: o texto fixo no canto inferior
    esquerdo da barra de baixo ("← → navega · A/Enter joga · ...") foi
    removido a pedido do usuário — poluía a tela. A dica dos outros menus
    (↑↓ navega · A/Enter confirma · ...) continua, já que só aparece
    enquanto aquele menu específico está aberto.
  - **Instância única (novo nesta leva)**: bug reportado pelo usuário —
    "toda vez que ele abre sempre tá abrindo uma aba nova do launcher".
    `Program.cs` agora checa um `Mutex` nomeado ANTES de sequer inicializar
    o Avalonia; se outra instância já existe, essa segunda só avisa a
    primeira (via `RegisterWindowMessage`/`PostMessage` broadcast, lido
    pelo mesmo hook de WndProc que já existia pro Ctrl+Alt+H) pra vir pra
    frente e **sai imediatamente, sem nunca abrir uma segunda janela**.
    Cobre qualquer caminho de abertura duplicada (clique duplo no atalho,
    corrida do watchdog com um lançamento manual, o Modo Xbox reabrindo o
    "app principal" de novo), não só o watchdog. **Nunca testado num
    build real.**
  - **Scroll da fileira de jogos (novo nesta leva)**: bug reportado pelo
    usuário, mesma classe do bug já corrigido no seletor de capas — mover
    ← → além da área visível da fileira não acompanhava a seleção com
    scroll automático. `ScrollGameRowSelectionIntoView` em
    `MainWindow.axaml.cs` calcula a posição horizontal da tile atual
    (considerando que a tile do "último jogo aberto" é mais larga, 460px
    contra 170px das outras) e ajusta o `ScrollViewer.Offset` quando ela
    sai da área visível. **Nunca testado num build real.**
  - **"Enforcer" de Topmost não rouba mais o foco (ajuste nesta leva)**:
    bug reportado pelo usuário — com o launcher em modo kiosk, abrir
    outro app com Alt+Tab ou abrir a ferramenta de Captura/Recorte
    (Snipping Tool) não funcionava: a janela deles não vinha pra frente.
    Causa provável: o "enforcer" (ver bullet mais acima) chamava
    `Activate()` a cada 2 segundos, que força o FOCO/janela em primeiro
    plano pra nós, não só a posição no topo da faixa "sempre visível" do
    Topmost — então a cada 2 segundos ele roubava de volta o foco de
    qualquer coisa que o usuário tivesse trocado por conta própria. O
    `Activate()` foi removido do enforcer; só o liga/desliga do `Topmost`
    continua, o que deveria bastar pra reconquistar a posição no topo sem
    sequestrar o foco de ninguém. **Risco**: se isso enfraquecer demais o
    enforcer e a Legion Space voltar a ganhar a frente no Modo Xbox, o
    próximo passo seria reaplicar o `Activate()` só numa janela curta
    depois do boot, não pra sempre. **Nunca testado num build real.**
  - **"CURRENT LOGO" + nome do jogo — regra final (3ª tentativa nesta
    sessão, corrigida de novo)**: a leva anterior tinha restringido o logo
    só à WIDE CAPSULE. O usuário corrigiu de vez, com uma regra explícita e
    literal: *"a LOGO não vai dentro das capas dos jogos (wide capsule e
    capsule), ele vai apenas dentro da hero (banner) lá em cima e ACIMA DAS
    CAPAS NO LUGAR DO NOME"*. Ou seja, o logo aparece em DOIS lugares, e
    NUNCA dentro de uma capa:
    1. Dentro do banner HERO no topo (canto inferior esquerdo), pro jogo
       atualmente focado.
    2. ACIMA de QUALQUER capa da fileira — CAPSULE ou WIDE CAPSULE,
       qualquer uma que já tenha logo baixado — substituindo o nome
       (nunca os dois juntos). Sem logo ainda pra aquele jogo, cai pro
       nome normalmente. Na WIDE CAPSULE o logo renderiza um pouco maior
       (`CaptionLogoHeight` = 32 contra 22 da CAPSULE normal), a pedido do
       usuário ("deixa ela um pouco maior o tamanho na wide capsule").
    - `GameTileViewModel`: `ShowCurrentLogo => HasLogo` (não depende mais
      de `IsLastPlayed`), `ShowNameCaption => !HasLogo` — mutuamente
      exclusivos, qualquer tile.
    - A caption de cada tile é um `Grid` de altura FIXA (36, antes 28) —
      com o logo podendo vir em dois tamanhos diferentes (22/32) a altura
      fixa garante que a capa de baixo nunca desalinha entre as tiles da
      fileira, não importa se mostra nome, logo pequeno ou logo grande.
    - **"Atualizar Capas Automático" corrigido**: antes só buscava logo
      pra tile do último jogo aberto (porque só ali aparecia); agora busca
      pra TODA a biblioteca, igual capa/banner já faziam, já que o logo
      pode aparecer em qualquer tile agora.
    **Nunca testado num build real.**
  - **Tamanho das capas aumentado (novo nesta leva)**: pedido do usuário
    ("pode aumentar um pouco mais o tamanho das capas em geral"). CAPSULE
    170x230 → 190x255, WIDE CAPSULE (largura) 460 → 510 (mesma proporção
    de antes). Só a fileira principal — a grade do seletor de capa/
    banner/logo (`Border.coverCandidate`) ficou do tamanho que já era, por
    ser uma UI diferente (escolha rápida, não a tela principal). **Nunca
    testado num build real.**
  - **Bateria real na barra de cima (bug corrigido nesta leva)**: o
    usuário reportou "a bateria não está acompanhando o real, ela está
    cravada a 100%" — e o motivo era simples: aquele "⚡100%" sempre foi um
    texto FIXO digitado na hora, nunca ligado a nada de verdade (o
    comentário antigo já dizia isso: "placeholder for now"). Novo arquivo
    `Platform/BatteryStatus.cs` chama `GetSystemPowerStatus` (API nativa do
    Windows, `kernel32.dll`) e `MainWindow.StartBatteryMonitor` atualiza
    `MainViewModel.BatteryStatusText` a cada 30s (e uma vez na abertura).
    Mostra "⚡86%" na bateria, "🔌 86%" carregando, e nada (texto vazio) num
    PC sem bateria nenhuma — melhor não mostrar nada do que trocar um
    número fixo por outro. Wifi/relógio continuam só placeholder (●●●),
    não foi pedido mexer neles. **Nunca testado num build real** — num PC
    de mesa (sem bateria) o esperado é não aparecer nada ali; num
    handheld de verdade (Legion Go 2/ROG Ally) é onde dá pra confirmar o
    número batendo com o real.
  - **Fonte do projeto = Comic Sans MS (novo nesta leva)**: pedido do
    usuário. Um único `Style Selector="Window"` em `Theme.axaml` troca a
    `FontFamily` — como essa propriedade é herdada pela árvore visual em
    Avalonia (confirmado lendo o código-fonte real do framework), isso
    cobre todo texto do app sem precisar repetir em cada estilo. "Comic
    Sans MS" é uma fonte do próprio Windows (vem instalada por padrão),
    então não precisa empacotar nada — só funciona assim em Windows
    mesmo, mas esse launcher já só roda em Windows. **Nunca testado num
    build real** — não dá pra confirmar visualmente sem abrir a janela de
    verdade.
- **`installer/`** — `Install-Launcher.ps1`/`Uninstall-Launcher.ps1` (e os
  `Instalar.bat`/`Desinstalar.bat` de duplo-clique por cima deles), ver
  seção "Instalar no Legion Go 2 / ROG Ally" mais abaixo. Os `.ps1`
  **confirmados funcionando** num PC Windows normal, de ponta a ponta, e o
  core (publish/cópia/kiosk/Modo Xbox) **confirmado no Legion Go 2 de
  verdade** também; o watchdog e a API key padrão são mudanças desta leva,
  **ainda não testados** num build real.
  **Novo nesta leva**: tenta instalar o RTSS automaticamente via `winget`
  (id `Guru3D.RTSS`) no passo 8, SE ainda não estiver instalado — pedido
  do usuário pra automatizar isso também. "Melhor esforço" igual o resto
  (SDK do .NET, registro do Game Bar, etc.): se o pacote não existir
  nessa fonte do winget ou faltar internet, só vira aviso no log, nunca
  derruba a instalação. **O id `Guru3D.RTSS` foi confirmado só por busca
  na web (ver nota mais abaixo), nunca rodando de verdade** — se o
  `winget install` falhar com esse id, me avisa com a mensagem exata do
  log pra eu corrigir.
- **Janela parando de "roubar" o topo o tempo todo no modo desktop
  (REGRESSÃO corrigida nesta leva)**: o "enforcer" que reafirma
  `Topmost` a cada 2s (pra brigar com a Legion Space pelo topo logo
  depois do boot, ver bullet mais acima) não tinha prazo — ficava
  rodando pra sempre enquanto o launcher estivesse aberto. Isso nunca
  incomoda em "Modo Xbox"/kiosk (nada mais compete pelo topo), mas no
  desktop normal do Windows a janela do launcher ficava pulando pra
  cima de qualquer outra janela a cada 2 segundos, pra sempre — o que o
  usuário reportou como "fica abrindo toda vez o app". Agora esse
  "enforcer" só roda por 2 minutos a partir da abertura da janela
  (`MainWindow.TopmostEnforcerGracePeriod`), tempo mais que suficiente
  pra vencer a disputa de boot, e depois disso para de se impor —
  mesmo em "Modo Xbox", onde continua sem fazer diferença (nada mais
  compete lá de qualquer forma). **Nunca testado num build real.**
- **Menu de contexto reordenado + submenu "Personalizar" (pedido do
  usuário: "muito desorganizado, vamos por partes")**: ordem agora é
  Abrir jogo → Favoritar/Desfavoritar → Excluir atalho/Excluir jogo (só
  um dos dois, dependendo da origem do jogo) → Personalizar. As 4
  edições de arte (Escolher capa/capa larga/banner/logo), que antes
  eram itens soltos no topo do menu, viraram um submenu só delas dentro
  de "Personalizar" (mais um "Voltar" pra retornar ao menu principal) —
  reaproveita o mesmo mecanismo de troca de conteúdo do overlay já usado
  pela confirmação de exclusão. **Nunca testado num build real.**
- **Botões Power renomeados**: "Reiniciar o Legion Go"/"Desligar o
  Legion Go" (específicos do Legion) viraram "Reiniciar o
  aparelho"/"Desligar o aparelho" — pedido do usuário, já que o projeto
  também roda no ROG Ally agora. "Desligar tela" e "Sair" continuam com
  o mesmo nome.
- **Engrenagem mudou de lugar + Minimizar virou ícone**: o ícone ⚙ saiu
  do canto SUPERIOR direito e foi pra barra de baixo — canto INFERIOR
  direito agora é sempre Minimizar(ícone "─") → Engrenagem(⚙) →
  Power(⏻), nessa ordem, igual pedido. O menu que a engrenagem abre
  continua exatamente o mesmo (`ToggleGlobalMenu`), só mudou de onde o
  botão fica na tela.
- **Ícone do app no canto superior esquerdo**: `Assets/app-icon.ico`
  (mesmo arquivo do ícone da janela/bandeja) agora aparece antes do
  nome "Handheld Launcher" na barra de cima, como pedido.
- **Jogo Xbox aparecendo duplicado (não-Steam E Xbox) — bug real
  corrigido**: acontece quando um jogo foi adicionado à mão (via
  "Adicionar jogo", antes do `XboxScanFolders` existir ou antes da
  pasta certa estar configurada) e DEPOIS a mesma instalação também
  passa a ser encontrada pelo `XboxFolderScanner` — ficavam duas tiles
  (Ids diferentes: uma GUID manual, outra `xboxfolder_...`) pro mesmo
  jogo. `LibraryService.Refresh()` agora compara o EXECUTÁVEL (caminho
  completo, sem diferenciar maiúsculas) entre as entradas antes de
  somar a do scanner — se já existe alguém apontando pro mesmo `.exe`,
  pula o duplicado do scanner. Prioriza a entrada MANUAL de propósito
  (preserva Id/favorito/capas já escolhidas), então o efeito colateral
  é que o jogo duplicado continua classificado como "Não Steam", não
  "Xbox" — pra corrigir a categoria é preciso apagar o atalho manual
  ("Excluir atalho") e deixar o scanner recriá-lo (perde favorito/capas
  já escolhidas nesse caso específico). **Nunca testado num build
  real.**

Ainda **não** feito:

1. Capas **animadas** (GIF/WEBM) no seletor — fase 2 do seletor de capas,
   ver seção própria mais abaixo (fase 1, estático, já está feita e
   confirmada funcionando)
2. **Simplificar/automatizar mais a instalação** — testando no Legion Go 2
   de verdade apareceu bastante coisa manual (instalar o SDK do .NET,
   configurar o OmniConsole, desligar Legion Space/Xbox/Steam duplicados
   em Configurações > Apps > Inicializar). O instalador já automatiza o
   SDK sozinho agora (pergunta e instala via `winget` se faltar — ver
   "Instalar no Legion Go 2 / ROG Ally"), e a API key do SteamGridDB já
   pode vir pré-configurada (ver seção própria abaixo), mas falta pensar
   num jeito de reduzir o resto também — ideia pra próxima leva, ainda sem
   desenho definido.
3. **Decoupling do controle do Legion Go da Legion Space** — achado
   testando no Legion Go 2: desligar o autostart da Legion Space (pra
   evitar ela abrir junto no boot) quebra a tradução dos joysticks do
   Legion Go (XInput) dentro dos jogos — os controles só funcionam
   enquanto algum processo da Legion Space está rodando. Pesquisei se a
   Legion Space tem alguma opção de abrir "minimizada/na bandeja" em vez
   de em tela cheia, e não achei nenhuma opção assim documentada — pode
   ser que simplesmente não exista. Enquanto isso, a recomendação atual é
   **deixar a Legion Space com autostart ligado mesmo** (ela vai abrir
   junto no boot) e contar com o `Topmost` do nosso launcher (ver bullet
   do modo kiosk acima) pra garantir que a nossa janela fique por cima de
   qualquer forma. Sem solução definitiva ainda — se alguém achar uma
   configuração de bandeja/minimizado escondida em alguma versão futura
   da Legion Space, essa seção é pra atualizar.
4. ~~Confirmação antes de Reiniciar/Desligar o Legion Go~~ — **feito na
   v0.9.4**, ver "Pendências rápidas — v0.9.4" mais abaixo.
5. ~~Tela de configurações de verdade~~ — **feito nesta leva**: item
   "Configurações" do menu geral agora abre uma `SettingsWindow` própria
   (mesmo padrão do "Adicionar jogo") com os campos de SteamGridDB key,
   Steam Web API key, SteamID64 e pastas extras do Xbox app — ver "Tela
   de Configurações" mais abaixo. **Nunca testado num build real.**
6. ~~Botão de excluir atalho de jogo~~ — **feito nesta leva**: "Excluir
   atalho" no menu de contexto (não-Steam adicionado à mão), ver "Status
   atual" acima.
7. **Excluir o jogo de verdade, de fato apagando arquivos** — ainda NÃO
   feito pra jogos não-Steam. O que foi feito nesta leva pro lado Steam
   ("Excluir jogo" abre `steam://uninstall/{appid}` e deixa a PRÓPRIA
   Steam apagar os arquivos) é diferente disso — o launcher nunca
   encosta em arquivo nenhum nesse caso. Pra um jogo manual/Xbox,
   "Excluir atalho" (item 6) só tira da nossa lista, não toca na
   instalação de verdade — apagar os arquivos de fato continua sendo uma
   ideia em aberto, sem desenho ainda, e merece conversa própria antes
   de qualquer código (é bem mais arriscado/irreversível).
8. ~~Organizar a pasta `installer/`~~ — **feito nesta leva**, ver
   "Instalar no Legion Go 2 / ROG Ally" mais abaixo pra estrutura nova.
9. ~~Confirmação de "Excluir atalho"/"Excluir jogo" é só um menu de
   2 opções~~ — **item vermelho adicionado na v0.9.4** (ver "Pendências
   rápidas — v0.9.4"); continua sem ícone de aviso, se quiser isso também
   é só pedir.
10. ~~"Editor de onde procurar" jogos Xbox sem tela de configuração~~ —
    **feito numa leva posterior**: a tela de Configurações (item 5) tem
    um campo de texto multi-linha pra isso agora (uma pasta por linha),
    em vez de só o hand-edit do `settings.json`.
11. **Progresso de download da Epic/Xbox app no painel de Downloads** —
    o "baixando agora" (feito nesta leva, ver "Status atual") é só Steam;
    Epic e Xbox app não têm nenhum arquivo local documentado/estável pra
    ler progresso de download (diferente do `.acf` da Steam), então isso
    ficou de fora. Revisitar só se o recurso pegar e surgir algum jeito
    confiável de ler isso de algum dos dois.
12. **"Na sua conta, não instalado" pra Epic** — o usuário só marcou Steam
    na pergunta de esclarecimento, então Epic ficou de fora de propósito;
    também não existe API pública simples pra isso sem implementar OAuth
    completo da Epic, então mesmo se pedido depois isso não é um ajuste
    rápido.
13. **Painel de Downloads navegável por controle** — a lista em si
    (baixando agora, na conta não instalado, botão "Instalar") é
    mouse/touch-only nesta leva (ver "Status atual"); o controle só abre
    e fecha o painel inteiro. Decisão deliberada pra não ter que desenhar
    um modelo de foco novo pra uma lista de tamanho variável com botão por
    item — avisar se isso for um problema de verdade no handheld.
14. ~~Overlay de FPS sem o RTSS instalado~~ — **resolvido em duas levas**:
    o instalador tenta instalar o RTSS sozinho via `winget` (pacote
    `Guru3D.RTSS`) se não achar ele nos caminhos padrão (ver "Instalar no
    Legion Go 2 / ROG Ally"), e desde a v0.9.4 o launcher também liga
    "Show On-Screen Display"/desliga "Show own statistics" sozinho (ver
    "Pendências rápidas — v0.9.4") — melhor esforço nos dois casos, nunca
    bloqueia nada. Só "On-Screen Display zoom"/"fill" (tamanho da letra/
    fundo) continuam manuais, por escolha (ver "Configurar o RTSS pra
    mostrar o FPS" mais abaixo).
15. ~~Tela de configuração pra `SteamWebApiKey`/`SteamId64`~~ — **feito
    numa leva posterior**, junto do item 5 (mesma `SettingsWindow`).

## Reorganização da tela de Configurações — v0.9.14

- **Abas**: a tela de Configurações, que tinha virado uma lista única rolável
  cada vez mais longa (pedido do usuário: "já vi algo que quero mudar o menu
  configuração novamente, separar em tópicos porque encheu"), agora é
  dividida em 4 abas — **Contas e Chaves** / **Diretórios** / **Modo de
  uso** / **Aparência** — cada uma com só o que é dela. Mesmo padrão visual
  das abas de filtro da biblioteca (botão "pill" que muda de cor quando
  ativo) em vez de um `TabControl` de verdade do Avalonia, pra herdar o
  visual do resto do app sem precisar estilizar o chrome padrão do
  FluentTheme do zero. Navegável pelo controle: LB/RB ciclam as abas (mesmo
  botão que já cicla os filtros da biblioteca por trás), igual peço pra
  qualquer outra lista do launcher. A subdivisão de "Aparência" em
  temas/cores-fontes-tamanhos/sons/imagem, que o próprio usuário marcou como
  "avaliar se compensa", **não** entrou nesta leva — só os 4 tópicos de
  topo, por ora.
- **Overlay de desempenho duplicado, removido**: o ComboBox "Overlay de
  desempenho" que vivia em Configurações → Aparência foi embora de vez —
  ele é repetido com a seleção de preset que já existe, completa, no Menu
  Global → HUD do próprio launcher (marcador ●/○ por preset, status do
  RTSS, atalho L3+R3) desde antes desta leva. Pedido do usuário: "Nos temos
  o overlay em menu-HUD e depois em menu-configurações-preferencias-overlay
  de desempenho (retirar esse do preferências)." Nada de persistência se
  perde — o Menu Global já grava o preset escolhido por conta própria.

## Tema Switch, volume dos sons e modos de imagem/cor — v0.9.13

- **Tema Switch**: novo tema em Configurações → Aparência → Tema
  (`Platform/ThemeManager`) — cinza-grafite + vermelho vibrante, mesmo
  padrão dos outros 10 temas (só paleta de cor), mas com um toque de
  "disposição" junto: `TileCornerRadius`/`BarCornerRadius` (antes globais,
  fixos em Theme.axaml) agora também variam por tema, e o Switch usa um
  valor bem mais "quadrado" (10 em vez de 16/18) — a parte de "layout" do
  pedido original ("só a disposição limpa/minimalista dos itens"), dentro
  do que dá pra variar sem reescrever o XAML inteiro de cada tela.
  Usuário optou por eu criar o visual livremente (sem mandar fotos de
  referência como fez no v0.7.0) — sem nenhum ícone/fonte/logo oficial do
  aparelho real, só a paleta e o arredondamento.
  **Atualização, pós-feedback do usuário**: isso não era o pedido —
  "disposição" aqui ficou só no arredondamento dos cantos; a ideia de
  verdade era um LAYOUT diferente (menu embaixo, capas minimalistas,
  configurações em cima, fundo claro), igual outros temas pensados pra
  SteamOS/Xbox Series. Esse projeto de verdade foi adiado de propósito
  pelo próprio usuário pra depois — ver item 6 do roteiro.
- **Volume dos sons de interação**: `PlaySound` (winmm) não tem parâmetro
  de volume próprio — a escala agora é aplicada direto nas amostras PCM
  de cada som antes de copiar pra memória não gerenciada (ver
  `Platform/UiSounds.Volume`, slider novo em Configurações → Aparência,
  0-100%, também ajustável pelo ←/→ do controle — primeiro `Slider` do
  projeto, então `DialogGamepadNavigator` ganhou suporte a ele). Padrão
  caiu de 100% pra 70%: usuário reportou "dá aquela estourada" no volume
  máximo do aparelho testando a v0.9.12.
- **Modos de otimização de imagem/cor (experimental)**: Vívido, OLED
  (contraste alto), Noturno (luz quente) e Confortável, via
  `SetDeviceGammaRamp` (gdi32.dll — `Platform/DisplayColorManager`,
  pesquisado desde o item 6 do roteiro). Ajusta a curva de cor da tela
  INTEIRA (fora do launcher também) enquanto ele estiver aberto. Como é
  uma API que afeta o Windows inteiro e sobrevive ao launcher fechar se
  não for desfeita, captura a curva ORIGINAL de verdade (via
  `GetDeviceGammaRamp`) antes de aplicar qualquer preset, e devolve
  exatamente ela no modo "Padrão" e ao fechar o launcher (`desktop.Exit`
  + `ProcessExit`/`UnhandledException` como rede de segurança pra um
  encerramento mais abrupto) — nunca um chute de curva neutra, que
  poderia não bater com uma calibração já existente no PC. Mesmo assim,
  marcado como experimental na tela: não dá pra confirmar daqui que o
  driver/GPU de cada PC aceita essas curvas igual, nem que isso não
  bagunça o HDR/perfil de cor das telas OLED do Legion Go 2/ROG Ally —
  se a tela ficar com cor estranha depois de trocar, fechar e abrir o
  launcher de novo (ou reiniciar o aparelho) resolve.

## Bug dos botões de energia e auditoria de código — v0.9.12

- **Bug corrigido** (relatado pelo usuário: "os botões de energia tem
  bugão às vezes tanto no modo desktop quanto no modo handheld"): tanto o
  menu geral (View/Back, inclui a página de Energia) quanto o menu de
  contexto de cada jogo (Start) reagiam a `PointerEntered` (hover do mouse
  ou toque) escrevendo no MESMO índice de seleção que ↑↓ do controle/
  teclado usa. Na prática, só o cursor do mouse parado em cima de outro
  item (desktop, posição que fica de uma abertura anterior do menu) ou o
  dedo encostando de raspão na tela enquanto segura o aparelho e navega
  pelo D-pad (handheld) já trocava, em silêncio, qual ação executaria no
  próximo A/Enter — podendo ser uma ação destrutiva como "Desligar o
  aparelho" sem o usuário ter escolhido isso. Corrigido removendo os
  handlers de hover dos dois menus: clique/toque (`PointerPressed`)
  continua funcionando normal porque já identifica e confirma o item
  certo por conta própria, sem depender desse índice compartilhado.
- **Limpeza**: removida, no processo, uma implementação antiga e paralela
  do menu de Energia (`IsPowerMenuOpen`/`PowerMenuItems`/`BuildPowerMenu`
  e companhia) que não era mais aberta por nada — sobrou de antes do Power
  ser absorvido pelo menu geral, só código morto, sem efeito em
  comportamento.
- **Auditoria geral**: revisão do código adicionado nas últimas versões
  (tela de detalhes do jogo, sistema de sons, HUD numa thread própria)
  em busca de problemas parecidos com o do hover — nada mais encontrado
  por enquanto.
- Removida a frase "nunca testado num aparelho ainda" dos release notes e
  do README (histórico completo, não só esta versão): ela era escrita no
  momento do lançamento de cada leva, antes do teste real, mas ficava pra
  sempre no changelog — dando a entender, pra quem baixasse o projeto
  agora, que ele nunca passou por teste nenhum.

## Correção do som, pacotes extras e tela de detalhes do jogo — v0.9.11

- **Bug corrigido** (relatado pelo usuário: "quando segura ou passa muito
  rápido o som fica bugado bem estranho"): a v0.9.10 passava um `byte[]`
  gerenciado direto pro `PlaySound(..., SND_ASYNC | SND_MEMORY, ...)`. O
  P/Invoke só fixa (pin) esse array na memória DURANTE a chamada — como
  `SND_ASYNC` retorna na hora e o Windows continua lendo esse buffer em
  segundo plano enquanto o som toca, o garbage collector tinha liberdade
  pra mover/coletar aquele array a qualquer momento depois da chamada
  retornar, corrompendo o áudio — mais provável quanto mais rápido o som é
  disparado de novo (segurar um direcional = repetição rápida). Corrigido
  copiando cada `.wav`, uma única vez, pra memória NÃO gerenciada
  (`Marshal.AllocHGlobal`) — o garbage collector nunca toca nisso, então o
  buffer fica sempre válido pra qualquer replay assíncrono.
- **3 estilos de som novos** (pedido: "podemos por opções de sons também
  no tema, alguns diferentes, nada muito exagerado"), além do "Clássico"
  (o original, mantido igual — pedido explícito do usuário: "gostei desse,
  está bem estilo Mario"): **Suave** (mais grave, ataque mais lento),
  **Retro** (onda quadrada, chiptune) e **Future** (senoide + um harmônico
  quieto por cima, mais agudo — "shimmer" digital sem virar chiptune).
  Escolha em Configurações → Aparência → "Estilo do som" (`SoundTheme` em
  `AppSettings`, `Platform/UiSounds.PackNames`).
- **Tela "Detalhes do jogo"** (segundo item do roteiro de ideias novas —
  ver "Roteiro planejado"), só pra jogos da Steam: horas jogadas de
  verdade (`IPlayerService/GetOwnedGames`, já integrado desde antes pro
  painel de Downloads — diferente de `GameEntry.PlaytimeMinutes`, que é só
  o controle local do próprio launcher) e conquistas
  (`ISteamUserStats/GetSchemaForGame` + `GetPlayerAchievements`, cliente
  novo — `SteamAchievementsClient`). Jogo sem conquista nenhuma, ou perfil
  Steam privado, mostram uma mensagem em vez de travar a tela. Abre pelo
  botão **X** do controle (livre até agora) com o jogo em foco na
  biblioteca, ou por "Detalhes do jogo" no menu de contexto (Start) —
  mouse/touch também funciona, já que é um item de menu normal. Precisa
  da Steam Web API key + SteamID64 já configurados (mesma credencial do
  painel de Downloads). Classificação/resumo da loja ficaram de fora
  (API não-oficial, pouco estável — ver "Roteiro planejado").

## Som de interação pelo controle — v0.9.10

- **Primeiro item do roteiro de ideias novas** (planejamento feito com o
  usuário: som de interação → tela de informações do jogo → temas). Três
  tons curtos e sintéticos — gerados do zero por script, sem nenhum banco
  de efeitos de terceiros — `Assets/sfx-move.wav` (navegação),
  `sfx-confirm.wav` (A/confirmar) e `sfx-back.wav` (B/voltar).
  `Platform/UiSounds` toca cada um via `winmm.dll PlaySound` com
  `SND_MEMORY` (direto de um buffer já em memória, sem escrever nada em
  disco nem precisar de pacote extra).
- **Um ponto só de disparo**: `MainWindow.OnGamepadTick` já tinha um único
  `foreach` por onde passa toda ação do controle antes de se espalhar pros
  estados (biblioteca, seletor de capa, menus de contexto/global/energia,
  painel de Downloads) — então um som ali já cobre tudo isso de uma vez,
  sem precisar de uma chamada em cada switch interno. `CycleOverlay`
  (L3+R3) e o fechamento do "Sobre" têm seus próprios pontos de disparo
  (saem do loop principal antes de chegar nesse ponto único).
  **Escopo desta leva: só controle** — mouse/touch ainda não tem som,
  fica pra depois se fizer falta.
- **"Sons de interação"** nova opção em Configurações → Aparência, ligada
  por padrão, mesmo padrão da opção "Animações" já existente (aplica na
  hora, sem reiniciar).

## Ajuste fino da barra de cima, HUD numa thread própria e menu de Energia mais limpo — v0.9.9

- **Barra de cima reorganizada**: pedido do usuário ("ícone de download,
  depois vir o ícone de energia e bateria e ícone de relógio... e dá um
  espaçamento maior entre eles"). Ordem agora é downloads → bateria →
  relógio (antes era relógio → bateria), com mais espaço entre os grupos
  (16px → 28px) — ficavam colados demais um no outro.
- **HUD "só atualiza com o launcher em primeiro plano" — nova tentativa**:
  bug antigo (reportado já na v0.6.1 pro combo L3+R3, aparentemente ainda
  presente no HUD em si). `PerformanceHud` disparava a leitura/escrita a
  cada 1s por um `System.Threading.Timer`, que agenda a chamada no
  ThreadPool — mesmo com `Platform.PowerThrottling` desligando o EcoQoS do
  processo inteiro, o ThreadPool tem seu próprio algoritmo de injeção de
  threads, que pode atrasar um callback bem mais quando o app está em
  segundo plano. Trocado pra uma thread dedicada (mesmo padrão já usado
  pelo `Input/OverlayHotkeyWatcher` desde a v0.6.1, que resolveu
  exatamente esse tipo de atraso pro combo L3+R3): thread própria,
  prioridade AboveNormal, dormindo 1s entre leituras, sem depender do
  ThreadPool decidir quando rodar.
- **Menu de Energia (View/Back → Energia) mais limpo**: pedido do usuário
  ("tem coisa desnecessária"). Nova ordem: Desligar tela → Reiniciar o
  aparelho → Desligar o aparelho → Sair. "Minimizar" saiu de lá — já é um
  botão próprio na barra de baixo, duplicado sem necessidade ali dentro.
  "Ir para a área de trabalho" também saiu: só escondia a nossa janela e
  minimizava as outras (como um "Win+M"), o que não é a mesma coisa que o
  modo Desktop de verdade do Legion Go/ROG Ally — isso é um modo do
  próprio Windows ("Modo Xbox"/Full-Screen-Experience, já mencionado no
  roteiro planejado), não algo que dá pra replicar só escondendo janelas.
  Sem um jeito documentado de sair desse modo por conta própria, e com
  autorização explícita do usuário pra tirar se não funcionasse ("se
  funcionar, se não retira ele" — e não funcionava: "simplesmente
  minimiza, não entra no modo desktop"), foi removido.

## Navegação por controle corrigida, atualização silenciosa, números no HUD e relógio — v0.9.8

Leva inteira a partir de 4 bugs reportados depois do teste real da v0.9.7.

- **B/A dentro de uma lista aberta das Configurações — bug de verdade
  corrigido** (`Views/DialogGamepadNavigator`): a correção da v0.9.7 pra
  esse mesmo ponto ("A abre a lista, B fecha só a lista") tinha virado
  *dead code* na prática — reportado pelo usuário como *"quando entra na
  lista aperta B uma vez pra fechar ele fecha o menu todo"* e *"aperta A
  mas ele não seleciona a cor, fonte etc."*. Causa: assim que
  `ComboBox.IsDropDownOpen` fica `true`, o próprio Avalonia move o FOCO
  de teclado pro item dentro do popup — então checar "o que está focado
  agora é esse ComboBox com IsDropDownOpen=true" nunca batia de verdade,
  e B/A caíam direto no switch de fora (B fechava as Configurações
  inteiras; A não selecionava nada). Corrigido procurando "existe algum
  ComboBox com a lista aberta NESTA JANELA" na árvore lógica inteira, em
  vez de depender de qual controle específico está focado — agora, com
  uma lista aberta, ↑/↓/←/→ trocam o item destacado, A confirma e fecha
  só a lista, e B também só fecha a lista (nunca mais o diálogo inteiro).
- **"Telinha estilo Windows" na instalação da atualização — eliminada**:
  o usuário esclareceu que a tela "estilo Windows" aparecia DEPOIS da
  nossa `UpdateWindow` (temática), já baixada, durante a PARTE DE
  INSTALAÇÃO (troca de arquivos) — isso é a UI nativa do próprio Velopack
  (`UpdateManager.ApplyUpdatesAndRestart`), sem nenhuma opção documentada
  de cor/tema. Trocado por `WaitExitThenApplyUpdates(..., silent: true)`,
  que tem um parâmetro justamente pra suprimir essa UI nativa — agora a
  troca de arquivos acontece sem mostrar nada por cima da nossa própria
  tela. Diferença importante por trás: esse método não fecha o launcher
  por conta própria como o antigo fazia, então `UpdateService.
  ApplyAndRestart` agora chama `Environment.Exit(0)` depois, como a
  própria documentação da Velopack pede.
- **Gráfico de frametime no HUD — trocado por números**: depois de duas
  tentativas de "desenho" (barra Unicode na v0.9.5, rampa ASCII na
  v0.9.7) sem sucesso — a última ainda foi reportada como ilegível
  ("%%@@@---%% não entendi nada") — o HUD (preset Completo) agora mostra
  só os números: mínimo/média/pico (ms) da janela recente de frametime,
  tipo `8/17/42ms`. Não é um gráfico, mas diz exatamente o que importa
  (pico bem mais alto que a média = teve engasgo) sem precisar decifrar
  caractere nenhum.
- **Relógio e ícone de tomada na barra de cima**: faltava um relógio
  perto da bateria (pedido do usuário) — adicionado no lugar do antigo
  placeholder fixo de wifi "●●●" (nunca foi real). E o ícone de bateria
  tinha um bug sutil: só virava 🔌 enquanto a bateria estava realmente
  "carregando" — um aparelho na tomada com a bateria já CHEIA caía de
  volta pro ⚡, como se estivesse rodando na bateria. Agora o ícone
  responde só "está na tomada?", então fica 🔌 o tempo todo enquanto
  plugado, cheio ou não.

## Sparkline ASCII, navegação por controle nas Configurações e confirmação de Salvar — v0.9.7

- **Sparkline de frametime — bug confirmado e corrigido**: o risco já
  avisado no release notes da v0.9.5 se confirmou no aparelho — a fonte
  que o RTSS usa não tem os caracteres de bloco Unicode (▁▂▃▄▅▆▇█) e
  aparecia uma fileira de "??????" em vez da barrinha. Trocado por uma
  rampa de densidade 100% ASCII (`.-=+*#%@`, do mais "vazio" pro mais
  "cheio") — `HudFormatter.SparkChars`, mesma lógica de antes, só os
  caracteres mudaram. **Trocado de novo na v0.9.8** por números
  (mín/méd/pico em ms) — o usuário achou essa rampa ASCII igualmente
  ilegível.
- **Navegação por controle nas Configurações — dois bugs corrigidos**
  (`Views/DialogGamepadNavigator`; **um terceiro bug nessa mesma área foi
  encontrado e corrigido na v0.9.8**, ver seção abaixo):
  - *"quando clico o A pra selecionar um tema, ele vai mudando, ideal
    seria abrir a lista"*: A numa caixa de seleção (Tema, Fonte,
    Tamanho...) focada agora ABRE o dropdown de verdade — dá pra ver
    todas as opções e escolher, em vez de ficar ciclando uma por vez.
    Com a lista aberta, ↑/↓/←/→ trocam o item destacado e A confirma/
    fecha (B só fecha a lista, não fecha as Configurações). Girar a
    opção sem abrir nada (atalho rápido de antes) continua disponível
    em ←/→ com a caixa FECHADA.
  - *"quando chego lá na aba de salvar, se passo pro lado não vai, ele
    só obedece indo pra baixo"*: ←/→ fora de uma caixa de seleção não
    tinham NENHUM efeito — só ↑/↓ moviam o foco entre os controles.
    Agora ←/→ fazem a mesma coisa que ↑/↓ (próximo/anterior controle
    focável), então nenhum botão/campo fica "isolado" só alcançável
    por um lado.
- **Confirmação ao Salvar**: pedido do usuário ("clicar em salvar e ele
  já aparecer uma tela de confirmação e clicar em ok"). Antes, salvar só
  mostrava um texto verde discreto dentro da lista rolável das
  Configurações — fácil de nunca ver, principalmente navegando pelo
  controle (o foco ficava longe dele). Agora aparece uma telinha de
  verdade por cima (fundo escurecido + cartão central, mesmo visual do
  "Sobre"), com o aviso e um botão **OK** — focado automaticamente, então
  quem está no controle já aperta A na hora pra confirmar. O formulário
  por trás fica desabilitado enquanto a telinha está aberta (sem isso,
  ↑/↓ ainda conseguiam focar um campo escondido atrás do fundo
  escurecido).

## Badge de plataforma, logo consistente, performance e telinha de update — v0.9.6

Lote de quatro pedidos soltos numa mensagem só:

- **Badge de plataforma** no canto superior direito de cada capa
  (`GameTileViewModel.PlatformBadgeText`/`PlatformBadgeColorHex`,
  `MainWindow.axaml`) — pedido: "colocar um icone bem minimalista...
  indicando de qual plataforma". **Decisão importante**: NÃO é o logo
  oficial de cada loja (Steam/Epic/GOG/Ubisoft/EA/Xbox) — é só a
  inicial/sigla ("S", "E", "G", "U", "EA", "X") numa cor própria por loja,
  de propósito, pra não reproduzir marca registrada de terceiros dentro
  do launcher. Manual/Emulador não ganham badge (não tem "loja" nenhuma
  pra indicar). A estrelinha de favorito, que antes ficava nesse mesmo
  canto, foi pro canto inferior direito (o inferior esquerdo já é do
  badge "⇩ Instalar" da aba "Não instalados" — os dois nunca aparecem
  juntos, então não tem conflito).
- **Tamanho do LOGO normalizado**: pedido: "ajustar a logo dos jogos uns
  ficam pequenas em cima outros ficam em tamanho bom". Causa: o LOGO é um
  PNG transparente da SteamGridDB e cada fonte exporta com uma margem
  transparente diferente ao redor do desenho — como a altura exibida é
  fixa (`GameTileViewModel.CaptionLogoHeight`), a margem invisível contava
  como "tamanho do logo" e empurrava o desenho de verdade pra bem menor
  nos casos com mais margem. `ArtworkService.TrimTransparentPadding`
  corta a imagem pro menor retângulo que ainda tem pixel não-transparente,
  uma vez, no momento do download/aplicação (tanto no fetch automático
  quanto no picker manual) — não em toda renderização. Precisou adicionar
  `System.Drawing.Common` ao `HandheldLauncher.Core` (Windows-only desde
  o .NET 6, mas o projeto inteiro já é Windows-only de ponta a ponta:
  publish self-contained win-x64, `System.Management`, RTSS). **Só afeta
  LOGOs baixados/aplicados a partir desta versão** — os já salvos antes
  continuam com a margem antiga até serem buscados de novo ("Atualizar
  Capas Automático" ou escolher de novo no seletor).
- **Aba "Todos" lenta — corrigido**: pedido: "verificar a aba de todos
  jogos, parece que ela ta começando ficar lenta, os jogos nao instalados
  ta bem fluido". Causa real:
  `FileToBitmapConverter.Convert` fazia `new Bitmap(path)` — decodifica o
  arquivo do disco de novo, na UI thread, toda vez que o binding é
  reavaliado — e `MainViewModel.RebuildGamesFromLibrary` cria um
  `GameTileViewModel` NOVO pra cada jogo em toda troca de aba/favoritar/
  atualizar. Resultado: toda troca redecodificava a capa de TODOS os
  tiles visíveis do zero, e "Todos" (a lista com mais jogos, principalmente
  depois das lojas novas da v0.9.3) é exatamente onde isso mais pesa —
  "Não instalados" é um subconjunto bem menor, por isso parecia fluida por
  comparação, batendo exatamente com o relato. Corrigido cacheando o
  `Bitmap` já decodificado por caminho + data de modificação do arquivo
  (invalida sozinho se a capa for trocada).
- **Telinha de atualização temática**: pedido: "ajuste fino do
  instalador e do update uma telinha mais bonita e de acordo com o tema
  do launcher principalmente no update". Antes, tocar no aviso "⬆ vX
  pronta" da barra de cima aplicava e reiniciava NA HORA, sem tela
  nenhuma. Agora abre `Views/UpdateWindow` — mesmo visual das outras
  janelas secundárias (`AccountsWindow`): fundo/cores do tema, ícone do
  app, versão, "O que há de novo" (texto de verdade, não só o número da
  versão) e os botões "Ver no GitHub" / "Agora não" / "Atualizar e
  reiniciar" (desabilitado com jogo aberto). O "O que há de novo" vem do
  mesmo `release-notes/X.Y.Z.md` que a Release do GitHub já usava — agora
  também embutido DENTRO do pacote Velopack via `vpk pack --releaseNotes`
  (`release.yml`), e lido de volta por
  `UpdateService.PendingReleaseNotesMarkdown` /
  `VelopackAsset.NotesMarkdown`. Sem biblioteca de markdown no projeto, a
  `MainViewModel.PendingUpdateNotesText` só tira a pontuação (`##`, `**`,
  cabeçalho repetido) em vez de renderizar de verdade — como os arquivos
  já são simples (título + lista com "-"), o resultado fica legível
  igual. **Versões empacotadas antes desta leva não têm essas notas
  embutidas** (caem no aviso "confira no GitHub").
  - **Ajuste fino do instalador**: `vpk pack` ganhou
    `--splashProgressColor "#7C5CFC"` — pinta a barra de progresso do
    `Setup.exe` com o roxo do tema em vez da cor padrão do Velopack
    (opção confirmada na [documentação oficial do
    `vpk pack`](https://docs.velopack.io/reference/cli/content/vpk-windows)).
    Existe também `--splashImage` (uma imagem de fundo durante a
    instalação), mas a doc não confirma a dimensão/proporção esperada —
    melhor não arriscar um instalador com imagem esticada/cortada sem
    poder ver o resultado de verdade. Fica pendente até decidir com o
    usuário: ou ele testa uma imagem e reporta como ficou, ou eu arrisco
    um palpite de tamanho e ele confirma/ajusta depois.

## Privacidade das chaves + frametime no HUD — v0.9.5

- **Chaves/contas escondidas por padrão**: pedido do usuário ("gravar a
  tela de configuração mostrando o que tem pra configurar mas não queria
  exibir os códigos"). A tela de Configurações agora só mostra status
  (`SteamGridDbStatusText`/`SteamWebApiStatusText`/`SteamIdStatusText` em
  `SettingsViewModel`) — nunca o valor da chave nem o número do SteamID64
  (só o apelido da conta, quando detectada). As caixas de verdade ficam
  num diálogo separado (`Views/AccountsWindow`), aberto pelo botão "🔒
  Gerenciar contas e chaves de API" — mesma `SettingsViewModel` do caller
  (não uma cópia), então editar lá já atualiza o status na tela principal
  na hora. "Proteção de visualização" pedida também: os três campos usam
  `PasswordChar` (mascarados) por padrão, com um botão 👁 por campo pra
  revelar só quando precisar. Salvar continua só na tela principal — o
  diálogo de contas não tem Salvar próprio.
- **HUD: mini-gráfico de frametime** (item do planejamento original,
  nunca atacado até agora) — `PerformanceHud` guarda os últimos 20
  frametimes lidos do RTSS e `HudFormatter.Sparkline` desenha um gráfico
  de barras com caracteres Unicode (▁▂▃▄▅▆▇█), escalado de 0 a ~33ms (abaixo
  de 30 FPS já vira a barra mais alta). Só no preset Completo. RTSS OSD só
  desenha texto — não existe uma API de gráfico de verdade pra usar, então
  isso é a aproximação possível. Risco conhecido: se a fonte do RTSS não
  tiver esses glyphs, pode aparecer em branco — nesse caso é só avisar pra
  trocar por uma versão em caracteres ASCII simples. **Esse risco se
  confirmou** (apareceu "??????" em vez da barra) e a v0.9.7 trocou por
  caracteres ASCII — mas o usuário achou essa versão igualmente ilegível
  ("%%@@@---%% não entendi nada"), então a v0.9.8 **abandonou a ideia de
  "desenho" por completo**, trocando por números (mín/méd/pico em ms).

### Temperatura no HUD — pesquisado, ainda sem caminho bom

Pedido antigo ("HUD 2"), travado desde o início porque LibreHardwareMonitor/
WinRing0 exigem driver de kernel + admin e o Defender costuma marcar como
vulnerável. Pesquisa nesta leva, três alternativas avaliadas — nenhuma é um
"sim" claro:

1. **HWiNFO Shared Memory** — API madura e bem documentada, mas: (a) na
   versão grátis (não-Pro) tem limite de 12h contínuas, depois para e pede
   pra reativar na mão — ruim pro uso num handheld que fica ligado o dia
   todo; (b) a licença histórica do protocolo exigia manter a parte de
   leitura com CÓDIGO FECHADO em apps open-source — direto contra a nossa
   licença GPL-3.0 (qualquer modificação tem que ficar aberta). Tem uma
   mudança anunciada pela HWiNFO que tornaria isso mais aberto, mas ainda
   não está em vigor nem confirmada.
2. **AMD ADLX** (biblioteca oficial da AMD, Legion Go 2 e ROG Ally são os
   dois AMD) — sem precisar de outro app rodando, e tem binding oficial pra
   C#. Mas: só achei documentação/exemplos pra GPUs discretas Radeon (RX
   5000/6000/7000) — nenhuma confirmação de que funciona pra ler a
   temperatura da parte gráfica de uma APU integrada (que é o caso aqui);
   exige o driver "AMD Software: Adrenalin" instalado; e de qualquer forma
   só dá temperatura de GPU, não de CPU.
3. **WMI (ACPI Thermal Zone)** — zero dependência extra, sem admin, mas é
   historicamente pouco confiável em notebooks/handhelds (muitos reportam
   valor estático ou sem relação com CPU/GPU de verdade).

Sem decisão óbvia — fica pendente até decidir com o usuário qual caminho
(ou nenhum, por agora) vale tentar. Ver conversa/decisão mais recente.

## Pendências rápidas — v0.9.4

- **Confirmação antes de Reiniciar/Desligar**: o menu Energia agora abre uma
  página de confirmação ("Cancelar"/"Sim, reiniciar o aparelho") em vez de
  executar na hora — risco de perder progresso de jogo com um toque errado
  (item 4 da lista "Ainda não feito" abaixo).
- **Confirmações destrutivas mais chamativas**: "Excluir atalho"/"Excluir
  jogo" e as novas confirmações de Energia aparecem com texto vermelho
  (`ContextMenuItemViewModel.IsDanger`, estilo `Border.menuItem.danger` no
  Theme.axaml) em vez de um item igual aos outros (item 9).
- **RTSS configurado sozinho**: `Platform/RtssProfileConfig.cs` carrega
  `RTSSHooks64.dll` (de dentro da própria pasta de instalação do RTSS — API
  oficial de perfis, a mesma que o MSI Afterburner usa, nunca escreve nada
  em Program Files) e liga `EnableOSD`/desliga `EnableStat` no perfil
  global, sozinho, na abertura do launcher. Antes isso era uma dica pro
  usuário configurar na mão dentro do próprio RTSS — a dica na tela de
  Configurações foi atualizada. Melhor esforço: se o RTSS não estiver
  instalado ou a chamada falhar por qualquer motivo, nada quebra — só
  continua dependendo da configuração manual, igual antes.

## Mais lojas — v0.9.3

Mesmo princípio da Epic: só instalados, lidos do que cada loja registra no
Windows, sem login/senha
(`Core/Services/RegistryStoreScanners.cs`, interface `IStoreLibraryScanner`
que a Epic também usa agora; cada scanner isolado num try/catch):

- **GOG**: `HKLM\SOFTWARE\GOG.com\Games\<id>` (32/64 bits) — `gameName`,
  `exe`, `path`, `workingDir`, `launchParam`; pula DLC (`dependsOn`). Abre pelo
  .exe (sem DRM).
- **Ubisoft Connect**: `HKLM\SOFTWARE\Ubisoft\Launcher\Installs\<id>\InstallDir`;
  título = nome da pasta (o registro não tem nome); abre por `uplay://launch/<id>/0`.
- **EA app (experimental)**: não há índice aberto do EA app — usa as entradas
  "Programas instalados" (Uninstall) com Publisher "Electronic Arts",
  `InstallLocation` e um `.exe` em `DisplayIcon`; abre por esse .exe (o jogo
  chama o EA app). Pode não achar alguns jogos.
- **Dedup**: além do mesmo .exe, mesma pasta de instalação conta como o mesmo
  jogo (evita EA-na-Steam duplicado). Aba Oficiais inclui todas as lojas; Não
  Steam = só manuais/emuladores.

## Mouse e teclado no modo Desktop — v0.9.2

**Confirmado funcionando no PC pelo usuário** (checklist de 10 itens, todos ok).

Bugs reportados usando no PC:
- **Mouse não clicava nas opções dos menus**: itens do menu principal, do menu
  do jogo e do seletor de artes agora têm `PointerEntered` (destaca) e
  `PointerPressed` (executa — mesmos caminhos do controle). Clique no fundo
  escurecido fecha o menu aberto.
- **Botão direito na capa** abre o menu do jogo.
- **Teclado**: Esc / M / Tab abrem o menu principal (antes só Tab, e Esc fechava
  o jogo); Espaço ou a tecla de menu de contexto abrem o menu do jogo;
  **Ctrl+W** fecha o jogo aberto.
- **Enter/setas sem resposta depois de clicar num botão**: o botão focado
  consumia a tecla antes do launcher. O `KeyDown` da janela agora é registrado
  em modo túnel (`RoutingStrategies.Tunnel`), então o launcher vê toda tecla
  primeiro.

## Epic, licença e versões — v0.9.1

- **Epic Games Store (só instalados)**: `Core/Services/EpicLibraryScanner` lê
  `%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item`, ignora
  instalação incompleta, DLC (`MainGameAppName` ≠ `AppName`) e o que não for
  categoria "games"; Id `epic_<AppName>`; abre por `GameEntry.LaunchUri`
  (`com.epicgames.launcher://apps/<ns>%3A<item>%3A<app>?action=launch&silent=true`).
  Entra em Todos/Oficiais; capas pelo nome no SteamGridDB. Sem login (ver v0.9.0).
- **Licença GPL-3.0** adicionada.
- **Página de versões**: notas curtas em `release-notes/X.Y.Z.md` no lugar da
  lista automática de commits; o workflow mantém só as 3 versões mais recentes.

## Integração Steam (etapa 1) e segurança — v0.9.0

- **Conta Steam automática**: `Core/Services/SteamAccountDetector` lê
  `<Steam>\config\loginusers.vdf` (conta `MostRecent`) e preenche o SteamID64
  sozinho na abertura se estiver vazio; Configurações mostra a conta detectada e
  tem "Usar conta da Steam deste PC". Só SteamID64 + apelido — o `AccountName`
  (usuário de login) é ignorado; senha/token nunca.
- **Botões "Obter chave"** (Steam Web API e SteamGridDB) abrem as páginas
  oficiais — a chave da Steam continua sendo do próprio usuário (exigência dos
  termos da Valve; nunca embutir uma chave compartilhada no launcher).
- **DPAPI**: `AppSettingsStore` grava `SteamGridDbApiKey`/`SteamWebApiKey` como
  `dpapi:<base64>` (escopo do usuário + entropia fixa) e migra texto puro na
  primeira leitura. Arquivo de outro PC/usuário → chave vira vazia (não dá pra
  ler). Scripts PowerShell preservam o valor como está.
- **debug.log**: data completa e rotação em 1 MB (`debug.old.log`).
- **Sobre**: aviso de projeto independente / sem garantias / sem coleta de dados.
- **Próxima etapa (Epic)**: só jogos **instalados**, lidos dos manifestos locais
  da Epic (`ProgramData\Epic\EpicGamesLauncher\Data\Manifests\*.item`) e
  abertos pelo link oficial `com.epicgames.launcher://` — sem login: a Epic não
  oferece API pública pra launchers de terceiros, e os caminhos não oficiais
  (usar o login do próprio launcher da Epic) ficam numa zona cinza dos termos.

## Transições — v0.8.0 (Lote B1)

Tudo leve (só opacidade) e desligável em Configurações → Animações:

- **Banner**: crossfade ao trocar de jogo — `HeroBackImage` segura a arte
  anterior por baixo e `HeroImage` aparece por cima (`OnHeroImageChanged`),
  sem escurecer no meio. O fundo borrado da janela troca direto (fazer
  crossfade nele dobraria o custo do blur).
- **Menus, Downloads, Sobre, seletor de artes, "Iniciando"**: aparecem com fade
  (classe `fadeIn` + `Style.Animations` em `Panel.fadeIn[IsVisible=True]`).
- **Troca de aba**: a fileira de jogos entra com fade (`FadeIn`).
- Inclui a correção do tema Preto (OLED): barras/véu 100% opacos e texto da aba
  ativa com contraste automático (`OnAccentBrush`).

**Próximo combinado com o usuário**: integração das plataformas — primeiro
Steam, depois Epic — antes da versão pesada (artes animadas/vídeo).

## Ajustes v0.7.1

- **Fontes** (todas do Windows): Moderna (Segoe UI), Divertida (Comic Sans),
  Formal (Georgia), Gamer (Bahnschrift), Terminal (Consolas), Simples (Arial),
  Clássica (Times New Roman), Legível (Verdana), Elegante (Trebuchet).
- **Tamanho do texto** Pequeno/Médio/Grande (0.88×/1×/1.18×): tamanhos do
  Theme.axaml viraram `DynamicResource FontSize*`, sobrescritos em
  `Application.Resources` por `ThemeManager.ApplyFontSize` (vale na hora).
  Tamanhos fixos direto no XAML (alguns textos pequenos) não escalam.
- **Paletas**: + Verde, Laranja, Rosa, Ciano, Preto (OLED), Dourado (10 no total).
- **Cabeçalho dos menus**: a moldura de contorno virou faixa com fundo
  semitransparente na cor do tema (`AccentTranslucentBrush`), largura total.
- **Sobre** foi pro menu principal (abaixo de Energia) e ganhou o crédito
  "Desenvolvido com Claude (Anthropic)".

## Acabamento profissional — v0.7.0 (Lote A)

Pedidos do usuário a partir de fotos dos menus:

- **Fonte**: padrão agora é a do Windows (Segoe UI Variable / Segoe UI);
  Comic Sans virou opção "Divertida" em Configurações → Aparência → Fonte
  (`ThemeManager.ApplyFont`, valor local na janela).
- **Cabeçalho dos menus**: título centralizado numa moldura na cor de destaque
  (`Border.menuHeader`); no menu do jogo, com o ícone do jogo ao lado.
- **Sem as linhas de "↑↓ navega · A confirma..."** nos menus e no seletor de
  artes; só fica a dica do atalho do HUD na página HUD.
- **Textos**: sem a setinha "›" nas pastas; "HUD de desempenho" → "HUD";
  "Power" → "Energia"; Personalizar sem "Escolher" repetido (Capa, Capa larga,
  Banner, Logo, Ícone); seletor "Capa · <jogo>"; "Abrir Configurações" →
  "Preferências".
- **Rodapé**: "Handheld Launcher vX · por profcosta67".
- **Sobre** (Configurações → Sobre): ícone, versão, descrição curta, autor,
  link do GitHub e créditos.

**Próximos lotes combinados**: B (v0.8.0) artes animadas do SteamGridDB +
transições dinâmicas; C (v0.9.0) temas de layout (Console / Grade / Destaque).

## Correções v0.6.3

- **L3+R3 com o jogo na frente**: nas versões recentes do Windows a leitura de
  controle passa pelo GameInput, que por padrão não entrega botões pra apps em
  segundo plano. `Input/GameInputHelper`: processo ajudante (mesmo exe, arg
  `--gameinput-helper <pid>`) que liga `SetFocusPolicy(EnableBackgroundInput)`
  e sinaliza o evento nomeado `HandheldLauncher_HudCycle_7f3a9c2e` no L3+R3.
  Isolado em processo próprio porque a tabela de métodos COM do GameInput
  (v0) nunca foi testada — se falhar, só o ajudante cai (ver debug.log
  "GameInputHelper:"). Morto antes de aplicar atualização (seguraria o exe).
  `CycleOverlayPreset` ignora disparos em < 0,5 s (XInput + GameInput + atalho).
- **Ctrl+Alt+O** (atalho global) também troca o HUD — dá pra mapear num botão
  traseiro pelo Legion Space/Armoury Crate.
- **Tema**: barras de cima/baixo e véu do fundo eram cores fixas; agora
  `BarBrush`/`BackdropDimBrush` seguem o tema.

## Correções v0.6.2 — Configurações no handheld

- **Rolagem**: a janela tinha altura fixa maior que a tela do handheld (Salvar/
  Fechar ficavam fora da tela); agora se limita à área útil (`FitToScreen`).
- **Teclado virtual**: o Windows não abre o teclado de toque sozinho pro
  Avalonia; `Platform/TouchKeyboard` abre via COM `ITipInvocation` (TabTip,
  mesmo do botão da barra de tarefas) ao tocar numa caixa de texto com o dedo,
  ou pelo botão "⌨ Teclado"; plano B `osk.exe`. No Windows 11 não dá pra saber
  com certeza se o teclado já está aberto (trava de 1,5 s evita abre/fecha).
- **Controle nos diálogos** (`Views/DialogGamepadNavigator`): com Configurações
  ou Adicionar jogo aberto, o MainWindow repassa o controle pro diálogo: ↑↓
  foco, ←→ troca opção de caixa de seleção, A aperta/marca/abre teclado, B
  fecha. Antes o controle mexia na biblioteca por trás do diálogo.

## Correções v0.6.1

- **L3+R3 parava de trocar o HUD** (funcionava na primeira vez, depois não):
  o combo era lido pelo timer de 50 ms da janela, que o Windows 11 pode
  desacelerar com o launcher em segundo plano (modo de eficiência/EcoQoS) —
  toque rápido se perdia. Agora: `Input/OverlayHotkeyWatcher` (thread própria,
  lê os 4 slots do XInput a cada 40 ms, loga no debug.log) +
  `Platform/PowerThrottling` (pede ao Windows pra não desacelerar o launcher).
- **Faixa preta atrás do HUD**: é o "On-Screen Display fill" do próprio RTSS
  (fundo do OSD), não algo que o launcher desenha — desmarcar no RTSS deixa
  sem fundo. Controlar isso pelo launcher (sólido/transparente/sem fundo)
  exigiria editar o perfil do RTSS em Program Files (admin) — não feito.

## Aparência — v0.6.0

Tudo em Configurações → Aparência e vale na hora ao fechar a tela (sem
reiniciar):

- **Tema**: Escuro (original) / Claro / Azul / Vermelho — `Platform/ThemeManager.cs`
  troca a cor dos pincéis do `Theme.axaml` (mesma instância usada por todo
  `StaticResource`, então repinta ao vivo) e a variante do Fluent (Claro =
  Light). Cores fixas no XAML (véus pretos, badges) não mudam.
- **Tamanho das capas**: Pequeno 0.8× / Médio 1× / Grande 1.2×
  (`GameTileViewModel.SizeScale`; largura/altura agora bindadas direto na capa).
- **Biblioteca**: fileira horizontal (rola pros lados) ou **grade vertical**
  (WrapPanel, rola pra cima/baixo; ↑↓ vão pra linha de cima/baixo mais
  alinhada, pelas posições reais das tiles). Mesmo ItemsControl, só troca o painel.
- **Animações**: zoom no foco (já existia) + **zoom ao passar o mouse** +
  rolagem suave; desligar põe a classe `noAnim` na janela.
- Rolagem até o selecionado agora usa o retângulo real da tile (serve pros
  dois layouts e qualquer tamanho), com a conta antiga como plano B.

## Organização da interface — v0.5.0

- **Menu em subpastas** (View/Back ou ⚙): raiz com **Biblioteca ›**, **HUD de
  desempenho ›**, **Configurações ›** e **Power ›**; soltos no topo só
  "Reiniciar pra atualizar" e "Fechar: <jogo>" quando existem. B volta pra
  raiz, View fecha. O botão ⏻ abre direto na página Power (o menu Power
  separado antigo ficou sem uso — `BuildPowerMenu` é código morto).
- **Power**: Ir para a área de trabalho (esconde na bandeja +
  `Shell.Application.MinimizeAll`; processo continua vivo, a tarefa do modo
  Handheld não reabre), Minimizar, Desligar tela, Reiniciar, Desligar, Sair.
- **HUD ›**: escolhe o preset direto (● marca o atual) + estado do RTSS.
- **Downloads (Y)**: só downloads em andamento, com %, barra, **MB/s e tempo
  restante** (velocidade = diferença do `.acf` entre leituras; a Steam só
  regrava o arquivo de tempos em tempos, então mantém a última medida por
  até 30 s). Botão/A **"Abrir downloads na Steam"** (`steam://open/downloads`)
  — a Steam não permite outro programa pausar/retomar. A lista "na conta, não
  instalado" saiu do painel (já é a aba Não instalados).
- **Aba Não instalados**: véu cinza por cima da capa.
- **Seletor de artes**: novo "Escolher ícone" (ícone da tela "Iniciando").

## HUD de desempenho (estilo MangoHud) — v0.4.0

Texto colorido com a paleta do MangoHud (GPU verde, CPU azul, VRAM roxo, RAM
rosa, bateria salmão, FPS vermelho;
carga/FPS mudam de cor verde → amarelo → vermelho) desenhado **pelo RTSS
dentro do frame do jogo** — funciona até em tela cheia exclusiva.

- **Como funciona**: `Platform/PerformanceHud.cs` roda a cada 1 s numa thread
  de fundo: lê FPS/frametime da memória compartilhada do RTSS
  (`RTSSSharedMemoryV2`, `Platform/RtssSharedMemory.cs`), lê sensores
  (`Platform/SystemMetrics.cs`) e escreve o texto num slot de OSD próprio do
  RTSS (dono "HandheldLauncher", tags `<C=RRGGBB>`).
- **Sensores** (sem administrador): CPU % e clock (contadores "Processor
  Information"), GPU % (contadores "GPU Engine" 3D, mesma conta do Gerenciador
  de Tarefas), VRAM ("GPU Adapter Memory" dedicada+compartilhada), RAM, bateria
  %, consumo em W (WMI `root\WMI BatteryStatus`) e tempo restante.
  **Temperaturas ficaram de fora**: no Windows exigem driver de kernel +
  administrador.
- **v0.4.1 (após o 1º teste real, confirmado funcionando incl. L3+R3)**: tudo
  numa linha só com espaço largo entre os grupos; cor só nos rótulos (valores
  brancos, vermelho só bateria ≤15%); tempo restante de bateria (plano B via
  WMI RemainingCapacity ÷ consumo quando o Windows não informa) e horário no
  fim. Tamanho da letra = "On-Screen Display zoom" do RTSS. Temperatura
  continua pendente (pedido do usuário, avaliar no futuro).
- **Presets**: Desligado / Resumido (FPS + bateria numa linha) / Médio (GPU,
  CPU, RAM, bateria, FPS) / Completo (+ VRAM, clock, W, tempo de bateria).
  Escolhe em Configurações (vale na hora), no menu geral, ou **L3+R3 no
  controle durante o jogo**.
- **Requisito**: RTSS aberto. Desmarcar "Show own statistics" no RTSS evita o
  FPS duplicado (o próprio RTSS continua medindo).
- Junto: com um jogo aberto e o launcher em segundo plano, o controle não
  aciona mais nada no launcher (antes B "fechava jogo" e A "abria jogo" no
  launcher escondido) — exceto o L3+R3.

## Tela de Configurações

Novo nesta leva, a pedido do usuário depois de se perder tentando achar
onde colar a Steam Web API key ("seria possível criar uma tela de
configuração pelo próprio Launcher dessa chave? Acho que ficaria mais
fácil"). Item "Configurações" do menu geral (View/Back no controle, ou a
engrenagem no canto inferior direito) abre uma janela própria
(`SettingsWindow`, mesmo padrão do diálogo "Adicionar jogo") com quatro
campos:

- **SteamGridDB API key** (capas)
- **Steam Web API key** (lista "na sua conta, não instalado" do painel de
  Downloads)
- **SteamID64**
- **Pastas extras pra procurar jogos do Xbox app** — uma pasta por linha

Clicar em "Salvar" grava direto em
`%AppData%\HandheldLauncher\settings.json` (o mesmo arquivo que já
existia — essa tela só poupa o hand-edit) e mostra um aviso na própria
tela. **Importante**: salvar NÃO reconfigura os serviços já abertos
nessa execução do launcher (eles recebem os valores uma vez só, na
abertura do app) — precisa **fechar o launcher de vez e abrir
novamente** pra valer (Minimizar não é suficiente).

Mouse/teclado-only de propósito, igual o "Adicionar jogo" — ainda não dá
pra navegar os campos de texto pelo gamepad, só fechar a janela
(Esc no teclado; o controle ainda não tem um fechamento dedicado aqui,
mesma limitação do "Adicionar jogo"). **Nunca testado num build real.**

## Configurar a API key do SteamGridDB

**Pela tela de Configurações (recomendado agora):** abre "Configurações"
no menu geral, cola a chave no campo "SteamGridDB API key" e clica
"Salvar" (reinicia o launcher depois).

**Na mão, direto no arquivo (continua funcionando):**

1. Cria uma conta grátis em https://www.steamgriddb.com e gera uma API key
   em https://www.steamgriddb.com/profile/preferences/api
2. Roda o launcher uma vez (ele cria `%AppData%\HandheldLauncher\settings.json`
   automaticamente, com o campo vazio)
3. Abre esse arquivo e cola a chave:
   ```json
   { "SteamGridDbApiKey": "sua-chave-aqui" }
   ```
4. Reabre o launcher e clica em "Buscar capas"

**Deixar pré-configurada em todo install novo (PC, Legion Go 2, ROG Ally, ...):**

Pra não ter que repetir os passos acima em toda máquina/instalação nova,
cria um arquivo de texto `installer\Arquivos\steamgriddb-api-key.txt`
(caminho atualizado nesta leva — pasta `installer\` foi reorganizada, ver
"Instalar no Legion Go 2 / ROG Ally" mais abaixo) com só a sua chave dentro
(sem aspas, sem mais nada):
```powershell
"sua-chave-aqui" | Set-Content installer\Arquivos\steamgriddb-api-key.txt
```
Esse arquivo é só seu, fica de fora do git (`.gitignore`) — a chave nunca
é commitada no repositório. A partir daí, toda vez que `Install-Launcher.ps1`
rodar numa máquina onde esse arquivo está copiado junto (dentro da pasta
`installer\Arquivos\`) **e** ainda não existe uma key salva naquela máquina, ele usa
automaticamente essa chave pra preencher o `settings.json` — sem precisar
editar nada na mão depois de instalar. Se a máquina já tinha uma key salva
de antes (de uma instalação anterior, por exemplo), ela é preservada e o
arquivo padrão é ignorado nesse caso.

(Pra configurar pastas extras de busca de jogos Xbox/Game Pass, que também
é hand-edit do `settings.json` — mas de um campo diferente,
`XboxScanFolders` — ver bullet própria em "Status atual" mais acima.)

Sobre compartilhar a mesma key entre pessoas/máquinas: tecnicamente
funciona (não existe trava por máquina), mas a SteamGridDB tem limite de
requisições por key — quanto mais instalações usando a mesma chave ao
mesmo tempo, mais perto desse limite. Pra uso pessoal em algumas máquinas
suas (PC + Legion Go 2 + futuramente o ROG Ally) não deve ser problema;
pra distribuir pra terceiros, o ideal é cada um gerar a própria key grátis.

## Configurar a Steam Web API (painel de Downloads → "na sua conta, não instalado")

Só necessário pra ver a seção "na sua conta, não instalado" do painel de
Downloads (ver "Status atual" acima) — o "baixando agora" funciona sem
nada disso.

1. Gera uma Steam Web API key (grátis) em
   https://steamcommunity.com/dev/apikey — precisa estar logado e ter um
   número de telefone vinculado à conta Steam.
2. Descobre o seu SteamID64 (um número de 17 dígitos, diferente do seu
   nome de usuário) em https://steamid.io — cola o link do seu perfil
   Steam lá e ele mostra o SteamID64.
3. **Pela tela de Configurações (recomendado agora)**: abre
   "Configurações" no menu geral e cola os dois valores nos campos
   "Steam Web API key" e "SteamID64", depois "Salvar" — ver "Tela de
   Configurações" mais acima. Ou, na mão: roda o launcher uma vez (cria
   o `settings.json` se ainda não existir) e edita
   `%AppData%\HandheldLauncher\settings.json` direto, adicionando os
   dois campos:
   ```json
   { "SteamWebApiKey": "sua-chave-aqui", "SteamId64": "seu-steamid64-aqui" }
   ```
4. Reabre o launcher (fecha de vez, não só minimiza) e abre o painel de
   Downloads (item "Downloads" no menu geral) — a segunda seção deve
   trocar da mensagem "configure..." pra lista de verdade (ou erro de
   rede, se a chave/ID estiver errado).

Sem pré-configuração automática via arquivo ainda (como o
`steamgriddb-api-key.txt` do instalador faz pra API key do SteamGridDB);
se isso for repetir em várias máquinas, é usar a tela de Configurações
(ou editar o `settings.json`) em cada uma por enquanto.

Sua biblioteca Steam precisa estar com o perfil público (ou pelo menos
"detalhes do jogo" públicos) pra `GetOwnedGames` conseguir ler — se a
lista voltar vazia mesmo com jogos na conta, isso é a primeira coisa a
checar em https://steamcommunity.com/my/edit/settings.

## Configurar o RTSS pra mostrar o FPS na tela

O instalador tenta instalar o RTSS sozinho via `winget` (ver "Instalar
no Legion Go 2 / ROG Ally"), mas **instalado não é o mesmo que
configurado pra aparecer na tela** — o RTSS não vem com o On-Screen
Display pré-ligado nem com nenhum jogo "pré-configurado" igual o
MangoHud mostra fora da caixa. Depois de instalado, precisa abrir o
próprio RTSS (ícone na bandeja/área de notificação) e, na janela
principal dele:

1. Ligar o botão **"Show On-Screen Display"** no canto superior esquerdo
   da janela do RTSS (normalmente é esse o botão que "ativa" o overlay
   de verdade).
2. Garantir que o jogo apareça na lista de apps do RTSS (ele detecta
   jogos abertos automaticamente na maioria dos casos) ou confiar no
   perfil `<Global>` (aplica pra tudo).
3. No perfil do jogo (ou no `<Global>`), ligar **"On-Screen Display
   support"** E **"Show own statistics"** — esse segundo é o que faz o
   RTSS mostrar o PRÓPRIO contador de FPS sozinho, sem precisar do MSI
   Afterburner.
4. O campo de limite de quadros (framerate limit) é outra coisa (limita
   o FPS, não mostra ele na tela) — pode deixar em 0/sem limite.

Não achei, pesquisando, nenhum atalho de teclado padrão documentado pra
ligar/desligar o OSD em jogo — pode ser preciso configurar um na mão
dentro do próprio RTSS (seção de hotkeys) se quiser isso. Esse passo
continua manual — automatizar só a instalação (o que já foi feito) não
dá pra automatizar essa configuração de dentro do RTSS sem entender
melhor onde ele guarda esse estado (provavelmente registro do Windows,
não investigado ainda).

## Instalação e atualização automática (GitHub + Velopack)

**Este é o jeito recomendado de instalar daqui pra frente** (a seção "Instalar
no Legion Go 2 / ROG Ally" abaixo descreve o instalador antigo por `.bat`, que
continua no repositório mas fica obsoleto). **Confirmado funcionando de ponta
a ponta**: v0.3.1 instalada pelo `Setup.exe` e atualizada sozinha pra v0.3.2
pelo aviso na barra de cima (pacote delta). O workflow de build do GitHub
(`.github/workflows/ci.yml`) pega erro de compilação a cada envio.

### Como funciona

1. O código mora em <https://github.com/profcosta67/Handheld-Launcher>
   (repositório público — nenhuma chave de API está no código; ver abaixo).
2. A cada envio pro GitHub, `ci.yml` compila tudo num Windows de verdade
   (se quebrar, aparece o erro exato na aba **Actions**).
3. Pra lançar uma versão nova: **mude o número no arquivo `VERSION`** (raiz
   do repo, ex: `0.3.1` → `0.3.2`) e envie pro GitHub. `release.yml` roda
   sozinho: apaga Releases quebradas (sem arquivos), publica self-contained
   win-x64, empacota com o [Velopack](https://velopack.io) (`vpk`), cria a tag
   `vX.Y.Z` + a Release e anexa o instalador, os pacotes de atualização e o
   `Modo-Console.zip`. (Criar a Release/tag `vX.Y.Z` pelo site também
   funciona.) Se a versão já tiver Release completa, não faz nada.
4. **Primeira vez em cada aparelho**: baixe `HandheldLauncher-win-Setup.exe` da
   Release e execute. Instala em `%LocalAppData%\HandheldLauncher\`, cria
   atalhos e abre. Não precisa do .NET SDK.
5. **Depois disso nunca mais baixa nada na mão**: o launcher pergunta ao
   GitHub se há versão nova ~20 s depois de abrir e a cada 6 h, baixa em
   segundo plano (só o que mudou, quando dá) e mostra na barra de cima
   **"⬆ vX.Y.Z pronta — toque pra reiniciar"**. Tocar nesse aviso abre a
   telinha de atualização (`Views/UpdateWindow`, v0.9.6 — versão + "o que
   há de novo" + Atualizar/Agora não), em vez de reiniciar direto como
   antes; o item do menu View/Back continua aplicando na hora (sem a
   telinha), pra quem só quer confirmar rápido pelo controle. Com um jogo
   rodando ele **não** reinicia nem baixa (nem pela telinha, nem pelo
   menu). Menu → **Verificar atualizações** força a checagem na hora. A
   troca de arquivos em si (depois de confirmar) roda em modo silencioso
   desde a v0.9.8 — a Velopack mostrava uma telinha própria, sem tema
   nenhum ("estilo Windows"), bem rápido por cima da nossa; agora isso
   fica escondido e só a nossa UI aparece em qualquer ponto do processo.
6. `settings.json` (chaves, SteamID, pastas Xbox) e a biblioteca ficam em
   `%AppData%\HandheldLauncher\` — a atualização nunca mexe nisso.

### Modo Desktop × Handheld (Configurações)

Desde a v0.3.5 o modo é escolhido **dentro do launcher** (Configurações → "Modo
de uso"; na primeira abertura do launcher instalado ele abre essa tela
sozinho):

- **Desktop**: janela normal, nada automático, "Sair" fecha de verdade (remove
  a Tarefa Agendada `HandheldLauncherWatchdog`).
- **Handheld**: tela cheia, abre no login e reabre se fechar (o próprio
  launcher cria a tarefa via XML — roda também na bateria). "Sair" segura a
  reabertura até reiniciar o aparelho (`WatchdogGate`, marcador em
  `%AppData%\HandheldLauncher\quit-flag.txt`); a tarefa **não** é mais
  desativada pelo Sair (antes ficava desativada pra sempre).
- Trocar de modo exige reiniciar o launcher. Configurações também mostra a
  versão e tem o botão "Verificar atualizações".
- `debug.log` registra a cada início quem abriu o processo (`pai=`) e os
  argumentos — usado pra descobrir se algo fora da nossa tarefa (Modo Xbox,
  Legion Space) reabre o launcher depois de "Sair" no Legion Go 2.
- Local de instalação customizado: não implementado (o `Setup.exe` não tem
  tela pra isso; `--installto` nunca foi testado).

### Modo console via script (opcional, legado)

O `Setup.exe` só instala o app. O que o instalador antigo fazia além disso
(abrir em tela cheia no login, reabrir se fechar, desativar Legion Space/
Armoury Crate/Xbox Game Bar na inicialização, instalar o RTSS) agora é o script
**`Modo-Console.zip`** (também anexado à Release): extraia e dê duplo clique em
`Configurar-Modo-Console.bat` **depois** de instalar pelo `Setup.exe`. Ele:
migra e remove a instalação antiga (se existir), registra a Tarefa Agendada
`HandheldLauncherWatchdog` apontando pro exe novo, liga `KioskMode` **sem
apagar as outras chaves do `settings.json`**, e guarda log/backup em
`%AppData%\HandheldLauncher\`. `Desinstalar.bat` desfaz isso (o app em si
desinstala-se por Configurações → Aplicativos).

### Segurança do repositório público

- Nenhuma chave de API/SteamID está no código nem no histórico (conferido nos
  commits antes de publicar); elas só existem no `settings.json` de cada
  aparelho. `installer/Arquivos/steamgriddb-api-key.txt` é ignorado pelo git.
- `release.yml` tem uma trava: falha se aparecer qualquer `*api-key*`,
  `settings.json` ou `.pfx` dentro do pacote publicado.
- Os e-mails dos commits antigos foram trocados pelo e-mail anônimo do GitHub.

### Pontos de atenção

- **Versão do `vpk`** no `release.yml` (`VPK_VERSION`) tem que ser igual à
  versão do pacote `Velopack` no `HandheldLauncher.App.csproj` (hoje 1.2.161).
- Fora de uma instalação do `Setup.exe` (`dotnet run`, instalação antiga) o
  atualizador vira no-op silencioso (`UpdateService.IsInstalled == false`).
- Código: `Platform/UpdateService.cs`, `MainViewModel` (`RunUpdateLoopAsync`,
  `CheckForUpdatesAsync`, `ApplyUpdate`, `UpdateStatusText`), botão de aviso na
  barra de cima em `MainWindow.axaml` e `Program.Main` (`VelopackApp.Build().Run()`
  tem que ser a primeira linha).

## Instalar no Legion Go 2 / ROG Ally (beta v0.1)

**Confirmado funcionando num PC Windows normal** (publish, cópia, atalhos,
modo kiosk, ajuste do Xbox Game Bar, busca do Legion Space/Armoury Crate —
tudo rodou de ponta a ponta). Testado no Legion Go 2 de verdade também
(instalou certinho, ícone apareceu) — mas apareceu uma peça que a gente
não sabia que existia, ver aviso abaixo.

> **⚠️ Achado testando no Legion Go 2 de verdade**: depois de reiniciar, o
> Windows entrou no **"Modo Xbox" / "Full screen experience"** — um
> recurso do próprio Windows 11 (não do Legion Space) que assume a tela
> no boot e abre um "app principal" configurado (no caso, o Legion Space).
> É por isso que a busca "melhor esforço" do instalador não acha nada pra
> desativar: não é atalho na pasta de Inicialização nem chave Run, é um
> recurso de outro nível do Windows inteiro.
>
> Duas formas de resolver, com trade-off diferente:
>
> 1. **Desligar o Modo Xbox de vez** (Configurações > Jogos > Modo Xbox >
>    desliga "Entrar no modo Xbox ao iniciar", reinicia de verdade pelo
>    menu Iniciar por causa do Fast Startup). O boot cai no desktop normal
>    do Windows, e nosso atalho de inicialização automática abre o
>    launcher em fullscreen por cima — mas aparece uns segundos de
>    desktop/taskbar visíveis antes disso. Risco zero, mas não é
>    "boot direto" de verdade.
> 2. **(Escolhido) Manter o Modo Xbox ligado e registrar o NOSSO launcher
>    como "app principal" dele** — aí sim sem nunca mostrar o desktop,
>    igual o Legion Space faz hoje. A lista nativa de "Escolher app
>    principal" só aceita apps credenciados pela Microsoft/fabricante, não
>    qualquer `.exe` — pra contornar isso, usa-se uma ferramenta da
>    comunidade: **Xbox Full Screen Experience Tool**
>    (`github.com/8bit2qubit/XboxFullScreenExperienceTool`), que inclui um
>    módulo **OmniConsole** pra registrar qualquer executável personalizado
>    (o nosso incluso) nessa lista. **Não é uma ferramenta nossa, não foi
>    testada por nós** — é de terceiros, não assinada pela Microsoft
>    (Defender pode avisar), o próprio autor recomenda backup antes, e
>    pode parar de funcionar depois de um update grande do Windows. Se der
>    problema, ela tem um botão "Disable and Restore"; o caminho manual de
>    reverter é "Escolher app principal" > Nenhum + desligar o ViVeTool
>    flag + apagar a tarefa agendada `SetPanelDimensions`.
>
> **Status**: opção 2 escolhida e **confirmada funcionando** — o launcher
> abre pelo Modo Xbox de verdade. Só que junto dele também abriram Legion
> Space, Xbox app e Steam Big Picture (4 coisas de uma vez) — cada um tem
> seu próprio autostart independente do Modo Xbox, e o jeito simples de
> desligar os três é Configurações > Aplicativos > Inicializar (switch
> on/off pra cada um, zero risco).

O que o instalador faz, resumido (detalhes completos nos comentários do
próprio `Install-Launcher.ps1`): checa se o `.NET SDK` está instalado e
**oferece instalar sozinho via `winget`** se não estiver (pergunta antes,
não instala sem confirmar), publica o app (`dotnet publish`,
self-contained — a máquina onde ele vai RODAR não precisa ter o .NET
instalado), copia pra `%LocalAppData%\Programs\HandheldLauncher\`, cria
atalho na Área de Trabalho/Menu Iniciar, configura ele pra abrir sozinho a
cada login **e se reabrir sozinho se for fechado/crashar** — por padrão via
uma Tarefa Agendada `HandheldLauncherWatchdog` que checa a cada 1 minuto
(use `-NoWatchdog` pra voltar ao atalho simples antigo na pasta de
Inicialização, que abre uma vez só e não reabre — nenhum dos dois
substitui o shell do Windows, Alt+Tab continua funcionando normal), liga o
modo kiosk (fullscreen + sempre por cima) no `settings.json`, preenche a
API key do SteamGridDB automaticamente se você tiver um
`installer\Arquivos\steamgriddb-api-key.txt` (ver seção própria acima), e tenta
impedir o Xbox Game Bar e o Legion Space/Armoury Crate SE de abrirem
sozinhos no boot (os dois primeiros via chaves de registro bem
documentadas; os do fabricante via uma busca "melhor esforço" — ver
comentários do script pras limitações disso, e o aviso abaixo sobre o
Modo Xbox, que é o motivo real de essa busca não achar nada no Legion
Go 2).

**Estrutura da pasta `installer\` (reorganizada nesta leva)** — pedido do
usuário: a raiz só tem os dois `.bat` que você realmente clica; TUDO o
resto (os `.ps1`, a API key, a pasta `publish\` quando existir) agora mora
dentro de uma única subpasta `Arquivos\`:
```
installer/
├── Instalar.bat              # clique duplo pra instalar
├── Desinstalar.bat           # clique duplo pra desinstalar
└── Arquivos/
    ├── Install-Launcher.ps1
    ├── Uninstall-Launcher.ps1
    ├── steamgriddb-api-key.txt   # opcional, ver "Configurar a API key" acima
    └── publish/                 # gerado pelo publish, não existe até você rodar o instalador
```
Os `.bat` continuam funcionando exatamente igual (só chamam o `.ps1` certo
dentro de `Arquivos\` agora) — pra quem só usa o duplo-clique, nada muda
na prática, a pasta só ficou mais limpa de olhar.

**Jeito fácil (duplo-clique, sem digitar comando nenhum):**

Dentro de `installer\`, tem dois arquivos `.bat`:

- **`Instalar.bat`** — clica duas vezes. Ele detecta sozinho se o .NET SDK
  está instalado na máquina: se estiver (seu PC normal), publica o app do
  zero; se não estiver (o esperado no próprio Legion/ROG Ally), usa uma
  pasta `Arquivos\publish\` que precisa já estar copiada ali dentro (veja
  o passo 2 abaixo). A janela fica aberta no final (`pause`) pra você ler
  o resultado com calma, sem fechar sozinha.
- **`Desinstalar.bat`** — desfaz tudo (remove o app, atalhos, e restaura
  os ajustes de registro/inicialização pro estado de antes).

**Passo a passo pra levar pro Legion/ROG Ally** (que provavelmente não tem
o SDK do .NET instalado):

1. No seu PC normal (onde já funcionou), roda `Instalar.bat` uma vez — isso
   gera a pasta `installer\Arquivos\publish\` com o app já publicado e
   pronto pra rodar em qualquer Windows x64, sem precisar do SDK.
2. Copia a pasta `installer\` **inteira** (os dois `.bat` e a subpasta
   `Arquivos\` com tudo dentro, publish incluso) pro Legion/ROG Ally (pen
   drive, rede, como preferir).
3. No Legion/ROG Ally, clica duas vezes em `Instalar.bat`. Como não tem o
   SDK ali, ele detecta isso e usa a pasta `Arquivos\publish\` que você
   copiou, em vez de tentar publicar de novo.
4. No final, lê a mensagem na tela (a janela fica aberta esperando você
   apertar uma tecla) — ela mostra se achou ou não alguma entrada do
   Legion Space/Armoury Crate pra desativar. Se não achou nada e aqueles
   apps continuarem abrindo sozinhos, desativa manualmente em
   **Configurações > Aplicativos > Inicializar** (ou Gerenciador de
   Tarefas > aba Inicializar) — é o jeito seguro que funciona
   independente de qual mecanismo o fabricante usa.
5. Pra desfazer, clica duas vezes em `Desinstalar.bat`.

**Jeito manual (PowerShell direto, pra quem quiser passar alguma opção
extra como `-NoDesktopShortcut`, `-NoWatchdog` ou `-KeepFiles`)**: abre o PowerShell
**dentro da pasta** `installer\Arquivos\` (`cd` até lá — rodar de outra
pasta, como `C:\WINDOWS\System32`, só vai dar "arquivo não encontrado") e
roda:
```powershell
powershell -ExecutionPolicy Bypass -File .\Install-Launcher.ps1
```
ou, usando uma pasta `publish\` já existente:
```powershell
powershell -ExecutionPolicy Bypass -File .\Install-Launcher.ps1 -SkipPublish
```
e pra desinstalar:
```powershell
powershell -ExecutionPolicy Bypass -File .\Uninstall-Launcher.ps1
```

## ⚠️ Build ainda não totalmente verificado aqui

Este projeto foi escrito num sandbox Linux que bloqueia acesso a
`api.nuget.org` — não dá pra rodar `dotnet restore`/`build` aqui pra validar.

**🔴 Modo kiosk + instalador — status**: `Install-Launcher.ps1` toca em
registro do Windows e em pastas de inicialização automática, então é tudo
pensado pra ser reversível (o próprio `Uninstall-Launcher.ps1` desfaz, e
qualquer passo de registro/inicialização que falhar vira só um aviso no
log, não derruba a instalação do app em si). **Confirmado funcionando**
num PC Windows normal, de ponta a ponta, e também **no Legion Go 2 de
verdade** (publish/cópia/atalhos, modo kiosk fullscreen, registro como
app principal do Modo Xbox via OmniConsole — ver seção "Instalar no
Legion Go 2 / ROG Ally" mais acima pros detalhes e achados de cada um). Um
bug já apareceu e foi corrigido: o script procurava
`HandheldLauncher.App.exe` depois do publish, mas o `.csproj` nomeia o
executável só de `HandheldLauncher.exe` (`<AssemblyName>` diferente do
nome da pasta do projeto). **Já testados no Legion Go 2 de verdade, com
resultado misto**: a API key automática via
`installer\Arquivos\steamgriddb-api-key.txt` **confirmada funcionando**; o
`Topmost = true` + watchdog **confirmados funcionando no boot em modo
desktop normal** (janela fica na frente, reabre sozinha se fechar); no
boot pelo Modo Xbox, porém, a Legion Space continuou ganhando a frente da
nossa janela mesmo com `Topmost = true` — o "enforcer" que reaplica
`Topmost` a cada 2 segundos (ver bullet própria mais acima) foi escrito
pra tentar corrigir isso, mas **ainda não passou por esse teste depois de
escrito**. O bug do botão "Sair" da barra de baixo não desativar o
watchdog (só o do menu geral fazia isso) também foi corrigido nesta leva,
sem teste depois ainda.

**🔴 Reorganização da UI (barra de baixo/menu geral/engrenagem) + Minimizar
bandeja + Power — status**: tudo isso é novo nesta leva (coordenadas de
beta v0.2 passadas pelo usuário depois de testar v0.1 no Legion Go 2) e
**nenhuma parte passou por teste num build real ainda** — só revisão
manual de código, pela mesma limitação de sempre (sandbox sem
`dotnet build`). Pontos que merecem atenção no primeiro teste:
- O ícone de engrenagem agora chama `vm.ToggleGlobalMenu()` — confirmar
  que abre o mesmo menu que View/Back abre.
- Barra de baixo só com "Fechar: <jogo>" (condicional)/"Minimizar"/"Power".
- "Minimizar" esconde a janela (`Hide()`) sem fechar o processo — ícone
  novo aparece na bandeja do Windows; clicar nele ou usar "Abrir" no menu
  dele restaura a janela; "Sair" no menu da bandeja fecha de vez
  (desativando o watchdog). **Ctrl+Alt+H de qualquer lugar também
  restaura** — essa parte usa `RegisterHotKey`/`Win32Properties.
  AddWndProcHookCallback`, a peça de interop Win32 mais arriscada desta
  leva (assinatura confirmada contra o código-fonte real do Avalonia,
  mas isso não substitui testar de verdade).
- Importante: o "enforcer" de Topmost (bullet acima) agora checa
  `!IsVisible` antes de reafirmar o Topmost — sem esse cuidado, ele ia
  forçar a janela a reaparecer a cada 2 segundos mesmo depois de
  minimizada, quebrando o "Minimizar" inteiro. Revisado, mas sem teste
  real ainda pra confirmar que o `Hide()` realmente fica escondido com o
  enforcer rodando.
- Botão "Power" abre um menu com "Desligar tela"/"Reiniciar o Legion
  Go"/"Desligar o Legion Go" — sem confirmação antes de executar (ver
  "Ainda não feito" item 4). "Desligar tela" usa `PostMessage` com
  `WM_SYSCOMMAND`/`SC_MONITORPOWER`; os outros dois chamam
  `shutdown.exe /r` e `/s` diretamente.
- Ícone do app/bandeja gerado em `Assets/app-icon.ico` (256→16px, "HL"
  nas cores do tema) e referenciado tanto no `<ApplicationIcon>` do
  `.csproj` quanto no `Icon` da `MainWindow` e do `TrayIcon` — primeira
  vez que o projeto usa um ícone próprio em vez do genérico do .NET.

**🔴 Instância única + ajustes de bug na UI/Topmost + CURRENT LOGO/fonte —
status**: tudo nesta leva também, sem teste num build real ainda. Pontos
que merecem atenção no primeiro teste:
- `SingleInstance.cs` (novo arquivo, `Platform/`) cria um `Mutex` nomeado
  em `Program.Main`, ANTES de `BuildAvaloniaApp()` rodar. Se uma segunda
  instância perde a corrida pelo Mutex, ela chama
  `RegisterWindowMessage`/`PostMessage` (broadcast) com uma mensagem
  própria e sai imediatamente — nunca chega a montar uma `MainWindow`.
  A instância real escuta essa mensagem no MESMO hook de WndProc que já
  existia pro Ctrl+Alt+H (`OnNativeWndProcHook` em `MainWindow.axaml.cs`)
  e reage chamando `RestoreFromTray()`. Teste importante: abrir o
  launcher, abrir de novo (atalho/exe direto) e confirmar que NENHUMA
  segunda janela aparece e a primeira vem pra frente.
- O "enforcer" de Topmost perdeu o `Activate()` (ver bullet própria mais
  acima) — teste importante: confirmar que 1) Alt+Tab e a ferramenta de
  Captura/Recorte voltam a funcionar normalmente com o launcher em modo
  kiosk rodando, E 2) o Topmost ainda ganha a frente da Legion Space no
  Modo Xbox como antes (ou se enfraqueceu, o que seria esperado — ver
  risco anotado na bullet própria).
- `SteamGridDbClient.GetBestLogoFor...` é a primeira chamada deste
  projeto ao endpoint `/logos/` do SteamGridDB — mesma forma dos
  endpoints `/heroes/` já testados, mas nunca exercitada de verdade.
- "Minimizar" e "Power" agora também vivem dentro do menu geral (além dos
  botões da barra de baixo) — teste importante: confirmar que escolher
  "Power" ali dentro fecha o menu geral e abre o menu Power certinho
  (mesmo padrão que "Adicionar jogo" já usava pra abrir outro diálogo).

**🔴 "Escolher logo" + nome acima da capa + diagnóstico do bug do "Sair" —
status**: tudo nesta leva também, sem teste num build real ainda. Pontos
que merecem atenção no primeiro teste:
- **"Escolher logo"** é um 4º item novo no menu de contexto, reusando a
  mesma grade/picker de "Escolher capa"/"Escolher banner" (só muda o
  endpoint do SteamGridDB e o campo que recebe o resultado) — teste
  importante: abrir, confirmar que a grade carrega opções reais de logo
  (não vazia/erro), escolher uma e ver o "CURRENT LOGO" trocar na hora
  sobre a WIDE CAPSULE do último jogo aberto.
- **Nome do jogo movido pra cima da capa** (correção do usuário sobre a
  leva anterior, que tinha deixado por dentro) — teste importante:
  confirmar visualmente que o nome aparece ACIMA de cada capa/banner (não
  mais sobreposto), que títulos longos truncam com "..." em vez de vazar
  da tile, e que a fileira continua alinhada certinho (todas as tiles
  pequenas com a mesma largura, a do último jogo mais larga) — isso
  depende do novo `GameTileViewModel.TileWidth` bater exatamente com as
  larguras do `Theme.axaml` (170/460), senão o scroll
  (`ScrollGameRowSelectionIntoView`) pode desalinhar.
- **Bug do "Sair" ainda reabrindo o launcher — ainda NÃO corrigido, só
  diagnosticado**: o usuário reportou que mesmo depois de usar "Sair" de
  propósito, o launcher volta a abrir sozinho. `DisableWatchdogIfPresent()`
  (chamado toda vez que "Sair" é usado, pra desativar a tarefa agendada do
  watchdog antes de fechar) agora loga o exit code e stdout/stderr reais do
  `schtasks.exe` via `DebugLog` — antes essa chamada era 100% silenciosa,
  então não tinha como saber se 1) o `schtasks` estava de fato falhando
  aqui, ou 2) ele funciona certinho e é OUTRA coisa reabrindo o app depois.
  Suspeito mais provável pro caso 2: o Modo Xbox/OmniConsole (ver seção
  "Instalar no Legion Go 2 / ROG Ally" mais acima) registra o launcher como
  "app principal" dele por um mecanismo totalmente separado da nossa
  Scheduled Task — se for isso, desativar nosso watchdog não resolve nada,
  porque o culpado nem é o nosso código. **Preciso do log real** (arquivo
  de debug do app, ou a saída no terminal se rodar via `dotnet run`) depois
  de reproduzir o bug pra saber qual dos dois é — e também ajuda saber se
  o "Sair" reabrindo aconteceu no modo desktop normal ou já com o Modo
  Xbox/OmniConsole ativo, porque o caminho de correção é bem diferente
  pra cada caso.

**🔴 Banner do topo de volta + logo em 2 lugares + capas maiores + bateria
real — status**: a leva passada tinha removido o banner do topo
interpretando errado o pedido do usuário sobre o split HERO/WIDE CAPSULE.
O usuário corrigiu de novo nesta leva, com uma regra final e bem mais
explícita dessa vez (ver bullets "Banner HERO no topo" e "'CURRENT LOGO'
+ nome do jogo — regra final" na seção "Status atual" mais acima pros
detalhes completos), e aproveitou pra reportar o bug da bateria e pedir
pra aumentar o tamanho das capas. Tudo nesta leva, sem teste num build
real ainda. Pontos que merecem atenção no primeiro teste:
- **Banner hero no topo voltou** (`Border.heroBanner`, Height 320, sem
  blur) — teste importante: confirmar que aparece de novo seguindo o jogo
  focado, com a mensagem "Nenhum jogo encontrado..." aparecendo certinho
  numa biblioteca vazia (voltou pra dentro do banner, onde estava antes
  da remoção da leva passada).
- **Logo agora aparece em DOIS lugares, nunca dentro de uma capa**: no
  canto inferior esquerdo do banner HERO (dentro da mesma barra
  translúcida que já mostrava título/minutos jogados — cai pro título
  quando não tem logo ainda) E acima de QUALQUER capa da fileira (CAPSULE
  ou WIDE CAPSULE, no lugar do nome) — teste importante: confirmar que o
  logo aparece nos dois lugares quando existe, que cai pro nome/título
  direitinho quando não existe, e que o logo da WIDE CAPSULE renderiza
  visivelmente maior que o das CAPSULE normais.
- **CAPSULE/WIDE CAPSULE maiores** (190x255/510x255, eram 170x230/460x230)
  — teste importante: confirmar que a fileira continua alinhada e rolando
  certinho com os novos tamanhos (`ScrollGameRowSelectionIntoView` foi
  atualizado pros mesmos valores).
- **Bateria real na barra de cima** — teste importante: comparar o número
  mostrado com a bateria real do Legion Go 2/ROG Ally (e confirmar que
  continua trocando sozinho a cada ~30s, sem travar em um valor).
- **"Ícones muito em baixo"**: o usuário reportou isso olhando o build da
  leva passada, que tinha removido o banner e deixado a fileira grudada
  no fundo da janela com um espaço vazio enorme em cima dela. Não mexi em
  nada de posicionamento além de trazer o banner de volta — a hipótese é
  que isso já resolve sozinho (a fileira volta a ficar logo abaixo do
  banner, como era antes da remoção), mas vale confirmar no teste; se
  ainda estiver desproporcional, me avisa que ajusto de novo.

**Bug corrigido** (reportado pelo usuário): nos dois menus (Start e
View/Back), só o D-pad movia ↑↓ — o analógico esquerdo só lia o eixo X
(esquerda/direita) desde o início do projeto, nunca o eixo Y (cima/
baixo), então empurrar o analógico pra cima/baixo nunca gerava `MoveUp`/
`MoveDown`. `GamepadPoller.Poll()` agora lê os dois eixos do analógico —
**confirmado funcionando** pelo usuário (D-pad e analógico ambos navegam
certinho nos dois menus).
Os commits até o menu geral (View/Back) já foram testados e corrigidos a
partir dos erros reais que você mandou (banner hero, layout Steam Deck,
último-jogo-aberto, blur de fundo, menu de contexto com Start, diálogo
"Adicionar jogo", menu geral — tudo confirmado funcionando). O commit mais
recente (seletor de capa/banner) ainda não passou por esse ciclo — se der
erro de build, me manda a mensagem. Pontos mais arriscados dessa leva:

- O seletor baixa até 24 imagens, uma de cada vez, antes de abrir a grade
  — num jogo com muita cobertura no SteamGridDB e internet lenta, pode
  levar alguns segundos com a tela em "Buscando opções...". Isso é
  esperado, não é bug (mas se ficar preso nisso por muito tempo sem nunca
  terminar, aí sim é bug).
- A grade usa `Image` do Avalonia pra cada uma das até 24 opções, cada uma
  na resolução cheia (não tem redução de tamanho, ver seção do seletor
  abaixo) — se a abertura do seletor der uma travadinha visível antes de
  desenhar a grade, é esperado por hoje, mas me avisa se ficar incômodo
  que dá pra otimizar depois.
- `ArtworkService` ganhou uma pasta nova de cache,
  `%AppData%\HandheldLauncher\artwork\candidates\` — é só onde ficam as
  imagens baixadas pra mostrar na grade antes de você escolher. Pode
  apagar essa pasta à vontade se crescer demais; ela se recria sozinha na
  próxima vez que abrir o seletor.
- **Bugs corrigidos** (reportados pelo usuário, testando a leva anterior)
  — **confirmados funcionando** ("os dpads na seleção das capas e os
  banners e capas aplicando na hora"):
  - Aplicar uma capa escolhida às vezes não fazia efeito na primeira
    tentativa, só na segunda. Provável causa: eu sobrescrevia o MESMO
    arquivo que já estava na tela naquele instante (ex: `ARC_grid.jpg`),
    e se o Windows ainda tivesse esse arquivo "em uso" pra exibição, a
    sobrescrita falhava em silêncio. Agora cada capa/banner aplicado vira
    um arquivo com nome novo (inclui o id da imagem do SteamGridDB),
    nunca sobrescrevendo o que já está carregado. Arquivos antigos de
    escolhas anteriores ficam órfãos na mesma pasta de cache (mesma
    solução: pode apagar à vontade).
  - Dentro da grade do seletor, ↑↓ se comportava como ←→ (apertar baixo
    ia pro item da direita, não pro de baixo de verdade). A grade usava
    `WrapPanel`, cujo número de colunas por fileira depende do tamanho em
    pixels na hora de renderizar — e o ViewModel não tinha como saber
    esse número pra pular a fileira certa. Troquei pra `UniformGrid` com
    `Columns="5"` fixo (constante espelhada em `MainViewModel.
    CoverPickerColumns`), então ↑↓ agora pulam exatamente uma fileira
    (5 posições) de verdade.
  - Aplicar um banner novo atualizava a miniatura pequena do "último jogo
    aberto" na hora, mas o banner GRANDE em cima só atualizava depois de
    trocar de jogo e voltar. Causa: `Refresh()` reconstrói todos os
    `GameTileViewModel` do zero, mas se o índice selecionado numericamente
    não muda (comum — o jogo que você acabou de editar geralmente
    continua no índice 0), o setter de `SelectedIndex` não reatribui
    `SelectedTile`, que ficava apontando pro objeto antigo (já descartado,
    com a arte velha). Agora `Refresh()` força essa reatribuição sempre.
    Esse bug podia estar afetando qualquer `Refresh()` (inclusive "Buscar
    capas" em massa), não só o seletor — a correção vale pra todos.
- **Bug corrigido nesta leva** (reportado pelo usuário) — **ainda não
  testado num build real**:
  - Na grade do seletor, rolar pra baixo (ou pra cima) além das fileiras
    visíveis não acompanhava a seleção — a tela ficava parada na mesma
    posição e não mostrava as capas/banners que já tinham ficado "fora da
    tela". Causa: a grade é um `ItemsControl` comum com uma flag
    `IsSelected` feita à mão por item (não um `ListBox`/
    `SelectingItemsControl`), então não existe o "rolar até o item
    selecionado" automático que esses controles dariam de graça — tinha
    que ser calculado na mão. Adicionei `ScrollCoverPickerSelectionIntoView`
    em `MainWindow.axaml.cs`: depois de cada movimento (← → ↑ ↓), calcula
    a posição Y da fileira da seleção atual (`CoverPickerIndex /
    CoverPickerColumns`, cada fileira = 170px de altura da célula + 12px
    de margem = 182px) e ajusta `ScrollViewer.Offset` só quando a seleção
    saiu da área visível (`Viewport`) — pra cima, rola até o topo dela
    ficar visível; pra baixo, rola até o fundo dela ficar visível.
    Deliberadamente não usei `Control.BringIntoView()`/
    `ItemsControl.ContainerFromIndex` (APIs que eu não consigo validar
    aqui sem compilar) — preferi a conta na mão com `Offset`/`Viewport`,
    que são bem mais básicas e certas de existir.

**🔴 D-pad/analógico segurado + suavização de movimento + favoritos/filtros +
feedback de abertura — status**: pedido único do usuário em 4 partes
("Sim vamos por partes, primeiro o que gostaria e fazer...") depois de ver
fotos reais do launcher rodando bem no Legion Go 2. Tudo nesta leva, sem
teste num build real ainda. Pontos que merecem atenção no primeiro teste:
- **Bug corrigido: segurar o direcional/analógico não andava até o fim**
  — o usuário reportou isso direto no Legion Go 2 de verdade: "quando
  segurar o direcional pra os lados ele não vai até o final, ele fica
  parado. Só passa dando toque." `GamepadPoller` nunca tinha repetição
  nenhuma (só detectava a borda de subida do botão, igual A/B/Start) —
  agora cada direção (D-pad OU analógico, tratados como a mesma coisa) tem
  um contador de "quantos polls seguidos está segurado": dispara no
  primeiro toque, espera ~350ms (`RepeatInitialDelayTicks`), depois repete
  a cada ~120ms (`RepeatIntervalTicks`) enquanto continuar segurado. A/B/
  Start/Back NÃO repetem (de propósito — repetir "Confirmar" com A
  segurado reabriria o jogo em loop). Teste importante: segurar ← ou → por
  1-2 segundos e confirmar que anda sozinho até parar de segurar, sem
  travar depois do primeiro passo.
- **Suavização ao trocar de jogo na fileira** — pedido do usuário ("um
  sistema de suavização e uma espécie de efeito quando passar pro lado
  entre os jogos"). Dois efeitos, os dois 100% declarativos via
  `Transitions` do Avalonia (reage a QUALQUER troca de valor da
  propriedade, não só troca por estilo/classe): 1) o scroll da fileira
  (`GameRowScrollViewer.Offset`, já calculado à mão em
  `ScrollGameRowSelectionIntoView`) agora desliza em vez de pular, via
  `VectorTransition` de 220ms; 2) a tile que ganha foco faz um "zoom"
  suave (escala 1.0 → 1.04 via `RenderTransform`/
  `TransformOperationsTransition`, 150ms) — `RenderTransform` não conta
  pro layout, então o zoom não empurra as tiles vizinhas. Teste
  importante: navegar ← → e confirmar que os dois efeitos aparecem juntos
  e não travam/engasgam, principalmente durante o auto-repeat do item
  acima (é o cenário que gera mais trocas de valor em sequência).
- **Favoritos** — `GameEntry.IsFavorite`/`GameMetadataOverride.IsFavorite`
  novos, persistem como override (sobrevivem a um re-scan igual
  capa/coleção/tempo jogado já sobreviviam). Toggla pelo menu de contexto
  (item "Favoritar"/"Desfavoritar", ver "Status atual" mais acima) e
  acende uma ★ discreta no canto de cada tile favoritada na fileira
  inteira (não só na aba "Favoritos"). Teste importante: favoritar um
  jogo, confirmar que a estrela aparece, fechar e abrir o launcher de
  novo (ou dar "Atualizar Biblioteca") e confirmar que continua
  favoritado.
- **Abas de filtro (Todos/Favoritos/Steam/Não Steam/Xbox)** — ver bullet
  própria em "Status atual" mais acima pros detalhes. Teste importante
  além do básico (cada aba mostra só quem deveria): confirmar que
  "Atualizar Capas Automático" continua completando capa/banner/logo da
  biblioteca INTEIRA mesmo com uma aba diferente de "Todos" selecionada
  (esse era um bug fácil de introduzir sem querer — corrigido de
  propósito antes de qualquer teste, iterando `_library.Games` em vez da
  lista já filtrada).
- **Checkbox "É um jogo do Xbox app / Game Pass"** no diálogo "Adicionar
  jogo" — marca `GameSource.XboxApp` em vez de `GameSource.Manual` (só
  isso muda; o jogo continua abrindo pelo executável informado,
  `GameLauncher` só trata Steam como caso especial). Teste importante:
  adicionar um jogo manual com o checkbox marcado e confirmar que ele
  aparece na aba "Xbox" (e NÃO aparece em "Steam"/"Não Steam" errado —
  "Não Steam" é "qualquer coisa que não seja Steam", então um jogo Xbox
  deve aparecer tanto em "Xbox" quanto em "Não Steam", o que é esperado).
- **Feedback visual ao abrir um jogo** — pedido do usuário ("feedback
  visual pra já kkk"). Um overlay "Iniciando <jogo>..." com uma barra de
  progresso indeterminada (`ProgressBar IsIndeterminate`) aparece na hora
  que você confirma, e desaparece sozinho depois de 2.5s fixos
  (`MainViewModel.LaunchFeedbackMilliseconds`) — não tem como saber de
  verdade quando o processo externo terminou de abrir (`GameLauncher` não
  expõe isso e cada jogo demora um tempo diferente), então é só "tempo
  suficiente pra confirmar que o toque funcionou", não uma barra de
  progresso real do carregamento do jogo. Teste importante: confirmar que
  o overlay aparece, não "pisca" rápido demais pra ler, e some sozinho
  sem travar a tela depois.

**🔴 Capa larga independente (urgente) + ordem/layout/controle das abas +
ajustes visuais (hero/alinhamento/tamanho/transição) — status**: pedido em
lista curta depois do usuário testar o build corrigido no Legion Go 2 de
verdade. Tudo nesta leva, sem teste num build real ainda. Pontos que
merecem atenção no primeiro teste:
- **Capa larga independente (urgente) — corrige a WIDE CAPSULE esticada**:
  esse era o item mais importante. Ver bullet "WIDE CAPSULE" em "Status
  atual" mais acima pros detalhes técnicos. Teste importante: dar
  "Atualizar Capas Automático" (ou abrir "Escolher capa larga" no menu de
  contexto de um jogo) e confirmar que a WIDE CAPSULE mostra uma imagem
  landscape de verdade, não mais a capa portrait distorcida — e que trocar
  a capa normal ("Escolher capa") NÃO muda a capa larga, e vice-versa (são
  dois arquivos/overrides totalmente separados agora).
- **Bug corrigido de passagem, achado revisando o código pra essa capa
  larga**: `ArtworkService.GetOrCreateOverride` recriava o override do
  jogo campo a campo, e esquecia `IsFavorite` (da leva anterior) — ou
  seja, qualquer busca/aplicação de capa/banner/logo depois de favoritar
  um jogo apagava o favorito silenciosamente. Nunca chegou a ser
  reportado, mas corrigido antes de virar queixa. Teste importante:
  favoritar um jogo, dar "Atualizar Capas Automático" (ou escolher uma
  capa manualmente), e confirmar que a estrelinha continua lá depois.
- **Ordem das abas de filtro mudou**: "Favoritos, Todos, Steam, Xbox, Não
  Steam" (era "Todos, Favoritos, Steam, Não Steam, Xbox") — pedido
  explícito do usuário.
- **Layout das abas mudou de StackPanel pra Grid de 5 colunas iguais,
  ocupando a tela inteira** — pedido do usuário: "centralize... fica com
  todos espaçamentos iguais e ocupe do início ao fim da tela". Teste
  importante: confirmar visualmente que as 5 abas têm exatamente a mesma
  largura e que a fileira vai de uma ponta à outra da área de conteúdo
  (não só do tamanho do texto de cada botão).
- **Abas de filtro agora alcançáveis pelo controle**: LB/RB ciclam entre
  elas na mesma ordem visual (`GamepadAction.PreviousFilter`/
  `NextFilter`, implementado em `GamepadPoller`, sem repetição ao segurar
  — um toque, uma troca) — usuário reportou "não consigo acessar os
  filtros... pelo controle". Teclado Q/E como fallback de dev. Teste
  importante: segurar/apertar LB e RB fora de qualquer menu e confirmar
  que a aba ativa muda ciclicamente (incluindo dar a volta: da última
  pra primeira e vice-versa).
- **Hero mais alto**: `Height="320"` → `"380"` — pedido direto ("aumenta
  o eixo vertical do hero deixe um pouco maior").
- **Nome/logo das tiles alinhados à esquerda**: eram centralizados,
  agora começam na borda esquerda da capa (mesma largura da capa, então
  alinhar a caption à esquerda já alinha com o início da arte) — pedido:
  "alinhe os ícones de nome dos jogos a esquerda iniciando no começo da
  arte do jogo".
- **Capas maiores de novo + zoom de foco mais forte**: CAPSULE 190x255 →
  220x290; WIDE CAPSULE 510x255 → 620x290 (proporção ajustada mais perto
  do formato landscape real da nova capa larga independente, ~2.14:1 em
  vez de 2.0:1); escala do "zoom" de foco 1.04 → 1.08; duração da
  transição de foco 0.15s → 0.18s; duração do scroll suave da fileira
  0.22s → 0.28s. Tudo isso em resposta a "aumente mais o tamanho de
  todos as capas... nao notei a transição entre os ícones" — como o
  build anterior tinha quebrado no `AVLN2000` (ver entrada abaixo), é
  possível que o usuário só tenha visto a transição funcionando de fato
  agora, então vale conferir se o efeito mais forte já é suficiente antes
  de aumentar ainda mais.

**🔴 Erro de build AVLN2000 no Theme.axaml — corrigido**: reportado pelo
usuário direto do `dotnet publish` real no Windows — `Style.Transitions`
não existe (Transitions é propriedade do CONTROLE, não do `Style` em si);
trocado pra um `Setter Property="Transitions"` normal. Esse foi o
primeiro erro de build REAL que apareceu neste projeto (todos os outros
"nunca testado" até aqui eram só falta de teste, não erro de compilação)
— bom sinal de que o fluxo de build/instalação em si está funcionando,
só esse Setter que estava errado.

**🔴 Ajuste fino pós-foto real (tile larga sobrepondo, espaçamento,
excluir atalho/jogo, pasta `installer/`, editor de pastas Xbox) —
status**: seis pedidos a partir de uma foto nova do Legion Go 2 de
verdade —
- **WIDE CAPSULE "pegando o ícone do lado"**: `Border.gameTile.lastPlayed`
  Width 620 → 580, `StackPanel Spacing` da fileira 14 → 18 (`MainWindow.axaml`
  + `GameTileViewModel.TileWidth` + `MainWindow.axaml.cs`'s
  `ScrollGameRowSelectionIntoView`, os três têm que bater exatos). **Nunca
  testado num build real.**
- **Espaçamento hero→abas / abas→fileira**: os dois já eram
  `Margin="...,16"` (iguais), mas o usuário reportou desalinhamento —
  igualados pra `20` nos dois pra garantir visualmente, já que não dá pra
  renderizar aqui pra comparar o antes/depois de verdade. **Se ainda
  não for exatamente isso que estava incomodando, precisa de mais detalhe
  (foto com seta apontando o que exatamente está torto, por exemplo).**
- **Espaço vazio embaixo da fileira de capas**: a `Grid` externa
  (`RowDefinitions="Auto,Auto,*"`) dava TODO o espaço sobrando pro
  `ScrollViewer` da fileira, e o `ItemsControl` de dentro, sem alinhamento
  vertical próprio, ficava ancorado no topo desse espaço — sobrava tudo
  embaixo. `VerticalAlignment="Center"` no `ItemsControl` resolve (o
  espaço sobrando se divide em cima/embaixo em vez de se acumular todo
  embaixo). **Nunca testado num build real.**
- **"Excluir atalho"/"Excluir jogo" no menu de contexto** (pedido
  explícito: "apertando start em cima do ícone"): o menu de contexto, que
  até aqui era uma lista FIXA de 6 itens com despacho por índice (já tinha
  precisado reindexar à mão 3 vezes nesta sessão), virou reconstruído do
  zero a cada abertura (`MainViewModel.BuildContextMenu`, mesmo padrão do
  menu geral/Power) — ver bullet própria em "Status atual" mais acima pra
  detalhes de Steam vs. não-Steam vs. escaneado automaticamente, e da
  confirmação de 2 opções antes de executar. **Decisão tomada sem
  perguntar**: pra jogos Steam, "Excluir jogo" abre
  `steam://uninstall/{appid}` e deixa a Steam cuidar de tudo — o launcher
  nunca apaga arquivo nenhum diretamente, em nenhum caso (nem Steam, nem
  manual/atalho). Apagar arquivos de instalação de verdade continua fora
  do escopo (ver "Ainda não feito", item 7). **Nunca testado num build
  real.**
- **Pasta `installer/` reorganizada**: raiz agora só tem
  `Instalar.bat`/`Desinstalar.bat`; tudo o resto (`Install-Launcher.ps1`,
  `Uninstall-Launcher.ps1`, `steamgriddb-api-key.txt`, `publish/` quando
  existir) foi movido pra uma subpasta nova `Arquivos/` — ver seção
  "Instalar no Legion Go 2 / ROG Ally" pra estrutura completa. Os `.bat`
  foram atualizados pra chamar `Arquivos\...ps1`, e
  `Install-Launcher.ps1` teve seu cálculo de `$repoRoot` ajustado pra
  subir mais um nível (`Arquivos` → `installer` → raiz do repo) — **essa
  é a parte mais arriscada desta leva inteira, porque não dá pra testar
  um `.ps1`/`.bat` aqui, só ler com calma**; revisado linha por linha mas
  é a primeira coisa a conferir se a próxima instalação falhar.
- **"Editor de onde procurar" jogos Xbox** (`AppSettings.XboxScanFolders`
  + `XboxFolderScanner` novo, ver bullet própria em "Status atual" mais
  acima): heurística de pasta (maior `.exe` de cada subpasta imediata),
  não um scanner real de UWP/Xbox. **Decisão tomada sem perguntar**: é um
  CAMPO no `settings.json` pra editar na mão, não uma tela de
  configuração dentro do app — se o pedido era por um editor visual de
  verdade, avisar que isso ainda falta. **Nunca testado num build real.**

**🔴 Regressão real no espaço vazio embaixo da fileira + gerenciador de
downloads + overlay de FPS (RTSS) — status**: depois de entregar o ajuste
fino acima, o usuário testou no Legion Go 2 de verdade e reportou uma
regressão (não só "ainda não testado") e pediu duas funções novas maiores.
- **Regressão: `VerticalAlignment="Center"` piorou o corte de baixo** — o
  fix anterior pro "espaço vazio embaixo" ("agora baixou demais e tá
  cortando a parte de baixo kkkk" — real, no Legion Go 2, não só
  cosmético). Revertido: `ItemsControl` da fileira voltou a não ter
  `VerticalAlignment` explícito (alinhamento padrão no topo), com
  comentário no código deixando claro que ajuste fino de verdade precisa
  de dados reais de resolução do aparelho, não mais "chute no escuro" —
  **ainda sem solução definitiva pro espaço vazio em si**, só a regressão
  foi desfeita. Durante a reversão, um `Edit` deixou a tag
  `<ItemsControl ...` sem o `>` de fechamento por um instante — pego pela
  verificação de XML bem-formado de sempre (`xml.etree.ElementTree.parse`),
  corrigido antes de qualquer entrega. **Nunca testado num build real
  depois da reversão.**
- **Gerenciador de downloads** (pedido: "mais pra olhar os jogos que estão
  sendo baixado nos outros launcher... mas se der pra ver os jogos que tem
  na conta seria legal também") e **overlay de FPS tipo MangoHud via RTSS**
  (pedido: "sem usar aquele nativo do legion") — ver "Status atual" acima
  pros dois recursos e "Ainda não feito" itens 11-15 pras decisões de
  escopo tomadas sem perguntar de volta (Epic/Xbox fora do progresso de
  download e fora de "na conta não instalado"; lista do painel de
  Downloads é mouse/touch-only; RTSS só detecta/abre, não instala).
  **Nenhuma das duas partes testada num build real ainda** — nem a
  leitura do `.acf` em downloads de verdade, nem a Steam Web API, nem a
  detecção/auto-start do RTSS.
- Xbox ainda não detectou nada automaticamente de verdade, nem depois do
  `XboxScanFolders` da leva anterior — o campo exige configurar as pastas
  na mão (ver "Status atual" e a seção "Configurar a Steam Web API" não se
  aplica aqui, é só o `XboxScanFolders` mesmo); o usuário informou
  `C:\Xbox` e `D:\Xbox` como pastas prováveis, mas sem certeza da raiz
  exata — avisado que isso precisa ser configurado manualmente e
  verificado.

**🔴 Ajustes urgentes pós-teste real (regressão do Topmost, reorganização
de menus, dedup de jogo Xbox, RTSS automático) — status**: usuário testou
no PC/Legion Go 2/ROG Ally e reportou uma regressão real nova, mais um
lote de pedidos de organização de UI —
- **Regressão real: janela "abrindo"/pulando pra frente o tempo todo no
  modo desktop** — o "enforcer" de `Topmost` (criado numa leva anterior
  pra brigar com a Legion Space pelo topo no boot do Modo Xbox) não tinha
  prazo, rodava pra sempre. Em "Modo Xbox" isso nunca incomoda (nada mais
  compete pelo topo), mas no desktop normal a janela ficava pulando pra
  cima de outras janelas a cada 2s indefinidamente — exatamente o que o
  usuário reportou. Corrigido: agora só roda por 2 minutos a partir da
  abertura da janela, tempo de sobra pra vencer a disputa de boot sem
  incomodar depois disso. **Importante**: isso NÃO era o overlay do RTSS
  (`EnsureRunning()` só roda UMA VEZ no início, nunca de novo) — o
  usuário suspeitou do RTSS, mas o código confirma que o culpado era o
  enforcer do Topmost mesmo. **Nunca testado num build real.**
- **Reorganização de menus** (pedido: "muito desorganizado, vamos por
  partes") — menu de contexto reordenado (Abrir jogo → Favoritar →
  Excluir → Personalizar) com as 4 edições de arte agrupadas no novo
  submenu "Personalizar"; botões Power renomeados pra não citar "Legion
  Go" especificamente (o projeto também roda no ROG Ally); engrenagem
  movida do canto superior pro inferior direito, junto de um Minimizar
  agora em ícone, na ordem Minimizar → Engrenagem → Power; ícone do app
  adicionado antes do nome na barra de cima. Ver "Status atual" pros
  detalhes de cada um. **Nenhuma parte testada num build real.**
- **Jogo Xbox duplicado (não-Steam + Xbox) — bug real corrigido**:
  causado por um jogo manual antigo + a mesma instalação agora também
  sendo achada pelo `XboxFolderScanner` — ver "Status atual" pro
  mecanismo de dedup por caminho de executável e o efeito colateral
  (continua classificado como "Não Steam" até apagar o atalho manual).
  **Nunca testado num build real.**
- **RTSS via winget no instalador** (pedido: "não tem como por no bat
  pra executar a instalação também desse app?") — confirmado por busca
  na web que existe um pacote winget `Guru3D.RTSS`, usado no passo 8 do
  `Install-Launcher.ps1`, mesmo padrão "melhor esforço" já usado pro SDK
  do .NET. **O id do pacote nunca foi testado rodando de verdade** (esse
  sandbox não tem `winget`) — se a instalação falhar com esse id
  especificamente, me manda a mensagem exata do log.
- **Chave da Steam Web API**: o usuário informou o valor direto no chat —
  por segurança não fica registrado aqui no repositório (só vive no
  `settings.json` da própria máquina dele). O valor que ele colou não
  parecia ter o formato de uma Steam Web API key de verdade (normalmente
  32 caracteres hexadecimais, gerada em steamcommunity.com/dev/apikey) —
  vale confirmar se foi copiada certa antes de colar no `settings.json`,
  e lembrar que `SteamId64` também
  precisa estar preenchido pra seção "na sua conta, não instalado"
  funcionar.
- **Tela de login da Steam dentro do launcher** — pedido como ideia pra
  futuro, não pra agora; fica anotado aqui, sem desenho ainda.

**🔴 Tela de Configurações + RTSS sem FPS na tela — status**: usuário
reportou que o RTSS instalou certinho (via winget, leva anterior) mas o
jogo não mostrou o FPS — esperava algo pré-configurado igual o MangoHud —
e, revendo a confusão da key da Steam, pediu uma tela de configuração de
verdade pra colar as chaves em vez de editar `settings.json` na mão
("seria possível criar uma tela de configuração pelo próprio Launcher
dessa chave? Acho que ficaria mais fácil").
- **Tela de Configurações criada** — item "Configurações" do menu geral
  abre uma `SettingsWindow` própria (mesmo padrão do diálogo "Adicionar
  jogo": `AddGameWindow.axaml` já deixava um comentário reservando esse
  design pra "eventual tela de configurações", então foi esse o caminho
  seguido em vez de um painel-overlay da janela principal) com campos
  pra SteamGridDB key, Steam Web API key, SteamID64 e pastas extras do
  Xbox app — ver "Tela de Configurações" mais abaixo pros detalhes e o
  aviso de "precisa reiniciar o launcher pra aplicar". **Nunca testado
  num build real.**
- **RTSS instalado ≠ FPS aparecendo na tela** — isso é esperado, não é
  bug: diferente do MangoHud, o RTSS não vem com o On-Screen Display
  pré-ligado nem nenhum jogo pré-configurado. É preciso abrir o RTSS e
  ligar "Show On-Screen Display" +, no perfil do jogo (ou no
  `<Global>`), "On-Screen Display support" e "Show own statistics" (esse
  último é o que mostra o FPS do próprio RTSS, sem precisar do MSI
  Afterburner) — ver "Configurar o RTSS pra mostrar o FPS na tela" mais
  abaixo pro passo a passo completo. Isso continua manual, dentro do
  próprio RTSS — não automatizei essa parte (só a instalação).

**🔴 Leva 1 do roteiro planejado: abas reorganizadas + atalho de Downloads
— status**: depois do primeiro teste real confirmando a biblioteca,
Downloads e RTSS funcionando de ponta a ponta, o usuário pediu mais
ajustes (overlay mais completo, abas divididas, atalho de download na
barra, temas) e sugeriu planejar antes de codar tudo — ver "Roteiro
planejado" lá no topo do arquivo pra ordem combinada. Esta leva ataca os
dois primeiros itens (menor risco):
- **Abas reorganizadas** — Favoritos/Todos/Oficiais/Não Steam/Não
  instalados, "Oficiais" juntando Steam+Xbox — ver "Status atual" pro
  bullet completo.
- **Aba "Não instalados" nova** — jogos da conta Steam ainda não
  instalados aparecem como tiles de verdade na biblioteca (não só na
  lista do painel de Downloads), com badge "Instalar" e capa buscada sob
  demanda — ver "Status atual" pro bullet completo.
- **Atalho de Downloads na barra de cima** — ícone (⇩) perto da bateria,
  com pontinho quando há download ativo — ver "Status atual".
- **Nada disso foi testado num build real ainda.** A aba "Não instalados"
  em especial mexe num caminho novo (GameEntry sintética, nunca
  persistida, reaproveitando o esquema de Id `steam_{appid}`) — vale
  prestar atenção especial se a capa aparecer certo e se "Instalar"
  realmente abre a Steam.

## Seletor de capas tipo Decky Loader — é viável?

Sim, e a **fase 1 (capas estáticas) já está implementada** — ver "Status
atual" acima. Resumo do que mudou:

- `ISteamGridDbClient` ganhou `GetGridCandidatesFor...`/`GetHeroCandidatesFor...`
  (steam app id e título) devolvendo a lista inteira de candidatas em vez
  de só a melhor — a API já devolvia várias por chamada, o código antigo
  simplesmente descartava tudo que não era a de maior `score`.
  `GetBestGridForSteamAppAsync` etc. continuam existindo (usados pelo
  "Buscar capas" em massa da barra de baixo), só que agora por trás são a
  primeira posição dessa mesma lista.
- Toda chamada (lista ou "melhor") já pede `types=static` pra API —
  SteamGridDB filtra as animadas (`types=animated`, formato GIF/webm) do
  nosso lado do servidor, então a gente nunca nem baixa uma imagem que o
  `Image` do Avalonia não ia conseguir mostrar de qualquer forma.
- `IArtworkService` ganhou `GetGridCandidatesAsync`/`GetHeroCandidatesAsync`
  (baixa até 24 candidatas pra arquivos locais, cacheadas por id — reabrir
  o seletor pro mesmo jogo não baixa de novo o que já tem) e
  `ApplyGridCandidateAsync`/`ApplyHeroCandidateAsync` (torna uma
  candidata escolhida a capa/banner oficial do jogo — só copia o arquivo
  já baixado, sem precisar baixar de novo).
- UI: grade de miniaturas (`WrapPanel` dentro de um `ItemsControl`) aberta
  pelo menu de contexto do Start ("Escolher capa"/"Escolher banner"), ←
  → ↑ ↓ navega, A/Enter aplica, B/Esc cancela.

**Fase 2 (capas animadas) continua não implementada.** O ponto trabalhoso
de verdade é só esse: o `Image` do Avalonia não anima GIF/WEBM sozinho,
então precisaria converter pra um formato que ele aceite ou usar um
player de vídeo/GIF — não tem opção zero-dependência pronta tão simples
quanto o resto do projeto, então isso provavelmente vai exigir o primeiro
pacote NuGet do projeto, quebrando a filosofia "zero dependência" seguida
até aqui. Sem compromisso ainda de quando (ou se) isso entra — ver "Ainda
não feito" acima.

## Como buildar (Windows, com internet normal)

Pré-requisito: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
instalado.

```powershell
cd handheld-launcher
dotnet restore
dotnet run --project src/HandheldLauncher.App
```

## Estrutura

```
HandheldLauncher.sln
src/
  HandheldLauncher.Core/     # modelos, scanner Steam, storage, launcher de processo, SteamGridDB
  HandheldLauncher.App/      # UI Avalonia (ViewModels, Views, Styles, Input/XInput)
installer/
  Instalar.bat / Desinstalar.bat   # os dois cliques-duplos
  Arquivos/                        # Install-Launcher.ps1 / Uninstall-Launcher.ps1 / API key / publish (ver "Instalar no Legion Go 2 / ROG Ally")
docs/                        # (vazio por enquanto)
```
