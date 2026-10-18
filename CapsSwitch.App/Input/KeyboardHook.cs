using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using CapsSwitch.App.Interop;

namespace CapsSwitch.App.Input;

public abstract record HookEvent
{
    /// <summary>AtMs=钩子回调时刻的时间戳(事件原始时间),消费者不得用处理时刻替代。</summary>
    public sealed record CapsDown(bool IsRepeat, long AtMs) : HookEvent;
    public sealed record CapsUp(long AtMs) : HookEvent;
    public sealed record OtherKeyDown(long AtMs) : HookEvent;
}

/// <summary>
/// WH_KEYBOARD_LL 钩子。在专用线程安装并跑 GetMessage 消息泵;
/// 回调里只解析 KBDLLHOOKSTRUCT 并写 BlockingCollection,立即返回。
/// Caps Lock 的 Down/Up 吞掉(返回 1),其他键一律放行;
/// 本进程模拟输入(KeyboardSimulator 的 dwExtraInfo 魔术值)直接放行且不产生事件。
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    public BlockingCollection<HookEvent> Events { get; } = new();

    private readonly Action<string> _log;
    private readonly Thread _thread;
    private IntPtr _hookHandle;
    private uint _threadId;
    private volatile bool _running = true;
    private volatile bool _capsDown;

    /// <summary>暂停快捷键切换时=true:Caps Lock 完全放行,状态机不再收到事件。</summary>
    public volatile bool PassthroughCaps;

    public KeyboardHook(Action<string> log)
    {
        _log = log;
        _thread = new Thread(HookThreadProc) { IsBackground = true, Name = "CapsSwitch-KeyboardHook" };
        _thread.Start();
    }

    public bool WaitForReady(int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (_hookHandle != IntPtr.Zero)
                return true;
            if (!_running)
                return false;
            Thread.Sleep(10);
        }
        return _hookHandle != IntPtr.Zero;
    }

    private void HookThreadProc()
    {
        _threadId = GetCurrentThreadId();
        using var proc = System.Diagnostics.Process.GetCurrentProcess();
        IntPtr hMod = proc.MainModule?.BaseAddress ?? IntPtr.Zero;
        _hookHandle = Win32Native.SetWindowsHookExW(Win32Native.WH_KEYBOARD_LL, HookCallback, hMod, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            _log($"[KeyboardHook] SetWindowsHookEx 失败,LastError=0x{Marshal.GetLastWin32Error():x8}");
            _running = false;
            Events.CompleteAdding();
            return;
        }
        _log("[KeyboardHook] WH_KEYBOARD_LL 已安装");

        while (_running)
        {
            int ret = Win32Native.GetMessageW(out var msg, IntPtr.Zero, 0, 0);
            if (ret <= 0) // 0=WM_QUIT,-1=错误
                break;
        }

        if (_hookHandle != IntPtr.Zero)
        {
            Win32Native.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
            _log("[KeyboardHook] 已卸载");
        }
        Events.CompleteAdding();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<Win32Native.KBDLLHOOKSTRUCT>(lParam);
            if (info.dwExtraInfo == KeyboardSimulator.ExtraInfoMagic)
            {
                // 本进程模拟输入:放行,不进状态机
                return Win32Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
            }

            int msg = wParam.ToInt32();
            long atMs = Environment.TickCount64; // 事件原始时间戳(回调时刻)
            if (info.vkCode == Win32Native.VK_CAPITAL)
            {
                if (PassthroughCaps)
                {
                    // 暂停切换:Caps 完全放行,不产状态机事件
                    _capsDown = false;
                    return Win32Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
                }
                bool isDown = msg is Win32Native.WM_KEYDOWN or Win32Native.WM_SYSKEYDOWN;
                if (isDown)
                {
                    bool repeat = _capsDown;
                    _capsDown = true;
                    if (!repeat)
                        Events.TryAdd(new HookEvent.CapsDown(false, atMs));
                }
                else
                {
                    _capsDown = false;
                    Events.TryAdd(new HookEvent.CapsUp(atMs));
                }
                // 吞掉 Caps Lock,由状态机决定行为
                return new IntPtr(1);
            }

            if (msg is Win32Native.WM_KEYDOWN or Win32Native.WM_SYSKEYDOWN)
                Events.TryAdd(new HookEvent.OtherKeyDown(atMs));
        }
        return Win32Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private int _disposed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _running = false;
        // 唤醒消息泵使其退出
        Win32Native.PostThreadMessageW(_threadId, Win32Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        if (!_thread.Join(2000))
            _log("[KeyboardHook] 钩子线程未在 2 秒内退出");
        Events.Dispose();
        GC.SuppressFinalize(this);
    }
}
