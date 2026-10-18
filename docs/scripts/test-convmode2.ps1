Add-Type @"
using System;
using System.Runtime.InteropServices;
public class ImcTest2 {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint tid);
    [DllImport("imm32.dll")] public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
}
"@
$ws = New-Object -ComObject WScript.Shell
$ws.AppActivate(8744) | Out-Null
Start-Sleep -Milliseconds 600
function Dump($tag) {
    $fg = [ImcTest2]::GetForegroundWindow()
    $ime = [ImcTest2]::ImmGetDefaultIMEWnd($fg)
    $mode = [int][ImcTest2]::SendMessage($ime, 0x283, [IntPtr]0x1, [IntPtr]0)
    $open = [int][ImcTest2]::SendMessage($ime, 0x283, [IntPtr]0x5, [IntPtr]0)
    $pidv = 0; $tid = [ImcTest2]::GetWindowThreadProcessId($fg, [ref]$pidv)
    $hkl = [int64][ImcTest2]::GetKeyboardLayout($tid)
    "$tag fg=0x{0:x} hkl=0x{1:x8} mode=0x{2:x4} open={3}" -f [int64]$fg, $hkl, $mode, $open
}
Dump "initial"
# set to Chinese mode (NATIVE only)
[ImcTest2]::SendMessage([ImcTest2]::ImmGetDefaultIMEWnd([ImcTest2]::GetForegroundWindow()), 0x283, [IntPtr]0x2, [IntPtr]0x0001) | Out-Null
Dump "set-native+0ms"
Start-Sleep -Milliseconds 700
Dump "set-native+700ms"
# set to English mode (0)
[ImcTest2]::SendMessage([ImcTest2]::ImmGetDefaultIMEWnd([ImcTest2]::GetForegroundWindow()), 0x283, [IntPtr]0x2, [IntPtr]0x0) | Out-Null
Dump "set-english+0ms"
Start-Sleep -Milliseconds 700
Dump "set-english+700ms"
# restore Chinese
[ImcTest2]::SendMessage([ImcTest2]::ImmGetDefaultIMEWnd([ImcTest2]::GetForegroundWindow()), 0x283, [IntPtr]0x2, [IntPtr]0x0001) | Out-Null
Start-Sleep -Milliseconds 700
Dump "restored"
