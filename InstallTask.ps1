# ============================================================
# Handheld Launcher - Instalação do Modo Console
# ============================================================
param([switch]$SkipReboot)

$ErrorActionPreference = "Stop"

# ============================================================
# v0.14.2 — Fim da duplicação de tarefas.
# O script NÃO cria mais a tarefa "HandheldLauncher_Startup".
# Quem cria/remove tarefa de boot agora é o WatchdogTask.cs (chamado
# quando o usuário salva Desktop/Handheld nas Configurações), que usa
# LogonTrigger com Delay + RestartOnFailure limitado. Isso elimina
# o loop de "abrir 2 janelas no boot" e "reabrir a cada 1 minuto".
# ============================================================

Write-Host "InstallTask.ps1 v0.14.2: configurando modo console..."

# 1. Ativa o KioskMode no settings.json
$settingsDir = Join-Path $env:APPDATA "HandheldLauncher"
$settingsFile = Join-Path $settingsDir "settings.json"

if (-not (Test-Path $settingsDir)) {
    New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null
}

$settings = @{}
if (Test-Path $settingsFile) {
    try {
        $settings = Get-Content $settingsFile -Raw | ConvertFrom-Json -AsHashtable
    } catch {
        $settings = @{}
    }
}

# Ativa o modo console
$settings["KioskMode"] = $true
$settings["AutoRestart"] = $true
$settings["ModeChosen"] = $true

$settings | ConvertTo-Json -Depth 10 | Set-Content $settingsFile -Encoding UTF8
Write-Host "OK: Modo Console (KioskMode) ativado."

# ============================================================
# v0.14.2 — Remove tarefa duplicada que o script antigo criava.
# Se a "HandheldLauncher_Startup" (versão antiga) existir, apaga
# para não conflitar com a "HandheldLauncherWatchdog" gerenciada
# pelo WatchdogTask.cs.
# ============================================================
try {
    $existing = schtasks /Query /TN "HandheldLauncher_Startup" 2>$null
    if ($LASTEXITCODE -eq 0) {
        schtasks /Delete /TN "HandheldLauncher_Startup" /F | Out-Null
        Write-Host "OK: Tarefa antiga 'HandheldLauncher_Startup' removida (duplicava o watchdog)."
    }
} catch {
    # Best effort
}

# 2. Agenda o reboot (a menos que o usuario passe -SkipReboot)
if (-not $SkipReboot) {
    Write-Host ""
    Write-Host "=================================================" -ForegroundColor Yellow
    Write-Host "O sistema sera reiniciado em 30 segundos para" -ForegroundColor Yellow
    Write-Host "entrar no Modo Console." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Para CANCELAR: shutdown /a" -ForegroundColor Cyan
    Write-Host "=================================================" -ForegroundColor Yellow

    shutdown /r /t 30 /c "Handheld Launcher: Reiniciando em 30s para entrar no Modo Console. Execute 'shutdown /a' para cancelar."
} else {
    Write-Host "Reboot ignorado (flag -SkipReboot)."
}