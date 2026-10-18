using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using CapsSwitch.App.Interop;
using CapsSwitch.App.Logging;

namespace CapsSwitch.App.Focus;

/// <summary>光标定位走到的层级(日志/调试用)。</summary>
public enum CaretLocationMethod
{
    UiAutomationTextPattern2,
    SelectionRange,
    GuiThreadInfo,
    BoundingRectangle,
    None,
}

#region 原生 UIA COM 互操作(vtable 顺序摘自本机 Windows SDK 10.0.26100 uiautomationclient.h,IID 以头文件为准)

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee")] // IID_IUIAutomation
internal interface IUIAutomationMinimal
{
    // CompareElements / CompareRuntimeIds / GetRootElement
    int Dummy1();
    int Dummy2();
    int Dummy3();
    // slot 6: ElementFromHandle( IUIAutomation*, UIA_HWND, IUIAutomationElement** )
    [PreserveSig]
    int ElementFromHandle(IntPtr hwnd, out IUIAutomationElementMinimal? element);
    // slot 7: ElementFromPoint
    int Dummy4();
    // slot 8: GetFocusedElement( IUIAutomationElement** )
    [PreserveSig]
    int GetFocusedElement(out IUIAutomationElementMinimal? element);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e")] // IID_IUIAutomationElement
internal interface IUIAutomationElementMinimal
{
    // SetFocus, GetRuntimeId, FindFirst, FindAll, FindFirstBuildCache, FindAllBuildCache,
    // BuildUpdatedCache, GetCurrentPropertyValue, GetCurrentPropertyValueEx,
    // GetCachedPropertyValue, GetCachedPropertyValueEx, GetCurrentPatternAs, GetCachedPatternAs
    int Dummy1();
    int Dummy2();
    int Dummy3();
    int Dummy4();
    int Dummy5();
    int Dummy6();
    int Dummy7();
    int Dummy8();
    int Dummy9();
    int Dummy10();
    int Dummy11();
    int Dummy12();
    int Dummy13();
    // slot 16: GetCurrentPattern( int patternId, IUnknown** )
    [PreserveSig]
    int GetCurrentPattern(int patternId, [MarshalAs(UnmanagedType.IUnknown)] out object? pattern);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("506a921a-fcc9-409f-b23b-37eb74106872")] // IID_IUIAutomationTextPattern2(注意:不是常见的 6f861120 开头)
internal interface IUIAutomationTextPattern2Minimal
{
    // RangeFromPoint, RangeFromChild, GetSelection, GetVisibleRanges, get_DocumentRange,
    // get_SupportedTextSelection, RangeFromAnnotation
    int Dummy1();
    int Dummy2();
    int Dummy3();
    int Dummy4();
    int Dummy5();
    int Dummy6();
    int Dummy7();
    // slot 10: GetCaretRange( BOOL *isActive, IUIAutomationTextRange **range )
    [PreserveSig]
    int GetCaretRange(out int isActive, out IntPtr range);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("a543cc6a-f4ae-494b-8239-c814481187a8")] // IID_IUIAutomationTextRange
internal interface IUIAutomationTextRangeMinimal
{
    // Clone, Compare, CompareEndpoints, ExpandToEnclosingUnit, FindAttribute, FindText, GetAttributeValue
    int Dummy1();
    int Dummy2();
    int Dummy3();
    int Dummy4();
    int Dummy5();
    int Dummy6();
    int Dummy7();
    // slot 10: GetBoundingRectangles( SAFEARRAY(double)** )
    [PreserveSig]
    int GetBoundingRectangles([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_R8)] out double[]? rects);
}

internal static class NativeUia
{
    public static readonly Guid ClsidCuiAutomation = new("ff48dba4-60ef-4201-aa87-54103eef594e");
    public const int UiaTextPattern2Id = 10024;

    public static IUIAutomationMinimal CreateAutomation()
    {
        object? obj = Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidCuiAutomation)!);
        return obj is null
            ? throw new InvalidOperationException("CoCreateInstance(CLSID_CUIAutomation) 失败")
            : (IUIAutomationMinimal)obj;
    }
}

#endregion

/// <summary>
/// 光标定位(屏幕物理坐标,PMv2 感知,支持多显示器/负坐标),三级降级:
///  1. 原生 UIA IUIAutomationTextPattern2::GetCaretRange → GetBoundingRectangles 首个矩形
///  2. 托管 TextPattern.GetSelection() 退化区间;或 GetGUIThreadInfo(前台tid) rcCaret + ClientToScreen
///  3. 控件 UIA BoundingRectangle 左上角偏移
///  全部失败 → None(默认不显示)。
/// 必须在 FocusMonitor 的 UIA 线程上调用。
/// </summary>
public static class CaretLocator
{
    private static readonly Action<string> _defaultLog = _ => { };

    public static bool TryLocate(
        AutomationElement? element, IntPtr hwnd, Action<string>? log,
        out Rectangle caretRect, out CaretLocationMethod method)
    {
        log ??= _defaultLog;
        caretRect = Rectangle.Empty;
        method = CaretLocationMethod.None;

        if (TryNativeCaretRange(hwnd, log, ref caretRect))
        {
            method = CaretLocationMethod.UiAutomationTextPattern2;
            return true;
        }

        if (element is not null && TrySelectionRange(element, log, ref caretRect))
        {
            method = CaretLocationMethod.SelectionRange;
            return true;
        }

        if (TryGuiThreadInfo(hwnd, log, ref caretRect))
        {
            method = CaretLocationMethod.GuiThreadInfo;
            return true;
        }

        if (element is not null && TryControlBounds(element, ref caretRect))
        {
            method = CaretLocationMethod.BoundingRectangle;
            return true;
        }

        log("[CaretLocator] 所有层级均失败,放弃定位");
        return false;
    }

