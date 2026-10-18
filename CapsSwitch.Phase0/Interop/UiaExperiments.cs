using System.Windows.Automation;
using CapsSwitch.Phase0.Interop;

namespace CapsSwitch.Phase0;

internal static class UiaExperiments
{
    public static int FocusCommand(int seconds)
    {
        Console.WriteLine($"=== focus: UIA 焦点监听 + 光标定位实验 ({(seconds > 0 ? $"{seconds} 秒" : "直到 Ctrl+C")}) ===");
        var context = new ApplicationContext();
        var thread = new Thread(() =>
        {
            try
            {
                Automation.AddAutomationFocusChangedEventHandler(OnFocusChanged);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AddFocusChangedEventHandler 失败: {ex.Message}");
                context.ExitThread();
                return;
            }
            Application.Run(context);
        });
        thread.SetApartmentState(ApartmentState.MTA);
        thread.IsBackground = true;
        thread.Start();
        Console.WriteLine("焦点事件监听已启动,请切换窗口焦点。按 Ctrl+C 结束。");

        DateTime start = DateTime.Now;
        while (seconds <= 0 || (DateTime.Now - start).TotalSeconds < seconds)
            Thread.Sleep(200);

        try
        {
            context.ExitThread();
            Automation.RemoveAllEventHandlers();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"移除事件处理器时出现异常(忽略): {ex.Message}");
        }
        Console.WriteLine("focus 结束");
        return 0;
    }

    private static void OnFocusChanged(object sender, AutomationFocusChangedEventArgs e)
    {
        try
        {
            if (sender is not AutomationElement element)
                return;
            var current = element.Current;
            bool isTextLike = current.ControlType == ControlType.Edit
                || current.ControlType == ControlType.Document
                || (bool)element.GetCurrentPropertyValue(AutomationElement.IsTextPatternAvailableProperty);
            if (!isTextLike)
                return;

            string name = current.Name.Length > 40 ? current.Name[..40] + "…" : current.Name;
            string processName = GetProcessName(current.NativeWindowHandle);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] 焦点控件: ControlType={current.ControlType.ProgrammaticName} Name=\"{name}\" FrameworkId={current.FrameworkId} 进程={processName}");

            PrintCaretPosition(element);
        }
        catch (ElementNotAvailableException)
        {
            // 元素已销毁,忽略
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  焦点处理异常(忽略): {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void PrintCaretPosition(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object pattern) && pattern is TextPattern text)
            {
                Console.WriteLine("  TextPattern 可用,尝试 GetSelection() 退化区间(等效插入符):");
                var selection = text.GetSelection();
                if (selection is { Length: > 0 })
                {
                    var rects = selection[0].GetBoundingRectangles();
                    string rectText = rects.Length > 0
                        ? string.Join(", ", rects.Select(r => $"({r.X:F0},{r.Y:F0} {r.Width:F0}x{r.Height:F0})"))
                        : "(空)";
                    Console.WriteLine($"  caret(退化区间) 矩形={rectText}");
                    return;
                }
                Console.WriteLine("  GetSelection 返回空");
            }
            else
            {
                Console.WriteLine("  TextPattern 不可用");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  TextPattern 路径失败: {ex.GetType().Name}: {ex.Message}");
        }

        // fallback: NativeWindowHandle → GetGUIThreadInfo
        try
        {
            IntPtr hwnd = new(element.Current.NativeWindowHandle);
            if (hwnd == IntPtr.Zero)
            {
                Console.WriteLine("  fallback: NativeWindowHandle=0,无法定位");
                return;
            }
            uint tid = Win32Native.GetWindowThreadProcessId(hwnd, out _);
            var info = new Win32Native.GUITHREADINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32Native.GUITHREADINFO>() };
            if (!Win32Native.GetGUIThreadInfo(tid, ref info))
            {
                Console.WriteLine($"  fallback: GetGUIThreadInfo 失败,LastError=0x{System.Runtime.InteropServices.Marshal.GetLastWin32Error():x8}");
                return;
            }
            var caret = info.rcCaret;
            var pt = new Win32Native.POINT { X = caret.Left, Y = caret.Top };
            Win32Native.ClientToScreen(info.hwndCaret, ref pt);
            Console.WriteLine($"  fallback: GetGUIThreadInfo rcCaret=({caret.Left},{caret.Top},{caret.Right},{caret.Bottom}) hwndCaret=0x{info.hwndCaret.ToInt64():x8} → 屏幕坐标({pt.X},{pt.Y})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  fallback 路径失败: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string GetProcessName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return "?";
        try
        {
            Win32Native.GetWindowThreadProcessId(hwnd, out uint pid);
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch
        {
            return "?";
        }
    }
}
