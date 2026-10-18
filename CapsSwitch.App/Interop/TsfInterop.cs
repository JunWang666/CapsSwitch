using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CapsSwitch.App.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct TF_INPUTPROCESSORPROFILE
{
    public int dwSize;
    public short langid;
    public Guid clsid;
    public Guid guidProfile;
    public Guid catid;
    public IntPtr hklSubstitute;
    public int dwCaps;
    public IntPtr hkl;
    public int dwFlags;
}

// 本机 Windows SDK (10.0.26100) msctf.h 确认的实际 vtable 顺序;
// 注意与网络资料不同:ActivateProfile 第一个参数是 dwProfileType、最后才是 dwFlags,
// GetActiveProfile 排在第 8 位,中间夹了 GetProfile/ReleaseInputProcessor/Register/Unregister。
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("71C6E74C-0F28-11D8-A82A-00065B84435C")]
internal interface ITfInputProcessorProfileMgr
{
    [PreserveSig]
    int ActivateProfile(int dwProfileType, short langid, [In] ref Guid clsid, [In] ref Guid guidProfile, IntPtr hkl, int dwFlags);
    [PreserveSig]
    int DeactivateProfile(int dwProfileType, short langid, [In] ref Guid clsid, [In] ref Guid guidProfile, IntPtr hkl, int dwFlags);
    [PreserveSig]
    int GetProfile(int dwProfileType, short langid, [In] ref Guid clsid, [In] ref Guid guidProfile, IntPtr hkl, out TF_INPUTPROCESSORPROFILE pProfile);
    [PreserveSig]
    int EnumProfiles(short langid, out IntPtr ppEnum);
    [PreserveSig]
    int ReleaseInputProcessor([In] ref Guid rclsid, int dwFlags);
    [PreserveSig]
    int RegisterProfile([In] ref Guid rclsid, short langid, [In] ref Guid guidProfile, [MarshalAs(UnmanagedType.LPWStr)] string pchDesc, int cchDesc, [MarshalAs(UnmanagedType.LPWStr)] string pchIconFile, int cchFile, int uIconIndex, IntPtr hklsubstitute, int dwPreferredLayout, [MarshalAs(UnmanagedType.Bool)] bool bEnabledByDefault, int dwFlags);
    [PreserveSig]
    int UnregisterProfile([In] ref Guid rclsid, short langid, [In] ref Guid guidProfile, int dwFlags);
    [PreserveSig]
    int GetActiveProfile([In] ref Guid catid, out TF_INPUTPROCESSORPROFILE pProfile);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("71C6E74D-0F28-11D8-A82A-00065B84435C")]
internal interface IEnumTfInputProcessorProfiles
{
    // 该枚举器方法顺序非标准:Clone 在前、Next 在后
    int Clone(out IntPtr ppEnum);
    int Next(int ulCount, out TF_INPUTPROCESSORPROFILE pProfile, out int pcFetch);
}

internal static class TsfInterop
{
    public static readonly Guid CLSID_TF_InputProcessorProfiles = new("33C53A50-F456-4884-B049-85FD643ECFED");
    public static readonly Guid IID_ITfInputProcessorProfileMgr = new("71C6E74C-0F28-11D8-A82A-00065B84435C");
    // 注意:常见网络资料写作 B2F9-4784-AE17-10F52D336E95,但本机 EnumProfiles 实测
    // 键盘类 profile 的 catid 为 34745C63-B2F0-4784-8B67-5E12C8701A31,以此为准
    public static readonly Guid GUID_TFCAT_TIP_KEYBOARD = new("34745C63-B2F0-4784-8B67-5E12C8701A31");

    public const int TF_PROFILETYPE_INPUTPROCESSOR = 0x1;
    public const int TF_PROFILETYPE_KEYBOARDLAYOUT = 0x2;

    public const int TF_IPPMF_ENABLEPROFILE = 0x1;
    public const int TF_IPPMF_DISABLEPROFILE = 0x2;
    public const int TF_IPPMF_DONTCARECURRENTINPUTLANGUAGE = 0x4;
    public const int TF_IPPMF_FORPROCESS = unchecked((int)0x10000000);
    public const int TF_IPPMF_FORSESSION = unchecked((int)0x20000000);

    private const string TsfTipRegPath = @"SOFTWARE\Microsoft\CTF\TIP";

    private static readonly (Guid clsid, Guid profile, string name)[] KnownProfiles =
    {
        (new Guid("81D4E9C9-1D3B-41BC-9E6C-4B40BF79E35E"), new Guid("FA550B04-5AD7-411F-A5AC-CA038EC515D7"), "微软拼音 (Microsoft Pinyin)"),
        (new Guid("03B5835F-F03C-411B-9CE2-AA23E1171E36"), Guid.Empty, "Microsoft IME (Japanese)"),
    };

