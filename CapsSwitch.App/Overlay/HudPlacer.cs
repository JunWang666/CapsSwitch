namespace CapsSwitch.App.Overlay;

/// <summary>
/// HUD 位置计算(纯函数,可单测):光标**下方** offsetY、水平与光标左对齐;
/// 屏幕底部放不下则翻到光标上方;按工作区裁剪,支持负坐标/多显示器。
/// (IME 候选窗也在光标下方,用户已知并接受,不做避让。)
/// 输入输出均为物理像素。
/// </summary>
public static class HudPlacer
{
    public static Point PlaceBelow(in Rectangle caret, in Size hud, in Rectangle workArea, int offsetY)
    {
        int x = caret.Left;
        x = Math.Clamp(x, workArea.Left, Math.Max(workArea.Left, workArea.Right - hud.Width));

        int y = caret.Bottom + offsetY;
        if (y + hud.Height > workArea.Bottom)
            y = caret.Top - hud.Height - offsetY; // 底部放不下 → 翻到光标上方
        y = Math.Clamp(y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - hud.Height));

        return new Point(x, y);
    }
}
