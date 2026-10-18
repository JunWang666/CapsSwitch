Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KTest8 {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint tid);
    [DllImport("imm32.dll")] public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
"@
Start-Process notepad.exe; Start-Sleep -Milliseconds 1500
$p = Get-Process notepad | Select-Object -First 1
(New-Object -ComObject WScript.Shell).AppActivate($p.Id) | Out-Null
Start-Sleep -Milliseconds 800
function Snap($tag) {
    $fg = [KTest8]::GetForegroundWindow()
    $pidv = 0; $tid = [KTest8]::GetWindowThreadProcessId($fg, [ref]$pidv)
    $hkl = [int64][KTest8]::GetKeyboardLayout($tid)
    $ime = [KTest8]::ImmGetDefaultIMEWnd($fg)
    $mode = [int][KTest8]::SendMessage($ime, 0x283, [IntPtr]0x1, [IntPtr]0)
    $open = [int][KTest8]::SendMessage($ime, 0x283, [IntPtr]0x5, [IntPtr]0)
    "$tag hkl=0x{0:x8} mode=0x{1:x4} open=$open" -f $hkl, $mode
}
function SetConv($m) { [KTest8]::SendMessage([KTest8]::ImmGetDefaultIMEWnd([KTest8]::GetForegroundWindow()), 0x283, [IntPtr]0x2, [IntPtr]$m) | Out-Null }
function SetOpen($o) { [KTest8]::SendMessage([KTest8]::ImmGetDefaultIMEWnd([KTest8]::GetForegroundWindow()), 0x283, [IntPtr]0x6, [IntPtr]$o) | Out-Null }

[KTest8]::PostMessage([KTest8]::GetForegroundWindow(), 0x50, [IntPtr]0, [IntPtr]0x04090409) | Out-Null
Start-Sleep -Milliseconds 600
[KTest8]::PostMessage([KTest8]::GetForegroundWindow(), 0x50, [IntPtr]0, [IntPtr]0x08040804) | Out-Null
Start-Sleep -Milliseconds 600
Snap "after roundtrip   "
"--- try SET conversion=1 x5 ---"
for ($i=1; $i -le 5; $i++) { SetConv 1; Start-Sleep -Milliseconds 150; Snap "set conv try $i" }
"--- try SET open=1 ---"
SetOpen 1; Start-Sleep -Milliseconds 200; Snap "after set open=1"
"--- try SET conversion=1 again ---"
SetConv 1; Start-Sleep -Milliseconds 200; Snap "after set conv=1"
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
