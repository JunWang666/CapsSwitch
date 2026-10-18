Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KTest {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint tid);
    [DllImport("imm32.dll")] public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
}
"@
Start-Process notepad.exe; Start-Sleep -Milliseconds 1500
$p = Get-Process notepad | Select-Object -First 1
(New-Object -ComObject WScript.Shell).AppActivate($p.Id) | Out-Null
Start-Sleep -Milliseconds 800

function State($tag) {
    $fg = [KTest]::GetForegroundWindow()
    $pidv = 0; $tid = [KTest]::GetWindowThreadProcessId($fg, [ref]$pidv)
    $hkl = [int64][KTest]::GetKeyboardLayout($tid)
    $ime = [KTest]::ImmGetDefaultIMEWnd($fg)
    $mode = if ($ime -ne [IntPtr]::Zero) { [int][KTest]::SendMessage($ime, 0x283, [IntPtr]0x1, [IntPtr]0) } else { -1 }
    $caps = [KTest]::GetKeyState(0x14) -band 1
    "$tag : hkl=0x{0:x8} imemode=0x{1:x4} capslock={2}" -f $hkl, $mode, $caps
}
function CapsPress($holdMs) {
    [KTest]::keybd_event(0x14, 0x45, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds $holdMs
    [KTest]::keybd_event(0x14, 0x45, 2, [UIntPtr]::Zero)
}

State "initial        "
CapsPress 40;  Start-Sleep -Milliseconds 900; State "short1(->EN?)   "
CapsPress 40;  Start-Sleep -Milliseconds 900; State "short2(->ZH?)   "
CapsPress 40;  Start-Sleep -Milliseconds 150; CapsPress 40; Start-Sleep -Milliseconds 1000; State "double(EN->3rd?) "
CapsPress 600; Start-Sleep -Milliseconds 800; State "long1(caps ON?) "
CapsPress 600; Start-Sleep -Milliseconds 800; State "long2(caps OFF?)"
# combo: Caps + A
[KTest]::keybd_event(0x14, 0x45, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60
[KTest]::keybd_event(0x41, 0x1E, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40
[KTest]::keybd_event(0x41, 0x1E, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60
[KTest]::keybd_event(0x14, 0x45, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 800; State "combo(no change?)"
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
