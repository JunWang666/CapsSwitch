namespace CapsSwitch.App.InputSources;

/// <summary>
/// 切换结果语义:HKL 读回验证成功即整体成功;中文模式强制失败只降为警告。
/// </summary>
public sealed record SwitchResult(bool HklVerified, bool? NativeEnforced)
{
    public bool Success => HklVerified;

    public string Describe(string targetName) => (HklVerified, NativeEnforced) switch
    {
        (true, false) => $"切换→{targetName}: 成功;警告: 中文模式强制未确认",
        (true, _) => $"切换→{targetName}: 成功",
        (false, _) => $"切换→{targetName}: 失败",
    };
}
