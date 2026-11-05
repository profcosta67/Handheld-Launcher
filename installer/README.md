# installer/

| Arquivo | Pra que serve | Situação |
| --- | --- | --- |
| `InstallTask.ps1` | Roda sozinho depois que o instalador oficial (Velopack, `HandheldLauncher-win-Setup.exe`) instala o app: registra a tarefa `HandheldLauncher_Startup` (abre o launcher no login, como administrador, pro TDP funcionar). Os workflows copiam este arquivo pro pacote. | **Em uso** |
| `Desinstalar.bat`, `Arquivos/Uninstall-Launcher.ps1` | Desfazem o Modo Console e limpam instalações antigas. Vão dentro do pacote `Modo-Console.zip` das Releases. | Em uso |
| `Configurar-Modo-Console.bat`, `Arquivos/Configurar-Modo-Console.ps1` | Troca o shell do Windows pelo launcher (Modo Console), sem passar pelo desktop. | Em uso (opcional, avançado) |

A instalação manual antiga (`Instalar.bat` / `Install-Launcher.ps1`) foi removida na v0.16.4: o `Setup.exe` das Releases faz tudo. O launcher continua limpando sozinho instalações antigas feitas por ela (`LegacyInstallCleanup`).
