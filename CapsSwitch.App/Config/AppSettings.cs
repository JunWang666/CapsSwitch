namespace CapsSwitch.App.Config;

public sealed class AppSettings
{
    public string? PreferredChineseId { get; set; }
    public string? PreferredEnglishId { get; set; }
    public List<string> OrderedSourceIds { get; set; } = [];
    public int LongPressThresholdMs { get; set; } = 400;
    public int SequenceGapMs { get; set; } = 300;
    public int HudDurationMs { get; set; } = 1000;
    /// <summary>聚焦触发 HUD 的冷却秒数:同状态 10s 内不重复显示;0=每次聚焦都显示。</summary>
    public int HudFocusCooldownSeconds { get; set; } = 10;
    public bool HudEnabled { get; set; } = true;
    public bool HotkeysEnabled { get; set; } = true;
    public bool AutoStart { get; set; }
}
