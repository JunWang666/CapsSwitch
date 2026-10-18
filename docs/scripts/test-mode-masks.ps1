Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KTest14 {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint tid);
    [DllImport("imm32.dll")] public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
}
"@
Start-Process notepad.exe; Start-Sleep -Milliseconds 1500
$p = Get-Process notepad | Select-Object -First 1
(New-Object -ComObject WScript.Shell).AppActivate($p.Id) | Out-Null
Start-Sleep -Milliseconds 800
function Snap($tag) {
    $fg = [KTest14]::GetForegroundWindow()
    $ime = [KTest14]::ImmGetDefaultIMEWnd($fg)
    $mode = [int][KTest14]::SendMessage($ime, 0x283, [IntPtr]0x1, [IntPtr]0)
    "$tag mode=0x{0:x4}" -f $mode
}
function SetConv($m) { [KTest14]::SendMessage([KTest14]::ImmGetDefaultIMEWnd([KTest14]::GetForegroundWindow()), 0x283, [IntPtr]0x2, [IntPtr]$m) | Out-Null }
[KTest14]::PostMessage([KTest14]::GetForegroundWindow(), 0x50, [IntPtr]0, [IntPtr]0x04090409) | Out-Null
Start-Sleep -Milliseconds 600
[KTest14]::PostMessage([KTest14]::GetForegroundWindow(), 0x50, [IntPtr]0, [IntPtr]0x08040804) | Out-Null
Start-Sleep -Milliseconds 600
Snap "after roundtrip "
foreach ($m in @(0x1, 0x401, 0x409, 0x9, 0x401)) {
    SetConv $m; Start-Sleep -Milliseconds 300; Snap ("SET 0x{0:x3} 后      " -f $m)
}
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
