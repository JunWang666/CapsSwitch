using CapsSwitch.App.Interop;

namespace CapsSwitch.App.InputSources;

/// <summary>
/// 把 TSF profile 行 + 已加载 HKL 列表合并成输入源列表的纯函数(可单测)。
/// 规则:
///  1. 只保留键盘类(catid=GUID_TFCAT_TIP_KEYBOARD)profile;
///  2. TIP 需已启用(dwFlags & TF_PROFILEFLAG_ENABLED),且能解析出已加载键盘布局;
///  3. 键盘布局伪 profile 的 HKL 与已启用 TIP 相同且 langid 相同时丢弃(TIP 优先,
///     避免同 HKL 伪布局成为连按循环里的假源,也保证强制中文模式逻辑生效);
///  4. 按 Id 去重。
/// </summary>
public static class SourceListBuilder
{
    public static List<InputSourceInfo> Build(
        IReadOnlyList<TF_INPUTPROCESSORPROFILE> rows,
        IntPtr[] loadedLayouts,
        Action<string>? log = null)
    {
        var result = new List<InputSourceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // EnumProfiles 在不同 langid 下会重复返回同一 profile,先按 (clsid, guidProfile) / hkl 去重
        var seenTipRows = new HashSet<(Guid, Guid)>();
        var seenLayoutRows = new HashSet<long>();

        // 先解析已启用 TIP 的 HKL,用于伪布局去重
        var enabledTips = new List<(TF_INPUTPROCESSORPROFILE row, IntPtr hkl)>();
        foreach (var row in rows)
        {
            if (row.catid != TsfInterop.GUID_TFCAT_TIP_KEYBOARD)
                continue;
            if (row.clsid == Guid.Empty)
                continue; // 键盘布局伪 profile 第二轮处理
            if (!seenTipRows.Add((row.clsid, row.guidProfile)))
                continue; // 重复行
            if ((row.dwFlags & 0x1) == 0)
            {
                log?.Invoke($"[SourceListBuilder] 跳过未启用 TIP {row.guidProfile:B} (langid=0x{row.langid & 0xFFFF:x4}, dwFlags=0x{row.dwFlags:x8})");
                continue;
            }
            var matches = loadedLayouts.Where(l => (int)(l.ToInt64() & 0xFFFF) == (row.langid & 0xFFFF)).ToList();
            if (matches.Count == 0)
            {
                log?.Invoke($"[SourceListBuilder] 跳过 TIP {row.guidProfile:B} (langid=0x{row.langid & 0xFFFF:x4}, dwFlags=0x{row.dwFlags:x8}): 无已加载的匹配键盘布局");
                continue;
            }
            if (matches.Count > 1)
                log?.Invoke($"[SourceListBuilder] TIP {row.guidProfile:B} 匹配到 {matches.Count} 个键盘布局,取第一个 0x{matches[0].ToInt64():x8}");
            enabledTips.Add((row, matches[0]));
        }

        foreach (var (row, hkl) in enabledTips)
            Add(result, seen, row, hkl, isTsfTip: true, log);

        foreach (var row in rows)
        {
            if (row.catid != TsfInterop.GUID_TFCAT_TIP_KEYBOARD)
                continue;
            if (row.clsid != Guid.Empty)
                continue; // TIP 已处理
            if (row.hkl == IntPtr.Zero)
                continue;
            if (!seenLayoutRows.Add(row.hkl.ToInt64()))
                continue; // 重复行
            // 同 HKL 且同 langid 的伪布局被已启用 TIP 覆盖 → 丢弃
            bool shadowed = enabledTips.Any(t =>
                t.hkl == row.hkl && (t.row.langid & 0xFFFF) == (row.langid & 0xFFFF));
            if (shadowed)
            {
                log?.Invoke($"[SourceListBuilder] 丢弃被 TIP 覆盖的键盘布局伪项 langid=0x{row.langid & 0xFFFF:x4} hkl=0x{row.hkl.ToInt64():x8}");
                continue;
            }
            Add(result, seen, row, row.hkl, isTsfTip: false, log);
        }

        return result;
    }

    private static void Add(
        List<InputSourceInfo> result, HashSet<string> seen,
        TF_INPUTPROCESSORPROFILE row, IntPtr hkl, bool isTsfTip, Action<string>? log)
    {
        int langId = row.langid & 0xFFFF;
        if (langId == 0)
            langId = (int)(hkl.ToInt64() & 0xFFFF);

        string id = isTsfTip ? row.guidProfile.ToString() : $"hkl:{hkl.ToInt64() & 0xFFFFFFFF:x8}";
        if (!seen.Add(id))
        {
            log?.Invoke($"[SourceListBuilder] 去重丢弃重复 Id={id}");
            return;
        }

        string description = TsfInterop.GetProfileDescription(row.clsid, row.langid, row.guidProfile);
        string friendly = TsfInterop.GetFriendlyName(row);
        string displayName = !string.IsNullOrEmpty(description) ? description
            : !string.IsNullOrEmpty(friendly) ? friendly
            : isTsfTip ? row.guidProfile.ToString() : $"键盘布局 0x{hkl.ToInt64():x8}";

        result.Add(new InputSourceInfo
        {
            Id = id,
            LangId = langId,
            Clsid = row.clsid,
            ProfileGuid = row.guidProfile,
            Hkl = hkl,
            DisplayName = displayName,
            ShortLabel = InputSourceInfo.ShortLabelFor(langId),
            IsTsfTip = isTsfTip,
        });
    }
}
