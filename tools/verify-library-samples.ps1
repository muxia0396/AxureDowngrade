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
        "target\library-verification"
}
if (-not (Test-Path -LiteralPath $BridgePath -PathType Leaf)) {
    throw "The downgrade bridge was not found: $BridgePath"
}

$libraryDirectory = Join-Path $Axure11Directory `
    "DefaultSettings\Libraries"
$cases = @(
    [pscustomobject]@{
        Name = "default-library"
        File = "Default.rplib"
    },
    [pscustomobject]@{
        Name = "flow-library"
        File = "Flow.rplib"
    },
    [pscustomobject]@{
        Name = "icons-library"
        File = "Icons.rplib"
    },
    [pscustomobject]@{
        Name = "sample-form-patterns"
        File = "Sample form patterns.rplib"
    },
    [pscustomobject]@{
        Name = "sample-ui-patterns"
        File = "Sample UI patterns.rplib"
    }
)
$requiredCoverage = @(
    "Axure:DiagramObject:Repeater",
    "Axure:DiagramObject:Table",
    "Axure:DiagramObject:TableCell",
    "Axure:DiagramObject:MenuObject",
    "Axure:DiagramObject:TreeNodeObject",
    "Axure:DiagramObject:ListBox",
    "Axure:DiagramObject:ComboBox",
    "Axure:DiagramObject:TextArea",
    "Axure:DiagramObject:InlineFrame",
    "Axure:DiagramObject:Screenshot",
    "Axure:DiagramObject:DynamicPanel",
    "Axure:DiagramObject:Layer"
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
$coveredWidgets = @{}
$results = foreach ($case in $cases) {
    $sourcePath = Join-Path $libraryDirectory $case.File
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Official Axure 11 library was not found: $sourcePath"
    }
    $outputPath = Join-Path $OutputDirectory `
        "$($case.Name)-rp9.rp"
    $bridgeJson = & $BridgePath `
        $Axure9Directory `
        $sourcePath `
        $outputPath
    if ($LASTEXITCODE -ne 0) {
        throw "Library conversion failed for $($case.File): $bridgeJson"
    }
    $bridgeReport = $bridgeJson | ConvertFrom-Json

    $sourceInventory = (
        & $BridgePath --inventory $Axure9Directory $sourcePath
    ) | ConvertFrom-Json
    $outputInventory = (
        & $BridgePath --inventory $Axure9Directory $outputPath
    ) | ConvertFrom-Json
    $sourceTypes = ConvertTo-CountTable $sourceInventory.recordTypes
    $outputTypes = ConvertTo-CountTable $outputInventory.recordTypes
    $allTypeNames = @(
        $sourceTypes.Keys + $outputTypes.Keys |
            Sort-Object -Unique
    )
    $changedTypes = @($allTypeNames | Where-Object {
        $sourceTypes[$_] -ne $outputTypes[$_]
    })
    $changedStaticTypes = @($changedTypes | Where-Object {
        $_ -notlike "Axure:Interaction*" -and
        $_ -notlike "Axure.Interaction*" -and
        $_ -notlike "Axure:Interation*"
    })
    if ($changedStaticTypes.Count -gt 0) {
        throw (
            "Static record counts changed in {0}: {1}" -f
            $case.File,
            ($changedStaticTypes -join ", ")
        )
    }

    $sourceTypes.GetEnumerator() | Where-Object {
        $_.Key.StartsWith(
            "Axure:DiagramObject:",
            [StringComparison]::Ordinal)
    } | ForEach-Object {
        $coveredWidgets[$_.Key] =
            [int]$coveredWidgets[$_.Key] + [int]$_.Value
    }

    [pscustomobject]@{
        sample = $case.File
        sourceFormat = [int]$sourceInventory.formatMajor
        outputFormat = [int]$outputInventory.formatMajor
        pages = [int]$bridgeReport.pagesRewritten
        objectPackages =
            [int]$bridgeReport.objectPackagesRewritten
        sourceRecords = (
            $sourceInventory.packages |
                Measure-Object recordCount -Sum
        ).Sum
        outputRecords = (
            $outputInventory.packages |
                Measure-Object recordCount -Sum
        ).Sum
        changedInteractionTypeKinds = $changedTypes.Count
        changedStaticTypeKinds = $changedStaticTypes.Count
        interactionsRemoved =
            [int]$bridgeReport.interactionsRemoved
        staticRecordsVerified =
            [int]$bridgeReport.staticRecordsVerified
        staticScalarsVerified =
            [int]$bridgeReport.staticScalarsVerified
        output = $outputPath
    }
}

$missingCoverage = @($requiredCoverage | Where-Object {
    -not $coveredWidgets.ContainsKey($_) -or
    $coveredWidgets[$_] -lt 1
})
if ($missingCoverage.Count -gt 0) {
    throw (
        "Official libraries did not cover expected widgets: {0}" -f
        ($missingCoverage -join ", ")
    )
}

$report = [pscustomobject]@{
    generatedAt = (Get-Date).ToString("o")
    cases = $results
    coveredWidgets = [pscustomobject]$coveredWidgets
}
$reportPath = Join-Path $OutputDirectory `
    "library-verification-report.json"
$report |
    ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $reportPath -Encoding utf8
$results |
    Format-Table sample, pages, objectPackages, sourceRecords, `
        outputRecords, changedStaticTypeKinds -AutoSize
Write-Host "Library verification report: $reportPath"
