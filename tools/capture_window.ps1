param(
    [Parameter(Mandatory=$true)][string]$Output,
    [string]$WindowTitle = "",
    [int]$MaxWidth = 0
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class ShiJiCapture {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetThreadDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
}
'@

$proc = Get-Process ShiJi -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowTitle } |
    Select-Object -First 1
if (-not $proc) {
    $proc = Get-Process dotnet -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowTitle } |
        Select-Object -First 1
}
if (-not $proc) { throw "ShiJi is not running." }

$handles = New-Object System.Collections.Generic.List[object]
$callback = [ShiJiCapture+EnumWindowsProc]{
    param($hwnd, $lparam)
    $pidValue = 0
    [ShiJiCapture]::GetWindowThreadProcessId($hwnd, [ref]$pidValue) | Out-Null
    if ($pidValue -eq $proc.Id -and [ShiJiCapture]::IsWindowVisible($hwnd)) {
        $sb = New-Object System.Text.StringBuilder 256
        [ShiJiCapture]::GetWindowText($hwnd, $sb, 256) | Out-Null
        if ($sb.Length -gt 0) {
            $handles.Add([pscustomobject]@{ Handle = $hwnd; Title = $sb.ToString() })
        }
    }
    return $true
}
[ShiJiCapture]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null
$target = $null
if ([string]::IsNullOrWhiteSpace($WindowTitle)) {
    $target = $handles | Sort-Object { $_.Title.Length } -Descending | Select-Object -First 1
} else {
    $target = $handles | Where-Object { $_.Title -eq $WindowTitle } | Select-Object -First 1
}
if (-not $target) { throw "Window not found: $WindowTitle" }

[ShiJiCapture]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
$rect = New-Object ShiJiCapture+RECT
[ShiJiCapture]::GetWindowRect($target.Handle, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()
[ShiJiCapture]::PrintWindow($target.Handle, $hdc, 2) | Out-Null
$graphics.ReleaseHdc($hdc)
$graphics.Dispose()

if ($MaxWidth -gt 0 -and $width -gt $MaxWidth) {
    $newHeight = [int]($height * $MaxWidth / $width)
    $scaled = New-Object System.Drawing.Bitmap $MaxWidth, $newHeight
    $g2 = [System.Drawing.Graphics]::FromImage($scaled)
    $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g2.DrawImage($bitmap, 0, 0, $MaxWidth, $newHeight)
    $g2.Dispose()
    $bitmap.Dispose()
    $bitmap = $scaled
}

$dir = Split-Path -Parent $Output
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
$bitmap.Save($Output, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
Write-Output "Saved $Output"
