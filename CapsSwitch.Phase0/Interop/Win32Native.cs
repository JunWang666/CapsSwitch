using System.Runtime.InteropServices;

namespace CapsSwitch.Phase0.Interop;

internal static partial class Win32Native
{
    public const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    public const uint WM_IME_CONTROL = 0x0283;
    public const IntPtr IMC_GETCONVERSIONMODE = 0x0001;
    public const IntPtr IMC_SETCONVERSIONMODE = 0x0002;
    public const IntPtr IMC_GETOPENSTATUS = 0x0005;
    public const int IME_CMODE_NATIVE = 0x0001;
    public const uint INPUTLANGCHANGE_FORWARD = 0x0002;

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetKeyboardLayout(uint idThread);

    [LibraryImport("user32.dll")]
    public static partial int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[]? lpList);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    public static partial IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO pgui);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr LoadKeyboardLayoutW(string pwszKLID, uint flags);

    [LibraryImport("imm32.dll")]
    public static partial IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public uint cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }
}
