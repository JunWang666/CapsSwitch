# HUD 五条改进集成验证:记事本聚焦 + 合成 Caps 按键 + PMv2 截屏
# 产物: docs/shots/hud5-a-badge.png / b-caps.png / c-list2.png / d-list3.png
$ErrorActionPreference = 'Stop'
$shots = "C:\DevCodes\Others\CapsSwitch\docs\shots"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public class Cap5 {
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    public static void Region(int x, int y, int w, int h, string path) {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        IntPtr bmp = CreateCompatibleBitmap(screen, w, h);
        IntPtr old = SelectObject(mem, bmp);
        BitBlt(mem, 0, 0, w, h, screen, x, y, 0x00CC0020 | 0x40000000); // CAPTUREBLT
        using (Bitmap b = Image.FromHbitmap(bmp)) { b.Save(path, ImageFormat.Png); }
        SelectObject(mem, old); DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
        Console.WriteLine("captured " + path);
    }
}
"@
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class K5 {
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
}
"@

function Press($holdMs) {
    [K5]::keybd_event(0x14, 0x45, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds $holdMs
    [K5]::keybd_event(0x14, 0x45, 2, [UIntPtr]::Zero)
}

# 0) 清场:杀掉残留的 CapsSwitch 与 notepad(后者仅本脚本启动的)
Get-Process CapsSwitch.App -ErrorAction SilentlyContinue | Stop-Process -Force
$existingNp = Get-Process notepad -ErrorAction SilentlyContinue
if ($existingNp) { "警告: 已有 notepad 在运行,将使用现有实例" }

# 1) 重置配置为默认(确定性行为),再启动 notepad + CapsSwitch(托盘)
Remove-Item "$env:LOCALAPPDATA\CapsSwitch\settings.json" -ErrorAction SilentlyContinue
Start-Process notepad.exe
Start-Sleep -Milliseconds 1500
$np = Get-Process notepad | Select-Object -First 1
$focused = $false
for ($i = 0; $i -lt 8 -and -not $focused; $i++) {
    # ALT 敲击:让本进程获得 SetForegroundWindow 权限(前台锁绕过)
    [K5]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
    [K5]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100
    (New-Object -ComObject WScript.Shell).AppActivate($np.Id) | Out-Null
    Start-Sleep -Milliseconds 400
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Fg5 {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out uint pid);
}
'@
    $fg = [Fg5]::GetForegroundWindow()
    $fgPid = 0; [Fg5]::GetWindowThreadProcessId($fg, [ref]$fgPid) | Out-Null
    $fgName = (Get-Process -Id $fgPid -ErrorAction SilentlyContinue).ProcessName
    $focused = ($fgName -eq 'notepad')
}
"foreground = $fgName (focused=$focused)"
Start-Process "C:\DevCodes\Others\CapsSwitch\dist\CapsSwitch.App.exe"
Start-Sleep -Milliseconds 2500
# 预热:让 PS 进程进入 PMv2,之后 VirtualScreen 返回物理像素
[Cap5]::Region(0, 0, 1, 1, "$shots\warmup.png")
Start-Sleep -Milliseconds 200

# 2a) 短按一次 → 紧凑徽章(HUD 应在光标下方)
Press 40
Start-Sleep -Milliseconds 600
[Cap5]::Region(0, 0, [System.Windows.Forms.SystemInformation]::VirtualScreen.Width, [System.Windows.Forms.SystemInformation]::VirtualScreen.Height, "$shots\hud5-a-badge.png")

# 2b) 长按 → CapsLock 状态 HUD(此刻 CapsLock 打开)
Press 600
Start-Sleep -Milliseconds 400
[Cap5]::Region(0, 0, [System.Windows.Forms.SystemInformation]::VirtualScreen.Width, [System.Windows.Forms.SystemInformation]::VirtualScreen.Height, "$shots\hud5-b-caps.png")

# 2c) 连按两次 → 列表模式,第二行高亮
Press 40; Start-Sleep -Milliseconds 250; Press 40
Start-Sleep -Milliseconds 600
[Cap5]::Region(0, 0, [System.Windows.Forms.SystemInformation]::VirtualScreen.Width, [System.Windows.Forms.SystemInformation]::VirtualScreen.Height, "$shots\hud5-c-list2.png")

# 2d) 连按三次 → 高亮循环到第三行/回绕
Start-Sleep -Milliseconds 1500
Press 40; Start-Sleep -Milliseconds 250; Press 40; Start-Sleep -Milliseconds 250; Press 40
Start-Sleep -Milliseconds 600
[Cap5]::Region(0, 0, [System.Windows.Forms.SystemInformation]::VirtualScreen.Width, [System.Windows.Forms.SystemInformation]::VirtualScreen.Height, "$shots\hud5-d-list3.png")

# 3) 恢复:长按关 CapsLock
Start-Sleep -Milliseconds 1500
$caps = [K5]::GetKeyState(0x14) -band 1
"capslock now = $caps (1=开)"
if ($caps -eq 1) { Press 600; Start-Sleep -Milliseconds 500 }
"final capslock = $(([K5]::GetKeyState(0x14) -band 1))"

# 4) 收尾
Get-Process CapsSwitch.App -ErrorAction SilentlyContinue | Stop-Process -Force
$np2 = Get-Process notepad -ErrorAction SilentlyContinue
if ($np2) { $np2 | ForEach-Object { $_.CloseMainWindow() | Out-Null } }
"done"
