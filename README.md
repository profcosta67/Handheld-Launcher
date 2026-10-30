## v0.9.30 - Preparação para Linux (Steam Deck) e Melhorias de Estabilidade

Esta atualização foca em dois pilares: deixar o launcher muito mais resistente a falhas no Windows e preparar todo o "motor" (Core) para rodar nativamente no Linux (Steam Deck e desktops).

### 🐧 Preparação para Linux (Core)
- **Substituição de Imagem:** O `System.Drawing.Common` (exclusivo do Windows) foi trocado por **SkiaSharp** no processamento de logos. Isso permite que o launcher corte as margens transparentes das capas no Linux sem erros.
- **Lançamento de Jogos:** O `GameLauncher` agora detecta o sistema operacional. No Linux, links (como `steam://`) são abertos via `xdg-open` e permissões de execução (`chmod +x`) são aplicadas automaticamente antes de rodar jogos.
- **Steam no Linux:** O scanner da Steam agora procura a instalação nos caminhos padrão do Linux (`~/.steam/steam` e `~/.local/share/Steam`), incluindo versões Flatpak.
- **Configurações no Linux:** O `settings.json` agora usa DPAPI no Windows, e no Linux usa permissões restritas de arquivo (`0600`) para proteger as chaves de API.

### 🚀 Estabilidade e Performance (Windows)
- **Cache de Overrides:** A aba "Não Instalados" e a navegação geral ficaram muito mais fluidas. O launcher não lê mais o arquivo de configuração repetidas vezes para cada jogo; agora ele usa um cache em memória inteligente.
- **Resiliência a Falhas:** Se um arquivo `settings.json` estiver corrompido ou sem permissão, o launcher **não crashará mais**. Ele abrirá com as configurações padrão e avisará no log.
- **Scanners Blindados:** Se a Steam, Epic, GOG ou qualquer outra loja falharem ao ler os arquivos do PC, o launcher vai ignorar a loja com erro e abrir os outros jogos sem travar.
- **Cancelamento de Rede:** A busca de jogos "na conta, não instalados" agora suporta cancelamento (`CancellationToken`). Se você trocar de aba, a requisição para a Steam Web API é abortada, evitando travamentos.

### 🔧 Ajustes Técnicos e Correções
- Correção de warnings do compilador relacionados a APIs exclusivas do Windows (`RegistryStoreScanners`).
- Melhoria no fechamento de processos para respeitar a diferença de maiúsculas/minúsculas nos caminhos de arquivo no Linux.
- Melhoria na legibilidade e tratamento de erros no `AppSettingsStore`.

**Nota:** A interface gráfica (UI) ainda está sendo adaptada para o Linux. Esta versão entrega o "motor" 100% pronto e otimizado para o Windows.