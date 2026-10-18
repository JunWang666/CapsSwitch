using System.Collections.Concurrent;
using System.Windows.Automation;
using CapsSwitch.App.Interop;

namespace CapsSwitch.App.Focus;

public sealed record FocusSnapshot(bool IsTextInput, IntPtr Hwnd, Rectangle? CaretRect, CaretLocationMethod Method);

/// <summary>
/// UIA 焦点监听:独立 MTA 线程,创建托管 UIA 实例注册 FocusChanged + 自带 WinForms 消息泵。
/// 识别可编辑文本控件(TextInputDetector),用 CaretLocator 在**本线程**定位光标,
/// 结果经事件封送出去。500ms 内同一控件(RuntimeId,取不到时 hwnd+矩形近似)的焦点事件去重。
/// 隐私约束:不读取、不记录用户文本内容,只取控件类型/只读性/几何信息。
/// </summary>
public sealed class FocusMonitor : IDisposable
{
    private const int DedupeWindowMs = 500;

    private readonly Action<string> _log;
    private readonly Thread _thread;
    private readonly BlockingCollection<(IntPtr hwnd, TaskCompletionSource<(Rectangle? rect, CaretLocationMethod method)> tcs)> _caretRequests = new();
    private ApplicationContext? _context;
    private volatile bool _disposed;
    private readonly Dictionary<long, Rectangle> _caretCache = new();
    private readonly object _caretCacheLock = new();

    /// <summary>同 hwnd 最近一次成功定位的光标矩形(UI 线程可读,HUD 首帧快路径用)。</summary>
    public Rectangle? LastCaretFor(IntPtr hwnd)
    {
        lock (_caretCacheLock)
            return _caretCache.TryGetValue(hwnd.ToInt64(), out var rect) ? rect : null;
    }

    private void CacheCaret(IntPtr hwnd, Rectangle rect)
    {
        lock (_caretCacheLock)
            _caretCache[hwnd.ToInt64()] = rect;
    }

    public event Action<FocusSnapshot>? FocusChanged;

    public FocusMonitor(Action<string> log)
    {
        _log = log;
        _thread = new Thread(ThreadProc) { IsBackground = true, Name = "CapsSwitch-FocusMonitor" };
        _thread.SetApartmentState(ApartmentState.MTA);
    }

    public void Start() => _thread.Start();

    private long _lastFocusKey = -1;
    private long _lastFocusAtMs;

