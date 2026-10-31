# 🎮 Handheld Launcher

Um launcher moderno e multiplataforma para Handhelds (Legion Go, ROG Ally, Steam Deck), focado em desempenho, design limpo e jogabilidade com controle.

## 🚀 Funcionalidades Principais
- **Interface Otimizada para Controles:** Navegação fluida com analógicos e botões.
- **Suporte a Múltiplas Plataformas:** Steam, Xbox, Epic, GOG, emuladores e atalhos manuais.
- **Capas Animadas:** Suporte a GIF e WebP para capas de jogos.
- **Shell Mode:** Substituição do `explorer.exe` no Windows (Kiosk Mode).
- **Idle Detector:** Bloqueio de tela automático para economia de bateria.
- **Quick Settings Overlay (NOVO!):** Menu lateral flutuante acessível durante o jogo.
  - Controle de Volume e Brilho nativo (Windows e Linux).
  - Interface preparada para controle de TDP, Modo de Cor e HUD (Fase 3).

## 🎮 Como usar o Overlay (Quick Settings)
Com um jogo aberto ou no launcher:
- **Pelo Controle:** Aperte o botão `Xbox/Guide` ou `L3 + R3`.
- **Pelo Teclado:** Aperte `Ctrl + Alt + O` (Windows).
- **Pelo Menu:** Vá em `Configurações > Abrir Quick Settings`.
- **Para Fechar:** Aperte `B / Voltar` no controle ou o botão `Fechar Menu` na tela.

## 🛠️ Arquitetura de Hardware
O launcher foi construído com uma camada de abstração de hardware (`IHardwareService`).
- **No Windows:** Usa WMI e PowerShell para controle de sistema.
- **No Linux:** Usa comandos nativos como `amixer` e `brightnessctl`.
- **No macOS (Dev):** Usa um serviço Mock para testes de interface.

## 🔧 Como Compilar e Rodar
...


# Handheld Launcher

**Status Atual: v0.11.0 — A Experiência de Console Definitiva** 🎮

O Handheld Launcher evoluiu para se tornar um verdadeiro sistema de entretenimento para handhelds. A versão 0.11.0 traz o **Modo Shell**, que permite que o Windows inicie diretamente no launcher, ignorando a área de trabalho e evitando que apps concorrentes (como Legion Space e Xbox app) roubem o foco.

Além disso, a tão esperada atualização de **Capas Animadas** chegou! O launcher agora exibe GIFs e WebPs do SteamGridDB de forma nativa e fluida, com um seletor dedicado que permite filtrar apenas por animações. O visual também foi refinado com gradientes suavizados no banner principal e otimização de performance para garantir que a interface rode lisa mesmo com as animações ativas.

O motor do launcher também está 100% preparado para o Linux (Steam Deck e Desktop), com execução nativa de jogos e leitura de sensores de hardware.

> **Aviso:** O Modo Shell é uma opção ativada durante a instalação (via PowerShell `-SetAsShell`). Se você desejar voltar ao Windows normal, basta usar a opção "Sair para o Windows" no menu de Energia do próprio launcher.