    /// <summary>仅原生 COM 层(供诊断;hudtest 日志用)。</summary>
    public static bool TryNativeCaretRange(IntPtr hwnd, Action<string>? log, ref Rectangle caretRect)
    {
        log ??= _defaultLog;
        IUIAutomationMinimal? automation = null;
        IUIAutomationElementMinimal? element = null;
        object? patternObj = null;
        IUIAutomationTextRangeMinimal? range = null;
        try
        {
            automation = NativeUia.CreateAutomation();
            // TextPattern2 挂在聚焦的编辑控件上,不在顶层窗口 → 先取焦点元素
            int hr = automation.GetFocusedElement(out element);
            if (hr != 0 || element is null)
            {
                Logger.Verbose($"[CaretLocator] GetFocusedElement hr=0x{hr:x8},回退 ElementFromHandle");
                hr = automation.ElementFromHandle(hwnd, out element);
            }
            if (hr != 0 || element is null)
            {
                Logger.Verbose($"[CaretLocator] ElementFromHandle hr=0x{hr:x8},原生层放弃");
                return false;
            }
            hr = element.GetCurrentPattern(NativeUia.UiaTextPattern2Id, out patternObj);
            if (hr != 0 || patternObj is null)
            {
                Logger.Verbose($"[CaretLocator] GetCurrentPattern(TextPattern2) hr=0x{hr:x8} pattern={(patternObj is null ? "null" : "非空")},元素不支持 TextPattern2,降级");
                return false;
            }
            if (patternObj is not IUIAutomationTextPattern2Minimal pattern)
            {
                Logger.Verbose("[CaretLocator] QI IUIAutomationTextPattern2 失败,降级");
                return false;
            }
            hr = pattern.GetCaretRange(out _, out IntPtr rangePtr);
            if (hr != 0 || rangePtr == IntPtr.Zero)
            {
                Logger.Verbose($"[CaretLocator] GetCaretRange hr=0x{hr:x8} range={(rangePtr == IntPtr.Zero ? "null" : "非空")},降级");
                return false;
            }
            range = (IUIAutomationTextRangeMinimal)Marshal.GetObjectForIUnknown(rangePtr);
            Marshal.Release(rangePtr);
            hr = range.GetBoundingRectangles(out double[]? rects);
            if (hr != 0 || rects is null || rects.Length < 4)
            {
                Logger.Verbose($"[CaretLocator] GetBoundingRectangles hr=0x{hr:x8} len={rects?.Length ?? -1}(插入符隐藏时为空,降级)");
                return false;
            }
            caretRect = new Rectangle(
                (int)rects[0], (int)rects[1],
                Math.Max(1, (int)rects[2]), Math.Max(1, (int)rects[3]));
            return true;
        }
        catch (Exception ex)
        {
            Logger.Verbose($"[CaretLocator] 原生 UIA 层异常(忽略,降级): {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            ReleaseCom(range);
            ReleaseCom(patternObj);
            ReleaseCom(element);
            ReleaseCom(automation);
        }
    }

    private static bool TrySelectionRange(AutomationElement element, Action<string> log, ref Rectangle caretRect)
    {
        try
        {
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out object pattern) || pattern is not TextPattern text)
                return false;
            var selection = text.GetSelection();
            if (selection is not { Length: > 0 })
                return false;
            var rects = selection[0].GetBoundingRectangles();
            if (rects.Length == 0 || rects[0].IsEmpty)
                return false;
            caretRect = RectToRectangle(rects[0]);
            return true;
        }
        catch (Exception ex)
        {
            log($"[CaretLocator] GetSelection 层异常(忽略,降级): {ex.GetType().Name}");
            return false;
        }
    }

    private static bool TryGuiThreadInfo(IntPtr hwnd, Action<string> log, ref Rectangle caretRect)
    {
        try
        {
            uint tid = Win32Native.GetWindowThreadProcessId(hwnd, out _);
            var info = new Win32Native.GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<Win32Native.GUITHREADINFO>() };
            if (!Win32Native.GetGUIThreadInfo(tid, ref info) || info.hwndCaret == IntPtr.Zero)
                return false;
            var rc = info.rcCaret;
            if (rc.Right <= rc.Left || rc.Bottom <= rc.Top)
                return false;
            var pt = new Win32Native.POINT { X = rc.Left, Y = rc.Top };
            Win32Native.ClientToScreen(info.hwndCaret, ref pt);
            caretRect = new Rectangle(pt.X, pt.Y, rc.Right - rc.Left, Math.Max(rc.Bottom - rc.Top, 8));
            return true;
        }
        catch (Exception ex)
        {
            log($"[CaretLocator] GetGUIThreadInfo 层异常(忽略,降级): {ex.GetType().Name}");
            return false;
        }
    }

    private static bool TryControlBounds(AutomationElement element, ref Rectangle caretRect)
    {
        var rect = element.Current.BoundingRectangle;
        if (rect.IsEmpty)
            return false;
        caretRect = new Rectangle((int)rect.X + 8, (int)rect.Y + 8, 2, 16);
        return true;
    }

    private static Rectangle RectToRectangle(System.Windows.Rect r) =>
        new((int)r.X, (int)r.Y, Math.Max(1, (int)r.Width), Math.Max(1, (int)r.Height));

    private static void ReleaseCom(object? obj)
    {
        if (obj is not null)
            Marshal.FinalReleaseComObject(obj);
    }
}
