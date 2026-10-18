namespace CapsSwitch.App.Overlay;

/// <summary>列表模式高亮推进(纯函数,可单测):环形移动;空列表返回 -1。</summary>
public static class ListHighlight
{
    public static int Move(int currentIndex, int delta, int count)
    {
        if (count <= 0)
            return -1;
        if (currentIndex < 0)
            return delta >= 0 ? 0 : count - 1;
        int m = delta % count;
        return (currentIndex + m + count) % count;
    }

    /// <summary>前进一步(等价 Move(i, +1, count))。</summary>
    public static int Next(int currentIndex, int count) => Move(currentIndex, 1, count);
}
