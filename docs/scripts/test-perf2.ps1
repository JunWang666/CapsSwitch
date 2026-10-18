# 流畅度验收:20 次短按分布 / Chrome 地址栏 / 连按成功率 / 预览延迟
$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KQ {
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out uint pid);
}
"@
function Press($holdMs) {
    [KQ]::keybd_event(0x14, 0x45, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds $holdMs
    [KQ]::keybd_event(0x14, 0x45, 2, [UIntPtr]::Zero)
}
function Focus-App($name, $id) {
    for ($i = 0; $i -lt 10; $i++) {
        [KQ]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
        [KQ]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 120
        (New-Object -ComObject WScript.Shell).AppActivate($id) | Out-Null
        Start-Sleep -Milliseconds 350
        $fg = [KQ]::GetForegroundWindow()
        $fgPid = 0; [KQ]::GetWindowThreadProcessId($fg, [ref]$fgPid) | Out-Null
        if ((Get-Process -Id $fgPid -ErrorAction SilentlyContinue).ProcessName -eq $name) { return $true }
    }
    return $false
}

Get-Process CapsSwitch.App -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Process notepad.exe
Start-Sleep -Milliseconds 1500
$np = Get-Process notepad | Select-Object -First 1
Focus-App 'notepad' $np.Id | Out-Null
Start-Process "C:\DevCodes\Others\CapsSwitch\dist\CapsSwitch.App.exe"
Start-Sleep -Milliseconds 2500

# ── a) 20 次短按 ──
for ($i = 0; $i -lt 20; $i++) { Press 40; Start-Sleep -Milliseconds 1100 }
"--- a done ---"

# ── c) 10 组连按(组内 200ms)──
for ($i = 0; $i -lt 10; $i++) {
    Press 40; Start-Sleep -Milliseconds 200; Press 40
    Start-Sleep -Milliseconds 1800
}
"--- c done ---"

# ── d) 列表可见期间连按 8 次 ──
Press 40; Start-Sleep -Milliseconds 200; Press 40
Start-Sleep -Milliseconds 200
for ($i = 0; $i -lt 8; $i++) { Press 40; Start-Sleep -Milliseconds 300 }
Start-Sleep -Milliseconds 2000
"--- d done ---"

# ── b) Chrome 地址栏 ──
$chrome = Get-Process chrome | Where-Object { $_.MainWindowTitle -ne '' } | Select-Object -First 1
if ($chrome) {
    Focus-App 'chrome' $chrome.Id | Out-Null
    [KQ]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
    [KQ]::keybd_event(0x4C, 0, 0, [UIntPtr]::Zero)
    [KQ]::keybd_event(0x4C, 0, 2, [UIntPtr]::Zero)
    [KQ]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 600
    for ($i = 0; $i -lt 10; $i++) { Press 40; Start-Sleep -Milliseconds 1100 }
    "--- b done ---"
} else { "no chrome" }

Start-Sleep -Milliseconds 1200
Get-Process CapsSwitch.App -ErrorAction SilentlyContinue | Stop-Process -Force
$np2 = Get-Process notepad -ErrorAction SilentlyContinue
if ($np2) { $np2 | ForEach-Object { $_.CloseMainWindow() | Out-Null } }
"done"
