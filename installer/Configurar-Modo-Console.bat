@echo off
rem Clique duas vezes neste arquivo DEPOIS de instalar o launcher pelo
rem HandheldLauncher-win-Setup.exe (pagina de Releases do GitHub).
rem Liga o "modo console": tela cheia no login, reabre se fechar, tenta
rem segurar Legion Space / Armoury Crate / Xbox Game Bar. Opcional - o
rem launcher funciona normal sem isso. Nao precisa do .NET SDK.
title Handheld Launcher - Configurar modo console
echo.
echo ============================================================
echo  Handheld Launcher - Configurar modo console
echo ============================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Arquivos\Configurar-Modo-Console.ps1" %*

echo.
echo ============================================================
echo  Terminou. Le a saida acima com calma.
echo  Se algo deu errado, copia a mensagem completa e manda pro Claude.
echo ============================================================
pause
