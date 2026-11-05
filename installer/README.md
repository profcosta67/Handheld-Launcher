# installer/

| Arquivo | Pra que serve | Situação |
| --- | --- | --- |
| `InstallTask.ps1` | Roda sozinho depois que o instalador oficial (Velopack, `HandheldLauncher-win-Setup.exe`) instala o app: registra a tarefa `HandheldLauncher_Startup` (abre o launcher no login, como administrador, pro TDP funcionar). Os workflows copiam este arquivo pro pacote. | **Em uso** |
| `Instalar.bat`, `Desinstalar.bat`, `Arquivos/Install-Launcher.ps1`, `Arquivos/Uninstall-Launcher.ps1` | Instalação manual antiga, de antes do Velopack (publica/copia a pasta e cria atalhos e tarefas na mão). | Legado — o caminho recomendado é o `Setup.exe` das Releases |
| `Configurar-Modo-Console.bat`, `Arquivos/Configurar-Modo-Console.ps1` | Troca o shell do Windows pelo launcher (Modo Console), sem passar pelo desktop. | Em uso (opcional, avançado) |

A instalação manual antiga pode ser removida numa próxima versão, se ninguém mais usar.
