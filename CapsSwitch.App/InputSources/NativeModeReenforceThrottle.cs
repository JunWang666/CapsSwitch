namespace CapsSwitch.App.InputSources;

public enum ReenforceDecision
{
    /// <summary>允许强制执行一次(新前台首次观察到,或间隔已过)。</summary>
    Allowed,
    /// <summary>同一前台窗口已自动纠正过一次,后续状态视为用户手动切换,不再纠正。</summary>
    AlreadyCorrectedForWindow,
    /// <summary>距上次尝试不足节流间隔。</summary>
    ThrottledByInterval,
}

/// <summary>
/// Monitor 自动纠正中文模式的节流判定(纯逻辑,可单测)。
/// 规则:同一前台 hwnd 只自动纠正一次(首次观察到该状态时);窗口切换后恢复一次额度;
/// 同一 (hwnd,hkl) 两次尝试之间至少间隔 intervalMs。
/// </summary>
public sealed class NativeModeReenforceThrottle
{
    private readonly long _intervalMs;
    private readonly Dictionary<(long hwnd, long hkl), long> _attempts = new();
    private long _currentHwnd = -1;
    private bool _currentHwndCorrected;

    public NativeModeReenforceThrottle(long intervalMs = 3000)
    {
        _intervalMs = intervalMs;
    }

    public ReenforceDecision TryAcquire(long hwnd, long hkl, long nowMs)
    {
        if (hwnd != _currentHwnd)
        {
            _currentHwnd = hwnd;
            _currentHwndCorrected = false;
        }
        if (_currentHwndCorrected)
            return ReenforceDecision.AlreadyCorrectedForWindow;
        if (_attempts.TryGetValue((hwnd, hkl), out long last) && nowMs - last < _intervalMs)
            return ReenforceDecision.ThrottledByInterval;

        _attempts[(hwnd, hkl)] = nowMs;
        _currentHwndCorrected = true;
        return ReenforceDecision.Allowed;
    }
}
