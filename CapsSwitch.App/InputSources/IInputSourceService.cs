using CapsSwitch.App.Interop;

namespace CapsSwitch.App.InputSources;

public interface IInputSourceService
{
    /// <summary>枚举当前系统已启用、可切换的输入源。</summary>
    IReadOnlyList<InputSourceInfo> GetSources();

    /// <summary>前台窗口线程当前使用的输入源;无法匹配时返回 null。</summary>
    InputSourceInfo? GetCurrent();

    /// <summary>切换到目标输入源并读回验证;绝不把"消息已投递"当成功。</summary>
    bool SwitchTo(InputSourceInfo target);

    /// <summary>已执行的切换次数(递增计数,兼作版本号)。</summary>
    long SwitchCount { get; }
}
