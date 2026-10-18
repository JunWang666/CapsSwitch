using System.Globalization;

namespace CapsSwitch.App.InputSources;

/// <summary>
/// 一个可切换的输入源。TIP 的 Id 用 guidProfile 字符串;键盘布局用 "hkl:00000409" 形式。
/// </summary>
public sealed record InputSourceInfo
{
    public required string Id { get; init; }
    public required int LangId { get; init; }
    public Guid Clsid { get; init; }
    public Guid ProfileGuid { get; init; }
    public IntPtr Hkl { get; init; }
    public required string DisplayName { get; init; }
    public required string ShortLabel { get; init; }
    public required bool IsTsfTip { get; init; }

    public static string ShortLabelFor(int langId) => langId switch
    {
        0x0804 => "中",
        0x0409 => "EN",
        0x0411 => "あ",
        _ => ShortLabelFromCulture(langId),
    };

    private static string ShortLabelFromCulture(int langId)
    {
        try
        {
            return CultureInfo.GetCultureInfo(langId).TwoLetterISOLanguageName.ToUpperInvariant();
        }
        catch (CultureNotFoundException)
        {
            return $"X{langId & 0xFFFF:x4}";
        }
    }
}
