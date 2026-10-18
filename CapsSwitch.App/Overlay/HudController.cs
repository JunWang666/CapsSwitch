using CapsSwitch.App.Config;
using CapsSwitch.App.Focus;
using CapsSwitch.App.InputSources;
using CapsSwitch.App.Logging;

namespace CapsSwitch.App.Overlay;

/// <summary>
/// HUD 显示调度。必须在 UI 线程构造(窗体句柄随之创建);对外方法可从任意线程调用,
/// 内部经 Form.BeginInvoke 封送到 UI 线程。
/// 触发源:焦点变化(总是显示) / 切换完成(短按=紧凑徽章,连按=循环列表) /
/// 外部输入源变化(紧凑徽章) / 长按 CapsLock 状态提示。
/// 显示请求带序号,异步定位返回时若已有更新请求则丢弃。
/// </summary>
public sealed class HudController : IDisposable
{
    private const int RefineMoveThresholdPx = 8;
    private const int LocateTimeoutMs = 500;

    private readonly Action<string> _log;
    private AppSettings _settings;
    private string? _chineseTipId;
    private readonly InputSourceService _service;
    private readonly FocusMonitor _focusMonitor;
    private readonly InputSourceMonitor _monitor;
    private readonly InputHudForm _form;
    private readonly System.Windows.Forms.Timer _hideTimer;
    private IntPtr _lastCaretHwnd;
    private Rectangle? _lastCaretRect;

    /// <summary>列表 HUD 被隐藏(超时)或被徽章显示替换时触发:循环会话据此结束。</summary>
    public event Action? ListHidden;
    private int _sequence;
    private string? _lastShownKey;
    private long _lastSwitchAtMs;
    private bool _disposed;

    public HudController(
        Action<string> log, AppSettings settings, string? chineseTipId,
        InputSourceService service, FocusMonitor focusMonitor, InputSourceMonitor monitor)
    {
        _log = log;
        _settings = settings;
        _chineseTipId = chineseTipId;
        _service = service;
        _focusMonitor = focusMonitor;
        _monitor = monitor;
        _form = new InputHudForm { Visible = false };
        _ = _form.Handle; // 立即创建句柄,保证任意线程 BeginInvoke 可用
        _hideTimer = new System.Windows.Forms.Timer { Interval = Math.Max(200, settings.HudDurationMs) };
        _hideTimer.Tick += (_, _) => Hide();
    }

    private void MarshalToUi(Action action)
    {
        if (_disposed || _form.IsDisposed)
            return;
        if (_form.InvokeRequired)
            _form.BeginInvoke(action);
        else
            action();
    }

    /// <summary>设置窗口保存后热更新配置。</summary>
    public void UpdateSettings(AppSettings settings, string? chineseTipId)
    {
        MarshalToUi(() =>
        {
            _settings = settings;
            _chineseTipId = chineseTipId;
        });
    }

    /// <summary>焦点变化触发(UIA 线程上调入):总是显示,不做 (hwnd+源+模式) 同状态去重。</summary>
    public void NotifyFocusChanged(FocusSnapshot snapshot)
    {
        if (!_settings.HudEnabled || !snapshot.IsTextInput || snapshot.CaretRect is null)
            return;
        MarshalToUi(() => ShowForCaret(snapshot.CaretRect.Value, snapshot.Hwnd, posSource: $"focus:{snapshot.Method}", suppressIfUnchanged: false));
    }

    /// <summary>切换完成触发(切换线程上调入)。短按(index=0)=紧凑徽章;连按(≥1)=循环列表。
    /// pressedAtMs=按键事件时间戳(0=未知),用于 [Perf] 计时日志。</summary>
    public void NotifySwitchCompleted(int sequenceIndex, InputSourceInfo? target, long pressedAtMs = 0)
    {
        if (!_settings.HudEnabled)
            return;
        _lastSwitchAtMs = Environment.TickCount64;
        if (sequenceIndex >= 1 && target is not null)
            RequestShowList(target, pressedAtMs);
        else
            RequestShowBadge(null, null, pressedAtMs, target);
    }

