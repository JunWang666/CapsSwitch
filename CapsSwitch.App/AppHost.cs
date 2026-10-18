using CapsSwitch.App.Config;
using CapsSwitch.App.Focus;
using CapsSwitch.App.Input;
using CapsSwitch.App.InputSources;
using CapsSwitch.App.Overlay;

namespace CapsSwitch.App;

/// <summary>
/// 运行期宿主:持有并编排 InputSourceService / Monitor / FocusMonitor / KeyboardHook /
/// 状态机 / 切换线程 / HUD。TrayContext 与 Program 通过它与后台交互;跨线程安全。
/// </summary>
public sealed class AppHost : IDisposable
{
    private readonly Action<string> _log;
    private AppSettings _settings;
    private volatile SwitchConfig _switchConfig;
    private volatile CapsKeyStateMachine _stateMachine;
    private readonly ISwitchStrategy _strategy = new PreferredPairCycleStrategy();
    private readonly CycleSession _cycleSession = new();
    private long _lastPressAtMs;

    private List<(string Id, string Label, string Name)> ResolveCycleItems()
    {
        var sources = _service.GetSources();
        var items = new List<(string, string, string)>();
        foreach (var id in _settings.OrderedSourceIds)
        {
            var s = sources.FirstOrDefault(x => x.Id == id);
            if (s is not null)
                items.Add((s.Id, s.ShortLabel, s.DisplayName));
        }
        return items;
    }

    private int OrderedIndexOf(InputSourceInfo target)
    {
        var ids = _settings.OrderedSourceIds;
        for (int i = 0; i < ids.Count; i++)
            if (ids[i] == target.Id)
                return i;
        return -1;
    }

    private readonly InputSourceService _service;
    private readonly SettingsStore _store;
    private readonly InputSourceMonitor _monitor;
    private readonly FocusMonitor _focusMonitor;
    private KeyboardHook? _hook;
    private Thread? _hookLoopThread;
    private Thread? _switchThread;
    private readonly ManualResetEventSlim _switchGate = new();
    private readonly object _pendingLock = new();
    private int? _pendingSequence;
    private bool _disposed;

    // HUD(UI 线程惰性构造)
    private readonly object _hudGate = new();
    private HudController? _hud;

    public AppHost(Action<string> log, AppSettings settings, SettingsStore store)
    {
        _log = log;
        _settings = settings;
        _store = store;
        _switchConfig = ToSwitchConfig(settings);
        _stateMachine = new CapsKeyStateMachine(settings.LongPressThresholdMs, settings.SequenceGapMs);
        _service = new InputSourceService(log);
        _service.OnTipsRecovered = RerunConfigNormalization;
        _monitor = new InputSourceMonitor(log, _service, settings.PreferredChineseId);
        _focusMonitor = new FocusMonitor(log);
    }

    public InputSourceService Service => _service;
    public InputSourceMonitor Monitor => _monitor;
    public AppSettings Settings => _settings;
    private static SwitchConfig ToSwitchConfig(AppSettings s) => new()
    {
        PreferredChineseId = s.PreferredChineseId,
        PreferredEnglishId = s.PreferredEnglishId,
        OrderedSourceIds = s.OrderedSourceIds,
    };

    /// <summary>启动全部后台线程(UI 线程调用)。</summary>
    public bool Start()
    {
        var sources = _service.GetSources();
        _log($"[App] 发现 {sources.Count} 个输入源");
        _monitor.Start();
        _focusMonitor.Start();
        _hook = new KeyboardHook(_log);
        if (!_hook.WaitForReady())
        {
            _log("[App] 键盘钩子安装失败");
            return false;
        }
        _stateMachine.Action += OnCapsAction;

        _switchThread = new Thread(SwitchLoop) { IsBackground = true, Name = "CapsSwitch-SwitchWorker" };
        _switchThread.Start();
        _hookLoopThread = new Thread(HookEventLoop) { IsBackground = true, Name = "CapsSwitch-HookEventLoop" };
        _hookLoopThread.Start();
        _hook.PassthroughCaps = !_settings.HotkeysEnabled;
        _log($"[App] 快捷键切换={(_settings.HotkeysEnabled ? "启用" : "暂停")}");
        _log("[App] 就绪。短按 Caps=切换输入源,长按 Caps=系统 CapsLock。");
        return true;
    }

