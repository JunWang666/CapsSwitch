Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Shot2 {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
$out = "C:\DevCodes\Others\CapsSwitch\docs\shots"
New-Item -ItemType Directory -Force -Path $out | Out-Null
Start-Process notepad.exe; Start-Sleep -Milliseconds 2000
$p = Get-Process notepad | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$hwnd = $p.MainWindowHandle
# 强制前台,重试确认
$ok = $false
for ($i = 0; $i -lt 10; $i++) {
    [Shot2]::ShowWindow($hwnd, 9) | Out-Null
    [Shot2]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 400
    if ([Shot2]::GetForegroundWindow() -eq $hwnd) { $ok = $true; break }
}
"notepad hwnd=0x{0:x} foreground=$ok" -f [int64]$hwnd
function Capture($file) {
    $r = New-Object Shot2+RECT
    [Shot2]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $x = [Math]::Max(0, $r.Left - 100); $y = [Math]::Max(0, $r.Top - 100)
    $w = ($r.Right - $r.Left) + 200; $h = ($r.Bottom - $r.Top) + 200
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size, [Enum]::ToObject([System.Drawing.CopyPixelOperation], 0x40000000 + 0xCC0020))
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    "saved $file"
}
Start-Sleep -Milliseconds 400
Capture "$out\hud-a-zh.png"
[Shot2]::PostMessage($hwnd, 0x50, [IntPtr]0, [IntPtr]0x04090409) | Out-Null
Start-Sleep -Milliseconds 700
Capture "$out\hud-b-en.png"
[Shot2]::PostMessage($hwnd, 0x50, [IntPtr]0, [IntPtr]0x08040804) | Out-Null
Start-Sleep -Milliseconds 700
Capture "$out\hud-c-zh2.png"
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
