using CapsSwitch.App.Overlay;
namespace CapsSwitch.App.InputSources;

public enum CycleAction
{
    /// <summary>主副输入源互切(正常模式,seq=0 语义)。</summary>
    PairToggle,

    /// <summary>沿 OrderedSourceIds 循环推进一项(循环模式)。</summary>
    CycleNext,
}

public enum CycleMode
{
    Normal,
    Cycling,
}

/// <summary>
/// 循环会话(纯逻辑,可单测):生命周期跟随列表 HUD 可见期,不受 SequenceGapMs 限制。
/// 正常模式:短按 seq=0 → 主副互切;短按 seq≥1(300ms 内连按)→ 进入循环模式并推进一项。
/// 循环模式:任意间隔的短按都循环推进;长按或列表隐藏 → 回正常模式。
/// 计时由 HUD 隐藏计时器承担(见 HudController 列表显示时长)。
/// </summary>
public sealed class CycleSession
{
    public CycleMode Mode { get; private set; } = CycleMode.Normal;

    /// <summary>上一次(主副或循环)目标在 OrderedSourceIds 中的索引;-1=未知。</summary>
    public int CurrentIndex { get; private set; } = -1;

    public CycleAction OnShortPress(int sequenceIndex)
    {
        if (Mode == CycleMode.Cycling)
            return CycleAction.CycleNext; // 任意间隔继续循环
        if (sequenceIndex >= 1)
        {
            Mode = CycleMode.Cycling;
            return CycleAction.CycleNext;
        }
        return CycleAction.PairToggle;
    }

    /// <summary>循环预览:立即推进到下一项(按键到达即更新 HUD 高亮,不等切换完成)。</summary>
    public int PreviewNext(int orderedCount)
    {
        CurrentIndex = ListHighlight.Next(CurrentIndex, orderedCount);
        return CurrentIndex;
    }

    /// <summary>对账:切换完成(或 Monitor 实际状态)确认当前项索引。</summary>
    public void CommitIndex(int index)
    {
        if (index >= 0)
            CurrentIndex = index;
    }

    /// <summary>长按 Caps(大写动作)打断循环。</summary>
    public void OnLongPress() => Mode = CycleMode.Normal;

    /// <summary>列表 HUD 隐藏(隐藏计时器超时或被徽章替换)→ 循环会话结束。</summary>
    public void OnListHidden() => Mode = CycleMode.Normal;

    /// <summary>暂停快捷键切换等场景整体复位。</summary>
    public void Reset()
    {
        Mode = CycleMode.Normal;
        CurrentIndex = -1;
    }
}
