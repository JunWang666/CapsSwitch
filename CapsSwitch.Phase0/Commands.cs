using System.Diagnostics;
using System.Runtime.InteropServices;
using CapsSwitch.Phase0.Interop;
using Microsoft.Win32;

namespace CapsSwitch.Phase0;

internal static class Commands
{
    private static readonly Guid PinyinClsid = new("81D4E9C9-1D3B-41BC-9E6C-4B40BF79E35E");
    private static readonly Guid PinyinProfile = new("FA550B04-5AD7-411F-A5AC-CA038EC515D7");

    public static int EnumCommand()
    {
        Console.WriteLine("=== enum: 注册表扫描的输入源 (HKLM\\SOFTWARE\\Microsoft\\CTF\\TIP) ===");
        var registryProfiles = TsfInterop.EnumRegistryProfiles().ToList();
        var langids = registryProfiles.Select(p => p.langid).Distinct().OrderBy(x => x).ToList();
        if (langids.Count == 0)
            Console.WriteLine("  (注册表扫描未找到 LanguageProfile 条目)");
        foreach (var (langid, clsid, profile) in registryProfiles)
            Console.WriteLine($"  0x{langid:x4}  {clsid:B}  {profile:B}");

        Console.WriteLine();
        Console.WriteLine("=== enum: TSF EnumProfiles 逐 langid 结果 ===");
        var mgr = TsfInterop.CreateProfileMgr();
        try
        {
            foreach (var langid in langids)
            {
                Console.WriteLine($"langid=0x{langid:x4}:");
                foreach (var p in TsfInterop.EnumProfiles(mgr, langid))
                    TsfInterop.PrintProfile(p);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(mgr);
        }

        Console.WriteLine();
        Console.WriteLine("=== enum: GetKeyboardLayoutList / 当前线程 HKL ===");
        int count = Win32Native.GetKeyboardLayoutList(0, null);
        var list = new IntPtr[count];
        Win32Native.GetKeyboardLayoutList(count, list);
        foreach (var hkl in list)
            Console.WriteLine($"  HKL=0x{hkl.ToInt64():x8}");
        Console.WriteLine($"  当前线程 HKL=0x{Win32Native.GetKeyboardLayout(0).ToInt64():x8}");
        return 0;
    }

    public static int CurrentCommand()
    {
        Console.WriteLine("=== current: 当前活动 Profile (GUID_TFCAT_TIP_KEYBOARD) ===");
        var mgr = TsfInterop.CreateProfileMgr();
        try
        {
            var catid = TsfInterop.GUID_TFCAT_TIP_KEYBOARD;
            var profile = new TF_INPUTPROCESSORPROFILE { dwSize = Marshal.SizeOf<TF_INPUTPROCESSORPROFILE>() };
            int hr = mgr.GetActiveProfile(ref catid, out profile);
            Console.WriteLine($"GetActiveProfile HRESULT={TsfInterop.HresultToString(hr)}");
            if (hr == 0)
                TsfInterop.PrintProfile(profile);
        }
        finally
        {
            Marshal.ReleaseComObject(mgr);
        }
        Console.WriteLine($"当前线程 HKL=0x{Win32Native.GetKeyboardLayout(0).ToInt64():x8}");
        return 0;
    }

    public static int FgCommand()
    {
        Console.WriteLine("=== fg: 前台窗口线程输入法状态 ===");
        PrintForegroundState();
        return 0;
    }

    private static void PrintForegroundState()
    {
        IntPtr hwnd = Win32Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            Console.WriteLine("  当前没有前台窗口");
            return;
        }
        uint tid = Win32Native.GetWindowThreadProcessId(hwnd, out uint pid);
        string processName = GetProcessName(pid);
        IntPtr hkl = Win32Native.GetKeyboardLayout(tid);
        Console.WriteLine($"  hwnd=0x{hwnd.ToInt64():x8} pid={pid}({processName}) tid={tid} HKL=0x{hkl.ToInt64():x8}");

        IntPtr imeWnd = Win32Native.ImmGetDefaultIMEWnd(hwnd);
        if (imeWnd == IntPtr.Zero)
        {
            Console.WriteLine("  ImmGetDefaultIMEWnd=0 (前台线程无关联 IME 窗口)");
            return;
        }
        IntPtr cmode = Win32Native.SendMessageW(imeWnd, Win32Native.WM_IME_CONTROL, Win32Native.IMC_GETCONVERSIONMODE, IntPtr.Zero);
        bool native = (cmode.ToInt32() & Win32Native.IME_CMODE_NATIVE) != 0;
        Console.WriteLine($"  IME 转换模式=0x{cmode.ToInt32():x4} ({(native ? "中文(IME_CMODE_NATIVE)" : "英文(IME_CMODE_ALPHANUMERIC)")})");
        IntPtr open = Win32Native.SendMessageW(imeWnd, Win32Native.WM_IME_CONTROL, Win32Native.IMC_GETOPENSTATUS, IntPtr.Zero);
        Console.WriteLine($"  IME 打开状态={(open != IntPtr.Zero ? "开" : "关")}");
    }

