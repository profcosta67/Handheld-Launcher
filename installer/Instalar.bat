@echo off
rem Clique duas vezes neste arquivo para instalar o Handheld Launcher -
rem nao precisa abrir PowerShell nem digitar comando nenhum.
rem
rem Detecta sozinho se o .NET SDK esta presente nesta maquina:
rem   - se estiver (normalmente no seu PC de desenvolvimento), publica o
rem     app do zero (dotnet publish) antes de instalar.
rem   - se NAO estiver (o caso esperado no proprio Legion Go 2 / ROG Ally),
rem     usa a pasta "publish" que precisa estar copiada aqui do lado deste
rem     arquivo (veja o README, secao "Instalar no Legion Go 2 / ROG Ally").
title Handheld Launcher - Instalador
echo.
echo ============================================================
echo  Handheld Launcher - Instalador
echo ============================================================
echo.

where dotnet >nul 2>nul
if %errorlevel% neq 0 (
    echo .NET SDK nao encontrado nesta maquina.
    echo Vou tentar instalar usando uma pasta "publish" ja existente ao
    echo lado deste arquivo. Se ela nao existir, vai dar erro explicando
    echo o que falta.
    echo.
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Launcher.ps1" -SkipPublish %*
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Launcher.ps1" %*
)

echo.
echo ============================================================
echo  Terminou. Le a saida acima com calma.
echo  Se algo deu errado, copia a mensagem completa e manda pro Claude.
echo ============================================================
pause
