using CapsSwitch.App.Overlay;

namespace CapsSwitch.Tests;

public class FocusCooldownTests
{
    private const string KeyZhNative = "pinyin-guid|native";
    private const string KeyZhAlpha = "pinyin-guid|alpha";
    private const long T0 = 1_000_000;

    [Fact]
    public void 十秒内同状态_跳过()
    {
        Assert.True(FocusCooldown.ShouldSkip(KeyZhNative, T0, KeyZhNative, T0 + 3000, 10));
        Assert.True(FocusCooldown.ShouldSkip(KeyZhNative, T0, KeyZhNative, T0 + 9999, 10));
    }

    [Fact]
    public void 十秒后同状态_显示()
    {
        Assert.False(FocusCooldown.ShouldSkip(KeyZhNative, T0, KeyZhNative, T0 + 10_000, 10));
        Assert.False(FocusCooldown.ShouldSkip(KeyZhNative, T0, KeyZhNative, T0 + 60_000, 10));
    }

    [Fact]
    public void 十秒内状态变化_立即显示()
    {
        Assert.False(FocusCooldown.ShouldSkip(KeyZhNative, T0, KeyZhAlpha, T0 + 1000, 10));
        Assert.False(FocusCooldown.ShouldSkip(KeyZhNative, T0, "other|native", T0 + 100, 10));
    }

    [Fact]
    public void 冷却为零_总是显示()
    {
        Assert.False(FocusCooldown.ShouldSkip(KeyZhNative, T0, KeyZhNative, T0 + 1, 0));
        Assert.False(FocusCooldown.ShouldSkip(KeyZhNative, T0, KeyZhNative, T0 + 1, -5));
    }

    [Fact]
    public void 无历史或当前未知_显示()
    {
        Assert.False(FocusCooldown.ShouldSkip(null, 0, KeyZhNative, T0, 10));
        Assert.False(FocusCooldown.ShouldSkip(KeyZhNative, T0, null, T0, 10));
    }

    [Fact]
    public void 状态键大小写不敏感()
    {
        Assert.True(FocusCooldown.ShouldSkip("ABC|native", T0, "abc|native", T0 + 1000, 10));
    }
}
