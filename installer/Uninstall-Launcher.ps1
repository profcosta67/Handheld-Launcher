<#
.SYNOPSIS
    Desfaz tudo que Install-Launcher.ps1 fez: remove o app instalado, os
    atalhos, a inicializacao automatica, e restaura os ajustes de registro
    do Xbox Game Bar e as entradas de inicializacao de fabricante que
    tinham sido desativadas.

.PARAMETER KeepFiles
    Nao remove a pasta de instalacao
    (%LocalAppData%\Programs\HandheldLauncher) nem o settings.json - so
    desfaz atalhos/registro/inicializacao automatica. Use isso se quiser
    voltar a rodar o app manualmente via `dotnet run` sem perder a API
    key do SteamGridDB ja configurada (ela fica em
    %AppData%\HandheldLauncher\settings.json, que nao e tocado por este
    script de qualquer forma - só desliga o KioskMode, veja o aviso no
    final).

.EXAMPLE
    .\Uninstall-Launcher.ps1
    Remove tudo: app instalado, atalhos, e restaura os ajustes de
    sistema pro estado anterior a instalacao.
#>
[CmdletBinding()]
param(
    [switch]$KeepFiles
)

$ErrorActionPreference = 'Continue'   # continua tentando desfazer o resto mesmo se um passo falhar

$installDir = Join-Path $env:LOCALAPPDATA 'Programs\HandheldLauncher'
$backupPath = Join-Path $installDir 'kiosk-backup.json'

Write-Host "== Handheld Launcher - desinstalacao iniciada =="

# ---------------------------------------------------------------------------
# 1. Restaurar registro / entradas de inicializacao de fabricante a partir
#    do backup gravado pelo Install-Launcher.ps1
# ---------------------------------------------------------------------------
if (Test-Path $backupPath) {
    $backup = Get-Content $backupPath -Raw | ConvertFrom-Json

    $gameConfigPath = 'HKCU:\System\GameConfigStore'
    $gameDvrPath = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR'

    if ($null -eq $backup.GameDVR_Enabled) {
        Remove-ItemProperty -Path $gameConfigPath -Name 'GameDVR_Enabled' -ErrorAction SilentlyContinue
        Write-Host "GameDVR_Enabled: removido (nao existia antes da instalacao)."
    }
    else {
        New-ItemProperty -Path $gameConfigPath -Name 'GameDVR_Enabled' -PropertyType DWord -Value $backup.GameDVR_Enabled -Force | Out-Null
        Write-Host "GameDVR_Enabled: restaurado pro valor original ($($backup.GameDVR_Enabled))."
    }

    if ($null -eq $backup.AppCaptureEnabled) {
        Remove-ItemProperty -Path $gameDvrPath -Name 'AppCaptureEnabled' -ErrorAction SilentlyContinue
        Write-Host "AppCaptureEnabled: removido (nao existia antes da instalacao)."
    }
    else {
        New-ItemProperty -Path $gameDvrPath -Name 'AppCaptureEnabled' -PropertyType DWord -Value $backup.AppCaptureEnabled -Force | Out-Null
        Write-Host "AppCaptureEnabled: restaurado pro valor original ($($backup.AppCaptureEnabled))."
    }

    foreach ($entry in $backup.DisabledStartupEntries) {
        if ($entry.Type -eq 'StartupShortcut') {
            if (Test-Path $entry.BackupPath) {
                Move-Item -Path $entry.BackupPath -Destination $entry.OriginalPath -Force
                Write-Host "Atalho de inicializacao restaurado: $($entry.OriginalPath)"
            }
        }
        elseif ($entry.Type -eq 'RunKey') {
            $runKey = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
            New-ItemProperty -Path $runKey -Name $entry.Name -PropertyType String -Value $entry.Value -Force | Out-Null
            Write-Host "Entrada de registro Run restaurada: $($entry.Name)"
        }
    }
}
else {
    Write-Host "Aviso: nao achei kiosk-backup.json em $installDir - pulando a restauracao de registro/inicializacao de fabricante (ou ja foi desinstalado antes, ou a instalacao nao foi feita com Install-Launcher.ps1)."
}

# ---------------------------------------------------------------------------
# 2. Remover atalhos criados pelo instalador
# ---------------------------------------------------------------------------
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Handheld Launcher.lnk'
$startMenuShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Handheld Launcher.lnk'
$startupShortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'Handheld Launcher.lnk'

foreach ($shortcut in @($desktopShortcut, $startMenuShortcut, $startupShortcut)) {
    if (Test-Path $shortcut) {
        Remove-Item $shortcut -Force
        Write-Host "Atalho removido: $shortcut"
    }
}

# ---------------------------------------------------------------------------
# 3. Remover arquivos instalados (a menos que -KeepFiles)
# ---------------------------------------------------------------------------
if (-not $KeepFiles) {
    if (Test-Path $installDir) {
        Remove-Item $installDir -Recurse -Force
        Write-Host "Pasta de instalacao removida: $installDir"
    }

    $settingsPath = Join-Path $env:APPDATA 'HandheldLauncher\settings.json'
    if (Test-Path $settingsPath) {
        Write-Host "Aviso: $settingsPath NAO foi removido (guarda sua API key do SteamGridDB e a biblioteca de jogos). Se quiser desligar so o modo kiosk sem desinstalar de verdade, edita esse arquivo e troca `"KioskMode`": true pra false."
    }
}
else {
    Write-Host "Mantendo $installDir (-KeepFiles) - so os atalhos/registro/inicializacao foram desfeitos."
}

Write-Host ""
Write-Host "== Desinstalacao concluida =="
