param(
    [Parameter(Mandatory = $true)]
    [string]$Axure9Directory,
    [string]$BridgePath = "",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($BridgePath)) {
    $BridgePath = Join-Path $repositoryRoot `
        "desktop\src-tauri\bin\AxureDowngradeBridge.exe"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot `
        "target\fixture-verification"
}

$axureExe = Join-Path $Axure9Directory "AxureRP9.exe"
if (-not (Test-Path -LiteralPath $axureExe -PathType Leaf)) {
    throw "AxureRP9.exe was not found in: $Axure9Directory"
}
if (-not (Test-Path -LiteralPath $BridgePath -PathType Leaf)) {
    throw "The downgrade bridge was not found: $BridgePath"
}

$fixtureDirectory = Join-Path $repositoryRoot "fixtures\axure11"
$fixtures = Get-ChildItem -LiteralPath $fixtureDirectory -Filter "*.rp" |
    Sort-Object Name
if ($fixtures.Count -eq 0) {
    throw "No RP11 fixtures were found in: $fixtureDirectory"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$results = foreach ($fixture in $fixtures) {
    $outputName = "{0}-rp9.rp" -f $fixture.BaseName
    $outputPath = Join-Path $OutputDirectory $outputName
    $bridgeOutput = & $BridgePath `
        $Axure9Directory `
        $fixture.FullName `
        $outputPath
    if ($LASTEXITCODE -ne 0) {
        throw "Bridge failed for $($fixture.Name): $bridgeOutput"
    }

    $report = $bridgeOutput | ConvertFrom-Json
    if ($report.status -ne "success") {
        throw "Bridge did not report success for $($fixture.Name)"
    }
    if ($report.pagesRewritten -lt 1 -or
        $report.designDocumentsRewritten -lt 1 -or
        $report.settingsRewritten -lt 1 -or
        $report.rp9RequiredFieldsAdded -lt 1 -or
        $report.staticRecordsVerified -lt 1 -or
        $report.staticScalarsVerified -lt 1) {
        throw "Incomplete verification report for $($fixture.Name)"
    }

    $bytes = [System.IO.File]::ReadAllBytes($outputPath)
    if ($bytes.Length -lt 4 -or
        $bytes[0] -ne 0xAC -or
        $bytes[1] -ne 0xEF -or
        [BitConverter]::ToUInt16($bytes, 2) -ne 9) {
        throw "Output is not an Axure RP 9 container: $outputPath"
    }

    [pscustomobject]@{
        fixture = $fixture.Name
        output = $outputPath
        pagesRewritten = [int]$report.pagesRewritten
        interactionsRemoved = [int]$report.interactionsRemoved
        unsupportedStylePropertiesRemoved =
            [int]$report.unsupportedStylePropertiesRemoved
        rp9RequiredFieldsAdded = [int]$report.rp9RequiredFieldsAdded
        staticRecordsVerified = [int]$report.staticRecordsVerified
        staticScalarsVerified = [int]$report.staticScalarsVerified
        outputBytes = [int64]$report.outputBytes
    }
}

$reportPath = Join-Path $OutputDirectory "verification-report.json"
$results |
    ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath $reportPath -Encoding utf8
$results | Format-Table -AutoSize
Write-Host "Verification report: $reportPath"
