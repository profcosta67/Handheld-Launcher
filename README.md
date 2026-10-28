# Handheld Launcher

**Status: Beta v0.1** — primeira versão com todo o essencial fechado (biblioteca,
navegação por controle, seletor de capas/banners, modo kiosk, instalador). Ver
"Status atual" e "⚠️ Build ainda não totalmente verificado aqui" abaixo pra saber
exatamente o que já foi confirmado funcionando no Legion/ROG Ally de verdade e o
que ainda está esperando o primeiro teste real.

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
  - `SteamGridDbClient` / `ArtworkService` — busca a melhor capa ("grid")
    **e o banner largo ("hero")** no SteamGridDB (por AppId pros jogos
    Steam, por nome pros manuais), baixa e cacheia em
    `%AppData%\HandheldLauncher\artwork\`, persiste os dois caminhos como
    override.
  - `AppSettingsStore` — lê/cria `%AppData%\HandheldLauncher\settings.json`
    (hoje só guarda a API key do SteamGridDB — não tem tela de
    configurações ainda, é editar o arquivo na mão por enquanto).
- **`HandheldLauncher.App`** — UI em Avalonia:
  - Janela com banner hero largo/deitado em cima (segue o jogo que você
    está navegando no momento, usando o banner do SteamGridDB) + fileira
    horizontal de capas embaixo — layout estilo Steam Deck. Fundo da janela
    inteira preenchido com a arte do jogo em foco, desfocada (blur) e
    esticada — testado, funcionando.
  - Na fileira, o **último jogo aberto** (não o que está só focado/com o
    cursor em cima — o que foi realmente *lançado* por último) aparece
    largo/deitado (460x230) usando a arte hero, e só troca de lugar/tamanho
    quando você abre outro jogo de verdade; navegar com ← → só muda o
    banner de cima, não mexe na fileira. Ao abrir um jogo ele vira o
    primeiro da fileira (empurra o resto). Testado, funcionando.
  - Navegação por **controle via XInput** (dpad/analógico esquerdo, A
    confirma, B fecha o jogo) — testado e funcionando com um controle Xbox.
    Teclado (← → Enter Esc) continua funcionando como fallback de dev.
  - **Menu de contexto (Start no controle / Espaço no teclado)** sobre o
    jogo focado: "Escolher capa", "Escolher banner" e "Abrir jogo". ↑↓
    navega entre as opções, A/Enter confirma, B/Esc/Start fecha sem fazer
    nada. "Abrir jogo" já era testado e continua igual; os dois primeiros
    são a novidade desta leva — **ainda não testados num build real.**
  - **Seletor de capa/banner** ("Escolher capa"/"Escolher banner" acima):
    abre uma grade com até 24 opções estáticas vindas do SteamGridDB
    (ordenadas por pontuação, sem NSFW/humor, sem animadas — ver seção
    própria mais abaixo) pra aquele jogo específico. ← → navegam dentro
    da fileira, ↑ ↓ pulam uma fileira inteira (grade de verdade, 5
    colunas), A/Enter aplica a opção destacada na hora (capa ou banner do
    jogo, conforme qual dos dois você abriu), B/Esc cancela sem mudar
    nada. Enquanto carrega mostra "Buscando opções..."; se não achar
    nenhuma, mostra "Nenhuma opção estática encontrada". Substitui o
    antigo botão único "Buscar capa no SteamGridDB" (que só re-buscava a
    imagem de maior pontuação, sem escolha) — esse foi removido, o
    seletor faz tudo que ele fazia e mais. **Confirmado funcionando**
    pelo usuário (abre, carrega candidatas reais de capa E banner, aplica
    a capa na hora, D-pad/analógico navegando certinho por fileira, banner
    grande de cima atualizando na hora). **Pendente de teste**: a grade
    ainda não acompanha a seleção com scroll automático quando ela desce
    (ou sobe) além da área visível — corrigido nesta leva, ver "Build
    ainda não totalmente verificado".
  - **Botão "Adicionar jogo"** na barra de baixo abre um diálogo (nome,
    caminho do executável com seletor de arquivo, argumentos opcionais,
    pasta de trabalho opcional) e cadastra um jogo não-Steam na
    biblioteca. Testado, abre certinho.
  - **Menu geral (View/Back no controle / Tab no teclado)**: mesmo padrão
    visual do menu de contexto, mas com as ações da barra de baixo —
    "Fechar: <jogo>" (só com jogo rodando), "Buscar capas" (só com API
    key configurada), "Adicionar jogo", "Atualizar" e "Sair". Existe
    porque a barra de baixo em si não é alcançável pelo controle (só
    mouse) — isso é o que resolve. **Ainda não testado num build real.**
  - Botão "Fechar: <jogo>" (só aparece com jogo rodando), "Buscar capas"
    (dispara o fetch do SteamGridDB pros jogos sem capa), "Atualizar"
    (reescaneia) e "Sair" — na barra de baixo, hoje só clicáveis com
    mouse; o menu geral acima é o caminho via controle/teclado.
  - Ícone de engrenagem no topo — **só placeholder visual por enquanto**,
    desabilitado. Ainda não decidimos o escopo da tela de configurações
    (combinado: fica pra depois).
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
    cobrir a nossa sem querer).
  - **Reabrir sozinho se for fechado**: achado no Legion Go 2 — fechar o
    app (Alt+F4 ou crash) deixava ele fechado de vez, sem reabrir. Agora
    o instalador registra uma **Tarefa Agendada do Windows**
    (`HandheldLauncherWatchdog`) que checa a cada 1 minuto e reabre o app
    se não estiver rodando — sobrevive a Alt+F4 e a crash. Pra isso não
    virar uma prisão, o botão/opção **"Sair" do menu geral do launcher
    desativa esse watchdog antes de fechar** (`MainWindow.axaml.cs`), então
    um "Sair" deliberado continua fechando de vez; só o Alt+F4/crash é que
    volta sozinho. Quem não quiser esse comportamento usa a flag
    `-NoWatchdog` do instalador (ver seção própria mais abaixo) pra voltar
    ao atalho simples antigo, que não reabre.
- **`installer/`** — `Install-Launcher.ps1`/`Uninstall-Launcher.ps1` (e os
  `Instalar.bat`/`Desinstalar.bat` de duplo-clique por cima deles), ver
  seção "Instalar no Legion Go 2 / ROG Ally" mais abaixo. Os `.ps1`
  **confirmados funcionando** num PC Windows normal, de ponta a ponta, e o
  core (publish/cópia/kiosk/Modo Xbox) **confirmado no Legion Go 2 de
  verdade** também; o watchdog e a API key padrão são mudanças desta leva,
  **ainda não testados** num build real.

Ainda **não** feito:

1. Tela de configurações de verdade (hoje é só editar `settings.json` na mão)
2. Capas **animadas** (GIF/WEBM) no seletor — fase 2 do seletor de capas,
   ver seção própria mais abaixo (fase 1, estático, já está feita e
   confirmada funcionando)
3. **Simplificar/automatizar mais a instalação** — testando no Legion Go 2
   de verdade apareceu bastante coisa manual (instalar o SDK do .NET,
   configurar o OmniConsole, desligar Legion Space/Xbox/Steam duplicados
   em Configurações > Apps > Inicializar). O instalador já automatiza o
   SDK sozinho agora (pergunta e instala via `winget` se faltar — ver
   "Instalar no Legion Go 2 / ROG Ally"), e a API key do SteamGridDB já
   pode vir pré-configurada (ver seção própria abaixo), mas falta pensar
   num jeito de reduzir o resto também — ideia pra próxima leva, ainda sem
   desenho definido.
4. **Decoupling do controle do Legion Go da Legion Space** — achado
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
cria um arquivo de texto `installer\steamgriddb-api-key.txt` com só a sua
chave dentro (sem aspas, sem mais nada):
```powershell
"sua-chave-aqui" | Set-Content installer\steamgriddb-api-key.txt
```
Esse arquivo é só seu, fica de fora do git (`.gitignore`) — a chave nunca
é commitada no repositório. A partir daí, toda vez que `Install-Launcher.ps1`
rodar numa máquina onde esse arquivo está copiado junto (dentro da pasta
`installer\`) **e** ainda não existe uma key salva naquela máquina, ele usa
automaticamente essa chave pra preencher o `settings.json` — sem precisar
editar nada na mão depois de instalar. Se a máquina já tinha uma key salva
de antes (de uma instalação anterior, por exemplo), ela é preservada e o
arquivo padrão é ignorado nesse caso.

Sobre compartilhar a mesma key entre pessoas/máquinas: tecnicamente
funciona (não existe trava por máquina), mas a SteamGridDB tem limite de
requisições por key — quanto mais instalações usando a mesma chave ao
mesmo tempo, mais perto desse limite. Pra uso pessoal em algumas máquinas
suas (PC + Legion Go 2 + futuramente o ROG Ally) não deve ser problema;
pra distribuir pra terceiros, o ideal é cada um gerar a própria key grátis.

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
`installer\steamgriddb-api-key.txt` (ver seção própria acima), e tenta
impedir o Xbox Game Bar e o Legion Space/Armoury Crate SE de abrirem
sozinhos no boot (os dois primeiros via chaves de registro bem
documentadas; os do fabricante via uma busca "melhor esforço" — ver
comentários do script pras limitações disso, e o aviso abaixo sobre o
Modo Xbox, que é o motivo real de essa busca não achar nada no Legion
Go 2).

**Jeito fácil (duplo-clique, sem digitar comando nenhum):**

Dentro de `installer\`, tem dois arquivos `.bat`:

- **`Instalar.bat`** — clica duas vezes. Ele detecta sozinho se o .NET SDK
  está instalado na máquina: se estiver (seu PC normal), publica o app do
  zero; se não estiver (o esperado no próprio Legion/ROG Ally), usa uma
  pasta `publish\` que precisa já estar copiada ali do lado (veja o passo
  2 abaixo). A janela fica aberta no final (`pause`) pra você ler o
  resultado com calma, sem fechar sozinha.
- **`Desinstalar.bat`** — desfaz tudo (remove o app, atalhos, e restaura
  os ajustes de registro/inicialização pro estado de antes).

**Passo a passo pra levar pro Legion/ROG Ally** (que provavelmente não tem
o SDK do .NET instalado):

1. No seu PC normal (onde já funcionou), roda `Instalar.bat` uma vez — isso
   gera a pasta `installer\publish\` com o app já publicado e pronto pra
   rodar em qualquer Windows x64, sem precisar do SDK.
2. Copia a pasta `installer\publish\` **inteira**, junto com os dois
   arquivos `.bat` e os dois `.ps1`, pro Legion/ROG Ally (pen drive, rede,
   como preferir) — mantendo a mesma pasta `installer\` com tudo dentro.
3. No Legion/ROG Ally, clica duas vezes em `Instalar.bat`. Como não tem o
   SDK ali, ele detecta isso e usa a pasta `publish\` que você copiou, em
   vez de tentar publicar de novo.
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
**dentro da pasta** `installer\` (`cd` até lá — rodar de outra pasta, como
`C:\WINDOWS\System32`, só vai dar "arquivo não encontrado") e roda:
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
nome da pasta do projeto). **Ainda não testados num build real**: a
`Topmost = true` da janela, o watchdog (`HandheldLauncherWatchdog` +
"Sair" desativando ele) e o preenchimento automático da API key via
`installer\steamgriddb-api-key.txt` — os três são desta leva, implementados
a partir dos relatos do Legion Go 2 mas ainda sem um ciclo de teste depois
de escritos. Testa num momento em que você consegue acompanhar o
resultado, e lê o `install-log.txt` no final pra confirmar que cada passo
fez o que devia.

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
installer/                   # Install-Launcher.ps1 / Uninstall-Launcher.ps1 (ver "Instalar no Legion Go 2 / ROG Ally")
docs/                        # (vazio por enquanto)
```
