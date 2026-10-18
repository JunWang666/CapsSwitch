Add-Type -AssemblyName UIAutomationClient
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KTest11 {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint tid);
    [DllImport("imm32.dll")] public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
"@
$phase0 = "C:\DevCodes\Others\CapsSwitch\CapsSwitch.Phase0\bin\Debug\net10.0-windows\CapsSwitch.Phase0.exe"
Start-Process notepad.exe; Start-Sleep -Milliseconds 1500
$p = Get-Process notepad | Select-Object -First 1
(New-Object -ComObject WScript.Shell).AppActivate($p.Id) | Out-Null
Start-Sleep -Milliseconds 800
function Snap($tag) {
    $fg = [KTest11]::GetForegroundWindow()
    $pidv = 0; $tid = [KTest11]::GetWindowThreadProcessId($fg, [ref]$pidv)
    $hkl = [int64][KTest11]::GetKeyboardLayout($tid)
    $ime = [KTest11]::ImmGetDefaultIMEWnd($fg)
    $mode = [int][KTest11]::SendMessage($ime, 0x283, [IntPtr]0x1, [IntPtr]0)
    "$tag hkl=0x{0:x8} mode=0x{1:x4}" -f $hkl, $mode
}
function ReadText {
    $fg = [KTest11]::GetForegroundWindow()
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($fg)
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Document)
    $doc = $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if (-not $doc) { return "<no doc>" }
    $tp = $doc.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)
    return ($tp.DocumentRange.GetText(50) -replace "`r`n","~")
}
Snap "initial            "
[KTest11]::PostMessage([KTest11]::GetForegroundWindow(), 0x50, [IntPtr]0, [IntPtr]0x04090409) | Out-Null
Start-Sleep -Milliseconds 600
Snap "after msg->EN      "
# TSF FORSESSION 激活拼音,再 msg 切换线程 HKL
& $phase0 activate zh | Out-Null
Start-Sleep -Milliseconds 300
[KTest11]::PostMessage([KTest11]::GetForegroundWindow(), 0x50, [IntPtr]0, [IntPtr]0x08040804) | Out-Null
Start-Sleep -Milliseconds 800
Snap "after TSF+msg->ZH  "
[KTest11]::keybd_event(0x41, 0x1E, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40
[KTest11]::keybd_event(0x41, 0x1E, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 400
$t = ReadText
"typed a -> text=[$t]  (空/候选=中文模式; 含a=英文模式)"
[KTest11]::keybd_event(0x1B, 0x01, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40
[KTest11]::keybd_event(0x1B, 0x01, 2, [UIntPtr]::Zero)
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
