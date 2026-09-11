# shot.ps1: one screenshot of the game, only while Timberborn is the foreground window.
#
#   powershell -ExecutionPolicy Bypass -File .claude\skills\wardens-level-workshop\scripts\shot.ps1 -Out C:\path\shot.png [-Scale 0.33]
#
# Refuses (exit 2, nothing written) when another window is in front: the author plays and chats
# beside the game, and a capture of their screen is theirs, not a frame. DPI-aware, so the capture
# covers the whole screen (an unaware process on a scaled display captures only the top-left part,
# without the letterbox bar or the caption). Scale shrinks the image before saving (default a third).

param(
    [Parameter(Mandatory = $true)][string]$Out,
    [double]$Scale = 0.33
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WardensShotNative {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
}
"@

[void][WardensShotNative]::SetProcessDPIAware()
$hwnd = [WardensShotNative]::GetForegroundWindow()
$procId = 0
[void][WardensShotNative]::GetWindowThreadProcessId($hwnd, [ref]$procId)
$proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
if ($null -eq $proc -or $proc.ProcessName -notlike "Timberborn*") {
    $name = "unknown"
    if ($null -ne $proc) { $name = $proc.ProcessName }
    Write-Output "refused: the foreground window is '$name', not Timberborn; nothing captured"
    exit 2
}

$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$full = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
$g = [System.Drawing.Graphics]::FromImage($full)
$g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
$g.Dispose()

$w = [int]($bounds.Width * $Scale)
$h = [int]($bounds.Height * $Scale)
$small = New-Object System.Drawing.Bitmap $full, $w, $h
$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
$small.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$small.Dispose()
$full.Dispose()
Write-Output "saved $Out ($w x $h)"
