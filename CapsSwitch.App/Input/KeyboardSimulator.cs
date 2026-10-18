using System.Runtime.InteropServices;
using CapsSwitch.App.Interop;

namespace CapsSwitch.App.Input;

/// <summary>
/// 用 SendInput 模拟按键。注入的 dwExtraInfo 带魔术值,
/// KeyboardHook 凭此识别并放行本进程模拟的输入,避免自反馈。
/// </summary>
public static class KeyboardSimulator
{
    public static readonly UIntPtr ExtraInfoMagic = new(0xCA950001);

    public static void ToggleCapsLock()
    {
        Win32Native.INPUT[] inputs =
        [
            KeyInput(0),
            KeyInput(Win32Native.KEYEVENTF_KEYUP),
        ];        uint sent = Win32Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32Native.INPUT>());
        if (sent != inputs.Length)
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [KeyboardSimulator] SendInput 发送 {sent}/{inputs.Length},LastError=0x{Marshal.GetLastWin32Error():x8}");
    }

    private static Win32Native.INPUT KeyInput(uint flags) => new()
    {
        type = Win32Native.INPUT_KEYBOARD,
        u = new Win32Native.INPUTUNION
        {
            ki = new Win32Native.KEYBDINPUT
            {
                wVk = Win32Native.VK_CAPITAL,
                wScan = 0,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = ExtraInfoMagic,
            },
        },
    };
}
