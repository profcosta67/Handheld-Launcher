<#
.SYNOPSIS
    Passo OPCIONAL depois de instalar o launcher pelo Setup.exe da Release:
    liga o "modo console" (tela cheia no login, reabre se fechar, tenta
    segurar o Legion Space / Armoury Crate / Xbox Game Bar).

.DESCRIPTION
    O Setup.exe (Velopack) ja instala o launcher em
    %LocalAppData%\HandheldLauncher\ e cria os atalhos. Este script cuida
    so do que o Setup.exe nao faz:

      1. Registra a Tarefa Agendada "HandheldLauncherWatchdog": abre o
         launcher no login e confere a cada 1 minuto se ainda esta
         rodando, reabrindo se fechou (Alt+F4, crash...). "Sair" no menu
         do proprio launcher desliga essa tarefa antes de fechar. Use
         -NoWatchdog pra so abrir uma vez no login, sem reabrir.
      2. Liga KioskMode=true no settings.json - PRESERVANDO todo o resto
         (chaves de API, SteamID, pastas Xbox...).
      3. Zera duas chaves de registro do Xbox Game Bar/DVR (HKCU, sem
         admin), guardando o valor anterior pra desinstalar desfazer.
      4. Desativa (com backup) atalhos/entradas Run (HKCU) de software do
         fabricante: legion, lenovo, armoury, asus, rog.
      5. Instala o RTSS via winget se ainda nao existir (melhor esforco).
      6. Se existir uma instalacao ANTIGA (feita pelo Install-Launcher.ps1,
         em %LocalAppData%\Programs\HandheldLauncher), fecha ela, migra o
         backup de registro e remove a pasta antiga - o launcher novo
         passa a ser o unico.

    Log e backup ficam em %AppData%\HandheldLauncher\ (junto do
    settings.json), nao dentro da pasta do app, porque a pasta do app e
    totalmente substituida a cada atualizacao.

    Este script nunca foi executado por quem o escreveu (sem Windows no
    ambiente de desenvolvimento): se algo falhar, copie a mensagem exata.
#>
[CmdletBinding()]
param(
    [switch]$NoWatchdog
)

$ErrorActionPreference = 'Stop'

$appRoot = Join-Path $env:LOCALAPPDATA 'HandheldLauncher'
$exeDir = Join-Path $appRoot 'current'
$exeName = 'HandheldLauncher.exe'
$exePath = Join-Path $exeDir $exeName
$dataDir = Join-Path $env:APPDATA 'HandheldLauncher'
$settingsPath = Join-Path $dataDir 'settings.json'
$logPath = Join-Path $dataDir 'console-mode-log.txt'
$backupPath = Join-Path $dataDir 'kiosk-backup.json'
$legacyDir = Join-Path $env:LOCALAPPDATA 'Programs\HandheldLauncher'
$legacyBackupPath = Join-Path $legacyDir 'kiosk-backup.json'

New-Item -ItemType Directory -Path $dataDir -Force | Out-Null

$logLines = [System.Collections.Generic.List[string]]::new()
function Write-Log {
    param([string]$Message)
    $line = "[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    Write-Host $Message
    $logLines.Add($line)
}

Write-Log "== Handheld Launcher - configurar modo console =="

if (-not (Test-Path $exePath)) {
    Write-Log "ERRO: nao achei o launcher instalado em $exePath. Rode primeiro o HandheldLauncher-win-Setup.exe (baixe na pagina de Releases do GitHub) e depois este script."
    exit 1
}
Write-Log "Launcher encontrado em: $exePath"

