param(
    [Parameter(Mandatory = $true)]
    [string]$AxureExecutable,
    [Parameter(Mandatory = $true)]
    [string]$DocumentPath,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,
    [int]$SettlingSeconds = 4,
    [int]$TimeoutSeconds = 20
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $AxureExecutable -PathType Leaf)) {
    throw "Axure executable was not found: $AxureExecutable"
}
if (-not (Test-Path -LiteralPath $DocumentPath -PathType Leaf)) {
    throw "Axure document was not found: $DocumentPath"
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force |
        Out-Null
}

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class AxureCaptureWindow
{
    public delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

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

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(
        IntPtr window,
        out Rect rectangle);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(
        IntPtr window,
        IntPtr deviceContext,
        uint flags);

}
'@

function Get-AxureWindows([int]$ProcessId) {
    $windows = New-Object System.Collections.Generic.List[object]
    [AxureCaptureWindow]::EnumWindows({
        param($window, $parameter)
        $owner = 0
        [AxureCaptureWindow]::GetWindowThreadProcessId(
            $window,
            [ref]$owner) | Out-Null
        if ($owner -eq $ProcessId -and
            [AxureCaptureWindow]::IsWindowVisible($window)) {
            $text = New-Object Text.StringBuilder 1024
            [AxureCaptureWindow]::GetWindowText(
                $window,
                $text,
                1024) | Out-Null
            $windows.Add([pscustomobject]@{
                Handle = $window
                Title = $text.ToString()
            })
        }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $windows.ToArray()
}

$document = (Resolve-Path -LiteralPath $DocumentPath).Path
$documentBaseName = [IO.Path]::GetFileNameWithoutExtension($document)
$process = Start-Process -FilePath $AxureExecutable `
    -ArgumentList $document `
    -PassThru
try {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $documentWindow = $null
    do {
        Start-Sleep -Milliseconds 250
        $windows = @(Get-AxureWindows -ProcessId $process.Id)
        $errorWindow = @($windows | Where-Object {
            $_.Title -eq "报告错误" -or
            $_.Title -eq "鎶ュ憡閿欒" -or
            $_.Title -like "*error*"
        } | Select-Object -First 1)
        if ($errorWindow.Count -gt 0) {
            throw "Axure displayed an error window: $($errorWindow[0].Title)"
        }
        $documentWindow = @($windows | Where-Object {
            $_.Title -like "$documentBaseName*Axure RP*"
        } | Select-Object -First 1)
    } while ($documentWindow.Count -eq 0 -and (Get-Date) -lt $deadline)

    if ($documentWindow.Count -eq 0) {
        $titles = ($windows.Title -join "; ")
        throw "Timed out waiting for an Axure document window. Titles: $titles"
    }

    $handle = [IntPtr]$documentWindow[0].Handle
    [AxureCaptureWindow]::ShowWindow($handle, 3) | Out-Null
    [AxureCaptureWindow]::SetForegroundWindow($handle) | Out-Null
    Start-Sleep -Seconds $SettlingSeconds

    $rectangle = New-Object AxureCaptureWindow+Rect
    if (-not [AxureCaptureWindow]::GetWindowRect(
        $handle,
        [ref]$rectangle)) {
        throw "Could not read the Axure window rectangle."
    }
    $width = $rectangle.Right - $rectangle.Left
    $height = $rectangle.Bottom - $rectangle.Top
    if ($width -le 0 -or $height -le 0) {
        throw "Axure returned an invalid window rectangle."
    }

    $bitmap = New-Object Drawing.Bitmap $width, $height
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $deviceContext = $graphics.GetHdc()
            try {
                $printed = [AxureCaptureWindow]::PrintWindow(
                    $handle,
                    $deviceContext,
                    2)
            }
            finally {
                $graphics.ReleaseHdc($deviceContext)
            }
            if (-not $printed) {
                $graphics.CopyFromScreen(
                    $rectangle.Left,
                    $rectangle.Top,
                    0,
                    0,
                    $bitmap.Size)
            }
        }
        finally {
            $graphics.Dispose()
        }
        $bitmap.Save(
            $OutputPath,
            [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }

    [pscustomobject]@{
        document = $document
        title = $documentWindow[0].Title
        output = (Resolve-Path -LiteralPath $OutputPath).Path
        width = $width
        height = $height
    } | ConvertTo-Json -Compress
}
finally {
    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
}
