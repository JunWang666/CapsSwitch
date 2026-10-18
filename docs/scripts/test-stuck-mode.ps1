Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KTest9 {
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
$ws = New-Object -ComObject WScript.Shell
$ws.AppActivate($p.Id) | Out-Null
Start-Sleep -Milliseconds 800
function Snap($tag) {
    $fg = [KTest9]::GetForegroundWindow()
    $pidv = 0; $tid = [KTest9]::GetWindowThreadProcessId($fg, [ref]$pidv)
    $hkl = [int64][KTest9]::GetKeyboardLayout($tid)
    $ime = [KTest9]::ImmGetDefaultIMEWnd($fg)
    $mode = [int][KTest9]::SendMessage($ime, 0x283, [IntPtr]0x1, [IntPtr]0)
    "$tag hkl=0x{0:x8} mode=0x{1:x4}" -f $hkl, $mode
}
[KTest9]::PostMessage([KTest9]::GetForegroundWindow(), 0x50, [IntPtr]0, [IntPtr]0x04090409) | Out-Null
Start-Sleep -Milliseconds 600
[KTest9]::PostMessage([KTest9]::GetForegroundWindow(), 0x50, [IntPtr]0, [IntPtr]0x08040804) | Out-Null
Start-Sleep -Milliseconds 600
Snap "after roundtrip   "
"--- wait 5s ---"
Start-Sleep -Seconds 5
Snap "after 5s wait     "
"--- focus away & back ---"
$ws.AppActivate("chrome") | Out-Null
Start-Sleep -Milliseconds 1200
$ws.AppActivate($p.Id) | Out-Null
Start-Sleep -Milliseconds 1200
Snap "after refocus     "
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
