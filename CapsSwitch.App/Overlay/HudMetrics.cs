namespace CapsSwitch.App.Overlay;

/// <summary>
/// HUD 的逻辑像素设计值与 DPI 换算(纯函数,可单测)。
/// 所有视觉尺寸按 96 DPI 逻辑值定义,运行时换算为物理像素,保证任何缩放下视觉一致。
/// </summary>
public static class HudMetrics
{
    public const int LogicalFontPt = 10;        // 字号(点,GDI+ 随 DPI 自动缩放)
    public const int LogicalPadHorizontal = 8;  // 水平内边距(逻辑 px)
    public const int LogicalPadVertical = 3;    // 垂直内边距(逻辑 px)
    public const int LogicalCornerRadius = 8;   // 圆角半径(逻辑 px)
    public const double OpacityValue = 0.78;    // 整体透明度
    public const int OffsetFromCaretY = 4;      // 距光标下方偏移(逻辑 px)

    public static int Scale(int logicalPx, int dpi) =>
        logicalPx <= 0 ? 0 : (int)Math.Round(logicalPx * (dpi / 96.0), MidpointRounding.AwayFromZero);
}
