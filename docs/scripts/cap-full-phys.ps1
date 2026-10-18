Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public class CapY {
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    public static void FullPhys(string path) {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        int x = GetSystemMetrics(76), y = GetSystemMetrics(77);
        int w = GetSystemMetrics(78), h = GetSystemMetrics(79);
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        IntPtr bmp = CreateCompatibleBitmap(screen, w, h);
        IntPtr old = SelectObject(mem, bmp);
        BitBlt(mem, 0, 0, w, h, screen, x, y, 0x00CC0020 | 0x40000000);
        using (Bitmap b = Image.FromHbitmap(bmp)) { b.Save(path, ImageFormat.Png); }
        SelectObject(mem, old); DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
        Console.WriteLine(string.Format("captured {0}x{1} from {2},{3}", w, h, x, y));
    }
}
"@
[CapY]::FullPhys("C:\DevCodes\Others\CapsSwitch\docs\shots\menu-phys.png")