    private static string GetProcessName(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch
        {
            return "?";
        }
    }

    public static int ActivateCommand(string target)
    {
        short langid; Guid clsid; Guid profile; IntPtr hkl;
        if (target.Equals("zh", StringComparison.OrdinalIgnoreCase))
        {
            langid = 0x804; clsid = PinyinClsid; profile = PinyinProfile; hkl = IntPtr.Zero;
        }
        else if (target.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            langid = 0x409; clsid = Guid.Empty; profile = Guid.Empty; hkl = new IntPtr(0x00000409);
            IntPtr loaded = Win32Native.LoadKeyboardLayoutW("00000409", 1);
            Console.WriteLine($"LoadKeyboardLayout(00000409)=0x{loaded.ToInt64():x8} (确保 US 键盘已加载)");
        }
        else
        {
            Console.WriteLine($"未知目标 '{target}',仅支持 zh|en");
            return 2;
        }

        Console.WriteLine($"=== activate {target}: langid=0x{langid:x4} clsid={clsid:B} guidProfile={profile:B} hkl=0x{hkl.ToInt64():x8} ===");
        var mgr = TsfInterop.CreateProfileMgr();
        try
        {
            var rows = TsfInterop.EnumProfiles(mgr, langid);
            Console.WriteLine($"EnumProfiles(0x{langid:x4}) 找到 {rows.Count} 个 profile:");
            foreach (var p in rows)
            {
                TsfInterop.PrintProfile(p);
                if (target == "zh" && hkl == IntPtr.Zero && p.hklSubstitute != IntPtr.Zero)
                    hkl = p.hklSubstitute;
            }
            if (hkl == IntPtr.Zero && target == "zh" && rows.Count > 0)
            {
                var zhRow = rows.FirstOrDefault(r => r.clsid == clsid && r.guidProfile == profile);
                if (zhRow.hklSubstitute != IntPtr.Zero)
                    hkl = zhRow.hklSubstitute;
            }
            Console.WriteLine($"最终用于 ActivateProfile 的 hkl=0x{hkl.ToInt64():x8}");
            int profileType = target == "en" ? TsfInterop.TF_PROFILETYPE_KEYBOARDLAYOUT : TsfInterop.TF_PROFILETYPE_INPUTPROCESSOR;
            string typeName = profileType == 2 ? "TF_PROFILETYPE_KEYBOARDLAYOUT" : "TF_PROFILETYPE_INPUTPROCESSOR";

            // 实测:KEYBOARDLAYOUT 类型带 TF_IPPMF_ENABLEPROFILE 才有意义;INPUTPROCESSOR 类型 flags=0 即可
            var flags = target == "en"
                ? new (int value, string name)[]
                {
                    (0, "dwFlags=0"),
                    (TsfInterop.TF_IPPMF_ENABLEPROFILE, "TF_IPPMF_ENABLEPROFILE"),
                    (TsfInterop.TF_IPPMF_ENABLEPROFILE | TsfInterop.TF_IPPMF_FORPROCESS, "ENABLEPROFILE|FORPROCESS"),
                    (TsfInterop.TF_IPPMF_ENABLEPROFILE | TsfInterop.TF_IPPMF_FORSESSION, "ENABLEPROFILE|FORSESSION"),
                }
                : new (int value, string name)[]
                {
                    (0, "dwFlags=0"),
                    (TsfInterop.TF_IPPMF_FORPROCESS, "TF_IPPMF_FORPROCESS"),
                    (TsfInterop.TF_IPPMF_FORSESSION, "TF_IPPMF_FORSESSION"),
                    (TsfInterop.TF_IPPMF_DONTCARECURRENTINPUTLANGUAGE, "DONTCARE"),
                    (TsfInterop.TF_IPPMF_DONTCARECURRENTINPUTLANGUAGE | TsfInterop.TF_IPPMF_FORPROCESS, "DONTCARE|FORPROCESS"),
                    (TsfInterop.TF_IPPMF_DONTCARECURRENTINPUTLANGUAGE | TsfInterop.TF_IPPMF_FORSESSION, "DONTCARE|FORSESSION"),
                };
            foreach (var (value, name) in flags)
            {
                var c = clsid; var g = profile;
                int hr = mgr.ActivateProfile(profileType, langid, ref c, ref g, hkl, value);
                Console.WriteLine($"ActivateProfile({typeName}, {name}) HRESULT={TsfInterop.HresultToString(hr)}");
                Thread.Sleep(300);
                PrintActivationState(mgr);
            }

            if (target == "en")
            {
                Console.WriteLine("TSF KEYBOARDLAYOUT 激活在本机返回失败,回退到 WM_INPUTLANGCHANGEREQUEST:");
                IntPtr hwnd = Win32Native.GetForegroundWindow();
                IntPtr fullHkl = new(0x04090409);
                bool ok = Win32Native.PostMessageW(hwnd, Win32Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, fullHkl);
                Console.WriteLine($"PostMessage(WM_INPUTLANGCHANGEREQUEST, hkl=0x{fullHkl.ToInt64():x8}) → {(ok ? "已投递" : $"失败 LastError=0x{Marshal.GetLastWin32Error():x8}")}");
                Thread.Sleep(300);
                PrintForegroundState();
            }
        }
        finally
        {
            Marshal.ReleaseComObject(mgr);
        }
        return 0;
    }

