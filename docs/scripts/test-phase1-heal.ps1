Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KTest5 {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint tid);
    [DllImport("imm32.dll")] public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
}
"@
Start-Process notepad.exe; Start-Sleep -Milliseconds 1500
$p = Get-Process notepad | Select-Object -First 1
(New-Object -ComObject WScript.Shell).AppActivate($p.Id) | Out-Null
Start-Sleep -Milliseconds 800
function Snap($tag) {
    $fg = [KTest5]::GetForegroundWindow()
    $pidv = 0; $tid = [KTest5]::GetWindowThreadProcessId($fg, [ref]$pidv)
    $hkl = [int64][KTest5]::GetKeyboardLayout($tid)
    $ime = [KTest5]::ImmGetDefaultIMEWnd($fg)
    $mode = if ($ime -ne [IntPtr]::Zero) { "0x{0:x4}" -f [int][KTest5]::SendMessage($ime, 0x283, [IntPtr]0x1, [IntPtr]0) } else { "none" }
    "$tag hkl=0x{0:x8} mode=$mode" -f $hkl
}
function SetMode($m) {
    $fg = [KTest5]::GetForegroundWindow()
    $ime = [KTest5]::ImmGetDefaultIMEWnd($fg)
    [KTest5]::SendMessage($ime, 0x283, [IntPtr]0x2, [IntPtr]$m) | Out-Null
}
Snap "initial(pinyin zh?)  "
SetMode 0
Start-Sleep -Milliseconds 300
Snap "forced english mode "
Start-Sleep -Milliseconds 1500
Snap "after self-heal wait"
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