    public static ITfInputProcessorProfileMgr CreateProfileMgr()
    {
        var type = Type.GetTypeFromCLSID(CLSID_TF_InputProcessorProfiles)!;
        var obj = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("CoCreateInstance(CLSID_TF_InputProcessorProfiles) 失败");
        return (ITfInputProcessorProfileMgr)obj;
    }

    public static IEnumerable<(short langid, Guid clsid, Guid guidProfile)> EnumRegistryProfiles()
    {
        var seen = new HashSet<(short, Guid, Guid)>();
        using var tipRoot = Registry.LocalMachine.OpenSubKey(TsfTipRegPath);
        if (tipRoot is null) yield break;
        foreach (var clsidName in tipRoot.GetSubKeyNames())
        {
            if (!Guid.TryParse(clsidName, out var clsid)) continue;
            using var clsidKey = tipRoot.OpenSubKey(clsidName + @"\LanguageProfile");
            if (clsidKey is null) continue;
            foreach (var langName in clsidKey.GetSubKeyNames())
            {
                if (!langName.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) continue;
                if (!short.TryParse(langName.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var langid)) continue;
                using var langKey = clsidKey.OpenSubKey(langName);
                if (langKey is null) continue;
                foreach (var profileName in langKey.GetSubKeyNames())
                {
                    if (!Guid.TryParse(profileName, out var profile)) continue;
                    if (seen.Add((langid, clsid, profile)))
                        yield return (langid, clsid, profile);
                }
            }
        }
    }

    public static string GetProfileDescription(Guid clsid, short langid, Guid guidProfile)
    {
        // Guid 的 "B" 格式自带花括号
        string regPath = $@"{TsfTipRegPath}\{clsid:B}\LanguageProfile\0x{langid:x8}\{guidProfile:B}";
        using var key = Registry.LocalMachine.OpenSubKey(regPath);
        return key?.GetValue("Description") as string ?? string.Empty;
    }

    public static string GetFriendlyName(in TF_INPUTPROCESSORPROFILE p)
    {
        foreach (var (clsid, profile, name) in KnownProfiles)
        {
            if (p.clsid == clsid && (profile == Guid.Empty || p.guidProfile == profile))
                return name;
        }
        if (p.guidProfile == Guid.Empty && p.langid != 0 && (p.hkl.ToInt64() & 0xFFFF) == 0x0409)
            return "English (US) Keyboard";
        return string.Empty;
    }

    public static List<TF_INPUTPROCESSORPROFILE> EnumProfiles(ITfInputProcessorProfileMgr mgr, short langid)
    {
        var result = new List<TF_INPUTPROCESSORPROFILE>();
        int hr = mgr.EnumProfiles(langid, out IntPtr enumPtr);
        if (hr != 0 || enumPtr == IntPtr.Zero)
        {
            Console.WriteLine($"  EnumProfiles(0x{langid:x4}) 失败 HRESULT=0x{hr:x8}");
            return result;
        }
        try
        {
            var en = (IEnumTfInputProcessorProfiles)Marshal.GetObjectForIUnknown(enumPtr);
            try
            {
                while (true)
                {
                    var p = new TF_INPUTPROCESSORPROFILE { dwSize = Marshal.SizeOf<TF_INPUTPROCESSORPROFILE>() };
                    hr = en.Next(1, out p, out int fetched);
                    if (hr != 0 && hr != 1) // S_FALSE=1 表示到头
                    {
                        Console.WriteLine($"  IEnum.Next 失败 HRESULT=0x{hr:x8}");
                        break;
                    }
                    if (fetched == 0) break;
                    result.Add(p);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(en);
            }
        }
        finally
        {
            Marshal.Release(enumPtr);
        }
        return result;
    }

    public static void PrintProfile(in TF_INPUTPROCESSORPROFILE p, string indent = "  ")
    {
        string desc = GetProfileDescription(p.clsid, p.langid, p.guidProfile);
        string friendly = GetFriendlyName(p);
        Console.WriteLine($"{indent}langid=0x{p.langid:x4} clsid={p.clsid:B} guidProfile={p.guidProfile:B}");
        Console.WriteLine($"{indent}catid={p.catid:B} hklSubstitute=0x{p.hklSubstitute.ToInt64():x8} hkl=0x{p.hkl.ToInt64():x8} dwCaps=0x{p.dwCaps:x8} dwFlags=0x{p.dwFlags:x8}");
        if (!string.IsNullOrEmpty(desc) || !string.IsNullOrEmpty(friendly))
            Console.WriteLine($"{indent}描述={desc}{(friendly.Length > 0 ? $"  友好名={friendly}" : string.Empty)}");
    }

    public static string HresultToString(int hr) => $"0x{hr:x8} ({(hr == 0 ? "S_OK" : hr == 1 ? "S_FALSE" : hr < 0 ? "失败" : "非零成功")})";
}
