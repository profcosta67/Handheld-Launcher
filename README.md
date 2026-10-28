# Handheld Launcher

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
    própria mais abaixo) pra aquele jogo específico. ← → ↑ ↓ movem o
    destaque uma posição por vez (lista "achatada", sem navegação por
    linha de verdade ainda), A/Enter aplica a opção destacada na hora
    (capa ou banner do jogo, conforme qual dos dois você abriu), B/Esc
    cancela sem mudar nada. Enquanto carrega mostra "Buscando opções...";
    se não achar nenhuma, mostra "Nenhuma opção estática encontrada".
    Substitui o antigo botão único "Buscar capa no SteamGridDB" (que só
    re-buscava a imagem de maior pontuação, sem escolha) — esse foi
    removido, o seletor faz tudo que ele fazia e mais. **Ainda não
    testado num build real** — ver ponto de risco mais abaixo.
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

Ainda **não** feito:

1. Modo kiosk: boot direto em tela cheia + suprimir Legion Space / Xbox FSE
2. Instalador
3. Tela de configurações de verdade (hoje é só editar `settings.json` na mão)
4. Capas **animadas** (GIF/WEBM) no seletor — fase 2 do seletor de capas,
   ver seção própria mais abaixo (fase 1, estático, já está feita, só
   falta testar num build real)

## Configurar a API key do SteamGridDB

1. Cria uma conta grátis em https://www.steamgriddb.com e gera uma API key
   em https://www.steamgriddb.com/profile/preferences/api
2. Roda o launcher uma vez (ele cria `%AppData%\HandheldLauncher\settings.json`
   automaticamente, com o campo vazio)
3. Abre esse arquivo e cola a chave:
   ```json
   { "SteamGridDbApiKey": "sua-chave-aqui" }
   ```
4. Reabre o launcher e clica em "Buscar capas"

## ⚠️ Build ainda não totalmente verificado aqui

Este projeto foi escrito num sandbox Linux que bloqueia acesso a
`api.nuget.org` — não dá pra rodar `dotnet restore`/`build` aqui pra validar.
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
installer/                   # (vazio por enquanto — instalador vem depois)
docs/                        # (vazio por enquanto)
```
