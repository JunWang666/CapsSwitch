Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KTest3 {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint tid);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
}
"@
Start-Process notepad.exe; Start-Sleep -Milliseconds 1500
$p = Get-Process notepad | Select-Object -First 1
(New-Object -ComObject WScript.Shell).AppActivate($p.Id) | Out-Null
Start-Sleep -Milliseconds 800
function Snap($tag) {
    $fg = [KTest3]::GetForegroundWindow()
    $pidv = 0; $tid = [KTest3]::GetWindowThreadProcessId($fg, [ref]$pidv)
    $hkl = [int64][KTest3]::GetKeyboardLayout($tid)
    # GetAsyncKeyState low bit of returned short is not toggle state; use Console.CapsLock instead
    "$tag hkl=0x{0:x8} consoleCapsLock={1}" -f $hkl, [Console]::CapsLock
}
Snap "baseline"
[KTest3]::keybd_event(0x14, 0x45, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 60
[KTest3]::keybd_event(0x14, 0x45, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 700
Snap "after caps#1"
[KTest3]::keybd_event(0x14, 0x45, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 60
[KTest3]::keybd_event(0x14, 0x45, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 700
Snap "after caps#2"
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
