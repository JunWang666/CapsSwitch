using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using CapsSwitch.App.Config;
using CapsSwitch.App.Interop;
using CapsSwitch.App.Logging;
using CapsSwitch.App.UI;

namespace CapsSwitch.App;

internal static class Program
{
    private static void Log(string message) => Logger.Info(message);

    [STAThread]
    private static int Main(string[] args)
    {
        // PMv2 尽早设置:UIA 返回物理像素矩形,直接用;必须早于任何窗口创建
        Win32Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

        string command = args.Length == 0 ? "run" : args[0];
        bool cliMode = command is "list" or "capsstate" or "hudtest" or "autostart";

        Logger.Initialize();
        if (cliMode && TryAttachConsole())
            Log($"[App] CLI 模式:{command}(已挂接父控制台)");

        return command switch
        {
            "run" => RunTrayMode(),
            "list" => ListCommand(),
            "capsstate" => CapsStateCommand(),
            "hudtest" => HudTestCommand(),
            "autostart" => AutoStartCommand(args.Skip(1).FirstOrDefault()),
            "settings" => SettingsUiCommand(),
            _ => Usage(command),
        };
    }

    /// <summary>挂接父控制台并重建 stdout;失败(false)时输出只进日志文件。</summary>
    private static bool TryAttachConsole()
    {
        if (!Win32Native.AttachConsole(-1))
            return false;
        try
        {
            var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(writer);
            var error = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
            Console.SetError(error);
            Logger.ConsoleAttached = true;
            return true;
        }
        catch (Exception ex)
        {
            Log($"[App] AttachConsole 后重定向输出失败:{ex.Message}");
            return false;
        }
    }

    private static int Usage(string command)
    {
        Logger.ConsoleAttached = true; // 用错命令时尽量直接打到控制台
        Console.WriteLine($"未知命令 '{command}'");
        Console.WriteLine("用法: CapsSwitch.App [run|list|capsstate|hudtest|autostart on|off|status]");
        Console.WriteLine("  run (默认)    托盘模式启动");
        Console.WriteLine("  list          列出输入源与当前前台输入源");
        Console.WriteLine("  capsstate     打印真实 CapsLock 锁定状态(调试)");
        Console.WriteLine("  hudtest       在当前前台光标处强行显示 HUD \"中\" 3 秒");
        Console.WriteLine("  autostart     开机自启设置(on/off/status)");
        return 2;
    }

    private static int RunTrayMode()
    {
        using var singleInstance = new Mutex(initiallyOwned: true, name: @"Local\CapsSwitch.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            Log("[App] 已有 CapsSwitch 实例在运行,退出");
            return 0;
        }

        Log("[App] CapsSwitch Phase 3 启动(托盘模式)");
        using var service = new InputSources.InputSourceService(Log);
        var settingsStore = new SettingsStore(Log);
        var settings = settingsStore.Load(service.GetSources());
        Log($"[App] 首选中文={settings.PreferredChineseId ?? "(无)"} 首选英文={settings.PreferredEnglishId ?? "(无)"} 顺序=[{string.Join(", ", settings.OrderedSourceIds)}] HUD={(settings.HudEnabled ? $"开({settings.HudDurationMs}ms)" : "关")}");

        var host = new AppHost(Log, settings, settingsStore);
        if (!host.Start())
            return 1;

        using var tray = new TrayContext(Log, host, settingsStore, settingsStore.IsFirstRun);
        Application.Idle += (_, _) => host.OnUiPumpStarted();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Application.Exit();
        };
        int exitAfterMs = 0;
        if (int.TryParse(Environment.GetEnvironmentVariable("CAPSSWITCH_EXIT_AFTER_MS"), out var v) && v > 0)
            exitAfterMs = v;
        using var exitTimer = exitAfterMs > 0
            ? new System.Threading.Timer(_ => Application.Exit(), null, exitAfterMs, Timeout.Infinite)
            : null;

        Application.Run(tray);