# ---------------------------------------------------------------------------
# 0. Instalacao antiga (Install-Launcher.ps1): fechar, migrar backup, limpar
# ---------------------------------------------------------------------------
try {
    if (Test-Path $legacyDir) {
        Write-Log "Instalacao antiga encontrada em $legacyDir - migrando."

        Get-Process -Name 'HandheldLauncher' -ErrorAction SilentlyContinue | Where-Object {
            $_.Path -and $_.Path.StartsWith($legacyDir, [System.StringComparison]::OrdinalIgnoreCase)
        } | ForEach-Object {
            Write-Log "Fechando launcher antigo (PID $($_.Id))."
            Stop-Process -Id $_.Id -Force
        }
        Start-Sleep -Seconds 1

        # O backup antigo guarda os valores ORIGINAIS do registro (antes de
        # qualquer ajuste nosso). Se a gente nao trouxesse ele pra ca, o
        # passo 3 abaixo leria os valores ja alterados e o desinstalador
        # "restauraria" pro valor errado.
        if ((Test-Path $legacyBackupPath) -and -not (Test-Path $backupPath)) {
            Copy-Item $legacyBackupPath $backupPath -Force
            Write-Log "Backup de registro antigo migrado pra $backupPath."
        }

        # Atalhos antigos apontando pra pasta antiga (os do Setup.exe novo
        # apontam pra outra pasta e NAO sao tocados).
        $wsh = New-Object -ComObject WScript.Shell
        $shortcutDirs = @(
            [Environment]::GetFolderPath('Desktop'),
            [Environment]::GetFolderPath('Programs'),
            [Environment]::GetFolderPath('Startup')
        )
        foreach ($dir in $shortcutDirs) {
            Get-ChildItem -Path $dir -Filter '*.lnk' -ErrorAction SilentlyContinue | ForEach-Object {
                try {
                    $target = $wsh.CreateShortcut($_.FullName).TargetPath
                    if ($target -and $target.StartsWith($legacyDir, [System.StringComparison]::OrdinalIgnoreCase)) {
                        Remove-Item $_.FullName -Force
                        Write-Log "Atalho antigo removido: $($_.FullName)"
                    }
                }
                catch { }
            }
        }

        Remove-Item $legacyDir -Recurse -Force
        Write-Log "Pasta antiga removida: $legacyDir"
    }
}
catch {
    Write-Log "Aviso: nao consegui limpar a instalacao antiga por completo ($($_.Exception.Message)). Nao e critico - pode apagar $legacyDir na mao depois."
}

# ---------------------------------------------------------------------------
# 1. Tarefa Agendada (abrir no login + watchdog)
# ---------------------------------------------------------------------------
$watchdogTaskName = 'HandheldLauncherWatchdog'
try {
    $triggerLogon = New-ScheduledTaskTrigger -AtLogOn
    # --watchdog: avisa o launcher que esta abertura e automatica - ele mesmo
    # decide sair na hora se o usuario clicou 'Sair' (ate reiniciar) ou
    # desligou 'Abrir sozinho' nas Configuracoes (ver WatchdogGate.cs).
    $action = New-ScheduledTaskAction -Execute $exePath -Argument '--watchdog' -WorkingDirectory $exeDir
    $settingsSet = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)

    if ($NoWatchdog) {
        Register-ScheduledTask -TaskName $watchdogTaskName -Trigger @($triggerLogon) -Action $action -Settings $settingsSet -Description 'Abre o Handheld Launcher no login.' -Force | Out-Null
        Write-Log "Tarefa '$watchdogTaskName' registrada: abre no login, NAO reabre se fechar (-NoWatchdog)."
    }
    else {
        $triggerRepeat = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 1) -RepetitionDuration (New-TimeSpan -Days 3650)
        Register-ScheduledTask -TaskName $watchdogTaskName -Trigger @($triggerLogon, $triggerRepeat) -Action $action -Settings $settingsSet -Description 'Garante que o Handheld Launcher esteja sempre rodando; reabre em ate 1 minuto se fechar.' -Force | Out-Null
        Write-Log "Tarefa '$watchdogTaskName' registrada: abre no login e confere a cada 1 minuto, reabrindo se fechar. 'Sair' no menu do launcher desativa a tarefa antes de fechar. Desativar na mao: Disable-ScheduledTask -TaskName '$watchdogTaskName'."
    }
}
catch {
    Write-Log "ERRO ao registrar a Tarefa Agendada: $($_.Exception.Message)"
    exit 1
}

# ---------------------------------------------------------------------------
# 2. settings.json: liga KioskMode preservando TODAS as outras chaves
# ---------------------------------------------------------------------------
$settings = $null
if (Test-Path $settingsPath) {
    try {
        $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    }
    catch {
        Write-Log "Aviso: settings.json existente nao pode ser lido (formato inesperado) - recriando do zero."
    }
}

