using CapsSwitch.App.Input;

namespace CapsSwitch.Tests;

public class CapsKeyStateMachineTests
{
    private const long Threshold = 400;
    private const long Gap = 300;

    private static (CapsKeyStateMachine sm, List<CapsKeyAction> actions) Create()
    {
        var sm = new CapsKeyStateMachine(Threshold, Gap);
        var actions = new List<CapsKeyAction>();
        sm.Action += actions.Add;
        return (sm, actions);
    }

    [Fact]
    public void 按下后阈值前释放_产生短按序列0()
    {
        var (sm, actions) = Create();
        sm.OnCapsDown(0, false);
        sm.OnCapsUp(100);
        var act = Assert.Single(actions);
        Assert.Equal(new CapsKeyAction.ShortPress(0), act);
    }

    [Theory]
    [InlineData(399)]
    [InlineData(400)] // 计时器未触发时释放,仍算短按
    public void 阈值边界前释放仍是短按(long releaseAt)
    {
        var (sm, actions) = Create();
        sm.OnCapsDown(0, false);
        sm.OnCapsUp(releaseAt);
        Assert.Equal(new CapsKeyAction.ShortPress(0), Assert.Single(actions));
    }

    [Fact]
    public void 计时到399未到阈值_不触发长按()
    {
        var (sm, actions) = Create();
        sm.OnCapsDown(0, false);
        sm.OnTimer(399);
        Assert.Empty(actions);
    }

    [Fact]
    public void 计时到400触发长按_之后释放无动作()
    {
        var (sm, actions) = Create();
        sm.OnCapsDown(0, false);
        sm.OnTimer(400);
        Assert.Equal(new CapsKeyAction.LongPressToggleCaps(), Assert.Single(actions));
        sm.OnCapsUp(600);
        Assert.Single(actions);
    }

    [Fact]
    public void 计时到401也触发长按()
    {
        var (sm, actions) = Create();
        sm.OnCapsDown(0, false);
        sm.OnTimer(401);
        Assert.Equal(new CapsKeyAction.LongPressToggleCaps(), Assert.Single(actions));
    }

    [Fact]
    public void 自动重复的Down被忽略()
    {
        var (sm, actions) = Create();
        sm.OnCapsDown(0, false);
        sm.OnCapsDown(50, true);
        sm.OnCapsDown(100, true);
        sm.OnCapsUp(150);
        Assert.Equal(new CapsKeyAction.ShortPress(0), Assert.Single(actions));
    }

    [Fact]
    public void 已按下时再来非重复Down也忽略()
    {
        var (sm, actions) = Create();
        sm.OnCapsDown(0, false);
        sm.OnCapsDown(120, false);
        sm.OnCapsUp(200);
        Assert.Equal(new CapsKeyAction.ShortPress(0), Assert.Single(actions));
    }

    [Fact]
    public void 无对应按下的重复Down不建立状态()
    {
        var (sm, actions) = Create();
        sm.OnCapsDown(0, true);
        sm.OnCapsUp(50);
        Assert.Empty(actions);
    }

    [Fact]
    public void 间隔300ms内的连续短按递增序列()
    {
        var (sm, actions) = Create();
        sm.OnCapsDown(0, false);
        sm.OnCapsUp(50);
        sm.OnCapsDown(200, false);
        sm.OnCapsUp(250);
        sm.OnCapsDown(400, false);
        sm.OnCapsUp(550); // 距上次释放 300,仍在窗口内
        Assert.Equal(
            [new CapsKeyAction.ShortPress(0), new CapsKeyAction.ShortPress(1), new CapsKeyAction.ShortPress(2)],
            actions);
    }

    [Fact]
    public void 间隔超过300ms序列重置为0()
    {
        var (sm, actions) = Create();
        sm.OnCapsDown(0, false);
        sm.OnCapsUp(50);
        sm.OnCapsDown(200, false);
        sm.OnCapsUp(250);
        sm.OnCapsDown(1000, false);
        sm.OnCapsUp(1050); // 距上次释放 800 > 300
        Assert.Equal(
            [new CapsKeyAction.ShortPress(0), new CapsKeyAction.ShortPress(1), new CapsKeyAction.ShortPress(0)],
            actions);
    }

    [Fact]
    public void 长按后序列重置_下一次短按为0()
    {
        var (sm, actions) = Create();
        sm.OnCapsDown(0, false);
        sm.OnTimer(400);
        sm.OnCapsUp(500);
        sm.OnCapsDown(1000, false);
        sm.OnCapsUp(1050);
        Assert.Equal(
            [new CapsKeyAction.LongPressToggleCaps(), new CapsKeyAction.ShortPress(0)],
            actions);
    }

    [Fact]
    public void 短按后长按再短按_序列从0重新开始()
    {
        var (sm, actions) = Create();
        sm.OnCapsDown(0, false);
        sm.OnCapsUp(50); // ShortPress(0)
        sm.OnCapsDown(200, false);
        sm.OnTimer(700); // LongPress
        sm.OnCapsUp(800);
        sm.OnCapsDown(1000, false);
        sm.OnCapsUp(1100); // 距上次短按释放 1050>300 且被重置 → 0
        Assert.Equal(
            [new CapsKeyAction.ShortPress(0), new CapsKeyAction.LongPressToggleCaps(), new CapsKeyAction.ShortPress(0)],
            actions);
    }

    [Fact]
    public void Caps按住期间按其他键_本次按下作废()
    {
        var (sm, actions) = Create();
        sm.OnCapsDown(0, false);
        sm.OnOtherKeyDown(100);
        sm.OnCapsUp(150);
        Assert.Empty(actions);
    }

    [Fact]
    public void 组合取消后计时器不再触发长按()
    {
        var (sm, actions) = Create();
        sm.OnCapsDown(0, false);
        sm.OnOtherKeyDown(100);
        sm.OnTimer(400);
        Assert.Empty(actions);
    }

    [Fact]
    public void 组合取消后再次按下仍可用()
    {
        var (sm, actions) = Create();
        sm.OnCapsDown(0, false);
        sm.OnOtherKeyDown(100);
        sm.OnCapsUp(150);
        sm.OnCapsDown(500, false);
        sm.OnCapsUp(560);
        Assert.Equal(new CapsKeyAction.ShortPress(0), Assert.Single(actions));
    }

    [Fact]
    public void 空闲时其他键不影响状态机()
    {
        var (sm, actions) = Create();
        sm.OnOtherKeyDown(100);
        sm.OnOtherKeyDown(200);
        Assert.Empty(actions);
    }
}