        Log("[App] 正在退出…");
        host.Dispose();
        service.Dispose();
        Log("[App] 已退出");
        return 0;
    }

    private static int ListCommand()
    {
        using var service = new InputSources.InputSourceService(Log);
        var sources = service.GetSources();
        Console.WriteLine($"共 {sources.Count} 个输入源:");
        foreach (var s in sources)
            Console.WriteLine($"  [{s.ShortLabel,-3}] {s.DisplayName}  Id={s.Id}  HKL=0x{s.Hkl.ToInt64():x8}  langid=0x{s.LangId & 0xFFFF:x4}  {(s.IsTsfTip ? "TSF TIP" : "键盘布局")}");
        var current = service.GetCurrent();
        Console.WriteLine(current is null ? "  当前前台: (无匹配)" : $"  当前前台: [{current.ShortLabel}] {current.DisplayName}");
        return 0;
    }

    /// <summary>在带 WinForms 消息泵的线程上读取 Control.IsKeyLocked(CapsLock)。</summary>
    private static int CapsStateCommand()
    {
        using var done = new ManualResetEventSlim();
        var pump = new Thread(() =>
        {
            var context = new ApplicationContext();
            using var timer = new System.Windows.Forms.Timer { Interval = 100 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                bool locked = Control.IsKeyLocked(Keys.CapsLock);
                Console.WriteLine($"CapsLock locked = {locked}");
                context.ExitThread();
                done.Set();
            };
            timer.Start();
            Application.Run(context);
        })
        { Name = "CapsSwitch-CapsState", IsBackground = true };
        pump.SetApartmentState(ApartmentState.STA);
        pump.Start();
        if (!done.Wait(5000))
        {
            Console.WriteLine("capsstate: 消息泵线程超时");
            return 1;
        }
        return 0;
    }

    /// <summary>hudtest:在当前前台光标处强行显示 HUD "中" 3 秒,验证浮窗样式/定位。</summary>
    private static int HudTestCommand()
    {
        Log("[hudtest] 启动 UI 线程");
        var ui = new Thread(() =>
        {
            var context = new ApplicationContext();
            using var service = new InputSources.InputSourceService(Log);
            var focusMonitor = new Focus.FocusMonitor(Log);
            focusMonitor.Start();
            var monitor = new InputSources.InputSourceMonitor(Log, service, null);
            monitor.Start();
            var settings = new AppSettings();
            Overlay.HudController? hud = null;
            Application.Idle += (_, _) =>
            {
                if (hud is not null)
                    return;
                hud = new Overlay.HudController(Log, settings, settings.PreferredChineseId, service, focusMonitor, monitor);
                hud.ShowTest("中");
                Log("[hudtest] HUD 已请求显示(预期 3 秒后自动消失)");
            };
            using var quitTimer = new System.Threading.Timer(_ => Application.Exit(), null, 3600, Timeout.Infinite);
            Application.Run(context);
            hud?.Dispose();
            monitor.Stop();
            focusMonitor.Dispose();
            Log("[hudtest] UI 线程退出");
        })
        { Name = "CapsSwitch-HudTestUI", IsBackground = true };
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        if (!ui.Join(10000))
        {
            Console.WriteLine("hudtest: UI 线程退出超时");
            return 1;
        }
        Log("[hudtest] 完成");
        return 0;
    }

    /// <summary>设置窗口调试入口:不安装键盘钩子,仅打开设置 UI(UI 验证/截图用)。</summary>
    private static int SettingsUiCommand()
    {
        Log("[settings] 打开设置窗口(无钩子调试模式)");
        var store = new SettingsStore(Log);
        using var service = new InputSources.InputSourceService(Log);
        var settings = store.Load(service.GetSources());
        var host = new AppHost(Log, settings, store); // 不 Start → 不装钩子
        ApplicationConfiguration.Initialize();
        SettingsForm.OpenSingleton(Log, host, store);
        Application.Run();
        host.Dispose();
        service.Dispose();
        return 0;
    }

    /// <summary>开机自启调试命令:on/off/status(等价于设置窗口勾选走的代码路径)。</summary>
    private static int AutoStartCommand(string? arg)
    {
        switch (arg)
        {
            case "on":
                AutoStartRegistrar.SetAutoStart(true);
                Console.WriteLine($"autostart on → 已写入 HKCU Run 键(值={AutoStartRegistrar.QuoteForRunKey(Environment.ProcessPath ?? "?")})");
                return 0;
            case "off":
                AutoStartRegistrar.SetAutoStart(false);
                Console.WriteLine("autostart off → 已删除 HKCU Run 键值");
                return 0;
            case "status":
            case null:
                Console.WriteLine($"autostart = {(AutoStartRegistrar.IsAutoStartEnabled() ? "on" : "off")}");
                return 0;
            default:
                Console.WriteLine("用法: autostart [on|off|status]");
                return 2;
        }
    }
}
