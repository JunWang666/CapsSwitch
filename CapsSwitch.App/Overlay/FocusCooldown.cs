namespace CapsSwitch.App.Overlay;

/// <summary>
/// 聚焦触发 HUD 的冷却判定(纯函数,可单测)。
/// 状态键 = 输入源 Id + 中英文模式(不含 hwnd):同状态跨窗口切换也冷却;
/// 冷却秒 = 0 → 总是显示(旧行为)。只有真正显示过才应由调用方更新时间戳。
/// </summary>
public static class FocusCooldown
{
    public static bool ShouldSkip(
        string? lastKey, long lastShownAtMs,
        string? currentKey, long nowMs,
        int cooldownSeconds)
    {
        if (cooldownSeconds <= 0)
            return false; // 冷却关闭 = 旧行为
        if (lastKey is null || currentKey is null)
            return false; // 无历史或当前状态未知 → 显示
        if (!string.Equals(lastKey, currentKey, StringComparison.OrdinalIgnoreCase))
            return false; // 状态变化 → 立即显示
        return nowMs - lastShownAtMs < cooldownSeconds * 1000L;
    }
}
