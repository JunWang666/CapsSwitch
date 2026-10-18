using System.Drawing;
using CapsSwitch.App.Overlay;

namespace CapsSwitch.Tests;

public class CaretPlacementTests
{
    [Fact]
    public void 差异小于阈值_视为同位置()
    {
        var a = new Rectangle(100, 100, 2, 20);
        var b = new Rectangle(105, 107, 2, 20);
        Assert.True(CaretPlacement.NearEqual(in a, in b, 8));
    }

    [Fact]
    public void 差异超过阈值_需要移动()
    {
        var a = new Rectangle(100, 100, 2, 20);
        var b = new Rectangle(109, 100, 2, 20);
        Assert.False(CaretPlacement.NearEqual(in a, in b, 8));
        var c = new Rectangle(100, 120, 2, 20);
        Assert.False(CaretPlacement.NearEqual(in a, in c, 8));
    }

    [Fact]
    public void 阈值边界_恰好等于视为同位置()
    {
        var a = new Rectangle(100, 100, 2, 20);
        var b = new Rectangle(108, 108, 2, 20);
        Assert.True(CaretPlacement.NearEqual(in a, in b, 8));
    }
}
