using System.Globalization;

namespace CapsSwitch.App.InputSources;

/// <summary>
/// 配置 Id 归一化(纯函数,可单测)。
/// 场景:冷启动 TSF 枚举丢失拼音 TIP 时,配置可能写入 "hkl:08040804" 这类键盘布局 Id;
/// TIP 恢复后该 Id 悬空(不等于拼音 guid)。本解析器在精确匹配失败后按 HKL 等价匹配,
/// 命中则归一化为源的真实 Id。
/// </summary>
public static class SourceIdResolver
{
    public const string HklIdPrefix = "hkl:";

    /// <summary>解析 "hkl:xxxxxxxx" 形式的 Id 为 HKL 值(低 32 位)。</summary>
    public static bool TryParseHklId(string id, out long hkl)
    {
        hkl = 0;
        if (!id.StartsWith(HklIdPrefix, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!long.TryParse(id.AsSpan(HklIdPrefix.Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long value))
            return false;
        if (value < 0 || value > 0xFFFFFFFF)
            return false;
        hkl = value;
        return true;
    }

    /// <summary>
    /// 把配置 Id 归一化为当前源列表中的真实 Id:
    /// 精确匹配 → 原样;否则按 HKL 等价匹配(多命中优先 TIP);都不命中 → null。
    /// </summary>
    public static string? Normalize(string? configuredId, IReadOnlyList<InputSourceInfo> sources)
    {
        if (configuredId is null)
            return null;

        for (int i = 0; i < sources.Count; i++)
            if (string.Equals(sources[i].Id, configuredId, StringComparison.OrdinalIgnoreCase))
                return configuredId;

        if (TryParseHklId(configuredId, out long hkl))
        {
            InputSourceInfo? fallback = null;
            foreach (var s in sources)
            {
                if (s.Hkl.ToInt64() != hkl)
                    continue;
                if (s.IsTsfTip)
                    return s.Id;
                fallback ??= s;
            }
            return fallback?.Id;
        }

        return null;
    }
}
