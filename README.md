# 🎮 Handheld Launcher

Launcher para PCs portáteis e handhelds Windows, reunindo biblioteca de jogos, atalhos, ajustes de hardware e ferramentas de captura em uma interface otimizada para controle.

[![Release](https://img.shields.io/github/v/release/profcosta67/Handheld-Launcher?label=vers%C3%A3o)](https://github.com/profcosta67/Handheld-Launcher/releases/latest) · [Novidades por versão](CHANGELOG.md) · [Roadmap](docs/roadmap.md) · [Aparelhos](docs/aparelhos.md)

## Funcionalidades

### Biblioteca de jogos
- Detecção de bibliotecas Steam, Epic Games, GOG, Xbox App, Ubisoft Connect e EA app, além de emuladores (Eden), programas e jogos/atalhos adicionados manualmente.
- Jogos possuídos, tempo de jogo e conquistas lidos da própria conta Steam.
- Metadados (descrição, desenvolvedora, gêneros) da loja da Steam e, para jogos de outras plataformas, da RAWG.
- Capas e artes obtidas pelo SteamGridDB, com download automático e seleção manual, incluindo capas animadas.
- Filtros por plataforma, favoritos e estado de instalação.

### Quick Settings e Overlay
- Ajustes de TDP (perfis Eco, Normal, Desempenho e Boost) e monitoramento de sensores compatíveis.
- Curva de ventoinha em aparelhos Lenovo suportados.
- Alteração de resolução, taxa de atualização e modo de cor.
- HUD de desempenho com FPS, temperaturas, memória, bateria, resolução e TDP.
- Controle de volume e brilho.
- Captura de tela e gravação pelo FFmpeg.
- Integração com o Lossless Scaling (ativar/desativar e multiplicador).
- Abre com **L3+R3** (ou Ctrl+Alt+O) inclusive com o jogo em primeiro plano.

### Central de Ajuda
- **Verificações** do launcher e do aparelho (controle, restauração de TDP/ventoinha/cor, atualizações, Lossless, capas, Eden), cada uma com dica e artigo.
- **Manual** de problemas e soluções, navegável pelo controle.
- **Reportar erro**: gera um .zip com relatório e log, sem chaves de API, SteamID, nome de usuário, nome do PC e endereços Bluetooth.

### Integração com handhelds
- Navegação por controle e teclado físico, incluindo nas janelas de diálogo.
- Modo console: o launcher assume o lugar do "Modo Xbox" (Xbox Full Screen Experience) no boot, sem passar pelo desktop.
- Atualização automática via Velopack + GitHub Releases, com **canal beta** opcional (Configurações → Preferências → Atualizações).
- Ajuste de dimensões do Overlay à área de trabalho disponível.

## Aparelhos suportados

| Aparelho | Biblioteca, artes, overlay | TDP (via PawnIO) | Curva de ventoinha |
| --- | --- | --- | --- |
| Lenovo Legion Go 2 (modelo 8AHP2, Z2/Hawk Point) | Sim | Sim, validado | Sim, validado |
| Outros handhelds com AMD Phoenix/Hawk Point (ex.: ROG Ally, ROG Ally X — Z1/Z1 Extreme) | Sim | Sim, mesma faixa de 5W-30W, mas **ainda não validado nesse modelo específico** — o overlay mostra esse aviso | Não — exclusivo do Legion Go (API própria da Lenovo) |

Fora da curva de ventoinha, que depende de uma API exclusiva da Lenovo, o restante do launcher — incluindo o TDP — roda em qualquer handheld Windows 10/11 x64 com processador AMD Phoenix ou Hawk Point.

## Requisitos

- Windows 10 ou Windows 11, 64 bits.
- Para TDP: processador AMD Phoenix ou Hawk Point (Ryzen Z1/Z1 Extreme/Z2, série 7040/8040 mobile) e o runtime do [PawnIO](https://pawnio.eu/) instalado. Validado no Legion Go 2; em outros modelos os comandos usam a mesma faixa seletiva de 5W-30W, mas sem validação própria ainda.

## Instalação

Baixe o pacote/instalador correspondente na seção **[Releases](https://github.com/profcosta67/Handheld-Launcher/releases)** do GitHub e siga as instruções publicadas junto à versão. Para builds portáteis de teste, extraia o ZIP em uma pasta e execute `HandheldLauncher.exe`.

O pacote de Windows inclui as ferramentas de terceiros necessárias à gravação e ao TDP, quando indicado nas notas da versão. O FFmpeg é distribuído como executável separado, acompanhado de seu aviso de licença e informações de origem; o módulo RyzenSMU usado pelo PawnIO vem do release oficial do [PawnIO.Modules](https://github.com/namazso/PawnIO.Modules), com hash conferido no build (veja `scripts/Prepare-PawnIO.ps1`).

## Linux (Bazzite)

O launcher também compila e roda **nativamente no Linux** (sem Wine/Bottles). O alvo principal é o **Bazzite** (handhelds AMD + Gamescope + MangoHud + ryzenadj).

Para instalar num Linux (Bazzite, Zorin ou outra distro), depois de extrair o `.tar.gz`:

```
handheld-launcher/
├── INSTALAR.desktop     ← duplo-clique (o instalador)
├── instalador/          ← scripts
└── arquivos/            ← binário e tudo o que ele precisa
```

**Duplo-clique em `INSTALAR`** e escolha no menu:

```
1) Instalar e abrir sozinho no login (handheld)   [Enter]
2) Instalar sem abrir no login
3) Desinstalar
```

Sem ambiente gráfico (ou prefira o terminal):

```bash
cd handheld-launcher
./instalador/instalar.sh          # abre o mesmo menu
./instalador/instalar.sh --sem-perguntas   # instala + arranque no login
./instalador/instalar.sh --desinstalar     # remove (--purge apaga os dados)
```

O instalador cria o atalho `~/.local/bin/handheld-launcher`, a entrada no menu de aplicações com ícone, **um atalho no ambiente de trabalho**, corrige o `PATH` no `~/.profile`, liga o **arranque automático no login** e confere as bibliotecas/ferramentas em falta (mostrando o comando `apt`/`dnf`/`rpm-ostree` exato). O log de cada execução fica em `~/.local/state/handheld-launcher/install.log`.

> No GNOME/Zorin, o primeiro duplo-clique num `.desktop` recém-extraído pode pedir autorização: botão direito → **Permitir execução**. Depois disso já abre com dois cliques.

Opções úteis (script `instalador/install-linux.sh`, que o menu usa):

```bash
./instalador/install-linux.sh --autostart              # liga o arranque no login
./instalador/install-linux.sh --no-autostart           # desliga (padrão no CLI)
./instalador/install-linux.sh --run                    # instala e abre no final
./instalador/install-linux.sh --no-desktop-shortcut     # sem atalho no desktop
./instalador/install-linux.sh --dry-run                # mostra sem alterar nada
./instalador/install-linux.sh --uninstall              # remove tudo
./instalador/install-linux.sh --help
```

> Prefira o `.tar.gz`: é o único formato que preserva as permissões de execução em qualquer extração. O `.zip` continua publicado, mas o zip gerado no Windows (até a beta.4) chegava sem o bit `+x` — era por isso que o launcher "instalava" e depois não abria no Zorin.

O que já funciona nativamente no Linux:
- **Biblioteca** (Steam/Epic/GOG/Heroic/Eden), capas e lançamento de jogos (`steam://` via `xdg-open`).
- **Quick Settings**: TDP via `ryzenadj`, brilho via `brightnessctl`, volume via `pactl`, resolução via `wlr-randr`/`xrandr`.
- **Navegação por gamepad** via `evdev` (`/dev/input/event*`), incluindo o combo L3+R3.
- **Rede**: Wi-Fi via `nmcli`, Bluetooth via `bluetoothctl`, rádio via `rfkill`.
- **Lossless (frame-gen)**: via **LSFG-VK** (camada Vulkan que reaproveita o teu `Lossless.dll`); o Quick Settings liga/desliga (`~/.config/environment.d`) e escolhe o multiplicador 2x/3x/4x (`~/.config/lsfg-vk/conf.toml`).
- **Syncthing** (o binário `syncthing` do sistema).

Para o "modo jogo" fullscreen, ative o **Modo Kiosk** nas Configurações (o launcher abre em `WindowState.FullScreen`); na sessão Gamescope do Bazzite o launcher corre como cliente Wayland.

## Temas de layout

O launcher oferece três experiências de biblioteca, compartilhando o mesmo núcleo de jogos, integrações e ferramentas:

- **BigScreen:** layout imersivo, inspirado na navegação de console do Steam Big Picture e Bazzite, com identidade visual própria e foco em controle.
- **Clean:** layout mais compacto (antigo "Switch"), com grade vertical e roleta de jogos.
- **Clássico:** layout original do Handheld Launcher.

Os três layouts reutilizam a mesma infraestrutura de biblioteca e funcionalidades para evitar duplicação de recursos. Veja as notas de cada versão em [`release-notes/`](release-notes/) para o histórico de mudanças.

## Desenvolvimento

### Tecnologias
- C# / .NET 8
- Avalonia UI
- Velopack (atualização automática)
- LibreHardwareMonitor
- FFmpeg
- PawnIO (TDP, Legion Go 2)

### Como contribuir

Veja [CONTRIBUTING.md](CONTRIBUTING.md): branches, padrão de commit, canal beta e como publicar uma versão.

### Compilar localmente

Instale o .NET 8 SDK e execute:

```powershell
git clone https://github.com/profcosta67/Handheld-Launcher.git
cd Handheld-Launcher
dotnet restore HandheldLauncher.sln
dotnet build HandheldLauncher.sln -c Release
```

Para publicar um build Windows x64:

```powershell
dotnet publish src/HandheldLauncher.App -c Release -r win-x64 --self-contained true
```

A preparação do pacote de distribuição também executa os scripts de provisionamento das dependências externas, incluindo FFmpeg e o módulo PawnIO (`scripts/Prepare-FFmpeg.ps1` e `scripts/Prepare-PawnIO.ps1`).

## Diagnóstico de TDP com HWiNFO (opcional)

O Launcher pode ler sensores publicados pelo HWiNFO64 via Shared Memory para auxiliar diagnósticos. O HWiNFO é externo e não é incluído no Launcher.

1. Abra o HWiNFO64 em modo **Sensors-only**.
2. Habilite **Shared Memory Support** nas configurações.
3. Mantenha o HWiNFO aberto durante os testes.
4. Consulte os registros com prefixo `[HWiNFO]` em `%AppData%\HandheldLauncher\debug.log`.

A disponibilidade dos sensores depende do dispositivo e da versão do HWiNFO. A ausência do HWiNFO não impede o funcionamento normal do Launcher.

## Aviso

Este é um projeto independente, mantido por hobby, sem vínculo com a Microsoft, a Valve, a Lenovo, a ASUS, a SteamGridDB ou qualquer outro fabricante ou plataforma citada. Os nomes de produtos de terceiros são usados apenas para identificação de compatibilidade.

Os ajustes de TDP e de curva de ventoinha alteram parâmetros de hardware do seu dispositivo. Use por sua conta e risco; o projeto e seus mantenedores não se responsabilizam por eventuais danos ao equipamento. Sempre prefira as versões publicadas na seção Releases, testadas nos aparelhos suportados.

## Licença

O Handheld Launcher é distribuído sob a licença GPL-3.0. Consulte o arquivo [LICENSE](LICENSE). Componentes de terceiros mantêm suas próprias licenças; consulte os avisos incluídos no pacote.
