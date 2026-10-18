namespace CapsSwitch.App.Interop;

/// <summary>
/// 跨进程 IME 转换模式读写,统一走 SendMessageTimeout(100ms, ABORTIFHUNG):
/// 目标应用繁忙/挂起时按"读不到"降级,绝不阻塞调用线程。
/// </summary>
public static class ImeModeIO
{
    public const uint TimeoutMs = 100;
    private const uint SmtoAbortIfHung = 0x0002;

    public static bool TryGetConversionMode(IntPtr imeWnd, out int mode)
    {
        mode = 0;
        if (imeWnd == IntPtr.Zero)
            return false;
        IntPtr result = IntPtr.Zero;
        IntPtr ret = Win32Native.SendMessageTimeoutW(
            imeWnd, Win32Native.WM_IME_CONTROL, Win32Native.IMC_GETCONVERSIONMODE, IntPtr.Zero,
            SmtoAbortIfHung, TimeoutMs, out result);
        if (ret == IntPtr.Zero)
            return false; // 超时/拒绝 → 读不到
        mode = result.ToInt32();
        return true;
    }

    public static bool TrySetConversionMode(IntPtr imeWnd, int mode)
    {
        if (imeWnd == IntPtr.Zero)
            return false;
        IntPtr result = IntPtr.Zero;
        IntPtr ret = Win32Native.SendMessageTimeoutW(
            imeWnd, Win32Native.WM_IME_CONTROL, Win32Native.IMC_SETCONVERSIONMODE, new IntPtr(mode),
            SmtoAbortIfHung, TimeoutMs, out result);
        return ret != IntPtr.Zero;
    }
}
