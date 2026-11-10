# installer/

| Arquivo | Pra que serve | Situação |
| --- | --- | --- |
| `InstallTask.ps1` | Roda sozinho depois que o instalador oficial (Velopack, `HandheldLauncher-win-Setup.exe`) instala o app: registra a tarefa `HandheldLauncher_Startup` (abre o launcher no login, como administrador, pro TDP funcionar). Os workflows copiam este arquivo pro pacote. | **Em uso** |
| `Desinstalar.bat`, `Arquivos/Uninstall-Launcher.ps1` | Desfazem o Modo Console e limpam instalações antigas. Vão dentro do pacote `Modo-Console.zip` das Releases. | Em uso |
| `Configurar-Modo-Console.bat`, `Arquivos/Configurar-Modo-Console.ps1` | Troca o shell do Windows pelo launcher (Modo Console), sem passar pelo desktop. | Em uso (opcional, avançado) |
| `install-linux.sh` | Instalador do pacote Linux: copia o app para `~/.local/share/handheld-launcher`, cria o atalho `~/.local/bin/handheld-launcher`, a entrada no menu de aplicações com ícone, corrige o `PATH` no `~/.profile`, confere dependências com `ldd` e grava um log em `~/.local/state/handheld-launcher/install.log`. Recusa correr com `sudo`. Opções: `--autostart`, `--no-add-path`, `--run`, `--dry-run`, `--prefix`, `--uninstall`, `--help`. | Em uso |
| `uninstall-linux.sh` | Desfaz a instalação Linux (app, atalho, menu, ícone e auto-arranque). Preserva `~/.config/HandheldLauncher` (settings, biblioteca, capas) — `--purge` apaga também. | Em uso |
| `icons/handheld-launcher.png` | Ícone 256×256 (extraído do `Assets/app-icon.ico`) que a entrada de menu usa no Linux. O `scripts/package-linux.sh` copia-o para a raiz do pacote. | Em uso |

O pacote Linux (`.tar.gz` e `.zip`) é montado por [`scripts/package-linux.sh`](../scripts/package-linux.sh) num runner Linux: ele fixa as permissões (`chmod 755`) antes de compactar, extrai o próprio arquivo gerado, instala-o num `HOME` temporário e **executa a aplicação instalada em modo autoteste** (`HandheldLauncher --selftest`, sem abrir janela) para provar que o binário arranca — não basta o atalho e o menu existirem. O `release.yml` e o `ci.yml` chamam esse script — ou seja, um pacote Linux quebrado falha o workflow em vez de chegar ao utilizador.

O `--selftest` (implementado em `src/HandheldLauncher.App/Diagnostics/StartupSelfTest.cs`) carrega todas as assemblies referenciadas pelo launcher e constrói os serviços de arranque. Foi adicionado no `0.24.3-beta.6`, depois de o beta.5 ter sido publicado com um binário que morria no arranque (`FileNotFoundException: LibreHardwareMonitorLib` — o pacote só publica binários para Windows e o publish `-r linux-x64` não o copiava).

A instalação manual antiga (`Instalar.bat` / `Install-Launcher.ps1`) foi removida na v0.16.4: o `Setup.exe` das Releases faz tudo. O launcher continua limpando sozinho instalações antigas feitas por ela (`LegacyInstallCleanup`).
