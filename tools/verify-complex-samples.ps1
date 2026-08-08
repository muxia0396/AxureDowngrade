param(
    [Parameter(Mandatory = $true)]
    [string]$Axure9Directory,
    [Parameter(Mandatory = $true)]
    [string]$Axure11Directory,
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
        "target\complex-verification"
}
if (-not (Test-Path -LiteralPath $BridgePath -PathType Leaf)) {
    throw "The downgrade bridge was not found: $BridgePath"
}

$trainingDirectory = Join-Path $Axure11Directory `
    "DefaultSettings\Training"
$cases = @(
    [pscustomobject]@{
        Name = "prototype-starter"
        File = "Prototype Starter.rp"
    },
    [pscustomobject]@{
        Name = "prototyping-basics"
        File = "Prototyping Basics.rp"
    },
    [pscustomobject]@{
        Name = "quick-win"
        File = "Quick Win.rp"
    },
    [pscustomobject]@{
        Name = "ux-prototyping"
        File = "UX Prototyping.rp"
    }
)

function ConvertTo-CountTable($recordTypes) {
    $counts = @{}
    $recordTypes.PSObject.Properties | ForEach-Object {
        $counts[$_.Name] = [int]$_.Value
    }
    return $counts
}

New-Item -ItemType Directory -Path $OutputDirectory -Force |
    Out-Null
$coveredRecordTypes = @{}
$results = foreach ($case in $cases) {
    $sourcePath = Join-Path $trainingDirectory $case.File
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Official Axure 11 sample was not found: $sourcePath"
    }
    $outputPath = Join-Path $OutputDirectory `
        "$($case.Name)-rp9.rp"

    $bridgeJson = & $BridgePath `
        $Axure9Directory `
        $sourcePath `
        $outputPath
    if ($LASTEXITCODE -ne 0) {
        throw "Conversion failed for $($case.File): $bridgeJson"
    }
    $bridgeReport = $bridgeJson | ConvertFrom-Json

    $sourceInventory = (
        & $BridgePath --inventory $Axure9Directory $sourcePath
    ) | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) {
        throw "Source inventory failed for $($case.File)"
    }
    $outputInventory = (
        & $BridgePath --inventory $Axure9Directory $outputPath
    ) | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) {
        throw "Output inventory failed for $($case.File)"
    }

    $sourceTypes = ConvertTo-CountTable `
        $sourceInventory.recordTypes
    $outputTypes = ConvertTo-CountTable `
        $outputInventory.recordTypes
    $allTypeNames = @(
        $sourceTypes.Keys + $outputTypes.Keys |
            Sort-Object -Unique
    )
    $changedTypes = @($allTypeNames | Where-Object {
        $sourceTypes[$_] -ne $outputTypes[$_]
    })
    $changedStaticTypes = @($changedTypes | Where-Object {
        $_ -notlike "Axure:Interaction*" -and
        $_ -notlike "Axure:Interation*"
    })
    if ($changedStaticTypes.Count -gt 0) {
        throw (
            "Static record counts changed in {0}: {1}" -f
            $case.File,
            ($changedStaticTypes -join ", ")
        )
    }

    $sourceTypes.GetEnumerator() | ForEach-Object {
        if (-not $_.Key.StartsWith(
            "Axure:Interaction",
            [StringComparison]::Ordinal) -and
            -not $_.Key.StartsWith(
                "Axure:Interation",
                [StringComparison]::Ordinal)) {
            $coveredRecordTypes[$_.Key] =
                [int]$coveredRecordTypes[$_.Key] + [int]$_.Value
        }
    }

    [pscustomobject]@{
        sample = $case.File
        sourceFormat = [int]$sourceInventory.formatMajor
        outputFormat = [int]$outputInventory.formatMajor
        pages = @($sourceInventory.packages | Where-Object {
            $_.kind -eq "page"
        }).Count
        objectPackages = @($sourceInventory.packages | Where-Object {
            $_.kind -eq "object"
        }).Count
        sourceRecords = (
            $sourceInventory.packages |
                Measure-Object recordCount -Sum
        ).Sum
        outputRecords = (
            $outputInventory.packages |
                Measure-Object recordCount -Sum
        ).Sum
        recordTypeKinds = $allTypeNames.Count
        removedInteractionTypeKinds = $changedTypes.Count
        changedStaticTypeKinds = $changedStaticTypes.Count
        staticRecordsVerified =
            [int]$bridgeReport.staticRecordsVerified
        staticScalarsVerified =
            [int]$bridgeReport.staticScalarsVerified
        output = $outputPath
    }
}

$requiredCoverage = @(
    "Axure:DiagramObject:VectorShape",
    "Axure:DiagramObject:ImageBox",
    "Axure:DiagramObject:DynamicPanel",
    "Axure:DiagramObject:TextBox",
    "Axure:DiagramObject:Checkbox",
    "Axure:DiagramObject:RadioButton",
    "Axure:DiagramObject:Connector"
)
$missingCoverage = @($requiredCoverage | Where-Object {
    -not $coveredRecordTypes.ContainsKey($_) -or
    $coveredRecordTypes[$_] -lt 1
})
if ($missingCoverage.Count -gt 0) {
    throw (
        "Official samples did not cover required static records: {0}" -f
        ($missingCoverage -join ", ")
    )
}

$report = [pscustomobject]@{
    generatedAt = (Get-Date).ToString("o")
    cases = $results
    coveredStaticRecordTypes = [pscustomobject]$coveredRecordTypes
}
$reportPath = Join-Path $OutputDirectory `
    "complex-verification-report.json"
$report |
    ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $reportPath -Encoding utf8
$results |
    Format-Table sample, pages, objectPackages, sourceRecords, outputRecords, `
        recordTypeKinds, changedStaticTypeKinds -AutoSize
Write-Host "Complex verification report: $reportPath"
