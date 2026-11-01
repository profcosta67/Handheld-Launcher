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
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("handheld-pawnio-" + [guid]::NewGuid().ToString("N"))
$archivePath = Join-Path $tempRoot "release.zip"

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    Invoke-WebRequest -Uri $archiveUrl -OutFile $archivePath

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
