# 最小化验证:仅连按列表模式(修复 RowCount 后的高亮证据)
$ErrorActionPreference = 'Stop'
$shots = "C:\DevCodes\Others\CapsSwitch\docs\shots"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public class CapL {
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    public static void Region(int x, int y, int w, int h, string path) {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        IntPtr bmp = CreateCompatibleBitmap(screen, w, h);
        IntPtr old = SelectObject(mem, bmp);
        BitBlt(mem, 0, 0, w, h, screen, x, y, 0x00CC0020 | 0x40000000);
        using (Bitmap b = Image.FromHbitmap(bmp)) { b.Save(path, ImageFormat.Png); }
        SelectObject(mem, old); DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
        Console.WriteLine("captured " + path);
    }
}
"@
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KL {
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out uint pid);
}
"@

function Press($holdMs) {
    [KL]::keybd_event(0x14, 0x45, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds $holdMs
    [KL]::keybd_event(0x14, 0x45, 2, [UIntPtr]::Zero)
}

Get-Process CapsSwitch.App -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item "$env:LOCALAPPDATA\CapsSwitch\settings.json" -ErrorAction SilentlyContinue
Start-Process notepad.exe
Start-Sleep -Milliseconds 1500
$np = Get-Process notepad | Select-Object -First 1
$fgName = ""
for ($i = 0; $i -lt 10; $i++) {
    [KL]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
    [KL]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
    (New-Object -ComObject WScript.Shell).AppActivate($np.Id) | Out-Null
    Start-Sleep -Milliseconds 350
    $fg = [KL]::GetForegroundWindow()
    $fgPid = 0; [KL]::GetWindowThreadProcessId($fg, [ref]$fgPid) | Out-Null
    $fgName = (Get-Process -Id $fgPid -ErrorAction SilentlyContinue).ProcessName
    if ($fgName -eq 'notepad') { break }
}
"foreground = $fgName"
Start-Process "C:\DevCodes\Others\CapsSwitch\dist\CapsSwitch.App.exe"
Start-Sleep -Milliseconds 2500
[CapL]::Region(0, 0, 1, 1, "$shots\warmup.png")
Start-Sleep -Milliseconds 200

# 连按两次 → 列表 active=1(EN 行高亮)
Press 40; Start-Sleep -Milliseconds 220; Press 40
Start-Sleep -Milliseconds 500
[CapL]::Region(0, 0, [System.Windows.Forms.SystemInformation]::VirtualScreen.Width, [System.Windows.Forms.SystemInformation]::VirtualScreen.Height, "$shots\hud5-c-list2.png")

Start-Sleep -Milliseconds 1600
# 连按三次 → 高亮循环,最后一按 active=2
Press 40; Start-Sleep -Milliseconds 220; Press 40; Start-Sleep -Milliseconds 220; Press 40
Start-Sleep -Milliseconds 500
[CapL]::Region(0, 0, [System.Windows.Forms.SystemInformation]::VirtualScreen.Width, [System.Windows.Forms.SystemInformation]::VirtualScreen.Height, "$shots\hud5-d-list3.png")

Start-Sleep -Milliseconds 1200
Get-Process CapsSwitch.App -ErrorAction SilentlyContinue | Stop-Process -Force
$np2 = Get-Process notepad -ErrorAction SilentlyContinue
if ($np2) { $np2 | ForEach-Object { $_.CloseMainWindow() | Out-Null } }
"done"