    private void ThreadProc()
    {
        try
        {
            Automation.AddAutomationFocusChangedEventHandler(OnFocusChanged);
            _log("[FocusMonitor] UIA FocusChanged 已注册(MTA)");
        }
        catch (Exception ex)
        {
            _log($"[FocusMonitor] 注册 FocusChanged 失败: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        // 消息泵 + 定时排空光标定位请求(定位必须在 UIA 线程做)
        _context = new ApplicationContext();
        using var drainTimer = new System.Windows.Forms.Timer { Interval = 15 };
        drainTimer.Tick += (_, _) =>
        {
            while (_caretRequests.TryTake(out var req))
            {
                try
                {
                    // 文本模式(TextPattern2/Selection)挂在聚焦控件上,不在顶层窗口 → 先取焦点元素
                    AutomationElement? element = null;
                    try { element = AutomationElement.FocusedElement; } catch { /* 忽略 */ }
                    if (element is null)
                    {
                        try { element = AutomationElement.FromHandle(req.hwnd); }
                        catch (Exception ex) { _log($"[FocusMonitor] FromHandle 失败(忽略): {ex.GetType().Name}"); }
                    }
                    long startedAt = Environment.TickCount64;
                    bool ok = CaretLocator.TryLocate(element, req.hwnd, _log, out var rect, out var method);
                    long elapsed = Environment.TickCount64 - startedAt;
                    if (ok)
                        CacheCaret(req.hwnd, rect);
                    _log($"[FocusMonitor] 光标定位({elapsed}ms): method={method} rect={(ok ? rect.ToString() : "(失败)")}");
                    req.tcs.SetResult(ok ? (rect, method) : (null, method));
                }
                catch (Exception ex)
                {
                    _log($"[FocusMonitor] 光标定位异常(忽略): {ex.GetType().Name}: {ex.Message}");
                    req.tcs.SetResult((null, CaretLocationMethod.None));
                }
            }
            if (_disposed)
                _context!.ExitThread();
        };
        drainTimer.Start();
        Application.Run(_context);
        drainTimer.Stop();

        try
        {
            Automation.RemoveAllEventHandlers();
        }
        catch (Exception ex)
        {
            _log($"[FocusMonitor] RemoveAllEventHandlers 异常(忽略): {ex.Message}");
        }
        _log("[FocusMonitor] UIA 线程退出");
    }

    /// <summary>请求定位光标;在 UIA 线程执行,结果异步返回(含定位层级)。</summary>
    public Task<(Rectangle? rect, CaretLocationMethod method)> LocateCaretAsync(IntPtr hwnd)
    {
        var tcs = new TaskCompletionSource<(Rectangle?, CaretLocationMethod)>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_disposed)
        {
            tcs.SetResult((null, CaretLocationMethod.None));
            return tcs.Task;
        }
        _caretRequests.Add((hwnd, tcs));
        return tcs.Task;
    }

    private void OnFocusChanged(object sender, AutomationFocusChangedEventArgs e)
    {
        try
        {
            if (sender is not AutomationElement element)
                return;

            var current = element.Current;
            bool hasTextPattern = (bool)element.GetCurrentPropertyValue(AutomationElement.IsTextPatternAvailableProperty);
            bool? isReadOnly = TryGetReadOnly(element);
            var desc = new TextControlDescription(current.ControlType.Id, hasTextPattern, isReadOnly);
            if (!TextInputDetector.IsTextInput(in desc))
                return;

            IntPtr hwnd = new(current.NativeWindowHandle);
            // 去重:500ms 内同一控件只发一次(RuntimeId 取不到时用 hwnd+矩形近似)
            long key = DedupeKey(element, current, hwnd);
            long now = Environment.TickCount64;
            if (key == _lastFocusKey && now - _lastFocusAtMs < DedupeWindowMs)
                return;
            _lastFocusKey = key;
            _lastFocusAtMs = now;

            // 只取几何/类型信息,不读取用户文本(隐私约束)
            bool ok = CaretLocator.TryLocate(element, hwnd, _log, out var rect, out var method);
            if (ok)
                CacheCaret(hwnd, rect);
            FocusChanged?.Invoke(new FocusSnapshot(true, hwnd, ok ? rect : null, method));
        }
        catch (ElementNotAvailableException)
        {
            // 元素已销毁,忽略
        }
        catch (Exception ex)
        {
            _log($"[FocusMonitor] 焦点处理异常(忽略): {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static long DedupeKey(AutomationElement element, AutomationElement.AutomationElementInformation current, IntPtr hwnd)
    {
        try
        {
            var runtimeId = element.GetRuntimeId();
            if (runtimeId is { Length: > 0 })
                return runtimeId.Aggregate(17L, (h, v) => h * 31 + v);
        }
        catch (Exception)
        {
            // 取不到 RuntimeId 时用 hwnd+矩形近似
        }
        var r = current.BoundingRectangle;
        return hwnd.ToInt64() ^ ((long)r.X << 20) ^ ((long)r.Y << 8) ^ (long)(r.Width * r.Height);
    }

    private static bool? TryGetReadOnly(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePattern) && valuePattern is ValuePattern vp)
                return vp.Current.IsReadOnly;
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object textPattern) && textPattern is TextPattern tp)
            {
                var attr = tp.DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
                if (attr is bool b)
                    return b;
            }
        }
        catch (Exception)
        {
            // 取不到 → 宽进
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _caretRequests.CompleteAdding();
        // 由 drainTimer 在 UIA 线程上 ExitThread
        if (!_thread.Join(3000))
            _log("[FocusMonitor] UIA 线程未在 3 秒内退出");
        _caretRequests.Dispose();
        GC.SuppressFinalize(this);
    }
}
