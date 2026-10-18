# 性能/流畅度实测:耗时日志、高亮即时推进、强制不阻塞、淡入淡出
$ErrorActionPreference = 'Stop'
$shots = "C:\DevCodes\Others\CapsSwitch\docs\shots"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public class CapP {
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
public class KP {
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out uint pid);
}
"@
function Press($holdMs) {
    [KP]::keybd_event(0x14, 0x45, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds $holdMs
    [KP]::keybd_event(0x14, 0x45, 2, [UIntPtr]::Zero)
}

Get-Process CapsSwitch.App -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Process notepad.exe
Start-Sleep -Milliseconds 1500
$np = Get-Process notepad | Select-Object -First 1
for ($i = 0; $i -lt 10; $i++) {
    [KP]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
    [KP]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
    (New-Object -ComObject WScript.Shell).AppActivate($np.Id) | Out-Null
    Start-Sleep -Milliseconds 350
    $fg = [KP]::GetForegroundWindow()
    $fgPid = 0; [KP]::GetWindowThreadProcessId($fg, [ref]$fgPid) | Out-Null
    if ((Get-Process -Id $fgPid -ErrorAction SilentlyContinue).ProcessName -eq 'notepad') { break }
}
Start-Process "C:\DevCodes\Others\CapsSwitch\dist\CapsSwitch.App.exe"
Start-Sleep -Milliseconds 2500
[CapP]::Region(0, 0, 1, 1, "$shots\warmup.png")
Start-Sleep -Milliseconds 200

# ── 3b:连按进列表(200ms 间隔),第二按后 200ms 截屏(切换未完成,高亮应已推进) ──
Press 40; Start-Sleep -Milliseconds 180; Press 40
Start-Sleep -Milliseconds 80
[CapP]::Region(0, 0, [System.Windows.Forms.SystemInformation]::VirtualScreen.Width, [System.Windows.Forms.SystemInformation]::VirtualScreen.Height, "$shots\perf-b-instant-highlight.png")
"--- b done ---"
Start-Sleep -Milliseconds 2000

# ── 3d:按一下切中文(后台强制 ~300ms 期间),+150ms 立即再按 → 不得排队 ──
Press 40                      # 切中文 → EnforceNativeQuick 后台启动
Start-Sleep -Milliseconds 150
Press 40                      # 强制进行中插入第二次切换
Start-Sleep -Milliseconds 800
"--- d done ---"

# ── 收尾 ──
Start-Sleep -Milliseconds 1500
Get-Process CapsSwitch.App -ErrorAction SilentlyContinue | Stop-Process -Force
$np2 = Get-Process notepad -ErrorAction SilentlyContinue
if ($np2) { $np2 | ForEach-Object { $_.CloseMainWindow() | Out-Null } }
"done"
