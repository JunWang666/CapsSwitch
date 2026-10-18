namespace CapsSwitch.App.Overlay;

/// <summary>光标位置对账判定(纯函数,可单测)。</summary>
public static class CaretPlacement
{
    /// <summary>两光标矩形差异是否 ≤ thresholdPx(视为同一位置,无需移动 HUD)。</summary>
    public static bool NearEqual(in Rectangle a, in Rectangle b, int thresholdPx)
    {
        int dx = Math.Abs(a.X - b.X);
        int dy = Math.Abs(a.Y - b.Y);
        return dx <= thresholdPx && dy <= thresholdPx;
    }
}
