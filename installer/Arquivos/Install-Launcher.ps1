<#
.SYNOPSIS
    Instala o Handheld Launcher como app "bigscreen".
#>
[CmdletBinding()]
param(
    [switch]$SkipPublish,
    [switch]$NoDesktopShortcut,
    [switch]$NoWatchdog,
    [switch]$SetAsShell   # NOVO: substitui o explorer.exe como shell do Windows
)

$ErrorActionPreference = 'Stop'

# --- Banner de Boas-Vindas ---
Clear-Host
$host.UI.RawUI.WindowTitle = "Handheld Launcher - Instalador"
Write-Host ""
Write-Host "  _   _                 _          _     _ " -ForegroundColor Cyan
Write-Host " | | | | __ _ _ __   __| |_ __ ___| |   | |" -ForegroundColor Cyan
Write-Host " | |_| |/ _' | '_ \ / _' | '__/ _ \ |   | |" -ForegroundColor Cyan
Write-Host " |  _  | (_| | | | | (_| | | |  __/ |___| |" -ForegroundColor Cyan
Write-Host " |_| |_|\__,_|_| |_|\__,_|_|  \___|_____|_|" -ForegroundColor Cyan
Write-Host "          L A U N C H E R" -ForegroundColor Magenta
Write-Host ""
Write-Host "  Bem-vindo! Vamos preparar o seu launcher." -ForegroundColor White
Write-Host "  ------------------------------------------" -ForegroundColor DarkGray
Write-Host ""

$scriptDir = $PSScriptRoot
$installerDir = Split-Path -Parent $scriptDir
$repoRoot = Split-Path -Parent $installerDir
$publishDir = Join-Path $scriptDir 'publish'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\HandheldLauncher'
$exeName = 'HandheldLauncher.exe'
$logPath = Join-Path $installDir 'install-log.txt'
$backupPath = Join-Path $installDir 'kiosk-backup.json'

$logLines = [System.Collections.Generic.List[string]]::new()
function Write-Log {
    param([string]$Message)
    $line = "[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    Write-Host $Message
    $logLines.Add($line)
}

Write-Log "== Handheld Launcher - instalacao iniciada =="

# ---------------------------------------------------------------------------
# 1. Publicar (a menos que -SkipPublish)
# ---------------------------------------------------------------------------
if (-not $SkipPublish) {
    $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetCmd) {
        Write-Log "'dotnet' nao encontrado no PATH."
        # (Resto da lógica do winget para instalar o .NET mantida igual)
        exit 1
    }

    $projectPath = Join-Path $repoRoot 'src\HandheldLauncher.App'
    Write-Log "Publicando (dotnet publish, self-contained win-x64, Release)..."
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    & dotnet publish $projectPath -c Release -r win-x64 --self-contained true -o $publishDir
    if ($LASTEXITCODE -ne 0) {
        Write-Log "ERRO: 'dotnet publish' falhou."
        exit 1
    }
    Write-Log "Publicacao concluida."
}
else {
    if (-not (Test-Path $publishDir)) {
        Write-Log "ERRO: -SkipPublish foi usado, mas nao achei a pasta 'publish\'."
        exit 1
    }
    Write-Log "Usando publicacao existente (-SkipPublish)."
}

# ---------------------------------------------------------------------------
# 2. Copiar para a pasta de instalacao
# ---------------------------------------------------------------------------
Write-Log "Copiando arquivos para: $installDir"
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir '*') -Destination $installDir -Recurse -Force

$exePath = Join-Path $installDir $exeName
if (-not (Test-Path $exePath)) {
    Write-Log "ERRO: nao encontrei $exeName."
    exit 1
}

# ---------------------------------------------------------------------------
# 3 & 4. Atalhos e Inicializacao (Watchdog vs Shell)
# ---------------------------------------------------------------------------
$wshShell = New-Object -ComObject WScript.Shell

function New-AppShortcut {
    param([string]$ShortcutPath)
    $shortcut = $wshShell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $exePath
    $shortcut.WorkingDirectory = $installDir
    $shortcut.Description = 'Handheld Launcher'
    $shortcut.Save()
}

if (-not $NoDesktopShortcut) {
    $desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Handheld Launcher.lnk'
    New-AppShortcut -ShortcutPath $desktopShortcut
}

$startMenuShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Handheld Launcher.lnk'
New-AppShortcut -ShortcutPath $startMenuShortcut

$startupDir = [Environment]::GetFolderPath('Startup')
$startupShortcut = Join-Path $startupDir 'Handheld Launcher.lnk'
$watchdogTaskName = 'HandheldLauncherWatchdog'

# Função do Shell movida para cá (antes de ser chamada)
function Set-CustomShell {
    param([string]$ExePath)

    Write-Log "Configurando o Handheld Launcher como Shell do Windows..."
    
    $regPath = "HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Winlogon"
    
    if (-not (Test-Path $regPath)) {
        New-Item -Path $regPath -Force | Out-Null
    }

    $currentShell = (Get-ItemProperty -Path $regPath -Name "Shell" -ErrorAction SilentlyContinue).Shell
    if ($currentShell -ne $ExePath) {
        if (-not $currentShell) { $currentShell = "explorer.exe" }
        Set-ItemProperty -Path $regPath -Name "OriginalShell" -Value $currentShell
        Write-Log "Shell original salvo: $currentShell"
    }

    Set-ItemProperty -Path $regPath -Name "Shell" -Value $ExePath
    Write-Log "Handheld Launcher configurado como Shell!"
}

