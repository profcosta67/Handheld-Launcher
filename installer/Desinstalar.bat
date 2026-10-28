@echo off
rem Clique duas vezes neste arquivo para desinstalar o Handheld Launcher
rem e desfazer tudo que o instalador mudou (atalhos, modo kiosk, ajustes
rem de registro do Xbox Game Bar, entradas de inicializacao do fabricante
rem que tinham sido desativadas) - nao precisa abrir PowerShell.
title Handheld Launcher - Desinstalador
echo.
echo ============================================================
echo  Handheld Launcher - Desinstalador
echo ============================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-Launcher.ps1" %*

echo.
echo ============================================================
echo  Terminou. Le a saida acima com calma.
echo ============================================================
pause
