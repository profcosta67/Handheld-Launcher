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
    horizontal de capas embaixo — layout estilo Steam Deck.
  - Na fileira, o **último jogo aberto** (não o que está só focado/com o
    cursor em cima — o que foi realmente *lançado* por último) aparece
    largo/deitado usando a arte hero, e só troca de lugar/tamanho quando
    você abre outro jogo de verdade; navegar com ← → só muda o banner de
    cima, não mexe na fileira. Ao abrir um jogo ele vira o primeiro da
    fileira (empurra o resto).
  - Fundo da janela inteira preenchido com a arte do jogo em foco, desfocada
    (blur) e esticada — evita vazio escuro embaixo/ao redor do conteúdo.
    **Isso usa `Avalonia.Media.BlurEffect`, ainda não testado num build
    real — ver aviso de build mais abaixo.**
  - Navegação por **controle via XInput** (dpad/analógico esquerdo, A
    confirma, B fecha o jogo) — testado e funcionando com um controle Xbox.
    Teclado (← → Enter Esc) continua funcionando como fallback de dev.
  - Botão "Fechar: <jogo>" (só aparece com jogo rodando), "Buscar capas"
    (dispara o fetch do SteamGridDB pros jogos sem capa), "Atualizar"
    (reescaneia) e "Sair".
  - Ícone de engrenagem no topo — **só placeholder visual por enquanto**,
    desabilitado. Ainda não decidimos o escopo da tela de configurações.

Ainda **não** feito:

1. Modo kiosk: boot direto em tela cheia + suprimir Legion Space / Xbox FSE
2. Instalador
3. Tela de configurações de verdade (hoje é só editar `settings.json` na mão)

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
Os quatro primeiros commits já foram testados e corrigidos a partir dos
erros reais que você mandou; os dois commits mais recentes (banner hero +
layout Steam Deck, e último-jogo-aberto + blur de fundo) ainda não passaram
por esse ciclo — se der erro de build, me manda a mensagem. O ponto mais
arriscado dessa leva é o `<media:BlurEffect>` no `MainWindow.axaml` (classe
`Avalonia.Media.BlurEffect`) — se o namespace ou o nome da classe estiverem
errados, é exatamente o tipo de AVLN2000 que já corrigimos antes.

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
