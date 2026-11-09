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

# --- Instalação do HidHide ---
$hidHideInstaller = Join-Path $PSScriptRoot "Tools\HidHide_1.5.230_x64.exe"
$hidHideMarker = "HKLM:\SOFTWARE\Nefarius Software Solutions\HidHide"

# Verifica se o HidHide já está instalado para evitar reinstalações
if (-not (Test-Path $hidHideMarker)) {
    if (Test-Path $hidHideInstaller) {
        Write-Host "Instalando HidHide..."
        Start-Process -FilePath $hidHideInstaller -ArgumentList "/exenoui /qn /norestart" -Wait -NoNewWindow
    } else {
        Write-Warning "Instalador do HidHide não encontrado em: $hidHideInstaller"
    }
} else {
    Write-Host "HidHide já está instalado."
}
# --- Fim da instalação do HidHide ---

# --- Instalação do runtime do PawnIO (necessário para o TDP) ---
# O módulo RyzenSMU.bin (scripts/Prepare-PawnIO.ps1) NÃO chega: sem o driver
# PawnIO instalado, o log diz "Driver não encontrado" e o TDP nunca é aplicado.
# O instalador oficial (edição assinada, de https://pawnio.eu) tem de estar em
# Tools\PawnIO_setup.exe — é copiado para o pacote pela regra Tools\**\* do
# HandheldLauncher.App.csproj.
$pawnIoInstaller = Join-Path $PSScriptRoot "Tools\PawnIO_setup.exe"
$pawnIoService = Get-Service -Name "PawnIO" -ErrorAction SilentlyContinue

if ($pawnIoService) {
    Write-Host "PawnIO já está instalado (serviço presente)."
} elseif (Test-Path $pawnIoInstaller) {
    Write-Host "Instalando o runtime do PawnIO..."
    # Sintaxe oficial do PawnIO_setup.exe (o /S do NSIS é rejeitado — mostra a
    # usage na tela): -install -silent. -restricted instalaria a edição sem
    # assinatura, que NÃO é o que queremos.
    $pawnIoProcess = Start-Process -FilePath $pawnIoInstaller -ArgumentList "-install", "-silent" -Wait -PassThru -NoNewWindow
    Write-Host "PawnIO_setup.exe terminou com código $($pawnIoProcess.ExitCode)."
} else {
    Write-Warning "Runtime do PawnIO não encontrado em: $pawnIoInstaller. O TDP não vai funcionar até ser instalado (https://pawnio.eu)."
}

# v0.25.1: o driver é um KERNEL_DRIVER com StartType=Manual — depois de um
# reboot ficava STOPPED (código 1077) e o launcher dizia "Driver não
# encontrado" apesar de estar instalado. Deixa-se em automático e arranca-se
# já, para o TDP funcionar no primeiro arranque.
if (Get-Service -Name "PawnIO" -ErrorAction SilentlyContinue) {
    try {
        sc.exe config PawnIO start= auto | Out-Null
        $svc = Get-Service -Name "PawnIO"
        if ($svc.Status -ne "Running") { Start-Service -Name "PawnIO" }
        Write-Host "PawnIO: arranque automático configurado ($((Get-Service PawnIO).Status))."
    } catch {
        Write-Warning "PawnIO instalado mas não foi possível arrancar o driver: $($_.Exception.Message)"
    }
}
# --- Fim da instalação do PawnIO ---

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