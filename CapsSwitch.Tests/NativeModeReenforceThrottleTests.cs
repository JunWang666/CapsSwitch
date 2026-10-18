using CapsSwitch.App.InputSources;

namespace CapsSwitch.Tests;

public class NativeModeReenforceThrottleTests
{
    private const long HwndA = 0x1000;
    private const long HwndB = 0x2000;
    private const long HklZh = 0x08040804;

    [Fact]
    public void 首次观察_允许()
    {
        var t = new NativeModeReenforceThrottle(3000);
        Assert.Equal(ReenforceDecision.Allowed, t.TryAcquire(HwndA, HklZh, 0));
    }

    [Fact]
    public void 同一窗口再次请求_视为已纠正而拒绝()
    {
        var t = new NativeModeReenforceThrottle(3000);
        Assert.Equal(ReenforceDecision.Allowed, t.TryAcquire(HwndA, HklZh, 0));
        Assert.Equal(ReenforceDecision.AlreadyCorrectedForWindow, t.TryAcquire(HwndA, HklZh, 5000));
    }

    [Fact]
    public void 窗口切换后恢复一次额度()
    {
        var t = new NativeModeReenforceThrottle(3000);
        Assert.Equal(ReenforceDecision.Allowed, t.TryAcquire(HwndA, HklZh, 0));
        Assert.Equal(ReenforceDecision.Allowed, t.TryAcquire(HwndB, HklZh, 100));
        // 回到旧窗口:额度随窗口恢复,但 (hwnd,hkl) 间隔未过 → 节流
        Assert.Equal(ReenforceDecision.ThrottledByInterval, t.TryAcquire(HwndA, HklZh, 200));
    }

    [Fact]
    public void 同一组合间隔内节流_间隔过后允许()
    {
        var t = new NativeModeReenforceThrottle(3000);
        Assert.Equal(ReenforceDecision.Allowed, t.TryAcquire(HwndA, HklZh, 0));
        // 切走再切回,额度恢复但间隔未过
        Assert.Equal(ReenforceDecision.Allowed, t.TryAcquire(HwndB, HklZh, 100));
        Assert.Equal(ReenforceDecision.ThrottledByInterval, t.TryAcquire(HwndA, HklZh, 200));
        Assert.Equal(ReenforceDecision.Allowed, t.TryAcquire(HwndA, HklZh, 3100));
        // 额度已再次用掉
        Assert.Equal(ReenforceDecision.AlreadyCorrectedForWindow, t.TryAcquire(HwndA, HklZh, 3200));
    }

    [Fact]
    public void 同一窗口不同hkl_仍视为已纠正()
    {
        var t = new NativeModeReenforceThrottle(3000);
        Assert.Equal(ReenforceDecision.Allowed, t.TryAcquire(HwndA, HklZh, 0));
        Assert.Equal(ReenforceDecision.AlreadyCorrectedForWindow, t.TryAcquire(HwndA, 0xE0200804, 5000));
    }

    [Fact]
    public void 用户手动切英文场景_模拟交错窗口各纠正一次后不再纠正()
    {
        var t = new NativeModeReenforceThrottle(3000);
        // 自动切换到记事本 → 纠正
        Assert.Equal(ReenforceDecision.Allowed, t.TryAcquire(HwndA, HklZh, 0));
        // 用户在记事本里 Shift 切英文 → 不再纠正
        Assert.Equal(ReenforceDecision.AlreadyCorrectedForWindow, t.TryAcquire(HwndA, HklZh, 2000));
        // 切到 chrome 再切回记事本:chrome 有额度,记事本在 3s 后也有额度
        Assert.Equal(ReenforceDecision.Allowed, t.TryAcquire(HwndB, HklZh, 4000));
        Assert.Equal(ReenforceDecision.Allowed, t.TryAcquire(HwndA, HklZh, 8000));
        // 再次手动切英文 → 不再纠正
        Assert.Equal(ReenforceDecision.AlreadyCorrectedForWindow, t.TryAcquire(HwndA, HklZh, 8500));
    }
}