    /// <summary>循环预览:按键到达即推进列表高亮,不等切换完成(items 已由 AppHost 解析)。</summary>
    public void NotifyCyclePreview(IReadOnlyList<(string Id, string Label, string Name)> items, int activeIndex, long pressedAtMs = 0)
    {
        if (!_settings.HudEnabled)
            return;
        MarshalToUi(() =>
        {
            if (_form.Visible && _lastShownKey == "list")
            {
                // 列表已在显示:高亮立即推进,重置隐藏计时
                _form.SetListActive(activeIndex);
                StartHideTimer(null);
                if (pressedAtMs > 0)
                    Logger.Verbose($"[Perf] 按键→预览帧 {Environment.TickCount64 - pressedAtMs}ms (高亮={activeIndex})");
                return;
            }
            ShowList(items, activeIndex, pressedAtMs, "preview", IntPtr.Zero, _lastCaretRect);
        });
    }

    /// <summary>切换失败:与真实状态对账——回到真实当前源的徽章显示(覆盖可能错误的高亮)。</summary>
    public void NotifySwitchFailed()
    {
        if (_settings.HudEnabled)
            RequestShowBadge();
    }

    /// <summary>外部输入源变化触发(Monitor 线程上调入):紧凑徽章。
    /// 刚完成过一次切换(<600ms)时跳过——切换自带的 HUD 流程已覆盖,避免竞争抑制列表模式。</summary>
    public void NotifyExternalChange(InputSourceState state)
    {
        if (!_settings.HudEnabled)
            return;
        if (Environment.TickCount64 - _lastSwitchAtMs < 600)
            return;
        RequestShowBadge();
    }

    /// <summary>长按 CapsLock 后:延迟 100ms 在 UI 线程读系统真实 CapsLock 状态并显示。</summary>
    public void NotifyCapsLockToggled()
    {
        if (!_settings.HudEnabled)
            return;
        int seq = Interlocked.Increment(ref _sequence);
        _ = Task.Run(async () =>
        {
            await Task.Delay(100); // 等 SendInput 生效
            IntPtr hwnd = Interop.Win32Native.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return;
            var fast = ResolveCaretFast(hwnd);
            // 控制台线程 GetKeyState 不可信 → 必须 UI 线程读
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            MarshalToUi(() => tcs.TrySetResult(Control.IsKeyLocked(Keys.CapsLock)));
            bool locked = await tcs.Task;
            if (fast is { } f)
            {
                MarshalToUi(() =>
                {
                    if (seq != Volatile.Read(ref _sequence))
                        return;
                    ShowForCaret(f.Caret, hwnd, forceText: locked ? "⇪ 大写" : "⇪ 小写", posSource: f.Source, suppressIfUnchanged: false);
                });
                _ = RefineCaretAsync(seq, hwnd);
                return;
            }
            var (caret, method) = await AwaitLocateWithTimeout(hwnd);
            if (caret is null)
                return;
            MarshalToUi(() =>
            {
                if (seq != Volatile.Read(ref _sequence))
                    return;
                ShowForCaret(caret.Value, hwnd, forceText: locked ? "⇪ 大写" : "⇪ 小写", posSource: $"uia:{method}", suppressIfUnchanged: false);
            });
        });
    }

    /// <summary>hudtest:强行显示指定文本(默认 3 秒),调试用。</summary>
    public void ShowTest(string text, int durationMs = 3000)
    {
        MarshalToUi(() => RequestShowBadge(text, durationMs));
    }

    // ── 紧凑徽章 ──

