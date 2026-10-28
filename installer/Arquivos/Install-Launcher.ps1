<#
.SYNOPSIS
    Instala o Handheld Launcher como app "bigscreen": publica (ou usa uma
    publicação já pronta), copia pra uma pasta de instalação, cria atalhos,
    liga o modo kiosk (fullscreen automático no login) e tenta impedir o
    Legion Space / Xbox Game Bar de abrirem sozinhos no boot.

.DESCRIPTION
    Como este projeto inteiro foi escrito num sandbox sem Windows (sem
    conseguir rodar `dotnet build`/`publish` nem testar PowerShell de
    verdade), este script nunca foi executado de fato antes de chegar até
    você — se algum passo der erro, copia a mensagem exata que apareceu no
    terminal, igual já vem fazendo com o resto do projeto.

    Passos, nesta ordem:
      1. (a menos que -SkipPublish) `dotnet publish` self-contained
         win-x64 Release — não depende do .NET estar instalado na máquina
         onde o app vai RODAR (ex: o próprio Legion Go 2 / ROG Ally),
         só na máquina onde esse publish é feito.
      2. Copia o resultado pra %LocalAppData%\Programs\HandheldLauncher\.
      3. Cria atalho na Área de Trabalho e no Menu Iniciar.
      4. Registra uma Tarefa Agendada ("HandheldLauncherWatchdog") que
         abre o launcher no login E confere a cada 1 minuto se ele ainda
         está rodando, reabrindo se tiver fechado (Alt+F4, crash, etc).
         SEM substituir o shell do Windows — o desktop normal continua
         por baixo, Alt+Tab continua funcionando. "Sair" no menu geral do
         próprio launcher desliga essa tarefa antes de fechar, então essa
         é a única forma de saída que realmente "pega" — Alt+F4 ou um
         crash são recuperados de propósito. Use `-NoWatchdog` pro
         comportamento antigo (abre uma vez no login, não reabre nunca,
         sempre dá pra sair com Alt+F4).
      5. Liga KioskMode=true no settings.json (preserva a API key do
         SteamGridDB se você já tiver configurado uma).
      6. Ajusta duas chaves de registro bem documentadas (HKCU, não
         precisa de admin) pra impedir o Xbox Game Bar/DVR de abrir
         automático — com backup do valor anterior.
      7. Procura, só nos dois lugares seguros de tocar sem admin (pasta
         de Inicialização do usuário + chave Run do usuário), qualquer
         atalho/entrada com "legion", "lenovo", "armoury", "asus" ou
         "rog" no nome, e desativa (move/remove COM backup, nunca apaga
         de vez) o que achar. Isso é um "melhor esforço" de verdade: se o
         Legion Space ou o Armoury Crate SE usarem outro mecanismo de
         inicialização (serviço do Windows, tarefa agendada, etc.), este
         script não vai achar — aí o jeito seguro de desativar na mão é
         Configurações > Aplicativos > Inicializar (ou Gerenciador de
         Tarefas > aba Inicializar).
      8. (novo nesta leva) Instala o RTSS (RivaTuner Statistics Server,
         overlay de FPS) via `winget`, SE ele ainda não estiver instalado
         — pedido do usuário pra automatizar isso também. "Melhor
         esforço" igual os passos 6/7: se o pacote não existir nessa
         fonte do winget, ou não tiver internet, só vira aviso no log.
      9. Grava um log (install-log.txt) e um backup (kiosk-backup.json)
         dentro da própria pasta de instalação — é o que
         Uninstall-Launcher.ps1 usa pra desfazer tudo depois.

