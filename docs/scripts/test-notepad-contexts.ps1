Add-Type -AssemblyName UIAutomationClient
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KTest6 {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint tid);
    [DllImport("imm32.dll")] public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO info);
    [StructLayout(LayoutKind.Sequential)] public struct GUITHREADINFO {
        public uint cbSize; public uint flags; public IntPtr hwndActive; public IntPtr hwndFocus;
        public IntPtr hwndCapture; public IntPtr hwndMenuOwner; public IntPtr hwndMoveSize; public IntPtr hwndCaret;
        public int rcCaretL; public int rcCaretT; public int rcCaretR; public int rcCaretB;
    }
}
"@
Start-Process notepad.exe; Start-Sleep -Milliseconds 1500
$p = Get-Process notepad | Select-Object -First 1
(New-Object -ComObject WScript.Shell).AppActivate($p.Id) | Out-Null
Start-Sleep -Milliseconds 800

function ReadModes() {
    $fg = [KTest6]::GetForegroundWindow()
    $pidv = 0; $tid = [KTest6]::GetWindowThreadProcessId($fg, [ref]$pidv)
    $hkl = [int64][KTest6]::GetKeyboardLayout($tid)
    $imeTop = [KTest6]::ImmGetDefaultIMEWnd($fg)
    $modeTop = if ($imeTop -ne [IntPtr]::Zero) { [int][KTest6]::SendMessage($imeTop, 0x283, [IntPtr]0x1, [IntPtr]0) } else { -1 }
    $info = New-Object KTest6+GUITHREADINFO
    $info.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($info)
    $modeFocus = -2; $focusHwnd = [IntPtr]::Zero
    if ([KTest6]::GetGUIThreadInfo($tid, [ref]$info)) {
        $focusHwnd = $info.hwndFocus
        $imeF = [KTest6]::ImmGetDefaultIMEWnd($info.hwndFocus)
        if ($imeF -ne [IntPtr]::Zero) { $modeFocus = [int][KTest6]::SendMessage($imeF, 0x283, [IntPtr]0x1, [IntPtr]0) }
    }
    return @{ Fg=$fg; Focus=$focusHwnd; Hkl=$hkl; ModeTop=$modeTop; ModeFocus=$modeFocus }
}
function Show($tag) {
    $s = ReadModes
    "$tag top=0x{0:x} focus=0x{1:x} hkl=0x{2:x8} modeTop=0x{3:x4} modeFocus=0x{4:x4}" -f [int64]$s.Fg, [int64]$s.Focus, $s.Hkl, $s.ModeTop, $s.ModeFocus
}
function SetTop($m) {
    $fg = [KTest6]::GetForegroundWindow()
    $ime = [KTest6]::ImmGetDefaultIMEWnd($fg)
    [KTest6]::SendMessage($ime, 0x283, [IntPtr]0x2, [IntPtr]$m) | Out-Null
}
function TypeKey($vk, $scan) {
    [KTest6]::keybd_event($vk, $scan, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40
    [KTest6]::keybd_event($vk, $scan, 2, [UIntPtr]::Zero)
}
function ReadText() {
    $fg = [KTest6]::GetForegroundWindow()
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($fg)
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Document)
    $doc = $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if (-not $doc) { return "<no doc>" }
    $tp = $doc.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)
    return ($tp.DocumentRange.GetText(50) -replace "`r`n","~")
}

Show "step0 initial        "
SetTop 0; Start-Sleep -Milliseconds 300; Show "step1 SET top=EN     "
TypeKey 0x41 0x1E; Start-Sleep -Milliseconds 300
"  typed a -> text=[$(ReadText)]  (期望: 提交 a = 英文模式)"
# 反复 SET 中文,最多 2 秒
$ok = $false
for ($i = 0; $i -lt 13; $i++) {
    SetTop 1; Start-Sleep -Milliseconds 150
    $s = ReadModes
    if (($s.ModeTop -band 1) -eq 1) { $ok = $true; "  set-zh 第 $($i+1) 次读回 modeTop=0x{0:x4}" -f $s.ModeTop; break }
}
if (-not $ok) { "  set-zh 13 次均未读回 NATIVE" }
Show "step2 SET top=ZH     "
TypeKey 0x42 0x30; Start-Sleep -Milliseconds 400
$txt = ReadText
"  typed b -> text=[$txt]  (期望: 无提交 = 中文模式候选中)"
TypeKey 0x1B 0x01; Start-Sleep -Milliseconds 200  # Esc 关闭候选
"  after Esc text=[$(ReadText)]"
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
