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
    (hoje só guarda a API key do SteamGridDB — não tem tela de
    configurações ainda, é editar o arquivo na mão por enquanto).
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
    "Favoritos", "Todos", "Steam", "Xbox", "Não Steam" (ordem pedida pelo
    usuário nesta leva: "fav, todos/steam/xbox/nao steam" — mudou de
    "Todos, Favoritos, Steam, Não Steam, Xbox"). Clicar numa aba não
    re-escaneia nada, só filtra a lista já carregada na hora
    (`MainViewModel.SetFilter`/`RebuildGamesFromLibrary`). Layout virou
    um `Grid` de 5 colunas IGUAIS ocupando a largura inteira (era um
    `StackPanel` colado à esquerda) — pedido do usuário: "centralize...
    fica com todos espaçamentos iguais e ocupe do início ao fim da
    tela". **Acesso por controle (novo nesta leva)**: LB/RB ciclam entre
    as abas nessa mesma ordem visual (`GamepadAction.PreviousFilter`/
    `NextFilter`, teclado Q/E como fallback de dev) — antes só dava pra
    clicar/tocar, e o usuário reportou não conseguir alcançar pelo
    controle. "Xbox" filtra por `GameSource.XboxApp`, que agora tem DUAS
    formas de preencher: marcar o checkbox "É um jogo do Xbox app / Game
    Pass" no diálogo "Adicionar jogo" (manual, continua igual), **ou,
    novo nesta leva, configurar pastas em `AppSettings.XboxScanFolders`**
    (ver bullet própria mais abaixo) — a detecção automática de
    verdade continua não existindo (precisaria de APIs de UWP que este
    projeto não referencia), isso é um atalho de pasta, não um scanner
    real do Xbox app. **Importante**: "Atualizar Capas Automático"
    continua sempre olhando a biblioteca INTEIRA, nunca só a aba ativa —
    corrigido de propósito pra não virar um bug silencioso. **Nunca
    testado num build real.**
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
  - **Ícone de engrenagem (⚙) no topo** — antes um placeholder
    desabilitado; agora abre o **mesmo** menu geral do botão View/Back
    (`OnGearMenuClick` só chama `vm.ToggleGlobalMenu()`, não é um menu
    separado) — assim mouse/touch chega exatamente nas mesmas opções que
    o controle, sem duplicar nada. Ainda não é uma "tela de
    configurações" de verdade (isso continua pendente, ver "Ainda não
    feito" abaixo).
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
4. **Confirmação antes de Reiniciar/Desligar o Legion Go** — o botão
   "Power" (ver "Status atual" acima) executa `shutdown /r` ou `/s` na
   hora, sem perguntar "tem certeza?" antes. Fica como decisão pendente
   se vale a pena adicionar uma etapa de confirmação (um segundo
   clique/tela antes de executar) pra evitar perder progresso de jogo
   por um toque errado.
5. Tela de configurações de verdade (o ícone de engrenagem agora abre o
   menu geral — ver "Status atual" — mas isso não é configurações, é só
   um menu de ações; editar `settings.json` na mão continua sendo o
   único jeito de mudar algo configurável)
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
9. **Confirmação de "Excluir atalho"/"Excluir jogo" é só um menu de
   2 opções** (Cancelar/Sim) reaproveitando o mesmo overlay do menu de
   contexto — funcional, mas bem minimalista; se o usuário preferir algo
   mais chamativo (cor vermelha, ícone de aviso, etc.) é só pedir.
10. **"Editor de onde procurar" jogos Xbox (`AppSettings.XboxScanFolders`,
    feito nesta leva) continua sendo hand-edit do `settings.json`, SEM
    tela de configuração** — nenhuma UI foi criada pra adicionar pastas
    visualmente (consistente com a API key do SteamGridDB, que também é
    hand-edit). Se o usuário queria um editor DE VERDADE dentro do app
    (não só um campo pra editar no JSON), isso ainda está pendente —
    avisar se for o caso.
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
14. **Overlay de FPS sem o RTSS instalado** — o launcher só detecta/abre o
    RTSS se ele já estiver instalado na máquina (ver "Status atual"); não
    baixa nem instala o RTSS sozinho, só abre a página de download no
    navegador se faltar. Instalar o RTSS de fato continua manual.
15. **Tela de configuração pra `SteamWebApiKey`/`SteamId64`** — mesma
    situação do item 10, hand-edit do `settings.json` (ver seção própria
    "Configurar a Steam Web API" mais abaixo), sem pré-configuração via
    arquivo como a API key do SteamGridDB tem.

## Configurar a API key do SteamGridDB

**Na mão, pra uma máquina só:**

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
3. Roda o launcher uma vez (cria o `settings.json` se ainda não existir) e
   edita `%AppData%\HandheldLauncher\settings.json`, adicionando os dois
   campos:
   ```json
   { "SteamWebApiKey": "sua-chave-aqui", "SteamId64": "seu-steamid64-aqui" }
   ```
4. Reabre o launcher e abre o painel de Downloads (item "Downloads" no
   menu geral) — a segunda seção deve trocar da mensagem "configure..."
   pra lista de verdade (ou erro de rede, se a chave/ID estiver errado).

Sem tela de configuração pra isso ainda (mesmo padrão hand-edit da key do
SteamGridDB e do `XboxScanFolders`) — nem pré-configuração automática via
arquivo (como o `steamgriddb-api-key.txt` do instalador faz pra API key do
SteamGridDB); se isso for repetir em várias máquinas, é editar o
`settings.json` na mão em cada uma por enquanto.

Sua biblioteca Steam precisa estar com o perfil público (ou pelo menos
"detalhes do jogo" públicos) pra `GetOwnedGames` conseguir ler — se a
lista voltar vazia mesmo com jogos na conta, isso é a primeira coisa a
checar em https://steamcommunity.com/my/edit/settings.

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
