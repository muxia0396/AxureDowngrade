param(
    [string]$Axure9Directory = "D:\ToolsWork\Axure9"
)

$ErrorActionPreference = "Stop"
$workspace = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $workspace "desktop\src-tauri\bin"
$compiler = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
$source = Join-Path $PSScriptRoot "AxureContainerRewriter\Program.cs"

if (-not (Test-Path -LiteralPath $compiler)) {
    throw ".NET Framework C# compiler not found: $compiler"
}

$legacyLz4 = Join-Path $Axure9Directory "K4os.Compression.LZ4.Legacy.dll"
$lz4 = Join-Path $Axure9Directory "K4os.Compression.LZ4.dll"
foreach ($dependency in @($legacyLz4, $lz4)) {
    if (-not (Test-Path -LiteralPath $dependency)) {
        throw "Axure 9 dependency not found: $dependency"
    }
}

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$bridge = Join-Path $outputDirectory "AxureDowngradeBridge.exe"

& $compiler `
    /nologo `
    /platform:x86 `
    /optimize+ `
    /r:System.Web.Extensions.dll `
    "/r:$lz4" `
    "/r:$legacyLz4" `
    "/out:$bridge" `
    $source

if ($LASTEXITCODE -ne 0) {
    throw "Bridge compilation failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath $lz4 -Destination $outputDirectory -Force
Copy-Item -LiteralPath $legacyLz4 -Destination $outputDirectory -Force
Write-Output "Built $bridge"
