param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"

# Official, signed PawnIO module release. Pin the complete archive digest so
# a changed or substituted upstream download is rejected before extraction.
$release = "0.2.11"
$archiveUrl = "https://github.com/namazso/PawnIO.Modules/releases/download/$release/release_0_2_11.zip"
$expectedSha256 = "43608cb89bc84247fef1368a139013f7d043e17db6d6c8dfc9b46bf0905a81f4"
# Official PawnIO driver installer 2.2.0 (release asset SHA-256).
$setupUrl = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe"
$setupSha256 = "1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032"
$setupPath = Join-Path $OutputDirectory "PawnIO_setup.exe"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("handheld-pawnio-" + [guid]::NewGuid().ToString("N"))
$archivePath = Join-Path $tempRoot "release.zip"

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    Invoke-WebRequest -Uri $archiveUrl -OutFile $archivePath

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $setupTemp = Join-Path $tempRoot "PawnIO_setup.exe"
    Invoke-WebRequest -Uri $setupUrl -OutFile $setupTemp
    $setupActualHash = (Get-FileHash -Path $setupTemp -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($setupActualHash -ne $setupSha256) {
        throw "Hash SHA-256 inesperado no instalador PawnIO 2.2.0. Esperado: $setupSha256; recebido: $setupActualHash"
    }
    Copy-Item -LiteralPath $setupTemp -Destination $setupPath -Force
    Write-Host "Instalador oficial PawnIO 2.2.0 preparado: $setupPath"

    $actualHash = (Get-FileHash -Path $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedSha256) {
        throw "Hash SHA-256 inesperado no pacote PawnIO.Modules $release. Esperado: $expectedSha256; recebido: $actualHash"
    }

    $extractPath = Join-Path $tempRoot "extracted"
    Expand-Archive -Path $archivePath -DestinationPath $extractPath -Force

    $modules = @(Get-ChildItem -Path $extractPath -Filter "RyzenSMU.bin" -File -Recurse)
    if ($modules.Count -ne 1) {
        throw "Esperado exatamente um RyzenSMU.bin no release oficial $release; encontrados: $($modules.Count)."
    }

    if ($modules[0].Length -lt 1024) {
        throw "RyzenSMU.bin está vazio ou menor que o esperado."
    }

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $destination = Join-Path $OutputDirectory "RyzenSMU.bin"
    Copy-Item -LiteralPath $modules[0].FullName -Destination $destination -Force

    Write-Host "PawnIO RyzenSMU $release preparado: $destination"
    Write-Host "SHA256 do arquivo: $((Get-FileHash -Path $destination -Algorithm SHA256).Hash.ToLowerInvariant())"
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item -Path $tempRoot -Recurse -Force
    }
}
