param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"

# Release imutável do BtbN/FFmpeg-Builds (FFmpeg 9.0, Windows x64, GPL static).
$releaseTag = "autobuild-2026-11-03-12-50"
$archiveName = "ffmpeg-n9.0.2-45-g1e65c6cd5d-win64-gpl-9.0.zip"
$downloadUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/$releaseTag/$archiveName"
$expectedSha256 = "ab9939af65bea84c161b4f80056511aa346d65315e8bec87ea2b8fc675839711"

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("HandheldLauncher-FFmpeg-" + [guid]::NewGuid().ToString("N"))
$archivePath = Join-Path $tempRoot $archiveName
$extractPath = Join-Path $tempRoot "extracted"
$targetPath = Join-Path $OutputDirectory "ffmpeg.exe"

try {
    New-Item -ItemType Directory -Force -Path $tempRoot, $extractPath, $OutputDirectory | Out-Null
    Write-Host "Baixando FFmpeg fixado em $releaseTag..."
    Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath -UseBasicParsing

    $actualSha256 = (Get-FileHash -Path $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSha256 -ne $expectedSha256) {
        throw "SHA-256 inválido para FFmpeg. Esperado: $expectedSha256; recebido: $actualSha256"
    }

    Write-Host "SHA-256 confirmado. Extraindo ffmpeg.exe..."
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractPath -Force
    $ffmpeg = Get-ChildItem -Path $extractPath -Filter "ffmpeg.exe" -File -Recurse |
        Where-Object { $_.Directory.Name -eq "bin" } |
        Select-Object -First 1

    if (-not $ffmpeg) {
        throw "O pacote FFmpeg foi extraído, mas ffmpeg.exe não foi encontrado em uma pasta bin."
    }

    Copy-Item -LiteralPath $ffmpeg.FullName -Destination $targetPath -Force
    if ((Get-Item $targetPath).Length -lt 1MB) {
        throw "ffmpeg.exe extraído parece incompleto."
    }

    $versionOutput = & $targetPath -version 2>&1
    if ($LASTEXITCODE -ne 0 -or -not ($versionOutput -match "ffmpeg version")) {
        throw "A validação de execução do ffmpeg.exe falhou."
    }

    $licenseText = @"
FFmpeg bundled with Handheld Launcher
=====================================

Binary: ffmpeg-n9.0.2-45-g1e65c6cd5d-win64-gpl-9.0.zip
Build release: https://github.com/BtbN/FFmpeg-Builds/releases/tag/$releaseTag
FFmpeg source: https://github.com/FFmpeg/FFmpeg/tree/n9.0.2-45-g1e65c6cd5d
Build scripts: https://github.com/BtbN/FFmpeg-Builds/tree/$releaseTag

This is a GPL-enabled FFmpeg build. FFmpeg is a separate executable launched
by Handheld Launcher; it is not linked into the launcher process.

License text: https://github.com/FFmpeg/FFmpeg/blob/n9.0.2-45-g1e65c6cd5d/COPYING.GPLv3
The corresponding FFmpeg source is available at the source URL above.
The build project and its source/build instructions are available at the
BtbN/FFmpeg-Builds URL above.

This notice identifies the third-party component and its corresponding
upstream source. FFmpeg is not authored by the Handheld Launcher project.
"@
    Set-Content -LiteralPath (Join-Path $OutputDirectory "FFmpeg-LICENSE.txt") -Value $licenseText -Encoding UTF8
    Write-Host "FFmpeg instalado e validado em $targetPath"
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
