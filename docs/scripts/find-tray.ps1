Add-Type @"
using System;
using System.Runtime.InteropServices;
public class TrayWnd {
    [DllImport("user32.dll", SetLastError=true, CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
}
"@
$tray = [TrayWnd]::FindWindow("Shell_TrayWnd", $null)
$overflow = [TrayWnd]::FindWindow("NotifyIconOverflowWindow", $null)
"Shell_TrayWnd=$tray NotifyIconOverflowWindow=$overflow"
