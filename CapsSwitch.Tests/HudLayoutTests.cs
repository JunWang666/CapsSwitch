using System.Drawing;
using CapsSwitch.App.Overlay;

namespace CapsSwitch.Tests;

public class HudMetricsTests
{
    [Theory]
    [InlineData(10, 96, 10)]
    [InlineData(10, 144, 15)]   // 150%
    [InlineData(10, 192, 20)]   // 200%
    [InlineData(8, 120, 10)]    // 125%
    [InlineData(0, 144, 0)]
    public void Scale_逻辑像素按DPI换算(int logical, int dpi, int expected)
    {
        Assert.Equal(expected, HudMetrics.Scale(logical, dpi));
    }

    [Fact]
    public void 设计值已按要求缩小()
    {
        Assert.Equal(10, HudMetrics.LogicalFontPt);
        Assert.Equal(8, HudMetrics.LogicalPadHorizontal);
        Assert.Equal(3, HudMetrics.LogicalPadVertical);
        Assert.True(HudMetrics.OpacityValue >= 0.7 && HudMetrics.OpacityValue <= 0.85);
    }
}

public class HudPlacerTests
{
    private static readonly Size Hud = new(60, 30);
    private static readonly Rectangle ScreenA = new(0, 0, 1920, 1080);
    private static readonly Rectangle ScreenB = new(-1920, 0, 1920, 1080); // 左侧副屏(负坐标)

    [Fact]
    public void 常规_光标下方左对齐()
    {
        var caret = new Rectangle(100, 100, 2, 20);
        var p = HudPlacer.PlaceBelow(in caret, in Hud, in ScreenA, 4);
        Assert.Equal(100, p.X);          // 与光标左对齐
        Assert.Equal(124, p.Y);          // caret.Bottom(120) + 4
    }

    [Fact]
    public void 底部放不下_翻到光标上方()
    {
        var caret = new Rectangle(100, 1060, 2, 20); // 贴底
        var p = HudPlacer.PlaceBelow(in caret, in Hud, in ScreenA, 4);
        Assert.Equal(1060 - 30 - 4, p.Y);
    }

    [Fact]
    public void 左缘放不下_水平裁剪到工作区左缘()
    {
        var caret = new Rectangle(-10, 100, 2, 20);
        var p = HudPlacer.PlaceBelow(in caret, in Hud, in ScreenA, 4);
        Assert.Equal(0, p.X);
    }

    [Fact]
    public void 副屏负坐标_位置落在工作区内()
    {
        var caret = new Rectangle(-100, 100, 2, 20);
        var p = HudPlacer.PlaceBelow(in caret, in Hud, in ScreenB, 4);
        Assert.InRange(p.X, ScreenB.Left, ScreenB.Right - Hud.Width);
        Assert.InRange(p.Y, ScreenB.Top, ScreenB.Bottom - Hud.Height);
    }

    [Fact]
    public void 极端情况_光标占满工作区_不抛异常()
    {
        var caret = new Rectangle(0, 0, 1920, 1080);
        var p = HudPlacer.PlaceBelow(in caret, in Hud, in ScreenA, 4);
        Assert.InRange(p.X, ScreenA.Left, ScreenA.Right - Hud.Width);
        Assert.InRange(p.Y, ScreenA.Top, ScreenA.Bottom - Hud.Height);
    }
}

public class ListHighlightTests
{
    [Fact]
    public void 前进_环形回绕()
    {
        Assert.Equal(1, ListHighlight.Next(0, 3));
        Assert.Equal(2, ListHighlight.Next(1, 3));
        Assert.Equal(0, ListHighlight.Next(2, 3)); // 回绕
    }

    [Fact]
    public void 后退()
    {
        Assert.Equal(2, ListHighlight.Move(0, -1, 3));
        Assert.Equal(0, ListHighlight.Move(1, -1, 3));
    }

    [Fact]
    public void 大步长按count取模()
    {
        Assert.Equal(2, ListHighlight.Move(1, 4, 3)); // (1+4)%3
        Assert.Equal(0, ListHighlight.Move(0, -3, 3));
    }

    [Fact]
    public void 非法索引从头部或尾部起步()
    {
        Assert.Equal(0, ListHighlight.Move(-1, 1, 3));
        Assert.Equal(2, ListHighlight.Move(-1, -1, 3));
    }

    [Fact]
    public void 合法索引起步正常推进()
    {
        Assert.Equal(1, ListHighlight.Move(0, 1, 3));
    }

    [Fact]
    public void 空列表返回负一()
    {
        Assert.Equal(-1, ListHighlight.Next(0, 0));
        Assert.Equal(-1, ListHighlight.Next(2, 0));
    }
}
