Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KTest7 {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint tid);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
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
    $fg = [KTest7]::GetForegroundWindow()
    $pidv = 0; $tid = [KTest7]::GetWindowThreadProcessId($fg, [ref]$pidv)
    $hkl = [int64][KTest7]::GetKeyboardLayout($tid)
    $ime = [KTest7]::ImmGetDefaultIMEWnd($fg)
    $valid = if ($ime -ne [IntPtr]::Zero) { [KTest7]::IsWindow($ime) } else { $false }
    $mode = if ($valid) { "0x{0:x4}" -f [int][KTest7]::SendMessage($ime, 0x283, [IntPtr]0x1, [IntPtr]0) } else { "n/a" }
    "$tag hkl=0x{0:x8} imeWnd=0x{1:x} valid=$valid mode=$mode" -f $hkl, [int64]$ime
}
Snap "initial(pinyin)   "
[KTest7]::PostMessage([KTest7]::GetForegroundWindow(), 0x50, [IntPtr]0, [IntPtr]0x04090409) | Out-Null
Start-Sleep -Milliseconds 600
Snap "after ->EN        "
[KTest7]::PostMessage([KTest7]::GetForegroundWindow(), 0x50, [IntPtr]0, [IntPtr]0x08040804) | Out-Null
Start-Sleep -Milliseconds 600
Snap "after ->pinyin    "
Start-Sleep -Milliseconds 1500
Snap "after +1.5s       "
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
