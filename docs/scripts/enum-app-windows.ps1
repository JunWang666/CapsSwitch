Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class EnumW {
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    public static List<string> OfPid(uint target) {
        var list = new List<string>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == target) {
                var sb = new StringBuilder(256); GetClassName(h, sb, 256);
                list.Add(string.Format("hwnd=0x{0:x} visible={1} class={2}", h.ToInt64(), IsWindowVisible(h), sb));
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
"@
[EnumW]::OfPid(62244) | ForEach-Object { $_ }