.PARAMETER SkipPublish
    Usa uma pasta `publish\` que já exista ao lado deste script (por
    exemplo, copiada de outra máquina onde você já rodou
    `dotnet publish`) em vez de publicar de novo agora. Use isto se
    estiver rodando o instalador direto no Legion Go 2 / ROG Ally e essa
    máquina não tiver o SDK do .NET instalado — só o app publicado
    (self-contained) precisa estar lá, não o SDK.

.PARAMETER NoDesktopShortcut
    Não cria o atalho na Área de Trabalho (ainda cria o do Menu Iniciar e
    o de inicialização automática).

.PARAMETER NoWatchdog
    Usa o comportamento antigo e mais simples: um atalho comum na pasta
    de Inicialização do Windows, que abre o launcher uma vez no login e
    NÃO reabre se ele for fechado (Alt+F4, crash, etc) — fechar sempre
    volta pro desktop até você abrir de novo na mão. Sem isto (padrão),
    é registrada uma Tarefa Agendada que também reabre o launcher
    automaticamente se ele cair.

.EXAMPLE
    .\Install-Launcher.ps1
    Publica e instala tudo numa máquina com o .NET 8 SDK instalado.

.EXAMPLE
    .\Install-Launcher.ps1 -SkipPublish
    Instala usando uma pasta publish\ já existente (copiada de outra
    máquina) — não precisa do SDK do .NET presente aqui.
#>
[CmdletBinding()]
param(
    [switch]$SkipPublish,
    [switch]$NoDesktopShortcut,
    [switch]$NoWatchdog
)

$ErrorActionPreference = 'Stop'

$scriptDir = $PSScriptRoot
# $scriptDir agora e installer\Arquivos (pasta reorganizada nesta leva -
# antes era installer\ diretamente), entao repoRoot precisa subir mais um
# nivel: Arquivos -> installer -> raiz do repo.
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
        $wingetCmd = Get-Command winget -ErrorAction SilentlyContinue

        if ($wingetCmd) {
            $answer = Read-Host "O .NET 8 SDK nao esta instalado. Quer que eu instale agora via winget? (S/N)"
            if ($answer -match '^(s|sim|y|yes)$') {
                Write-Log "Instalando .NET 8 SDK via winget (pode demorar um pouco)..."
                & winget install --id Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements
                if ($LASTEXITCODE -ne 0) {
                    Write-Log "ERRO: 'winget install' falhou (codigo $LASTEXITCODE). Instala manualmente em https://dotnet.microsoft.com/download/dotnet/8.0 e roda este script de novo."
                    exit 1
                }

                # winget grava o novo PATH no registro, mas o processo atual
                # do PowerShell nao recarrega isso sozinho - forca a
                # releitura aqui pra conseguir achar o 'dotnet' recem
                # instalado sem precisar fechar e abrir o terminal de novo.
                $env:Path = [System.Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [System.Environment]::GetEnvironmentVariable('Path', 'User')
                $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue

                if (-not $dotnetCmd) {
                    Write-Log "ERRO: instalei o .NET SDK, mas nao consegui achar o 'dotnet' nesta mesma janela ainda. Fecha este terminal, abre um novo, e roda o instalador de novo (so precisa dessa vez a mais)."
                    exit 1
                }
                Write-Log ".NET SDK instalado com sucesso, seguindo com a publicacao."
            }
            else {
                Write-Log "ERRO: 'dotnet' nao encontrado no PATH. Instale o .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0) ou rode este script com -SkipPublish numa pasta que ja tenha 'publish\' copiada de outra maquina."
                exit 1
            }
        }
        else {
            Write-Log "ERRO: 'dotnet' nao encontrado no PATH, e 'winget' tambem nao esta disponivel pra instalar automatico. Instale o .NET 8 SDK manualmente (https://dotnet.microsoft.com/download/dotnet/8.0) ou rode este script com -SkipPublish numa pasta que ja tenha 'publish\' copiada de outra maquina."
            exit 1
        }
    }

    $projectPath = Join-Path $repoRoot 'src\HandheldLauncher.App'
    Write-Log "Publicando (dotnet publish, self-contained win-x64, Release)..."
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    & dotnet publish $projectPath -c Release -r win-x64 --self-contained true -o $publishDir
    if ($LASTEXITCODE -ne 0) {
        Write-Log "ERRO: 'dotnet publish' falhou (codigo $LASTEXITCODE). Ve a saida do dotnet acima, me manda exatamente o que apareceu."
        exit 1
    }
    Write-Log "Publicacao concluida em: $publishDir"
}
else {
    if (-not (Test-Path $publishDir)) {
        Write-Log "ERRO: -SkipPublish foi usado, mas nao achei a pasta 'publish\' ao lado deste script ($publishDir). Copia a pasta 'publish\' gerada em outra maquina pra aqui, ou roda sem -SkipPublish numa maquina com o SDK do .NET."
        exit 1
    }
    Write-Log "Usando publicacao existente em: $publishDir (-SkipPublish)"
}

# ---------------------------------------------------------------------------
# 2. Copiar para a pasta de instalacao
# ---------------------------------------------------------------------------
Write-Log "Copiando arquivos para: $installDir"
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir '*') -Destination $installDir -Recurse -Force

$exePath = Join-Path $installDir $exeName
if (-not (Test-Path $exePath)) {
    Write-Log "ERRO: nao encontrei $exeName dentro da publicacao. Algo na publicacao saiu diferente do esperado - confere a saida do 'dotnet publish' acima e me manda."
    exit 1
}

# ---------------------------------------------------------------------------
# 3 & 4. Atalhos (Area de Trabalho, Menu Iniciar, Inicializacao automatica)
# ---------------------------------------------------------------------------
$wshShell = New-Object -ComObject WScript.Shell

function New-AppShortcut {
    param([string]$ShortcutPath)
    $shortcut = $wshShell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $exePath
    $shortcut.WorkingDirectory = $installDir
    $shortcut.Description = 'Handheld Launcher'
    $shortcut.Save()
    Write-Log "Atalho criado: $ShortcutPath"
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

if ($NoWatchdog) {
    New-AppShortcut -ShortcutPath $startupShortcut
    Write-Log "Inicializacao automatica configurada via pasta de Inicializacao do Windows (shell:startup) - abre uma vez no login, NAO reabre se for fechado (-NoWatchdog). NAO substitui o shell, o desktop normal continua por baixo."
}
else {
    # Watchdog via Tarefa Agendada em vez de um atalho comum: alem de
    # abrir no login, confere a cada 1 minuto se o launcher ainda esta
    # rodando e reabre se nao estiver - cobre Alt+F4, crash, ou qualquer
    # outro jeito dele fechar sozinho. "Sair" no menu geral do proprio
    # launcher desativa esta tarefa antes de fechar (ver DisableWatchdogIfPresent
    # em MainWindow.axaml.cs), entao uma saida deliberada continua
    # funcionando de verdade - so o fechamento inesperado e que volta.
    $triggerLogon = New-ScheduledTaskTrigger -AtLogOn
    $triggerRepeat = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 1) -RepetitionDuration (New-TimeSpan -Days 3650)
    $watchdogAction = New-ScheduledTaskAction -Execute $exePath -WorkingDirectory $installDir
    # -ExecutionTimeLimit ([TimeSpan]::Zero) == "sem limite de tempo": o
    # padrao do Task Scheduler mata a tarefa depois de 72h, o que seria
    # fatal pra um app que deveria ficar rodando indefinidamente.
    $watchdogSettings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)

    Register-ScheduledTask -TaskName $watchdogTaskName -Trigger @($triggerLogon, $triggerRepeat) -Action $watchdogAction -Settings $watchdogSettings -Description 'Garante que o Handheld Launcher esteja sempre rodando; reabre em ate 1 minuto se fechar.' -Force | Out-Null

    Write-Log "Inicializacao automatica + watchdog configurados via Tarefa Agendada '$watchdogTaskName': abre no login e confere a cada 1 minuto se ainda esta rodando, reabrindo se fechar (Alt+F4, crash, etc). 'Sair' no menu geral do launcher desativa essa tarefa antes de fechar, pra uma saida deliberada funcionar de verdade. Pra desativar na mao sem passar pelo app: Disable-ScheduledTask -TaskName '$watchdogTaskName' (reativa com Enable-ScheduledTask ou rodando este instalador de novo)."
}

# ---------------------------------------------------------------------------
# 5. settings.json - liga o modo kiosk, preserva a API key se ja existir
# ---------------------------------------------------------------------------
$settingsPath = Join-Path $env:APPDATA 'HandheldLauncher\settings.json'
New-Item -ItemType Directory -Path (Split-Path $settingsPath) -Force | Out-Null

$settings = [ordered]@{ SteamGridDbApiKey = $null; KioskMode = $true }
if (Test-Path $settingsPath) {
    try {
        $existing = Get-Content $settingsPath -Raw | ConvertFrom-Json
        if ($existing.SteamGridDbApiKey) { $settings.SteamGridDbApiKey = $existing.SteamGridDbApiKey }
    }
    catch {
        Write-Log "Aviso: settings.json existente nao pode ser lido (formato inesperado) - recriando do zero, com KioskMode ligado."
    }
}

# Se ainda nao tem key (maquina nova, sem settings.json previo), usa uma
# key padrao de um arquivo local opcional em vez de obrigar digitar na
# mao em toda instalacao nova - esse arquivo NUNCA e versionado no git
# (ver .gitignore), fica so na sua maquina. Crie-o uma vez com:
#   "sua-chave-aqui" | Set-Content installer\steamgriddb-api-key.txt
# e a partir dai toda instalacao (qualquer maquina que tenha esse
# arquivo copiado junto) ja sai configurada sozinha.
if (-not $settings.SteamGridDbApiKey) {
    $defaultKeyPath = Join-Path $scriptDir 'steamgriddb-api-key.txt'
    if (Test-Path $defaultKeyPath) {
        $defaultKey = (Get-Content $defaultKeyPath -Raw).Trim()
        if ($defaultKey) {
            $settings.SteamGridDbApiKey = $defaultKey
            Write-Log "API key do SteamGridDB preenchida a partir de installer\steamgriddb-api-key.txt."
        }
    }
}

($settings | ConvertTo-Json) | Set-Content -Path $settingsPath -Encoding UTF8
Write-Log "Modo kiosk ativado em: $settingsPath"

# ---------------------------------------------------------------------------
# 6 & 7 sao "melhor esforco": nao devem derrubar uma instalacao que, até
# aqui, já copiou os arquivos e ligou o modo kiosk com sucesso. Qualquer
# erro aqui vira um aviso no log, nao um exit 1.
# ---------------------------------------------------------------------------
$backup = [ordered]@{
    GameDVR_Enabled        = $null
    AppCaptureEnabled      = $null
    DisabledStartupEntries = @()
}

try {
    # -----------------------------------------------------------------
    # 6. Xbox Game Bar / GameDVR - chaves de registro bem documentadas
    #    (HKCU, nao precisa de admin), com backup do valor anterior.
    # -----------------------------------------------------------------
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

    $backup.GameDVR_Enabled = Get-BackedUpValue -Path $gameConfigPath -Name 'GameDVR_Enabled'
    $backup.AppCaptureEnabled = Get-BackedUpValue -Path $gameDvrPath -Name 'AppCaptureEnabled'

    New-Item -Path $gameConfigPath -Force | Out-Null
    New-ItemProperty -Path $gameConfigPath -Name 'GameDVR_Enabled' -PropertyType DWord -Value 0 -Force | Out-Null

    New-Item -Path $gameDvrPath -Force | Out-Null
    New-ItemProperty -Path $gameDvrPath -Name 'AppCaptureEnabled' -PropertyType DWord -Value 0 -Force | Out-Null

    Write-Log "Xbox Game Bar/DVR: GameDVR_Enabled e AppCaptureEnabled zerados (valores antigos salvos no backup pra desfazer com o uninstaller). Isso impede a captura em segundo plano e o Game Bar de abrir automatico; nao desinstala o app em si."
}
catch {
    Write-Log "Aviso: nao consegui ajustar o registro do Xbox Game Bar/DVR ($($_.Exception.Message)). Nao e critico - a instalacao do launcher em si continua normal. Se quiser, desativa o Game Bar na mao em Configuracoes > Jogos > Xbox Game Bar."
}

try {
    # -----------------------------------------------------------------
    # 7. Busca "melhor esforco" por software do fabricante (Legion
    #    Space, Armoury Crate SE, etc.) nos dois lugares seguros de
    #    tocar sem admin: pasta de Inicializacao do usuario e HKCU Run.
    # -----------------------------------------------------------------
    $vendorKeywords = @('legion', 'lenovo', 'armoury', 'asus', 'rog')
    $disabledDir = Join-Path $installDir 'disabled-startup-backup'
    New-Item -ItemType Directory -Path $disabledDir -Force | Out-Null

    Write-Log "Procurando entradas de inicializacao de software do fabricante (Legion Space / Armoury Crate / etc.) pra desativar..."

    # 7a. Atalhos na pasta de Inicializacao do usuario
    Get-ChildItem -Path $startupDir -Filter '*.lnk' -ErrorAction SilentlyContinue | ForEach-Object {
        $nameLower = $_.BaseName.ToLowerInvariant()
        $isOwnShortcut = $_.FullName -eq $startupShortcut
        $matchesVendor = ($vendorKeywords | Where-Object { $nameLower -like "*$_*" }) | Select-Object -First 1
        if (-not $isOwnShortcut -and $matchesVendor) {
            $destination = Join-Path $disabledDir $_.Name
            Move-Item -Path $_.FullName -Destination $destination -Force
            $backup.DisabledStartupEntries += [ordered]@{ Type = 'StartupShortcut'; OriginalPath = $_.FullName; BackupPath = $destination }
            Write-Log "Desativado (movido pra backup): atalho de inicializacao '$($_.Name)'"
        }
    }

    # 7b. Valores na chave Run do usuario atual (HKCU, nao precisa de admin)
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
                Write-Log "Desativado (removido, com backup): entrada de registro Run '$valueName'"
            }
        }
    }

    if ($backup.DisabledStartupEntries.Count -eq 0) {
        Write-Log "Nenhuma entrada do fabricante encontrada na pasta de Inicializacao nem na chave Run do usuario. Se o Legion Space / Armoury Crate SE continuar abrindo sozinho, desativa manualmente em Configuracoes > Aplicativos > Inicializar (ou Gerenciador de Tarefas > aba Inicializar) - funciona independente de como o fabricante configurou a inicializacao."
    }
}
catch {
    Write-Log "Aviso: a busca por software do fabricante parou num erro ($($_.Exception.Message)). Nao e critico - a instalacao do launcher em si continua normal. Desativa manualmente em Configuracoes > Aplicativos > Inicializar se precisar."
}

# ---------------------------------------------------------------------------
# 8. RTSS (overlay de FPS) - "melhor esforco" via winget, SO se ainda nao
#    estiver instalado. Pedido do usuario nesta leva ("nao tem como por
#    no bat pra executar a instalacao tambem desse app pra ficar
#    automatizado?"). Mesmos dois caminhos fixos que
#    PerformanceOverlayService usa pra detectar - nunca reinstala por
#    cima de uma instalacao ja existente. Pacote "Guru3D.RTSS" (id do
#    winget, confirmado via busca antes de escrever isto - NUNCA
#    verificado rodando de verdade neste sandbox, que nao tem winget).
#    NUNCA e critico: se o winget nao tiver esse pacote disponivel
#    (fonte desatualizada, sem internet, etc.), so vira aviso no log - a
#    instalacao do launcher em si segue normal, e o launcher continua
#    detectando/abrindo o RTSS manualmente instalado depois (ver
#    PerformanceOverlayService / item "Overlay FPS" no menu geral).
# ---------------------------------------------------------------------------
try {
    $rtssPaths = @(
        'C:\Program Files (x86)\RivaTuner Statistics Server\RTSS.exe',
        'C:\Program Files\RivaTuner Statistics Server\RTSS.exe'
    )
    $rtssInstalled = $rtssPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($rtssInstalled) {
        Write-Log "RTSS ja esta instalado em: $rtssInstalled (nao reinstala por cima)."
    }
    else {
        $wingetCmd = Get-Command winget -ErrorAction SilentlyContinue
        if ($wingetCmd) {
            Write-Log "RTSS nao encontrado - instalando via winget (id Guru3D.RTSS)..."
            & winget install --id Guru3D.RTSS --silent --accept-source-agreements --accept-package-agreements
            if ($LASTEXITCODE -eq 0) {
                Write-Log "RTSS instalado com sucesso via winget."
            }
            else {
                Write-Log "Aviso: 'winget install --id Guru3D.RTSS' retornou codigo $LASTEXITCODE (pacote pode nao estar disponivel nessa maquina/fonte do winget, ou essa nao e a id certa - nunca testado de verdade). Nao e critico - baixe manualmente em https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/ se quiser o overlay de FPS, ou use o item 'Overlay FPS (RTSS): baixar' no menu do launcher depois."
            }
        }
        else {
            Write-Log "Aviso: 'winget' nao disponivel - RTSS nao foi instalado automaticamente. Baixe manualmente em https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/ se quiser o overlay de FPS, ou use o item 'Overlay FPS (RTSS): baixar' no menu do launcher depois."
        }
    }
}
catch {
    Write-Log "Aviso: instalacao automatica do RTSS parou num erro ($($_.Exception.Message)). Nao e critico - a instalacao do launcher em si continua normal."
}

# ---------------------------------------------------------------------------
# 9. Salvar backup + log
# ---------------------------------------------------------------------------
($backup | ConvertTo-Json -Depth 5) | Set-Content -Path $backupPath -Encoding UTF8
$logLines | Set-Content -Path $logPath -Encoding UTF8

Write-Log ""
Write-Log "== Instalacao concluida =="
Write-Log "App instalado em: $installDir"
Write-Log "Log completo: $logPath"
Write-Log "Pra desinstalar (remove tudo e desfaz os ajustes de registro/inicializacao): .\Uninstall-Launcher.ps1"