if ($SetAsShell) {
    # MODO SHELL: Substitui o explorer.exe. Abre antes do Legion Space.
    # O Watchdog é ignorado, pois o próprio Windows já abre o launcher.
    Set-CustomShell -ExePath $exePath
    Write-Log "Modo SHELL ativado. Lembre-se: Para voltar ao Windows normal, use o botao 'Sair para o Windows' no menu de Energia do launcher."
}
elseif ($NoWatchdog) {
    New-AppShortcut -ShortcutPath $startupShortcut
    Write-Log "Inicializacao configurada via pasta Startup (-NoWatchdog)."
}
else {
    # Watchdog padrão (Tarefa Agendada)
    $triggerLogon = New-ScheduledTaskTrigger -AtLogOn
    $triggerRepeat = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 1) -RepetitionDuration (New-TimeSpan -Days 3650)
    $watchdogAction = New-ScheduledTaskAction -Execute $exePath -WorkingDirectory $installDir
    $watchdogSettings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)

    Register-ScheduledTask -TaskName $watchdogTaskName -Trigger @($triggerLogon, $triggerRepeat) -Action $watchdogAction -Settings $watchdogSettings -Description 'Garante que o Handheld Launcher esteja rodando.' -Force | Out-Null
    Write-Log "Watchdog configurado via Tarefa Agendada."
}

# ---------------------------------------------------------------------------
# 5. settings.json - KioskMode
# ---------------------------------------------------------------------------
$settingsPath = Join-Path $env:APPDATA 'HandheldLauncher\settings.json'
New-Item -ItemType Directory -Path (Split-Path $settingsPath) -Force | Out-Null

$settings = [ordered]@{ SteamGridDbApiKey = $null; KioskMode = $true }
if (Test-Path $settingsPath) {
    try {
        $existing = Get-Content $settingsPath -Raw | ConvertFrom-Json
        if ($existing.SteamGridDbApiKey) { $settings.SteamGridDbApiKey = $existing.SteamGridDbApiKey }
    } catch { }
}

if (-not $settings.SteamGridDbApiKey) {
    $defaultKeyPath = Join-Path $scriptDir 'steamgriddb-api-key.txt'
    if (Test-Path $defaultKeyPath) {
        $defaultKey = (Get-Content $defaultKeyPath -Raw).Trim()
        if ($defaultKey) { $settings.SteamGridDbApiKey = $defaultKey }
    }
}
($settings | ConvertTo-Json) | Set-Content -Path $settingsPath -Encoding UTF8

# ---------------------------------------------------------------------------
# 6, 7, 8. (GameBar, Legion Space, RTSS - Mantidos do original)
# ---------------------------------------------------------------------------
$backup = [ordered]@{ GameDVR_Enabled = $null; AppCaptureEnabled = $null; DisabledStartupEntries = @() }

try {
    # ... (Código original do GameBar mantido aqui) ...
    $gameConfigPath = 'HKCU:\System\GameConfigStore'
    $gameDvrPath = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR'
    New-Item -Path $gameConfigPath -Force | Out-Null
    New-ItemProperty -Path $gameConfigPath -Name 'GameDVR_Enabled' -PropertyType DWord -Value 0 -Force | Out-Null
    New-Item -Path $gameDvrPath -Force | Out-Null
    New-ItemProperty -Path $gameDvrPath -Name 'AppCaptureEnabled' -PropertyType DWord -Value 0 -Force | Out-Null
} catch { Write-Log "Aviso: Falha ao ajustar GameBar." }

try {
    # ... (Código original do Legion Space mantido aqui) ...
    $vendorKeywords = @('legion', 'lenovo', 'armoury', 'asus', 'rog')
    $disabledDir = Join-Path $installDir 'disabled-startup-backup'
    New-Item -ItemType Directory -Path $disabledDir -Force | Out-Null

    Get-ChildItem -Path $startupDir -Filter '*.lnk' -ErrorAction SilentlyContinue | ForEach-Object {
        $nameLower = $_.BaseName.ToLowerInvariant()
        $isOwnShortcut = $_.FullName -eq $startupShortcut
        $matchesVendor = ($vendorKeywords | Where-Object { $nameLower -like "*$_*" }) | Select-Object -First 1
        if (-not $isOwnShortcut -and $matchesVendor) {
            $destination = Join-Path $disabledDir $_.Name
            Move-Item -Path $_.FullName -Destination $destination -Force
            $backup.DisabledStartupEntries += [ordered]@{ Type = 'StartupShortcut'; OriginalPath = $_.FullName; BackupPath = $destination }
        }
    }
} catch { Write-Log "Aviso: Falha ao buscar entradas do fabricante." }

# ---------------------------------------------------------------------------
# 9. Salvar backup + log
# ---------------------------------------------------------------------------
($backup | ConvertTo-Json -Depth 5) | Set-Content -Path $backupPath -Encoding UTF8
$logLines | Set-Content -Path $logPath -Encoding UTF8

Write-Log ""
Write-Log "== Instalacao concluida =="
Write-Log "App instalado em: $installDir"
Write-Log "Log completo: $logPath"