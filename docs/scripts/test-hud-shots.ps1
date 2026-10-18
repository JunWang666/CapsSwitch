Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Shot {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
function Capture($file, $margin) {
    $fg = [Shot]::GetForegroundWindow()
    $r = New-Object Shot+RECT
    [Shot]::GetWindowRect($fg, [ref]$r) | Out-Null
    $x = [Math]::Max(0, $r.Left - $margin); $y = [Math]::Max(0, $r.Top - $margin)
    $w = [Math]::Min(3840 - $x, ($r.Right - $r.Left) + 2*$margin)
    $h = [Math]::Min(2160 - $y, ($r.Bottom - $r.Top) + 2*$margin)
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    "saved $file ($w x $h from $x,$y)"
}
$out = "C:\DevCodes\Others\CapsSwitch\docs\shots"
New-Item -ItemType Directory -Force -Path $out | Out-Null
Start-Process notepad.exe; Start-Sleep -Milliseconds 1500
$p = Get-Process notepad | Select-Object -First 1
(New-Object -ComObject WScript.Shell).AppActivate($p.Id) | Out-Null
Start-Sleep -Milliseconds 500
Capture "$out\hud-1-focus-zh.png" 80
Start-Sleep -Milliseconds 1500
Capture "$out\hud-2-hidden.png" 80
$fg = [Shot]::GetForegroundWindow()
[Shot]::PostMessage($fg, 0x50, [IntPtr]0, [IntPtr]0x04090409) | Out-Null
Start-Sleep -Milliseconds 500
Capture "$out\hud-3-external-en.png" 80
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
