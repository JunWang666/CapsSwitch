# 聚焦 HUD 冷却实测:跨 hwnd 同状态 10s 内跳过;状态变化立即显示
$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KC {
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
function Press($holdMs) {
    [KC]::keybd_event(0x14, 0x45, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds $holdMs
    [KC]::keybd_event(0x14, 0x45, 2, [UIntPtr]::Zero)
}
function Focus-Pid($name, $id) {
    for ($i = 0; $i -lt 10; $i++) {
        [KC]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
        [KC]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 120
        (New-Object -ComObject WScript.Shell).AppActivate($id) | Out-Null
        Start-Sleep -Milliseconds 350
        $fg = [KC]::GetForegroundWindow()
        $fgPid = 0; [KC]::GetWindowThreadProcessId($fg, [ref]$fgPid) | Out-Null
        if ((Get-Process -Id $fgPid -ErrorAction SilentlyContinue).ProcessName -eq $name) { return $true }
    }
    return $false
}

Get-Process CapsSwitch.App -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Process notepad.exe
Start-Sleep -Milliseconds 1500
$np = Get-Process notepad | Select-Object -First 1
Focus-Pid 'notepad' $np.Id | Out-Null
Start-Process "C:\DevCodes\Others\CapsSwitch\dist\CapsSwitch.App.exe"
Start-Sleep -Milliseconds 2500

# 1) 聚焦记事本 → 应显示
Focus-Pid 'notepad' $np.Id | Out-Null
Start-Sleep -Milliseconds 800

# 2) 10s 内切到 Chrome 地址栏(跨 hwnd,同状态)→ 应冷却跳过
$chrome = Get-Process chrome | Where-Object { $_.MainWindowTitle -ne '' } | Select-Object -First 1
Focus-Pid 'chrome' $chrome.Id | Out-Null
Start-Sleep -Milliseconds 400
[KC]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
[KC]::keybd_event(0x4C, 0, 0, [UIntPtr]::Zero)
[KC]::keybd_event(0x4C, 0, 2, [UIntPtr]::Zero)
[KC]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 800

# 3) 按 Caps 切换输入源(状态变化,切换路径不受冷却限制)→ 显示
Focus-Pid 'notepad' $np.Id | Out-Null
Start-Sleep -Milliseconds 300
Press 40
Start-Sleep -Milliseconds 800

# 4) 再聚焦 Chrome 地址栏(状态已变,EN)→ 应立即显示;随后 10s 内再回记事本(同 EN)→ 冷却跳过
Focus-Pid 'chrome' $chrome.Id | Out-Null
Start-Sleep -Milliseconds 400
[KC]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
[KC]::keybd_event(0x4C, 0, 0, [UIntPtr]::Zero)
[KC]::keybd_event(0x4C, 0, 2, [UIntPtr]::Zero)
[KC]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 800
Focus-Pid 'notepad' $np.Id | Out-Null
Start-Sleep -Milliseconds 800
"--- steps done ---"

Start-Sleep -Milliseconds 1200
Get-Process CapsSwitch.App -ErrorAction SilentlyContinue | Stop-Process -Force
$np2 = Get-Process notepad -ErrorAction SilentlyContinue
if ($np2) { $np2 | ForEach-Object { $_.CloseMainWindow() | Out-Null } }
"done"
