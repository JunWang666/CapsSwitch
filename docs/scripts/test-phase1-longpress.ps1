Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KTest2 {
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
}
"@
Start-Process notepad.exe; Start-Sleep -Milliseconds 1500
$p = Get-Process notepad | Select-Object -First 1
(New-Object -ComObject WScript.Shell).AppActivate($p.Id) | Out-Null
Start-Sleep -Milliseconds 800
function CapsPress($holdMs) {
    [KTest2]::keybd_event(0x14, 0x45, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds $holdMs
    [KTest2]::keybd_event(0x14, 0x45, 2, [UIntPtr]::Zero)
}
function CapsState($tag) { "$tag capslock={0}" -f ([KTest2]::GetKeyState(0x14) -band 1) }
CapsState "initial      "
CapsPress 600; Start-Sleep -Milliseconds 800; CapsState "long1(->ON?) "
CapsPress 600; Start-Sleep -Milliseconds 800; CapsState "long2(->OFF?)"
CapsPress 40;  Start-Sleep -Milliseconds 800; CapsState "short(unchanged?)"
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
