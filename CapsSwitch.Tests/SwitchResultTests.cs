using CapsSwitch.App.InputSources;

namespace CapsSwitch.Tests;

public class SwitchResultTests
{
    [Fact]
    public void HKL验证通过且无强制项_成功()
    {
        var r = new SwitchResult(true, null);
        Assert.True(r.Success);
        Assert.Equal("切换→拼音: 成功", r.Describe("拼音"));
    }

    [Fact]
    public void HKL验证通过且中文模式确认_成功()
    {
        var r = new SwitchResult(true, true);
        Assert.True(r.Success);
        Assert.Equal("切换→拼音: 成功", r.Describe("拼音"));
    }

    [Fact]
    public void HKL验证通过但中文模式未确认_仍为成功但带警告()
    {
        var r = new SwitchResult(true, false);
        Assert.True(r.Success);
        Assert.Equal("切换→Microsoft Pinyin: 成功;警告: 中文模式强制未确认", r.Describe("Microsoft Pinyin"));
    }

    [Fact]
    public void HKL验证失败_整体失败且警告不泄漏()
    {
        var r = new SwitchResult(false, null);
        Assert.False(r.Success);
        Assert.Equal("切换→拼音: 失败", r.Describe("拼音"));
    }
}
