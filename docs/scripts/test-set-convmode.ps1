Add-Type @"
using System;
using System.Runtime.InteropServices;
public class ImcTest {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("imm32.dll")] public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
}
"@
$fg = [ImcTest]::GetForegroundWindow()
$ime = [ImcTest]::ImmGetDefaultIMEWnd($fg)
"fg=$fg ime=$ime"
$cur = [ImcTest]::SendMessage($ime, 0x283, [IntPtr]0x1, [IntPtr]0)
"before mode=0x{0:x}" -f [int]$cur
$r = [ImcTest]::SendMessage($ime, 0x283, [IntPtr]0x2, [IntPtr]0x0401)
"set result=$r"
Start-Sleep -Milliseconds 300
$after = [ImcTest]::SendMessage($ime, 0x283, [IntPtr]0x1, [IntPtr]0)
"after mode=0x{0:x}" -f [int]$after
