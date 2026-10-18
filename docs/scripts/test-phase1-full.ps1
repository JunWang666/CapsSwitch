Add-Type -AssemblyName UIAutomationClient
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KTest4 {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint tid);
    [DllImport("imm32.dll")] public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
"@
Start-Process notepad.exe; Start-Sleep -Milliseconds 1500
$p = Get-Process notepad | Select-Object -First 1
(New-Object -ComObject WScript.Shell).AppActivate($p.Id) | Out-Null
Start-Sleep -Milliseconds 800

function Snap($tag) {
    $fg = [KTest4]::GetForegroundWindow()
    $pidv = 0; $tid = [KTest4]::GetWindowThreadProcessId($fg, [ref]$pidv)
    $hkl = [int64][KTest4]::GetKeyboardLayout($tid)
    $ime = [KTest4]::ImmGetDefaultIMEWnd($fg)
    $mode = if ($ime -ne [IntPtr]::Zero) { "0x{0:x4}" -f [int][KTest4]::SendMessage($ime, 0x283, [IntPtr]0x1, [IntPtr]0) } else { "none" }
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($fg)
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Document)
    $doc = $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    $text = ""
    if ($doc) { $tp = $doc.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern); $text = $tp.DocumentRange.GetText(50) }
    "$tag hkl=0x{0:x8} mode=$mode text=[{1}]" -f $hkl, ($text -replace "`r`n","~")
}
function CapsPress($holdMs) {
    [KTest4]::keybd_event(0x14, 0x45, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds $holdMs
    [KTest4]::keybd_event(0x14, 0x45, 2, [UIntPtr]::Zero)
}
function TypeA() {
    [KTest4]::keybd_event(0x41, 0x1E, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40
    [KTest4]::keybd_event(0x41, 0x1E, 2, [UIntPtr]::Zero)
}

Snap "initial          "
CapsPress 40;  Start-Sleep -Milliseconds 900; Snap "short1           "
CapsPress 40;  Start-Sleep -Milliseconds 900; Snap "short2           "
CapsPress 600; Start-Sleep -Milliseconds 800; TypeA; Start-Sleep -Milliseconds 400; Snap "long1+A(caps ON?)"
CapsPress 600; Start-Sleep -Milliseconds 800; TypeA; Start-Sleep -Milliseconds 400; Snap "long2+A(capsOFF?)"
CapsPress 40;  Start-Sleep -Milliseconds 150; CapsPress 40; Start-Sleep -Milliseconds 1000; Snap "double           "
# combo Caps+A: should type 'a', no switch
[KTest4]::keybd_event(0x14, 0x45, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60
TypeA; Start-Sleep -Milliseconds 60
[KTest4]::keybd_event(0x14, 0x45, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 800; Snap "combo+A          "
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