    private static void PrintActivationState(ITfInputProcessorProfileMgr mgr)
    {
        var catid = TsfInterop.GUID_TFCAT_TIP_KEYBOARD;
        var p = new TF_INPUTPROCESSORPROFILE { dwSize = Marshal.SizeOf<TF_INPUTPROCESSORPROFILE>() };
        int hr = mgr.GetActiveProfile(ref catid, out p);
        Console.WriteLine($"  [本进程视角] GetActiveProfile HRESULT={TsfInterop.HresultToString(hr)}" +
            (hr == 0 ? $" guidProfile={p.guidProfile:B} hkl=0x{p.hkl.ToInt64():x8}" : string.Empty));
        PrintForegroundState();
    }

    public static int ActivateMsgCommand(string hklHex)
    {
        if (!long.TryParse(hklHex, System.Globalization.NumberStyles.HexNumber, null, out long hklValue))
        {
            Console.WriteLine($"无法解析 hklHex '{hklHex}' (期望如 E0200804 或 00000409)");
            return 2;
        }
        IntPtr hkl = new(hklValue);
        Console.WriteLine($"=== activate-msg: WM_INPUTLANGCHANGEREQUEST hkl=0x{hklValue:x8} ===");
        IntPtr hwnd = Win32Native.GetForegroundWindow();
        Console.WriteLine($"前台 hwnd=0x{hwnd.ToInt64():x8}");
        if (!Win32Native.PostMessageW(hwnd, Win32Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl))
            Console.WriteLine($"PostMessage 失败,LastError=0x{Marshal.GetLastWin32Error():x8}");
        Thread.Sleep(300);
        PrintForegroundState();
        return 0;
    }

