namespace CapsSwitch.App.Input;

/// <summary>
/// CapsLock 键的语义动作。状态机只输出动作,不触碰输入源。
/// </summary>
public abstract record CapsKeyAction
{
    /// <summary>短按。SequenceIndex 从 0 开始,连按按 sequenceGapMs 间隔递增。</summary>
    public sealed record ShortPress(int SequenceIndex) : CapsKeyAction;

    /// <summary>长按达到阈值:应模拟一次 CapsLock 系统切换。</summary>
    public sealed record LongPressToggleCaps : CapsKeyAction;
}

/// <summary>
/// CapsLock 按下/短按/长按/组合取消的纯逻辑状态机。
/// 时间由调用方以毫秒时间戳注入,零 P/Invoke,可单测。
/// 计时约定:调用方在按下后 longPressThresholdMs 时调用 OnTimer。
/// </summary>
public sealed class CapsKeyStateMachine
{
    private enum State { Idle, CapsDown, Cancelled, LongPressFired }

    private long _longPressThresholdMs;
    private long _sequenceGapMs;

    private State _state = State.Idle;
    private long _capsDownAtMs;
    private long? _lastShortReleaseMs;
    private int _sequenceIndex;

    public CapsKeyStateMachine(long longPressThresholdMs = 400, long sequenceGapMs = 300)
    {
        _longPressThresholdMs = longPressThresholdMs;
        _sequenceGapMs = sequenceGapMs;
    }

    public event Action<CapsKeyAction>? Action;

    public void OnCapsDown(long nowMs, bool isRepeat)
    {
        if (_state != State.Idle)
        {
            // 已处于按下相关状态:自动重复或状态推导的重复 Down 一律忽略
            return;
        }
        if (isRepeat)
        {
            // 无对应按下的重复 Down(本进程启动前按住等),不建立新状态
            return;
        }
        _state = State.CapsDown;
        _capsDownAtMs = nowMs;
    }

    public void OnCapsUp(long nowMs)
    {
        switch (_state)
        {
            case State.CapsDown:
                _state = State.Idle;
                bool chained = _lastShortReleaseMs is long last && nowMs - last <= _sequenceGapMs;
                _sequenceIndex = chained ? _sequenceIndex + 1 : 0;
                _lastShortReleaseMs = nowMs;
                Action?.Invoke(new CapsKeyAction.ShortPress(_sequenceIndex));
                break;
            case State.Cancelled:
            case State.LongPressFired:
                // 作废的按下或已触发长按:松开不产生任何动作
                _state = State.Idle;
                break;
        }
    }

    public void OnOtherKeyDown(long nowMs)
    {
        // Caps 按住期间按下任何其他键 → 本次按下作废(仅当尚未触发长按时)
        if (_state == State.CapsDown)
            _state = State.Cancelled;
    }

    public void OnTimer(long nowMs)
    {
        if (_state == State.CapsDown && nowMs - _capsDownAtMs >= _longPressThresholdMs)
        {
            _state = State.LongPressFired;
            ResetSequence();
            Action?.Invoke(new CapsKeyAction.LongPressToggleCaps());
        }
    }

    /// <summary>恢复空闲(暂停切换/热更新参数时复位,避免残留状态)。</summary>
    public void Reset()
    {
        _state = State.Idle;
        ResetSequence();
    }

    /// <summary>热更新阈值(设置窗口保存后立即生效)。</summary>
    public void UpdateThresholds(long longPressThresholdMs, long sequenceGapMs)
    {
        _longPressThresholdMs = longPressThresholdMs;
        _sequenceGapMs = sequenceGapMs;
    }

    private void ResetSequence()
    {
        _sequenceIndex = 0;
        _lastShortReleaseMs = null;
    }
}
