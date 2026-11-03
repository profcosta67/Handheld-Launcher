# 🎮 Handheld Launcher

Launcher para PCs portáteis e handhelds Windows, reunindo biblioteca de jogos, atalhos, ajustes de hardware e ferramentas de captura em uma interface otimizada para controle.

**Versão em preparação:** v0.14.9

## Funcionalidades

### Biblioteca de jogos
- Detecção de bibliotecas Steam, Epic Games, GOG, Xbox App, Ubisoft e EA, além de emuladores e atalhos configurados.
- Capas e artes obtidas pelo SteamGridDB.
- Downloads automáticos de capas em formato estático e possibilidade de selecionar manualmente imagens animadas.
- Filtros por plataforma, favoritos e estado de instalação.

### Quick Settings e Overlay
- Ajustes de TDP e monitoramento de sensores compatíveis.
- Alteração de resolução, taxa de atualização e modo de cor.
- HUD de desempenho com FPS, temperaturas, memória, bateria, resolução e TDP.
- Controle de volume e brilho.
- Captura de tela e gravação de tela pelo FFmpeg.

### Integração com handhelds
- Navegação por controle e teclado físico.
- Atalhos de sistema e Quick Settings.
- Integração com PawnIO e RyzenAdj para controle de TDP em hardware compatível.
- Ajuste de dimensões do Overlay à área de trabalho disponível.

## Novidades previstas para v0.14.9

- Correção de estouro de pilha ao decodificar determinadas imagens PNG animadas, atualizando AnimatedImage.Avalonia de 2.1.2 para 2.1.4.
- Downloads automáticos de capas permanecem estáticos; seleção manual continua aceitando capas animadas.
- FFmpeg incluído no pacote Windows x64 para habilitar gravação de tela sem instalação separada.
- HUD mais compacto: resolução em nomenclatura como 1080p/900p/720p e TDP somente em watts.
- Ajustes de dimensionamento do Overlay para telas menores.
- Melhorias na navegação da janela de configurações pelo teclado.
- Referência fixa dos atalhos de teclado e controle, organizada em cartões separados.
- Ajustes no popup de inicialização do jogo, incluindo indicador de carregamento e atualização do ícone quando disponível.
- Ajustes de abertura minimizada do Lossless Scaling. O funcionamento de Aplicar Agora/Frame Generation depende de validação específica no dispositivo.

> Esta seção descreve o escopo da versão em preparação. Consulte as notas do GitHub Release para saber exatamente o que foi validado na publicação.

## Requisitos

- Windows 10 ou Windows 11.
- Dispositivo x64.
- Recursos de TDP dependem do hardware, drivers e ferramentas compatíveis.

## Instalação

Baixe o pacote/instalador correspondente na seção **Releases** do GitHub e siga as instruções publicadas junto à versão. Para builds portáteis de teste, extraia o ZIP em uma pasta e execute `HandheldLauncher.exe`.

O pacote de Windows inclui as ferramentas de terceiros necessárias à gravação, quando indicado nas notas da versão. O FFmpeg é distribuído como executável separado e acompanhado de seu aviso de licença e informações de origem.

## Desenvolvimento

### Tecnologias
- C# / .NET 8
- Avalonia UI
- Velopack
- LibreHardwareMonitor
- FFmpeg
- PawnIO / RyzenAdj

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

A preparação do pacote de distribuição também executa os scripts de provisionamento das dependências externas, incluindo FFmpeg e PawnIO.

## Diagnóstico de TDP com HWiNFO (opcional)

O Launcher pode ler sensores publicados pelo HWiNFO64 via Shared Memory para auxiliar diagnósticos. O HWiNFO é externo e não é incluído no Launcher.

1. Abra o HWiNFO64 em modo **Sensors-only**.
2. Habilite **Shared Memory Support** nas configurações.
3. Mantenha o HWiNFO aberto durante os testes.
4. Consulte os registros com prefixo `[HWiNFO]` em `%AppData%\HandheldLauncher\debug.log`.

A disponibilidade dos sensores depende do dispositivo e da versão do HWiNFO. A ausência do HWiNFO não impede o funcionamento normal do Launcher.

## Licença

O Handheld Launcher é distribuído sob a licença GPL-3.0. Consulte o arquivo [LICENSE](LICENSE). Componentes de terceiros mantêm suas próprias licenças; consulte os avisos incluídos no pacote.
