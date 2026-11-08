# ============================================================
# Handheld Launcher - Instalação do Modo Console
# ============================================================
param([switch]$SkipReboot)

$ErrorActionPreference = "Stop"

# 1. Cria a Tarefa Agendada para iniciar no boot com Admin (sem UAC)
$exePath = Join-Path $env:LOCALAPPDATA "HandheldLauncher\current\HandheldLauncher.exe"

if (-not (Test-Path $exePath)) {
    $exePath = Join-Path $env:LOCALAPPDATA "HandheldLauncher\HandheldLauncher.exe"
}

if (-not (Test-Path $exePath)) {
    Write-Host "AVISO: Executavel nao encontrado. A Tarefa Agendada nao foi criada."
} else {
    $action = New-ScheduledTaskAction -Execute $exePath
    $trigger = New-ScheduledTaskTrigger -AtLogOn
    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
    # 🟢 StartWhenAvailable: inicia assim que possível, sem esperar o atraso do Windows
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
    Register-ScheduledTask -Action $action -Trigger $trigger -Principal $principal -Settings $settings -TaskName "HandheldLauncher_Startup" -Description "Inicia o Handheld Launcher em Modo Console." -Force
    Write-Host "OK: Tarefa Agendada criada com sucesso."
}

# 2. Ativa o KioskMode no arquivo de configuracoes
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

# Executa o comando para preparar o sistema silenciosamente
# Verifica se precisa de elevação
if (-NOT ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Warning "A instalação do Modo Jogo requer privilégios de Administrador."
}

# Ativa o modo console
$settings["KioskMode"] = $true
$settings["ModeChosen"] = $true

$settings | ConvertTo-Json -Depth 10 | Set-Content $settingsFile -Encoding UTF8
Write-Host "OK: Modo Console (KioskMode) ativado."

# 3. Agenda o reboot (a menos que o usuario passe -SkipReboot)
if (-not $SkipReboot) {
    Write-Host ""
    Write-Host "=================================================" -ForegroundColor Yellow
    Write-Host "O sistema sera reiniciado em 30 segundos para" -ForegroundColor Yellow
    Write-Host "entrar no Modo Console." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Para CANCELAR: schtasks /Delete /TN HandheldLauncher_Reboot /F" -ForegroundColor Cyan
    Write-Host "=================================================" -ForegroundColor Yellow

    # 🟢 Cria uma tarefa agendada para o reboot, que é mais confiável
    $rebootAction = New-ScheduledTaskAction -Execute "shutdown.exe" -Argument "/r /t 0 /f"
    $rebootTrigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddSeconds(30)
    $rebootPrincipal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
    Register-ScheduledTask -Action $rebootAction -Trigger $rebootTrigger -Principal $rebootPrincipal -TaskName "HandheldLauncher_Reboot" -Description "Reinicia o PC apos a instalacao do Modo Console." -Force
} else {
    Write-Host "Reboot ignorado (flag -SkipReboot)."
}