if ($null -eq $settings) {
    $settings = [pscustomobject]@{ SteamGridDbApiKey = $null; KioskMode = $true }
}
else {
    $settings | Add-Member -NotePropertyName KioskMode -NotePropertyValue $true -Force
}

# Chave padrao opcional: se ainda nao ha chave do SteamGridDB e existir um
# steamgriddb-api-key.txt ao lado deste script (nunca versionado no git),
# usa ele. Hoje o jeito normal e colar a chave em Configuracoes, no proprio
# launcher.
if (-not $settings.SteamGridDbApiKey) {
    $defaultKeyPath = Join-Path $PSScriptRoot 'steamgriddb-api-key.txt'
    if (Test-Path $defaultKeyPath) {
        $defaultKey = (Get-Content $defaultKeyPath -Raw).Trim()
        if ($defaultKey) {
            $settings | Add-Member -NotePropertyName SteamGridDbApiKey -NotePropertyValue $defaultKey -Force
            Write-Log "Chave do SteamGridDB preenchida a partir de steamgriddb-api-key.txt."
        }
    }
}

(ConvertTo-Json -InputObject $settings -Depth 6) | Set-Content -Path $settingsPath -Encoding UTF8
Write-Log "Modo kiosk ligado em: $settingsPath (demais configuracoes preservadas)."

# ---------------------------------------------------------------------------
# Backup (reaproveita o que ja existe pra nunca sobrescrever os valores
# ORIGINAIS do registro por valores ja alterados por uma execucao anterior)
# ---------------------------------------------------------------------------
$backup = [ordered]@{
    GameDVR_Enabled        = $null
    AppCaptureEnabled      = $null
    DisabledStartupEntries = @()
}
$backupLoaded = $false
if (Test-Path $backupPath) {
    try {
        $old = Get-Content $backupPath -Raw | ConvertFrom-Json
        $backup.GameDVR_Enabled = $old.GameDVR_Enabled
        $backup.AppCaptureEnabled = $old.AppCaptureEnabled
        $backup.DisabledStartupEntries = @($old.DisabledStartupEntries)
        $backupLoaded = $true
        Write-Log "Backup existente reaproveitado ($backupPath)."
    }
    catch {
        Write-Log "Aviso: backup existente ilegivel - vai ser recriado."
    }
}

# ---------------------------------------------------------------------------
# 3. Xbox Game Bar / GameDVR (HKCU, sem admin)
# ---------------------------------------------------------------------------
try {
    function Get-BackedUpValue {
        param([string]$Path, [string]$Name)
        if (Test-Path $Path) {
            $prop = Get-ItemProperty -Path $Path -Name $Name -ErrorAction SilentlyContinue
            if ($null -ne $prop) { return $prop.$Name }
        }
        return $null
    }

    $gameConfigPath = 'HKCU:\System\GameConfigStore'
    $gameDvrPath = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR'

    if (-not $backupLoaded) {
        $backup.GameDVR_Enabled = Get-BackedUpValue -Path $gameConfigPath -Name 'GameDVR_Enabled'
        $backup.AppCaptureEnabled = Get-BackedUpValue -Path $gameDvrPath -Name 'AppCaptureEnabled'
    }

    New-Item -Path $gameConfigPath -Force | Out-Null
    New-ItemProperty -Path $gameConfigPath -Name 'GameDVR_Enabled' -PropertyType DWord -Value 0 -Force | Out-Null

    New-Item -Path $gameDvrPath -Force | Out-Null
    New-ItemProperty -Path $gameDvrPath -Name 'AppCaptureEnabled' -PropertyType DWord -Value 0 -Force | Out-Null

    Write-Log "Xbox Game Bar/DVR: GameDVR_Enabled e AppCaptureEnabled zerados (valores originais guardados no backup)."
}
catch {
    Write-Log "Aviso: nao consegui ajustar o registro do Xbox Game Bar/DVR ($($_.Exception.Message)). Nao e critico."
}

