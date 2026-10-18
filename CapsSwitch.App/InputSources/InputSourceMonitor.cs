using CapsSwitch.App.Interop;

namespace CapsSwitch.App.InputSources;

public sealed record InputSourceState(IntPtr ForegroundHwnd, IntPtr Hkl, bool NativeMode, bool HasIme);

/// <summary>
/// 后台每 200ms 采样前台 HKL + IME 转换模式(NATIVE 位)+ 前台 hwnd,
/// 变化时触发 Changed 事件(供 Phase 2 HUD 使用)并打日志。
/// 只按 NATIVE 位判断中英文,忽略 SYMBOL 位抖动。
/// 采样到的 HKL 在已知源列表中无匹配时,判定缓存可能过期,自动使服务缓存失效(自愈)。
/// </summary>
public sealed class InputSourceMonitor : IDisposable
{
    private const int PollIntervalMs = 200;

    private readonly Action<string> _log;
    private readonly InputSourceService _service;
    private string? _preferredChineseId;
    private readonly NativeModeReenforceThrottle _throttle = new();
    private readonly Thread _thread;
    private volatile bool _running;
    private InputSourceState? _lastState;
    private volatile InputSourceState? _latestState;
    private bool _manualSwitchNoted;

    /// <summary>最近一次采样的前台状态(UI 线程可读,零跨进程调用——HUD 热路径用)。</summary>
    public InputSourceState? LatestState => _latestState;

    public event Action<InputSourceState>? Changed;

    public InputSourceMonitor(Action<string> log, InputSourceService service, string? preferredChineseId = null)
    {
        _log = log;
        _service = service;
        _preferredChineseId = preferredChineseId;
        _thread = new Thread(Loop) { IsBackground = true, Name = "CapsSwitch-InputSourceMonitor" };
    }

    /// <summary>首选中文 TIP 变化(设置保存后)时更新。</summary>
    public void SetPreferredChineseId(string? id)
    {
        _preferredChineseId = id;
        _manualSwitchNoted = false;
    }

    public void Start() => _thread.Start();

    private void Loop()
    {
        _running = true;
        while (_running)
        {
            try
            {
                var state = Sample();
                if (state is not null)
                    _latestState = state;
                if (state is not null && !Equals(state, _lastState))
                {
                    _lastState = state;
                    if (state.HasIme)
                        _log($"[Monitor] 前台变化: hwnd=0x{state.ForegroundHwnd.ToInt64():x8} HKL=0x{state.Hkl.ToInt64():x8} 模式={(state.NativeMode ? "中文" : "英文")}");
                    else
                        _log($"[Monitor] 前台变化: hwnd=0x{state.ForegroundHwnd.ToInt64():x8} HKL=0x{state.Hkl.ToInt64():x8} (无 IME)");
                    Changed?.Invoke(state);
                    HealIfUnknown(state);
                    MaybeReenforceNative(state);
                }
            }
            catch (Exception ex)
            {
                _log($"[Monitor] 采样异常(忽略): {ex.GetType().Name}: {ex.Message}");
            }
            Thread.Sleep(PollIntervalMs);
        }
    }

    /// <summary>
    /// 自愈兜底:前台活动源是配置的中文 TIP 且 NATIVE 位未置位时,请求 service 再强制一次。
    /// 同一前台窗口只自动纠正一次(节流判定见 NativeModeReenforceThrottle),
    /// 之后同一窗口的英文模式视为用户手动 Shift 切换,不再纠正。
    /// </summary>
    private void MaybeReenforceNative(InputSourceState state)
    {
        if (_preferredChineseId is null || state.NativeMode)
        {
            _manualSwitchNoted = false;
            return;
        }
        var sources = _service.GetSources();
        var matched = sources.FirstOrDefault(s => s.Hkl == state.Hkl)
            ?? sources.FirstOrDefault(s => (s.Hkl.ToInt64() & 0xFFFF) == (state.Hkl.ToInt64() & 0xFFFF));
        if (matched?.Id != _preferredChineseId)
        {
            _manualSwitchNoted = false;
            return;
        }

        long hwnd = state.ForegroundHwnd.ToInt64();
        long hkl = state.Hkl.ToInt64();
        var decision = _throttle.TryAcquire(hwnd, hkl, Environment.TickCount64);
        switch (decision)
        {
            case ReenforceDecision.Allowed:
                _log($"[Monitor] 中文 TIP 处于英文模式,自动强制中文 (hwnd=0x{hwnd:x8})");
                _service.ReenforceNativeMode();
                break;
            case ReenforceDecision.AlreadyCorrectedForWindow:
                if (!_manualSwitchNoted)
                {
                    _manualSwitchNoted = true;
                    _log($"[Monitor] hwnd=0x{hwnd:x8} 已自动纠正过一次,当前英文模式视为手动切换,不再纠正");
                }
                break;
        }
    }

    /// <summary>前台 HKL 不在已知源列表里 → 缓存可能已过期,失效并触发重新枚举。</summary>
    private void HealIfUnknown(InputSourceState state)
    {
        var sources = _service.GetSources();
        bool known = sources.Any(s => s.Hkl == state.Hkl)
            || sources.Any(s => (s.Hkl.ToInt64() & 0xFFFF) == (state.Hkl.ToInt64() & 0xFFFF));
        if (!known)
        {
            _log($"[Monitor] HKL=0x{state.Hkl.ToInt64():x8} 不在已知源列表中,判定缓存过期,触发重新枚举");
            _service.InvalidateCache();
        }
    }

    private static InputSourceState? Sample()
    {
        IntPtr hwnd = Win32Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return null;
        uint tid = Win32Native.GetWindowThreadProcessId(hwnd, out _);
        if (tid == 0)
            return null;
        IntPtr hkl = Win32Native.GetKeyboardLayout(tid);
        // 与 InputSourceService 强制路径统一:走顶层前台窗口的 IMM 上下文(焦点子控件是幻影上下文)
        IntPtr imeWnd = Win32Native.ImmGetDefaultIMEWnd(hwnd);
        bool hasIme = imeWnd != IntPtr.Zero;
        bool native = false;
        // 跨进程读取走 100ms 超时:目标繁忙/挂起按"无 IME/英文"降级,不阻塞采样线程
        if (hasIme && ImeModeIO.TryGetConversionMode(imeWnd, out int mode))
            native = (mode & Win32Native.IME_CMODE_NATIVE) != 0;
        return new InputSourceState(hwnd, hkl, native, hasIme);
    }

    public void Stop()
    {
        _running = false;
        if (!_thread.Join(2000))
            _log("[Monitor] 采样线程未在 2 秒内退出");
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
