👨‍💻 Autor
Marcelo Costa (profcosta67)

GitHub: @profcosta67

# 🎮 Handheld Launcher

Um launcher completo, moderno e focado em performance para Handhelds (Legion Go, ROG Ally, Steam Deck e PCs portáteis).

O projeto foi desenhado para ser o seu **centro de controle (Hub)**, reunindo biblioteca de jogos, ajustes de hardware (TDP), monitoramento em tempo real e atalhos de sistema em uma interface única e amigável para controle.

## ✨ Funcionalidades

### 📚 Biblioteca de Jogos
- Escaneamento automático de bibliotecas: **Steam, Epic Games, GOG, Xbox App, Ubisoft, EA** e emuladores (Eden/Switch).
- Capas automáticas e suporte a **capas animadas** (GIF/WebP).
- Filtros rápidos por plataforma, favoritos e status de instalação.

### ⚡ Quick Settings (Overlay)
Aperte **L3 + R3** (ou o atalho `Ctrl + Alt + O`) durante o jogo para abrir o painel de controle rápido:
- **TDP:** Modos UltraEco (8W), Eco (15W), Normal (20W), Desempenho (25W) e Boost (30W) com proteção térmica de 90°C.
- **Tela:** Alterne a resolução (720p, 1080p, 2K) e a taxa de atualização (60Hz, 120Hz, 144Hz, 240Hz) sem sair do jogo.
- **Modo de Cor:** Padrão, Vívido, OLED e Noturno.
- **HUD de Desempenho:** Ative o monitor de FPS, temperatura (CPU/SoC), RAM e bateria no topo da tela.
- **Sistema:** Controle de Volume e Brilho em tempo real.
- **Captura:** Atalhos para tirar print (salva em `Imagens/Screenshots`) e gravar tela.

### 🛡️ Segurança e Performance
- **Shell Mode (Windows):** Substitui o `explorer.exe` no boot, transformando o dispositivo em um console dedicado.
- **Idle Detector:** Desliga a tela automaticamente para economizar bateria quando nenhum jogo está rodando.
- **Driver de TDP:** Arquitetura preparada para o **PawnIO** (driver seguro assinado pela Microsoft) com *fallback* automático para o RyzenAdj.

---

## 🛠️ Tecnologias Utilizadas

- **C# / .NET 8**
- **Avalonia UI** (Interface multiplataforma)
- **Velopack** (Instalador e sistema de atualização automática)
- **LibreHardwareMonitor** (Leitura de sensores)
- **FFmpeg** (Gravação de tela)
- **RyzenAdj / PawnIO** (Controle de TDP da APU)

---

## 🚀 Como Executar

### Pré-requisitos
- .NET 8 SDK
- Windows 10/11 (recomendado para todos os recursos) ou macOS/Linux (modo de desenvolvimento/teste).

### Rodando o projeto
```bash
# Clone o repositório
git clone https://github.com/profcosta67/Handheld-Launcher.git

# Entre na pasta
cd Handheld-Launcher

# Restaure as dependências
dotnet restore

# Compile e rode
dotnet run --project src/HandheldLauncher.App

📦 Instalação e Atualizações
O projeto utiliza o Velopack. O instalador oficial (HandheldLauncher-win-Setup.exe) pode ser baixado na aba Releases. O aplicativo verifica automaticamente por atualizações e notifica o usuário quando uma nova versão estiver disponível.

📜 Licença
Este projeto está sob a licença GPL-3.0. Veja o arquivo LICENSE para mais detalhes.