# Handheld Launcher

Launcher "bigscreen" próprio para Windows, feito pra rodar no **Legion Go 2** e no
**ROG Ally** — sem depender de nenhum launcher já existente. Boot direto em tela
cheia, biblioteca unificada (Steam + jogos adicionados manualmente), capas via
SteamGridDB, navegação por controle.

> O nome "HandheldLauncher" é só um placeholder de desenvolvimento — troca
> quando quiser (é um find-and-replace no `.sln`, nos `.csproj` e no
> namespace `HandheldLauncher.*`).

## Status atual (início do projeto)

Construído até agora:

- **`HandheldLauncher.Core`** — biblioteca sem UI, sem dependências externas:
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
    processo direto pros manuais) e rastreia quando fecham.
- **`HandheldLauncher.App`** — UI em Avalonia:
  - Janela com hero (jogo focado, grande) + fileira horizontal dos outros
    jogos, barra de topo e barra inferior — layout inspirado na referência
    que você mandou.
  - Navegação por teclado (seta esquerda/direita + Enter) como placeholder
    até o input de controle (XInput) entrar.
  - Botão "Atualizar" (reescaneia a biblioteca) e "Sair".

Ainda **não** feito (próximas etapas, nessa ordem que faz mais sentido):

1. Navegação por controle (gamepad)
2. Integração SteamGridDB (as capas hoje caem no fallback de texto)
3. Modo kiosk: boot direto em tela cheia + suprimir Legion Space / Xbox FSE
4. Instalador

## ⚠️ Importante: build ainda não verificado aqui

Este projeto foi escrito inteiro num sandbox Linux que **bloqueia acesso a
`api.nuget.org`** por política de rede — então não consegui rodar
`dotnet restore`/`dotnet build` aqui pra validar que compila. O `Core` não
tem nenhuma dependência externa (só a .NET base class library), mas o `App`
depende dos pacotes do Avalonia, que só vêm do NuGet.

**Então o primeiro build real precisa acontecer na sua máquina.** Se der erro
de compilação, me manda a mensagem de erro que eu corrijo.

## Como buildar (Windows, com internet normal)

Pré-requisito: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
instalado.

```powershell
cd handheld-launcher
dotnet restore
dotnet run --project src/HandheldLauncher.App
```

Isso deve abrir a janela do launcher. Se você tiver Steam instalado num
caminho padrão (`C:\Program Files (x86)\Steam` ou `D:\SteamLibrary` /
`D:\Steam` etc.), a biblioteca já deve aparecer escaneada automaticamente —
sem capas ainda (isso é a etapa do SteamGridDB), só com o nome do jogo como
placeholder na própria capa.

## Estrutura

```
HandheldLauncher.sln
src/
  HandheldLauncher.Core/     # modelos, scanner Steam, storage, launcher de processo
  HandheldLauncher.App/      # UI Avalonia (ViewModels, Views, Styles)
installer/                   # (vazio por enquanto — instalador vem depois)
docs/                        # (vazio por enquanto)
```
