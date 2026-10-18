Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public class Cap {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public static void CaptureRect(int x, int y, int w, int h, string path) {
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        IntPtr bmp = CreateCompatibleBitmap(screen, w, h);
        IntPtr old = SelectObject(mem, bmp);
        BitBlt(mem, 0, 0, w, h, screen, x, y, 0x00CC0020 | 0x40000000);
        using (Bitmap b = Image.FromHbitmap(bmp)) { b.Save(path, ImageFormat.Png); }
        SelectObject(mem, old); DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
    }
}
"@
$out = "C:\DevCodes\Others\CapsSwitch\docs\shots"
New-Item -ItemType Directory -Force -Path $out | Out-Null
Start-Process notepad.exe; Start-Sleep -Milliseconds 2000
$p = Get-Process notepad | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$hwnd = $p.MainWindowHandle
$ok = $false
for ($i = 0; $i -lt 10; $i++) {
    [Cap]::ShowWindow($hwnd, 9) | Out-Null
    [Cap]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 400
    if ([Cap]::GetForegroundWindow() -eq $hwnd) { $ok = $true; break }
}
"notepad hwnd=0x{0:x} foreground=$ok" -f [int64]$hwnd
function Capture($file) {
    $r = New-Object Cap+RECT
    [Cap]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $x = [Math]::Max(0, $r.Left - 100); $y = [Math]::Max(0, $r.Top - 100)
    [Cap]::CaptureRect($x, $y, ($r.Right - $r.Left) + 200, ($r.Bottom - $r.Top) + 200, $file)
    "saved $file"
}
Start-Sleep -Milliseconds 300
Capture "$out\hud-a-zh.png"
[Cap]::PostMessage($hwnd, 0x50, [IntPtr]0, [IntPtr]0x04090409) | Out-Null
Start-Sleep -Milliseconds 700
Capture "$out\hud-b-en.png"
[Cap]::PostMessage($hwnd, 0x50, [IntPtr]0, [IntPtr]0x08040804) | Out-Null
Start-Sleep -Milliseconds 700
Capture "$out\hud-c-zh2.png"
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
