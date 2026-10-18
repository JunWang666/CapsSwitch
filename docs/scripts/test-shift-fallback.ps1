Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KTest10 {
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
    $fg = [KTest10]::GetForegroundWindow()
    $pidv = 0; $tid = [KTest10]::GetWindowThreadProcessId($fg, [ref]$pidv)
    $hkl = [int64][KTest10]::GetKeyboardLayout($tid)
    $ime = [KTest10]::ImmGetDefaultIMEWnd($fg)
    $mode = [int][KTest10]::SendMessage($ime, 0x283, [IntPtr]0x1, [IntPtr]0)
    "$tag hkl=0x{0:x8} mode=0x{1:x4}" -f $hkl, $mode
}
[KTest10]::PostMessage([KTest10]::GetForegroundWindow(), 0x50, [IntPtr]0, [IntPtr]0x04090409) | Out-Null
Start-Sleep -Milliseconds 600
[KTest10]::PostMessage([KTest10]::GetForegroundWindow(), 0x50, [IntPtr]0, [IntPtr]0x08040804) | Out-Null
Start-Sleep -Milliseconds 600
Snap "after roundtrip    "
# 注入 Shift 单击 (vk=0x10 scan=0x2A)
[KTest10]::keybd_event(0x10, 0x2A, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 50
[KTest10]::keybd_event(0x10, 0x2A, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 500
Snap "after Shift tap    "
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