    private void RequestShowBadge(string? forceText = null, int? durationOverrideMs = null, long pressedAtMs = 0, InputSourceInfo? optimisticTarget = null)
    {
        int seq = Interlocked.Increment(ref _sequence);
        IntPtr hwnd = Interop.Win32Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return;
        _ = LocateAndShowBadgeAsync(seq, hwnd, forceText, durationOverrideMs, pressedAtMs, optimisticTarget);
    }

    private async Task LocateAndShowBadgeAsync(int seq, IntPtr hwnd, string? forceText, int? durationOverrideMs, long pressedAtMs, InputSourceInfo? optimisticTarget)
    {
        // 快路径:立即用最佳已知位置显示,UI 首帧不等 UIA 定位
        var fast = ResolveCaretFast(hwnd);
        if (fast is { } f)
        {
            MarshalToUi(() =>
            {
                if (seq != Volatile.Read(ref _sequence))
                    return;
                ShowForCaret(f.Caret, hwnd, forceText, durationOverrideMs, f.Source, true, pressedAtMs, optimisticTarget);
            });
            _ = RefineCaretAsync(seq, hwnd);
            return;
        }

        // 慢路径:无任何已知位置 → 等异步定位(500ms 上限,超时丢弃本次显示)
        var (caret, method) = await AwaitLocateWithTimeout(hwnd);
        if (caret is null)
        {
            Logger.Verbose("[HUD] 光标定位超时,丢弃本次徽章显示");
            return;
        }
        MarshalToUi(() =>
        {
            if (seq != Volatile.Read(ref _sequence))
                return; // 已有更新请求或定位失败 → 丢弃
            ShowForCaret(caret.Value, hwnd, forceText, durationOverrideMs, $"uia:{method}", true, pressedAtMs, optimisticTarget);
        });
    }

    /// <summary>UI 线程零跨进程调用的状态读取:Monitor 最近采样 + 本地快照匹配。</summary>
    private (InputSourceInfo? source, bool nativeMode) GetLocalForegroundState()
    {
        var state = _monitor.LatestState;
        if (state is null)
            return (null, false);
        var source = InputSourceService.MatchByHkl(_service.GetSourcesSnapshot(), state.Hkl);
        return (source, state.NativeMode);
    }

    private void ShowForCaret(
        Rectangle caretRect, IntPtr hwnd, string? forceText = null, int? durationOverrideMs = null,
        string posSource = "uia", bool suppressIfUnchanged = true,
        long pressedAtMs = 0, InputSourceInfo? optimisticTarget = null)
    {
        // 已在 UI 线程(零跨进程调用:状态来自 Monitor 采样缓存 + 无锁源快照)
        string? text = forceText;
        string? stateKey = null;
        if (text is null)
        {
            var state = GetLocalForegroundState();
            // 切换刚完成时强制尚未落位:对主中文 TIP 目标乐观按中文模式显示,Monitor 随后对账
            var effective = optimisticTarget ?? state.source;
            if (effective is null)
                return;
            bool native = optimisticTarget?.Id == _chineseTipId || state.nativeMode;
            text = HudTextMapper.Map(effective, native, _chineseTipId);
            if (text is null)
                return;
            stateKey = $"{hwnd.ToInt64():x8}|{effective.Id}|{(native ? "native" : "alpha")}";
        }

        if (suppressIfUnchanged && stateKey is not null && _form.Visible && stateKey == _lastShownKey)
            return; // 同一 (hwnd+源+模式) 未变,不重复闪烁(仅非焦点触发路径)

        if (_form.Visible && _lastShownKey == "list")
            ListHidden?.Invoke(); // 列表被徽章替换 → 循环会话结束
        _form.SetBadge(text);
        _form.PositionNear(caretRect);
        _lastCaretHwnd = hwnd;
        _lastCaretRect = caretRect;
        StartHideTimer(durationOverrideMs);
        if (!_form.Visible)
        {
            _form.FadeIn();
            string perf = pressedAtMs > 0 ? $" [Perf] 按键→HUD首帧 {Environment.TickCount64 - pressedAtMs}ms (位置={posSource})" : string.Empty;
            _log($"[HUD] 显示 '{text}' @ ({_form.Left},{_form.Top}) size={_form.Width}x{_form.Height} dpi={_form.DeviceDpi}{perf}");
        }
        _form.BringToFront();
    }

