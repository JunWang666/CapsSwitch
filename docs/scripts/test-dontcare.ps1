Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KTest13 {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint tid);
    [DllImport("imm32.dll")] public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
}
"@
$phase0 = "C:\DevCodes\Others\CapsSwitch\CapsSwitch.Phase0\bin\Debug\net10.0-windows\CapsSwitch.Phase0.exe"
Start-Process notepad.exe; Start-Sleep -Milliseconds 1500
$p = Get-Process notepad | Select-Object -First 1
(New-Object -ComObject WScript.Shell).AppActivate($p.Id) | Out-Null
Start-Sleep -Milliseconds 800
function Snap($tag) {
    $fg = [KTest13]::GetForegroundWindow()
    $pidv = 0; $tid = [KTest13]::GetWindowThreadProcessId($fg, [ref]$pidv)
    $hkl = [int64][KTest13]::GetKeyboardLayout($tid)
    $ime = [KTest13]::ImmGetDefaultIMEWnd($fg)
    $mode = [int][KTest13]::SendMessage($ime, 0x283, [IntPtr]0x1, [IntPtr]0)
    "$tag hkl=0x{0:x8} mode=0x{1:x4}" -f $hkl, $mode
}
Snap "initial          "
[KTest13]::PostMessage([KTest13]::GetForegroundWindow(), 0x50, [IntPtr]0, [IntPtr]0x04090409) | Out-Null
Start-Sleep -Milliseconds 600
Snap "after msg->EN    "
"=== phase0 activate zh (6 种 flag 组合) ==="
& $phase0 activate zh 2>&1 | Select-String -Pattern "ActivateProfile|前台" | ForEach-Object { $_.Line }
Snap "after all combos "
# 每个组合后仍未切线程 HKL 的话,补 msg
[KTest13]::PostMessage([KTest13]::GetForegroundWindow(), 0x50, [IntPtr]0, [IntPtr]0x08040804) | Out-Null
Start-Sleep -Milliseconds 800
Snap "after msg->ZH    "
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
