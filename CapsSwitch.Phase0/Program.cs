using CapsSwitch.Phase0.Interop;

namespace CapsSwitch.Phase0;

internal static class Program
{
    [MTAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            return args[0] switch
            {
                "enum" => Commands.EnumCommand(),
                "current" => Commands.CurrentCommand(),
                "fg" => Commands.FgCommand(),
                "activate" when args.Length >= 2 => Commands.ActivateCommand(args[1]),
                "activate-msg" when args.Length >= 2 => Commands.ActivateMsgCommand(args[1]),
                "watch" => Commands.WatchCommand(args.Length >= 2 && int.TryParse(args[1], out int w) ? w : 30),
                "focus" => UiaExperiments.FocusCommand(args.Length >= 2 && int.TryParse(args[1], out int f) ? f : 30),
                "pinyin" => Commands.PinyinCommand(),
                "all" => Commands.AllCommand(),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"命令 '{args[0]}' 顶层异常: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static int Unknown(string command)
    {
        Console.WriteLine($"未知命令 '{command}'");
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("CapsSwitch Phase 0 技术验证工具");
        Console.WriteLine();
        Console.WriteLine("用法: CapsSwitch.Phase0 <命令> [参数]");
        Console.WriteLine();
        Console.WriteLine("命令:");
        Console.WriteLine("  enum                 枚举全部已启用输入源");
        Console.WriteLine("  current              显示当前活动 Profile");
        Console.WriteLine("  fg                   显示前台窗口线程的输入法状态");
        Console.WriteLine("  activate <zh|en>     通过 TSF ActivateProfile 激活指定输入源并验证");
        Console.WriteLine("  activate-msg <hklHex> 向前台窗口 PostMessage WM_INPUTLANGCHANGEREQUEST 并验证");
        Console.WriteLine("  watch [秒数=30]      轮询观察输入源变化");
        Console.WriteLine("  focus [秒数=30]      UIA 焦点监听 + 光标定位实验");
        Console.WriteLine("  pinyin               微软拼音中英文模式检测实验");
        Console.WriteLine("  all                  依次自动跑 enum、current、pinyin");
    }
}
