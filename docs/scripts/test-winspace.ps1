Add-Type -AssemblyName UIAutomationClient
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KTest12 {
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
    $fg = [KTest12]::GetForegroundWindow()
    $pidv = 0; $tid = [KTest12]::GetWindowThreadProcessId($fg, [ref]$pidv)
    $hkl = [int64][KTest12]::GetKeyboardLayout($tid)
    $ime = [KTest12]::ImmGetDefaultIMEWnd($fg)
    $mode = [int][KTest12]::SendMessage($ime, 0x283, [IntPtr]0x1, [IntPtr]0)
    "$tag hkl=0x{0:x8} mode=0x{1:x4}" -f $hkl, $mode
}
function ReadText {
    $fg = [KTest12]::GetForegroundWindow()
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($fg)
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Document)
    $doc = $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if (-not $doc) { return "<no doc>" }
    $tp = $doc.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)
    return ($tp.DocumentRange.GetText(50) -replace "`r`n","~")
}
function WinSpace {
    [KTest12]::keybd_event(0x5B, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60
    [KTest12]::keybd_event(0x20, 0x39, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60
    [KTest12]::keybd_event(0x20, 0x39, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60
    [KTest12]::keybd_event(0x5B, 0, 2, [UIntPtr]::Zero)
}
Snap "initial           "
WinSpace; Start-Sleep -Milliseconds 1200
Snap "after WinSpace#1  "
WinSpace; Start-Sleep -Milliseconds 1200
Snap "after WinSpace#2  "
[KTest12]::keybd_event(0x41, 0x1E, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40
[KTest12]::keybd_event(0x41, 0x1E, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 400
$t = ReadText
"typed a -> text=[$t]"
[KTest12]::keybd_event(0x1B, 0x01, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40
[KTest12]::keybd_event(0x1B, 0x01, 2, [UIntPtr]::Zero)
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
