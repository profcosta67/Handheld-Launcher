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
    jogo focado: "Buscar capa no SteamGridDB" (rebusca grid+hero só pra
    aquele jogo) e "Abrir jogo". ↑↓ navega entre as opções, A/Enter
    confirma, B/Esc/Start fecha sem fazer nada. Testado, funcionando.
    Importante: hoje a busca sempre traz a imagem de maior pontuação do
    SteamGridDB — não é um seletor pra escolher entre várias capas
    candidatas, é um "tenta de novo". Escolha manual entre opções
    (incluindo capas animadas) é uma feature maior, ainda não
    implementada — ver "Ainda não feito" abaixo.
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
4. Seletor de capa/banner (escolher entre várias opções do SteamGridDB,
   incluindo capas animadas, em vez de só re-buscar a de maior pontuação —
   ver seção própria mais abaixo)

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
Os sete primeiros commits já foram testados e corrigidos a partir dos erros
reais que você mandou (banner hero, layout Steam Deck, último-jogo-aberto,
blur de fundo, menu de contexto com Start, diálogo "Adicionar jogo" — tudo
confirmado funcionando). O commit mais recente (menu geral com View/Back,
pra alcançar a barra de baixo pelo controle) ainda não passou por esse
ciclo — se der erro de build, me manda a mensagem. Ponto mais arriscado
dessa leva: `MainWindow`'s `DataContextChanged` — é a primeira vez que o
código depende desse evento (pra ligar os eventos `AddGameRequested`/
`QuitRequested` do ViewModel assim que ele é atribuído pelo composition
root). Se o menu geral abrir mas "Adicionar jogo"/"Sair" não fizerem nada
quando escolhidos por ele, é aí que vale olhar primeiro.

## Seletor de capas tipo Decky Loader — é viável?

Sim, dá pra fazer (reaproveitando o SteamGridDB, sem precisar de banco
próprio). O que muda em relação ao que existe hoje:

- `ISteamGridDbClient` hoje só devolve a **melhor** capa (`GetBestGridForSteamAppAsync`
  etc.) — a API do SteamGridDB na verdade já devolve uma **lista** de
  candidatas por chamada (`/grids/steam/{appid}` traz várias, não uma só);
  o código de hoje simplesmente pega a de maior `score` e descarta o
  resto. Pra um seletor, a mudança é expor a lista inteira em vez de só o
  topo.
- A API tem um parâmetro `types` que filtra por `static` ou `animated` —
  as capas animadas do SteamGridDB são `.webm` ou GIF. Isso é o ponto mais
  trabalhoso: o `Image` do Avalonia não anima GIF/WEBM sozinho, então
  precisaria ou converter pra um formato que ele aceite, ou usar um
  player de vídeo/GIF (não tem um pronto zero-dependência tão simples
  quanto o resto do projeto — provavelmente ia exigir um pacote NuGet,
  quebrando a filosofia "zero dependência" do projeto até aqui).
- UI: uma grade de miniaturas navegável por controle (↑↓←→ entre opções,
  A confirma, B cancela) no lugar/complemento do menu "Buscar capa no
  SteamGridDB" que já existe.

Dá pra fazer em duas fases: (1) seletor só de capas estáticas (grid +
hero), reaproveitando 100% do que já existe de autenticação/cache; (2)
suporte a capas animadas, que é a parte que provavelmente pede uma
dependência nova. Ainda não comecei nenhuma das duas — ver "Ainda não
feito" acima.

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
