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
      4. Cria atalho na pasta de Inicialização do Windows (shell:startup)
         pra abrir sozinho a cada login — SEM substituir o shell do
         Windows. O desktop normal continua por baixo; Alt+Tab e Alt+F4
         continuam funcionando, fechar o app sempre volta pro desktop.
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
      8. Grava um log (install-log.txt) e um backup (kiosk-backup.json)
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
    [switch]$NoDesktopShortcut
)

$ErrorActionPreference = 'Stop'

$scriptDir = $PSScriptRoot
$repoRoot = Split-Path -Parent $scriptDir
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
New-AppShortcut -ShortcutPath $startupShortcut
Write-Log "Inicializacao automatica configurada via pasta de Inicializacao do Windows (shell:startup) - NAO substitui o shell, o desktop normal continua por baixo. Pra desativar na mao sem o uninstaller: apaga esse atalho, ou desativa em Configuracoes > Aplicativos > Inicializar."

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
# 8. Salvar backup + log
# ---------------------------------------------------------------------------
($backup | ConvertTo-Json -Depth 5) | Set-Content -Path $backupPath -Encoding UTF8
$logLines | Set-Content -Path $logPath -Encoding UTF8

Write-Log ""
Write-Log "== Instalacao concluida =="
Write-Log "App instalado em: $installDir"
Write-Log "Log completo: $logPath"
Write-Log "Pra desinstalar (remove tudo e desfaz os ajustes de registro/inicializacao): .\Uninstall-Launcher.ps1"