    /// <summary>
    /// 冷启动 TIP 丢失污染配置后,枚举恢复出 TIP → 重跑归一化并热生效。
    /// 注意:由 service 工作线程回调,经 Task.Run 脱离,避免同步重入 service 造成自死锁。
    /// </summary>
    private void RerunConfigNormalization()
    {
        _ = Task.Run(() =>
        {
            try
            {
                var sources = _service.GetSources();
                var normalized = new AppSettings
                {
                    PreferredChineseId = _settings.PreferredChineseId,
                    PreferredEnglishId = _settings.PreferredEnglishId,
                    OrderedSourceIds = [.. _settings.OrderedSourceIds],
                    LongPressThresholdMs = _settings.LongPressThresholdMs,
                    SequenceGapMs = _settings.SequenceGapMs,
                    HudDurationMs = _settings.HudDurationMs,
                    HudEnabled = _settings.HudEnabled,
                    HotkeysEnabled = _settings.HotkeysEnabled,
                    AutoStart = _settings.AutoStart,
                };
                if (!_store.NormalizeInPlace(normalized, sources))
                    return; // 无变化不写盘(防抖)
                _store.Save(normalized);
                _log("[Settings] TIP 恢复后的配置归一化已写盘并热生效");
                UpdateSettings(normalized, applyAutoStart: false);
            }
            catch (Exception ex)
            {
                _log($"[Settings] 归一化重跑异常: {ex.GetType().Name}: {ex.Message}");
            }
        });
    }

    /// <summary>托盘"设置…"保存后调用(UI 线程):全部立即生效。</summary>
    public void UpdateSettings(AppSettings settings, bool applyAutoStart = true)
    {
        _settings = settings;
        _switchConfig = ToSwitchConfig(settings);
        _stateMachine.UpdateThresholds(settings.LongPressThresholdMs, settings.SequenceGapMs);
        _monitor.SetPreferredChineseId(settings.PreferredChineseId);
        if (applyAutoStart)
            AutoStartRegistrar.SetAutoStart(settings.AutoStart);
        lock (_hudGate)
        {
            if (_hud is not null)
                _hud.UpdateSettings(settings, settings.PreferredChineseId);
            else if (settings.HudEnabled)
                EnsureHud(); // 原关闭→打开:立即创建
        }
    }

    /// <summary>快捷键切换暂停/启用。暂停时 Caps 完全放行,状态机复位。</summary>
    public bool HotkeysEnabled
    {
        get => _hook is null || !_hook.PassthroughCaps;
        set
        {
            _settings.HotkeysEnabled = value;
            if (_hook is not null)
                _hook.PassthroughCaps = !value;
            if (!value)
            {
                _stateMachine.Reset();
                _cycleSession.Reset();
            }
            _log($"[App] 快捷键切换已{(value ? "启用" : "暂停")}");
        }
    }

    /// <summary>HUD 开关(托盘菜单;同时写回配置)。</summary>
    public void SetHudEnabled(bool enabled)
    {
        _settings.HudEnabled = enabled;
        lock (_hudGate)
        {
            if (_hud is not null)
                _hud.UpdateSettings(_settings, _settings.PreferredChineseId);
            else if (enabled)
                EnsureHud();
        }
    }

    private void EnsureHud()
    {
        lock (_hudGate)
        {
            if (_hud is not null || !_settings.HudEnabled)
                return;
            _hud = new HudController(_log, _settings, _settings.PreferredChineseId, _service, _focusMonitor, _monitor);
            _focusMonitor.FocusChanged += _hud.NotifyFocusChanged;
            _monitor.Changed += _hud.NotifyExternalChange;
            _hud.ListHidden += OnHudListHidden;
            _log("[App] HUD 已初始化");
        }
    }

    /// <summary>消息泵启动后调用(UI 线程 Application.Idle)。</summary>
    public void OnUiPumpStarted() => EnsureHud();

    /// <summary>列表 HUD 隐藏(超时或被替换)→ 循环会话结束。</summary>
    private void OnHudListHidden()
    {
        if (_cycleSession.Mode == CycleMode.Cycling)
            _log("[Cycle] 列表隐藏,循环会话结束,回到正常模式");
        _cycleSession.OnListHidden();
    }

    private void NotifyHud(Action<HudController?> notify)
    {
        HudController? hud;
        lock (_hudGate)
            hud = _hud;
        notify(hud);
    }

