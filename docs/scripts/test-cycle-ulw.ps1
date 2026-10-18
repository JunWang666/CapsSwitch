# 验收:循环会话(>300ms 间隔继续循环 / 超时回主副) + ULW 圆角锯齿检查
$ErrorActionPreference = 'Stop'
$shots = "C:\DevCodes\Others\CapsSwitch\docs\shots"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public class CapX {
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
public class KX {
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
}
"@

function Press($holdMs) {
    [KX]::keybd_event(0x14, 0x45, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds $holdMs
    [KX]::keybd_event(0x14, 0x45, 2, [UIntPtr]::Zero)
}

Get-Process CapsSwitch.App -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item "$env:LOCALAPPDATA\CapsSwitch\settings.json" -ErrorAction SilentlyContinue
Start-Process notepad.exe
Start-Sleep -Milliseconds 1500
$np = Get-Process notepad | Select-Object -First 1
for ($i = 0; $i -lt 10; $i++) {
    [KX]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
    [KX]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
    (New-Object -ComObject WScript.Shell).AppActivate($np.Id) | Out-Null
    Start-Sleep -Milliseconds 350
    $fg = [KX]::GetForegroundWindow()
    $fgPid = 0; [KX]::GetWindowThreadProcessId($fg, [ref]$fgPid) | Out-Null
    if ((Get-Process -Id $fgPid -ErrorAction SilentlyContinue).ProcessName -eq 'notepad') { break }
}
Start-Process "C:\DevCodes\Others\CapsSwitch\dist\CapsSwitch.App.exe"
Start-Sleep -Milliseconds 2500
[CapX]::Region(0, 0, 1, 1, "$shots\warmup.png")

# ── a) 连按进列表,等 600ms(>300ms)再按 → 必须继续循环推进 ──
Press 40; Start-Sleep -Milliseconds 220; Press 40          # 进入循环
Start-Sleep -Milliseconds 600                               # 超过 SequenceGapMs
Press 40                                                     # 循环中任意间隔 → 继续循环
Start-Sleep -Milliseconds 450
[CapX]::Region(0, 0, [System.Windows.Forms.SystemInformation]::VirtualScreen.Width, [System.Windows.Forms.SystemInformation]::VirtualScreen.Height, "$shots\cycle-a-continue.png")
"--- 阶段 a 完成,等待列表超时 ---"
Start-Sleep -Milliseconds 1800                              # 列表 1500ms 超时隐藏

# ── b) 超时后下一次短按 = 主副切换 ──
Press 40
Start-Sleep -Milliseconds 700
[CapX]::Region(0, 0, [System.Windows.Forms.SystemInformation]::VirtualScreen.Width, [System.Windows.Forms.SystemInformation]::VirtualScreen.Height, "$shots\cycle-b-pair.png")

# ── 恢复现场 ──
Start-Sleep -Milliseconds 1200
$caps = [KX]::GetKeyState(0x14) -band 1
if ($caps -eq 1) { Press 600; Start-Sleep -Milliseconds 400 }
Get-Process CapsSwitch.App -ErrorAction SilentlyContinue | Stop-Process -Force
$np2 = Get-Process notepad -ErrorAction SilentlyContinue
if ($np2) { $np2 | ForEach-Object { $_.CloseMainWindow() | Out-Null } }
"done"
