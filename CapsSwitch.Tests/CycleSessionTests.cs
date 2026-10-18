using CapsSwitch.App.InputSources;

namespace CapsSwitch.Tests;

public class CycleSessionTests
{
    [Fact]
    public void 正常模式首次短按_主副互切()
    {
        var s = new CycleSession();
        Assert.Equal(CycleAction.PairToggle, s.OnShortPress(0));
        Assert.Equal(CycleMode.Normal, s.Mode);
    }

    [Fact]
    public void 正常模式间隔外短按仍为主副互切()
    {
        var s = new CycleSession();
        s.OnShortPress(0);
        Assert.Equal(CycleAction.PairToggle, s.OnShortPress(0)); // 机器序列已归零
        Assert.Equal(CycleMode.Normal, s.Mode);
    }

    [Fact]
    public void 间隔内第二次短按_进入循环并推进()
    {
        var s = new CycleSession();
        Assert.Equal(CycleAction.PairToggle, s.OnShortPress(0));
        Assert.Equal(CycleAction.CycleNext, s.OnShortPress(1));
        Assert.Equal(CycleMode.Cycling, s.Mode);
    }

    [Fact]
    public void 循环模式中_任意间隔短按都继续循环()
    {
        var s = new CycleSession();
        s.OnShortPress(0);
        s.OnShortPress(1); // 进入循环
        // 机器序列已归零(seq=0),但循环模式不受影响
        Assert.Equal(CycleAction.CycleNext, s.OnShortPress(0));
        Assert.Equal(CycleAction.CycleNext, s.OnShortPress(0));
        Assert.Equal(CycleMode.Cycling, s.Mode);
    }

    [Fact]
    public void 循环模式_长按打断回正常()
    {
        var s = new CycleSession();
        s.OnShortPress(0);
        s.OnShortPress(1);
        s.OnLongPress();
        Assert.Equal(CycleMode.Normal, s.Mode);
        Assert.Equal(CycleAction.PairToggle, s.OnShortPress(0)); // 下一次短按回到主副语义
    }

    [Fact]
    public void 循环模式_列表隐藏回正常()
    {
        var s = new CycleSession();
        s.OnShortPress(0);
        s.OnShortPress(1);
        s.OnListHidden(); // HUD 隐藏计时器超时
        Assert.Equal(CycleMode.Normal, s.Mode);
        Assert.Equal(CycleAction.PairToggle, s.OnShortPress(0));
    }

    [Fact]
    public void 列表隐藏后再连按重新进入循环()
    {
        var s = new CycleSession();
        s.OnShortPress(0);
        s.OnShortPress(1);
        s.OnListHidden();
        Assert.Equal(CycleAction.PairToggle, s.OnShortPress(0));
        Assert.Equal(CycleAction.CycleNext, s.OnShortPress(1));
        Assert.Equal(CycleMode.Cycling, s.Mode);
    }

    [Fact]
    public void Reset_无条件回正常()
    {
        var s = new CycleSession();
        s.OnShortPress(0);
        s.OnShortPress(1);
        s.Reset();
        Assert.Equal(CycleMode.Normal, s.Mode);
    }
}
public class CycleSessionIndexTests
{
    [Fact]
    public void 预览推进_环形并从负一开始()
    {
        var s = new CycleSession();
        Assert.Equal(0, s.PreviewNext(3)); // 未知(-1)→ 0
        Assert.Equal(1, s.PreviewNext(3));
        Assert.Equal(2, s.PreviewNext(3));
        Assert.Equal(0, s.PreviewNext(3)); // 回绕
    }

    [Fact]
    public void 提交索引_负值忽略()
    {
        var s = new CycleSession();
        s.PreviewNext(3);
        s.CommitIndex(2);
        Assert.Equal(2, s.CurrentIndex);
        s.CommitIndex(-1);
        Assert.Equal(2, s.CurrentIndex);
    }

    [Fact]
    public void 主副提交后_预览从其后一项开始()
    {
        var s = new CycleSession();
        s.OnShortPress(0);        // 主副互切(提交目标索引由 AppHost 完成)
        s.CommitIndex(0);         // 目标=列表第 0 项
        Assert.Equal(1, s.PreviewNext(3)); // 循环从下一项开始
    }

    [Fact]
    public void Reset_清空索引()
    {
        var s = new CycleSession();
        s.PreviewNext(3);
        s.Reset();
        Assert.Equal(-1, s.CurrentIndex);
        Assert.Equal(0, s.PreviewNext(3)); // 重新从 0 起步
    }
}