    private void OnCapsAction(CapsKeyAction action)
    {
        switch (action)
        {
            case CapsKeyAction.ShortPress(var sequenceIndex):
                // 循环会话:正常模式 seq=0=主副互切;连按/循环中=循环推进(间隔不受 SequenceGapMs 限制)
                var cycleAction = _cycleSession.OnShortPress(sequenceIndex);
                int switchIndex = cycleAction == CycleAction.CycleNext ? 1 : 0;
                long pressedAtMs = Environment.TickCount64;
                _lastPressAtMs = pressedAtMs;
                if (cycleAction == CycleAction.CycleNext)
                {
                    // 高亮立即推进(不等切换完成):预览列表 + 索引先走
                    var items = ResolveCycleItems();
                    if (items.Count > 0)
                    {
                        int next = _cycleSession.PreviewNext(items.Count);
                        NotifyHud(h => h?.NotifyCyclePreview(items, next, pressedAtMs));
                    }
                }
                _log($"[Caps] 短按 sequence={sequenceIndex} 模式={_cycleSession.Mode} → {(cycleAction == CycleAction.CycleNext ? "循环推进" : "主副互切")}");
                lock (_pendingLock)
                    _pendingSequence = switchIndex;
                _switchGate.Set();
                break;
            case CapsKeyAction.LongPressToggleCaps:
                _log("[Caps] 长按:模拟系统 CapsLock 切换");
                _cycleSession.OnLongPress();
                KeyboardSimulator.ToggleCapsLock();
                NotifyHud(h => h?.NotifyCapsLockToggled());
                break;
        }
    }

    private void SwitchLoop()
    {
        while (true)
        {
            _switchGate.Wait();
            int index;
            lock (_pendingLock)
            {
                if (_pendingSequence is null)
                {
                    _switchGate.Reset();
                    continue;
                }
                index = _pendingSequence.Value;
                _pendingSequence = null;
                _switchGate.Reset();
            }
            if (_disposed)
                return;
            var sources = _service.GetSources();
            var current = _service.GetCurrent();
            var config = _switchConfig;
            var target = _strategy.GetTarget(current, index, sources, config);
            if (target is null)
            {
                _log($"[Switch] sequence={index} 当前={(current?.DisplayName ?? "(未知)")},无可用目标,跳过");
                continue;
            }
            _log($"[Switch] sequence={index} 当前={(current?.DisplayName ?? "(未知)")} → 目标 {target.DisplayName}");
            long pressAt = _lastPressAtMs;
            bool ok = _service.SwitchTo(target);
            _log($"[Perf] 按键→切换返回 {Environment.TickCount64 - pressAt}ms {(ok ? "成功" : "失败")}");
            if (ok)
            {
                // 对账:切换实际目标确认循环索引;HUD 若被预览抢先,以实际目标为准
                _cycleSession.CommitIndex(OrderedIndexOf(target));
                NotifyHud(h => h?.NotifySwitchCompleted(index, target, pressAt));
            }
            else
            {
                // 切换失败:HUD 与真实状态对账(真实源徽章,覆盖可能错误的预览高亮)
                NotifyHud(h => h?.NotifySwitchFailed());
            }
        }
    }

    private void HookEventLoop()
    {
        long? timerDueAtMs = null;
        while (!_disposed)
        {
            int timeout = Timeout.Infinite;
            if (timerDueAtMs is long due)
            {
                timeout = (int)Math.Max(0, due - Environment.TickCount64);
                if (timeout == 0)
                {
                    _stateMachine.OnTimer(due);
                    timerDueAtMs = null;
                    continue;
                }
            }

            if (_hook is null)
                return;
            HookEvent? ev;
            try
            {
                _hook.Events.TryTake(out ev, timeout);
            }
            catch (InvalidOperationException)
            {
                break; // CompleteAdding / 集合已释放:钩子已退出
            }
            if (ev is null)
            {
                if (timerDueAtMs is long due2)
                {
                    _stateMachine.OnTimer(due2);
                    timerDueAtMs = null;
                }
                continue;
            }

            // 一律用事件自带时间戳(回调时刻),处理延迟不计入按键间隔
            switch (ev)
            {
                case HookEvent.CapsDown(var _, var atDown):
                    _stateMachine.OnCapsDown(atDown, false);
                    timerDueAtMs = atDown + _settings.LongPressThresholdMs;
                    break;
                case HookEvent.CapsUp(var atUp):
                    _stateMachine.OnCapsUp(atUp);
                    timerDueAtMs = null;
                    break;
                case HookEvent.OtherKeyDown(var atOther):
                    _stateMachine.OnOtherKeyDown(atOther);
                    break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        lock (_hudGate)
            _hud?.Dispose();
        _monitor.Stop();
        _hook?.Dispose();
        _focusMonitor.Dispose();
        _service.Dispose();
        _switchGate.Set(); // 让切换线程退出 Wait
        GC.SuppressFinalize(this);
    }
}
