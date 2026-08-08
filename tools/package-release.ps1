[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifactRoot = Join-Path $repositoryRoot 'artifacts'
$releaseName = "AxureDowngrade-$Version-windows-x64-portable"
$portableDirectory = Join-Path $artifactRoot $releaseName
$archivePath = Join-Path $artifactRoot "$releaseName.zip"
$checksumPath = Join-Path $artifactRoot "$releaseName.sha256.txt"

function Assert-UnderArtifactRoot {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullArtifactRoot = [System.IO.Path]::GetFullPath($artifactRoot)
    if (-not $fullPath.StartsWith($fullArtifactRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the artifact directory: $fullPath"
    }
}

New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

foreach ($target in @($portableDirectory, $archivePath, $checksumPath)) {
    Assert-UnderArtifactRoot -Path $target
    if (Test-Path -LiteralPath $target) {
        if (-not $Force) {
            throw "Release target already exists: $target. Use -Force to replace it."
        }
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}

$packageFiles = @(
    @{
        Source = Join-Path $repositoryRoot 'target\release\axure-downgrade-desktop.exe'
        Destination = 'AxureDowngrade.exe'
    },
    @{
        Source = Join-Path $repositoryRoot 'desktop\src-tauri\bin\AxureDowngradeBridge.exe'
        Destination = 'bin\AxureDowngradeBridge.exe'
    },
    @{
        Source = Join-Path $repositoryRoot 'desktop\src-tauri\bin\K4os.Compression.LZ4.dll'
        Destination = 'bin\K4os.Compression.LZ4.dll'
    },
    @{
        Source = Join-Path $repositoryRoot 'desktop\src-tauri\bin\K4os.Compression.LZ4.Legacy.dll'
        Destination = 'bin\K4os.Compression.LZ4.Legacy.dll'
    },
    @{
        Source = Join-Path $repositoryRoot 'docs\ERROR_CODES.md'
        Destination = 'ERROR_CODES.md'
    },
    @{
        Source = Join-Path $repositoryRoot 'LICENSE'
        Destination = 'LICENSE'
    },
    @{
        Source = Join-Path $repositoryRoot 'NOTICE'
        Destination = 'NOTICE'
    },
    @{
        Source = Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md'
        Destination = 'THIRD_PARTY_NOTICES.md'
    },
    @{
        Source = Join-Path $repositoryRoot 'RELEASE_README.txt'
        Destination = 'README.txt'
    }
)

foreach ($file in $packageFiles) {
    if (-not (Test-Path -LiteralPath $file.Source -PathType Leaf)) {
        throw "Required release file is missing: $($file.Source)"
    }
}

$executablePath = Join-Path $repositoryRoot 'target\release\axure-downgrade-desktop.exe'
$executableVersion = (Get-Item -LiteralPath $executablePath).VersionInfo.ProductVersion
if ($executableVersion -ne $Version) {
    throw "Executable version $executableVersion does not match requested release $Version"
}

New-Item -ItemType Directory -Path (Join-Path $portableDirectory 'bin') -Force | Out-Null

foreach ($file in $packageFiles) {
    $destination = Join-Path $portableDirectory $file.Destination
    Copy-Item -LiteralPath $file.Source -Destination $destination
}

Compress-Archive -LiteralPath $portableDirectory -DestinationPath $archivePath -CompressionLevel Optimal

$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
$checksumLine = "$archiveHash  $releaseName.zip`r`n"
[System.IO.File]::WriteAllText(
    $checksumPath,
    $checksumLine,
    [System.Text.UTF8Encoding]::new($false)
)

Write-Host "Release directory: $portableDirectory"
Write-Host "Release archive:   $archivePath"
Write-Host "SHA-256:           $archiveHash"
