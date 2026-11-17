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

# Plano B (17/11/2026): o BtbN apaga autobuilds antigos e o pacote acima saiu do ar. O mesmo
# ffmpeg.exe (conferido pelo hash acima quando foi empacotado pela primeira vez) vai dentro de
# todo pacote publicado deste repositório; o plano B tira de uma das versões publicadas mais
# recentes (as antigas são apagadas pela limpeza do release.yml) e confere o hash do próprio exe.
$fallbackReleasesApi = "https://api.github.com/repos/profcosta67/Handheld-Launcher/releases?per_page=15"
$fallbackEntry = "lib/app/Tools/ffmpeg.exe"
$fallbackExeSha256 = "b48132b8a9300856c2dac0be9b1eedec7eeedeef6148754cd7a3450dbd4b274a"

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("HandheldLauncher-FFmpeg-" + [guid]::NewGuid().ToString("N"))
$archivePath = Join-Path $tempRoot $archiveName
$extractPath = Join-Path $tempRoot "extracted"
$targetPath = Join-Path $OutputDirectory "ffmpeg.exe"

try {
    New-Item -ItemType Directory -Force -Path $tempRoot, $extractPath, $OutputDirectory | Out-Null
    Write-Host "Baixando FFmpeg fixado em $releaseTag..."
    $upstreamOk = $true
    try { Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath -UseBasicParsing }
    catch { $upstreamOk = $false; Write-Host "Pacote original indisponível ($($_.Exception.Message)); usando o plano B." }

    if ($upstreamOk) {
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
    }
    else {
        $headers = @{ "User-Agent" = "HandheldLauncher-build" }
        if ($env:GITHUB_TOKEN) { $headers["Authorization"] = "Bearer $($env:GITHUB_TOKEN)" }
        $releases = Invoke-RestMethod -Uri $fallbackReleasesApi -Headers $headers
        $assets = @($releases | Where-Object { -not $_.draft } | ForEach-Object { $_.assets } |
            Where-Object { $_.name -like "*-full.nupkg" } | Select-Object -First 5)
        if ($assets.Count -eq 0) { throw "Nenhum pacote publicado encontrado para o plano B do FFmpeg." }

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $candidate = Join-Path $tempRoot "ffmpeg-fallback.exe"
        $found = $false
        foreach ($asset in $assets) {
            $packagePath = Join-Path $tempRoot "fallback.nupkg"
            Write-Host "Plano B: $($asset.name)"
            try { Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $packagePath -UseBasicParsing }
            catch { Write-Host "  falhou ao baixar: $($_.Exception.Message)"; continue }
            $zip = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
            try {
                $entry = $zip.GetEntry($fallbackEntry)
                if (-not $entry) { Write-Host "  sem ffmpeg.exe neste pacote"; continue }
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $candidate, $true)
            }
            finally { $zip.Dispose() }
            $exeSha256 = (Get-FileHash -Path $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($exeSha256 -eq $fallbackExeSha256) { $found = $true; break }
            Write-Host "  hash diferente ($exeSha256); tentando o próximo"
        }
        if (-not $found) { throw "Plano B do FFmpeg: nenhum pacote publicado tem o ffmpeg.exe com o hash esperado ($fallbackExeSha256)." }
        Copy-Item -LiteralPath $candidate -Destination $targetPath -Force
        Write-Host "SHA-256 do ffmpeg.exe confirmado (plano B)."
    }
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