    public static int WatchCommand(int seconds)
    {
        Console.WriteLine($"=== watch: 轮询输入源变化 ({(seconds > 0 ? $"{seconds} 秒" : "直到 Ctrl+C")}) ===");
        var mgr = TsfInterop.CreateProfileMgr();
        var snapshot = new (Guid, Guid, long, long, int)?[1];
        DateTime start = DateTime.Now;
        try
        {
            while (true)
            {
                if (seconds > 0 && (DateTime.Now - start).TotalSeconds >= seconds) break;
                var cur = TakeSnapshot(mgr);
                if (snapshot[0] is { } prev && cur != prev)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] 检测到变化:");
                    PrintSnapshot(cur, "  现在");
                    PrintSnapshot(prev, "  之前");
                }
                snapshot[0] = cur;
                Thread.Sleep(200);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(mgr);
        }
        Console.WriteLine("watch 结束");
        return 0;
    }

    private static (Guid clsid, Guid profile, long hkl, long fgHkl, int cmode) TakeSnapshot(ITfInputProcessorProfileMgr mgr)
    {
        var catid = TsfInterop.GUID_TFCAT_TIP_KEYBOARD;
        var p = new TF_INPUTPROCESSORPROFILE { dwSize = Marshal.SizeOf<TF_INPUTPROCESSORPROFILE>() };
        Guid clsid = Guid.Empty, profile = Guid.Empty;
        long hkl = 0;
        if (mgr.GetActiveProfile(ref catid, out p) == 0)
        {
            clsid = p.clsid; profile = p.guidProfile; hkl = p.hkl.ToInt64();
        }
        long fgHkl = 0; int cmode = -1;
        IntPtr hwnd = Win32Native.GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
        {
            uint tid = Win32Native.GetWindowThreadProcessId(hwnd, out _);
            fgHkl = Win32Native.GetKeyboardLayout(tid).ToInt64();
            IntPtr imeWnd = Win32Native.ImmGetDefaultIMEWnd(hwnd);
            if (imeWnd != IntPtr.Zero)
                cmode = Win32Native.SendMessageW(imeWnd, Win32Native.WM_IME_CONTROL, Win32Native.IMC_GETCONVERSIONMODE, IntPtr.Zero).ToInt32();
        }
        return (clsid, profile, hkl, fgHkl, cmode);
    }

    private static void PrintSnapshot((Guid clsid, Guid profile, long hkl, long fgHkl, int cmode) s, string label)
    {
        Console.WriteLine($"{label}: activeProfile clsid={s.clsid:B} guidProfile={s.profile:B} hkl=0x{s.hkl:x8} | 前台 HKL=0x{s.fgHkl:x8} | 前台 IME 转换模式={(s.cmode >= 0 ? $"0x{s.cmode:x4}" : "无 IME")}");
    }

    public static int PinyinCommand()
    {
        Console.WriteLine("=== pinyin: 微软拼音相关实验 ===");
        Console.WriteLine("--- ① 注册表 HKCU\\SOFTWARE\\Microsoft\\InputMethod\\Settings\\CHS 全量转储 ---");
        const string chsKeyPath = @"SOFTWARE\Microsoft\InputMethod\Settings\CHS";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(chsKeyPath);
            if (key is null)
            {
                Console.WriteLine($"  键不存在: {chsKeyPath}");
            }
            else
            {
                foreach (var name in key.GetValueNames())
                {
                    object? value = key.GetValue(name);
                    Console.WriteLine($"  {name} = {FormatRegValue(value)}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  读取注册表失败: {ex.Message}");
        }

        Console.WriteLine("--- ② 前台窗口 IME 转换模式检测 ---");
        PrintForegroundState();

        Console.WriteLine("--- ③ 说明 ---");
        Console.WriteLine("  Windows 设置里关闭 Shift 中英文切换的路径:");
        Console.WriteLine("  设置 → 时间和语言 → 语言和区域 → 中文 → 微软拼音 → 按键 → 模式切换");
        return 0;
    }

    private static string FormatRegValue(object? value) => value switch
    {
        null => "(null)",
        byte[] bytes => $"0x{Convert.ToHexString(bytes)}",
        _ => $"{value} ({value.GetType().Name})",
    };

    public static int AllCommand()
    {
        RunSection("enum", EnumCommand);
        RunSection("current", CurrentCommand);
        RunSection("pinyin", PinyinCommand);
        return 0;
    }

    private static void RunSection(string title, Func<int> action)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 60));
        Console.WriteLine($"### {title}");
        Console.WriteLine(new string('=', 60));
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{title} 抛出异常: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