# ---------------------------------------------------------------------------
# 4. Software do fabricante na inicializacao (melhor esforco)
# ---------------------------------------------------------------------------
try {
    $vendorKeywords = @('legion', 'lenovo', 'armoury', 'asus', 'rog')
    $startupDir = [Environment]::GetFolderPath('Startup')
    $disabledDir = Join-Path $dataDir 'disabled-startup-backup'
    New-Item -ItemType Directory -Path $disabledDir -Force | Out-Null
    $disabledCount = 0

    Get-ChildItem -Path $startupDir -Filter '*.lnk' -ErrorAction SilentlyContinue | ForEach-Object {
        $nameLower = $_.BaseName.ToLowerInvariant()
        $matchesVendor = ($vendorKeywords | Where-Object { $nameLower -like "*$_*" }) | Select-Object -First 1
        if ($matchesVendor) {
            $destination = Join-Path $disabledDir $_.Name
            Move-Item -Path $_.FullName -Destination $destination -Force
            $backup.DisabledStartupEntries += [ordered]@{ Type = 'StartupShortcut'; OriginalPath = $_.FullName; BackupPath = $destination }
            $disabledCount++
            Write-Log "Desativado (movido pra backup): atalho de inicializacao '$($_.Name)'"
        }
    }

    $runKey = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
    if (Test-Path $runKey) {
        $runKeyItem = Get-Item -Path $runKey
        foreach ($valueName in $runKeyItem.Property) {
            $nameLower = $valueName.ToLowerInvariant()
            $matchesVendor = ($vendorKeywords | Where-Object { $nameLower -like "*$_*" }) | Select-Object -First 1
            if ($matchesVendor) {
                $value = (Get-ItemProperty -Path $runKey -Name $valueName).$valueName
                Remove-ItemProperty -Path $runKey -Name $valueName
                $backup.DisabledStartupEntries += [ordered]@{ Type = 'RunKey'; Name = $valueName; Value = $value }
                $disabledCount++
                Write-Log "Desativado (removido, com backup): entrada de registro Run '$valueName'"
            }
        }
    }

    if ($disabledCount -eq 0) {
        Write-Log "Nenhuma entrada de fabricante nova encontrada. Se o Legion Space / Armoury Crate continuar abrindo sozinho, desative em Configuracoes > Aplicativos > Inicializar."
    }
}
catch {
    Write-Log "Aviso: a busca por software do fabricante parou num erro ($($_.Exception.Message)). Nao e critico."
}

# ---------------------------------------------------------------------------
# 5. RTSS via winget (melhor esforco)
# ---------------------------------------------------------------------------
try {
    $rtssPaths = @(
        'C:\Program Files (x86)\RivaTuner Statistics Server\RTSS.exe',
        'C:\Program Files\RivaTuner Statistics Server\RTSS.exe'
    )
    $rtssInstalled = $rtssPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($rtssInstalled) {
        Write-Log "RTSS ja esta instalado em: $rtssInstalled"
    }
    else {
        $wingetCmd = Get-Command winget -ErrorAction SilentlyContinue
        if ($wingetCmd) {
            Write-Log "RTSS nao encontrado - instalando via winget (id Guru3D.RTSS)..."
            & winget install --id Guru3D.RTSS --silent --accept-source-agreements --accept-package-agreements
            if ($LASTEXITCODE -eq 0) {
                Write-Log "RTSS instalado via winget."
            }
            else {
                Write-Log "Aviso: winget retornou codigo $LASTEXITCODE ao instalar o RTSS. Nao e critico - baixe em https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/ se quiser o FPS na tela."
            }
        }
        else {
            Write-Log "Aviso: winget indisponivel - RTSS nao instalado automaticamente. Baixe em https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/ se quiser o FPS na tela."
        }
    }
}
catch {
    Write-Log "Aviso: instalacao automatica do RTSS parou num erro ($($_.Exception.Message)). Nao e critico."
}

# ---------------------------------------------------------------------------
# 6. Salvar backup + log e abrir o launcher
# ---------------------------------------------------------------------------
(ConvertTo-Json -InputObject $backup -Depth 6) | Set-Content -Path $backupPath -Encoding UTF8
$logLines | Set-Content -Path $logPath -Encoding UTF8

if (-not (Get-Process -Name 'HandheldLauncher' -ErrorAction SilentlyContinue)) {
    Start-Process -FilePath $exePath -WorkingDirectory $exeDir
    Write-Log "Launcher aberto."
}

Write-Log ""
Write-Log "== Modo console configurado =="
Write-Log "Log: $logPath"
Write-Log "Pra desfazer: Desinstalar.bat (ou .\Uninstall-Launcher.ps1)."
