param(
    [Parameter(Mandatory = $true)]
    [string]$Axure9Directory,
    [string]$InputDirectory = "",
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($InputDirectory)) {
    $InputDirectory = Join-Path $repositoryRoot `
        "target\fixture-verification"
}
$axureExe = Join-Path $Axure9Directory "AxureRP9.exe"
if (-not (Test-Path -LiteralPath $axureExe -PathType Leaf)) {
    throw "AxureRP9.exe was not found in: $Axure9Directory"
}

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;

public static class AxureWindowProbe
{
    public delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(
        EnumWindowsProc callback,
        IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(
        IntPtr window,
        StringBuilder text,
        int count);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr window);
}
'@

function Get-VisibleWindowTitles([int]$ProcessId) {
    $titles = New-Object System.Collections.Generic.List[string]
    [AxureWindowProbe]::EnumWindows({
        param($window, $parameter)
        $owner = 0
        [AxureWindowProbe]::GetWindowThreadProcessId(
            $window,
            [ref]$owner) | Out-Null
        if ($owner -eq $ProcessId -and
            [AxureWindowProbe]::IsWindowVisible($window)) {
            $text = New-Object Text.StringBuilder 1024
            [AxureWindowProbe]::GetWindowText(
                $window,
                $text,
                1024) | Out-Null
            if ($text.Length -gt 0) {
                $titles.Add($text.ToString())
            }
        }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $titles.ToArray()
}

$files = Get-ChildItem -LiteralPath $InputDirectory -Filter "*.rp" |
    Sort-Object Name
if ($files.Count -eq 0) {
    throw "No RP9 verification files were found in: $InputDirectory"
}

$results = foreach ($file in $files) {
    $process = Start-Process -FilePath $axureExe `
        -ArgumentList $file.FullName `
        -PassThru
    try {
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        $titles = @()
        do {
            Start-Sleep -Milliseconds 250
            $titles = @(Get-VisibleWindowTitles -ProcessId $process.Id)
            $errorDialog = @($titles | Where-Object {
                $_ -eq "报告错误" -or $_ -like "*error*"
            })
            $documentTitle = @($titles | Where-Object {
                $_ -like "$($file.BaseName) - Axure RP 9*"
            })
        } while (
            $errorDialog.Count -eq 0 -and
            $documentTitle.Count -eq 0 -and
            (Get-Date) -lt $deadline)

        [pscustomobject]@{
            file = $file.Name
            processId = $process.Id
            passed = (
                $errorDialog.Count -eq 0 -and
                $documentTitle.Count -gt 0)
            titles = $titles
        }
    }
    finally {
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 250
    }
}

$failed = @($results | Where-Object { -not $_.passed })
$reportPath = Join-Path $InputDirectory "axure9-gui-report.json"
$results |
    ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath $reportPath -Encoding utf8
$results | Format-Table file, passed, titles -Wrap -AutoSize
Write-Host "Axure 9 GUI report: $reportPath"
if ($failed.Count -gt 0) {
    throw "$($failed.Count) Axure 9 GUI verification case(s) failed."
}
