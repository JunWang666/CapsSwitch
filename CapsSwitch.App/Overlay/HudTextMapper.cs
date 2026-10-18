using CapsSwitch.App.InputSources;

namespace CapsSwitch.App.Overlay;

/// <summary>
/// (当前源, IME NATIVE 位) → HUD 显示文本(纯函数,可单测)。
/// 首选中文 TIP:中文模式→"中",英文模式→"A";英语键盘→"EN";日语→"あ";
/// 其他→语言代码大写;源未知→null(不显示)。
/// </summary>
public static class HudTextMapper
{
    public static string? Map(InputSourceInfo? source, bool nativeMode, string? chineseTipId)
    {
        if (source is null)
            return null;

        if (chineseTipId is not null && source.Id == chineseTipId)
            return nativeMode ? "中" : "A";

        return (source.LangId & 0xFFFF) switch
        {
            0x0409 => "EN",
            0x411 => "あ",
            var x => InputSourceInfo.ShortLabelFor(x),
        };
    }
}