    // ── 光标位置快解析(同步、微秒级;UI 首帧不等待 UIA 定位) ──

    private readonly record struct CaretFastResult(Rectangle Caret, string Source);

    private CaretFastResult? ResolveCaretFast(IntPtr hwnd)
    {
        // ① 同 hwnd 光标位置缓存
        if (_focusMonitor.LastCaretFor(hwnd) is { } cached)
            return new CaretFastResult(cached, "cache");
        // ② GetGUIThreadInfo(本地 Win32,微秒级)
        if (TryGuiThreadCaret(hwnd, out var rc))
            return new CaretFastResult(rc, "guithread");
        // ③ 该 hwnd 上次 HUD 显示位置
        if (_lastCaretRect is { } last && hwnd == _lastCaretHwnd)
            return new CaretFastResult(last, "lastpos");
        return null;
    }

    private static bool TryGuiThreadCaret(IntPtr hwnd, out Rectangle caret)
    {
        caret = Rectangle.Empty;
        uint tid = Interop.Win32Native.GetWindowThreadProcessId(hwnd, out _);
        if (tid == 0)
            return false;
        var info = new Interop.Win32Native.GUITHREADINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Interop.Win32Native.GUITHREADINFO>() };
        if (!Interop.Win32Native.GetGUIThreadInfo(tid, ref info) || info.hwndCaret == IntPtr.Zero)
            return false;
        var r = info.rcCaret;
        if (r.Right <= r.Left || r.Bottom <= r.Top)
            return false;
        var pt = new Interop.Win32Native.POINT { X = r.Left, Y = r.Top };
        Interop.Win32Native.ClientToScreen(info.hwndCaret, ref pt);
        caret = new Rectangle(pt.X, pt.Y, r.Right - r.Left, Math.Max(r.Bottom - r.Top, 8));
        return true;
    }

    /// <summary>异步精修:UIA 精确定位返回后与当前显示位置对账,差异 >8px 才移动。</summary>
    private async Task RefineCaretAsync(int seq, IntPtr hwnd)
    {
        var (caret, method) = await AwaitLocateWithTimeout(hwnd);
        if (caret is null)
            return;
        MarshalToUi(() =>
        {
            if (seq != Volatile.Read(ref _sequence) || !_form.Visible)
                return; // 已有更新请求或已隐藏 → 丢弃精修
            if (_lastCaretRect is { } last && CaretPlacement.NearEqual(last, caret.Value, RefineMoveThresholdPx))
                return;
            _form.PositionNear(caret.Value);
            _lastCaretHwnd = hwnd;
            _lastCaretRect = caret.Value;
            Logger.Verbose($"[HUD] 位置精修(uia:{method})→ ({_form.Left},{_form.Top})");
        });
    }

    private async Task<(Rectangle? rect, CaretLocationMethod method)> AwaitLocateWithTimeout(IntPtr hwnd)
    {
        var task = _focusMonitor.LocateCaretAsync(hwnd);
        var timeout = Task.Delay(LocateTimeoutMs);
        var done = await Task.WhenAny(task, timeout);
        if (done != task)
            return (null, CaretLocationMethod.None); // 500ms 上限
        return await task;
    }

    // ── 循环列表(连按 ≥1) ──

    private void RequestShowList(InputSourceInfo target, long pressedAtMs)
    {
        int seq = Interlocked.Increment(ref _sequence);
        IntPtr hwnd = Interop.Win32Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return;
        _ = LocateAndShowListAsync(seq, hwnd, target, pressedAtMs);
    }

    private async Task LocateAndShowListAsync(int seq, IntPtr hwnd, InputSourceInfo target, long pressedAtMs)
    {
        var fast = ResolveCaretFast(hwnd);
        await ShowListCore(seq, hwnd, target, pressedAtMs, fast?.Caret, fast?.Source ?? "uia");
        if (fast is not null)
            _ = RefineCaretAsync(seq, hwnd);
    }

    private async Task ShowListCore(int seq, IntPtr hwnd, InputSourceInfo target, long pressedAtMs, Rectangle? caret, string posSource)
    {
        if (caret is null)
        {
            // 无任何已知位置 → 等异步定位(500ms 上限)
            var located = await AwaitLocateWithTimeout(hwnd);
            if (located.rect is null)
            {
                Logger.Verbose("[HUD] 光标定位超时,丢弃本次列表显示");
                return;
            }
            caret = located.rect;
            posSource = $"uia:{located.method}";
        }

        // 有序 Id 列表解析;目标不在列表时退化为徽章
        var items = ResolveCycleItems();
        int activeIndex = items.FindIndex(i => i.Id == target.Id);
        if (items.Count == 0 || activeIndex < 0)
        {
            MarshalToUi(() =>
            {
                if (seq == Volatile.Read(ref _sequence))
                    ShowForCaret(caret.Value, hwnd, forceText: target.ShortLabel, posSource: posSource);
            });
            return;
        }

        MarshalToUi(() =>
        {
            if (seq != Volatile.Read(ref _sequence))
                return;
            ShowList(items, activeIndex, pressedAtMs, posSource, hwnd, caret);
        });
    }

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

    /// <summary>列表显示(循环会话可见期:固定下限 1500ms,期间任意间隔按键都继续循环)。</summary>
    private void ShowList(
        IReadOnlyList<(string Id, string Label, string Name)> items, int activeIndex,
        long pressedAtMs, string posSource, IntPtr hwnd, Rectangle? caret = null)
    {
        int duration = Math.Max(_settings.HudDurationMs, 1500);
        bool sameList = _form.Visible && _lastShownKey == "list";
        _form.SetList(items.Select(i => (i.Label, i.Name)).ToList(), activeIndex);
        if (caret.HasValue)
        {
            _form.PositionNear(caret.Value);
            _lastCaretHwnd = hwnd;
            _lastCaretRect = caret.Value;
        }
        StartHideTimer(duration);
        _lastShownKey = "list";
        if (!sameList)
        {
            _form.FadeIn();
            string perf = pressedAtMs > 0 ? $" [Perf] 按键→HUD首帧 {Environment.TickCount64 - pressedAtMs}ms (位置={posSource})" : string.Empty;
            _log($"[HUD] 列表模式 active={activeIndex}/{items.Count} @ ({_form.Left},{_form.Top}) size={_form.Width}x{_form.Height}{perf}");
        }
        _form.BringToFront();
    }

    private void StartHideTimer(int? durationOverrideMs)
    {
        _hideTimer.Stop();
        _hideTimer.Interval = durationOverrideMs ?? Math.Max(200, _settings.HudDurationMs);
        _hideTimer.Start();
    }

    private void Hide()
    {
        _hideTimer.Stop();
        if (!_form.Visible)
            return;
        bool wasList = _lastShownKey == "list";
        _form.FadeOut(() =>
        {
            _lastShownKey = null;
            if (wasList)
                ListHidden?.Invoke(); // 隐藏计时器超时 → 循环会话结束
            _log("[HUD] 隐藏");
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_form.IsHandleCreated && !_form.IsDisposed && _form.InvokeRequired)
        {
            try { _form.BeginInvoke(DisposeCore); } catch (InvalidOperationException) { }
        }
        else
        {
            DisposeCore();
        }
        GC.SuppressFinalize(this);
    }

    private void DisposeCore()
    {
        _hideTimer.Stop();
        _hideTimer.Dispose();
        _form.Close();
        _form.Dispose();
    }
}
