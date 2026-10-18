using CapsSwitch.App.Interop;
using CapsSwitch.App.InputSources;

namespace CapsSwitch.Tests;

public class SourceListBuilderTests
{
    private static readonly Guid KeyboardCatid = new("34745C63-B2F0-4784-8B67-5E12C8701A31");
    private static readonly Guid PinyinClsid = new("81D4E9C9-1D3B-41BC-9E6C-4B40BF79E35E");
    private static readonly Guid PinyinProfile = new("FA550B04-5AD7-411F-A5AC-CA038EC515D7");
    private static readonly Guid OtherClsid = new("03B5835F-F03C-411B-9CE2-AA23E1171E36");

    private static readonly IntPtr[] Loaded =
        [new IntPtr(0x08040804), new IntPtr(0x04090409), new IntPtr(0x04110411)];

    private static TF_INPUTPROCESSORPROFILE Row(
        short langid, Guid clsid, Guid profile, IntPtr hkl, int dwFlags, Guid? catid = null) => new()
    {
        dwSize = 88,
        langid = langid,
        clsid = clsid,
        guidProfile = profile,
        catid = catid ?? KeyboardCatid,
        hklSubstitute = IntPtr.Zero,
        dwCaps = 0,
        hkl = hkl,
        dwFlags = dwFlags,
    };

    private static TF_INPUTPROCESSORPROFILE PinyinTipRow(int dwFlags = 0x3) =>
        Row(0x0804, PinyinClsid, PinyinProfile, IntPtr.Zero, dwFlags);

    private static TF_INPUTPROCESSORPROFILE ZhLayoutRow() =>
        Row(0x0804, Guid.Empty, Guid.Empty, new IntPtr(0x08040804), 0x0);

    private static TF_INPUTPROCESSORPROFILE EnLayoutRow() =>
        Row(0x0409, Guid.Empty, Guid.Empty, new IntPtr(0x04090409), 0x2);

    [Fact]
    public void 同langid同hkl的伪布局被已启用TIP覆盖_只剩TIP()
    {
        var logs = new List<string>();
        var result = SourceListBuilder.Build([ZhLayoutRow(), PinyinTipRow()], Loaded, logs.Add);
        var single = Assert.Single(result);
        Assert.True(single.IsTsfTip);
        Assert.Equal(PinyinProfile.ToString(), single.Id);
        Assert.Contains(logs, l => l.Contains("丢弃被 TIP 覆盖"));
    }

    [Fact]
    public void 不同hkl的布局与TIP都保留()
    {
        var result = SourceListBuilder.Build([ZhLayoutRow(), PinyinTipRow(), EnLayoutRow()], Loaded);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.IsTsfTip && s.Id == PinyinProfile.ToString());
        Assert.Contains(result, s => !s.IsTsfTip && s.Id == "hkl:04090409");
    }

    [Fact]
    public void 不同langid即使hkl相同也不覆盖()
    {
        // TIP langid=0x0411 解析到 0x04110411;伪布局 langid=0x0804 hkl=0x08040804 → 互不覆盖是另一用例,
        // 这里验证 hkl 相同但 langid 不同的伪布局不被丢弃(构造同 hkl 不同 langid)
        var pseudo = Row(0x0409, Guid.Empty, Guid.Empty, new IntPtr(0x08040804), 0x0);
        var tip = PinyinTipRow();
        var result = SourceListBuilder.Build([pseudo, tip], Loaded);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void 未启用TIP被过滤并记录日志()
    {
        var logs = new List<string>();
        var result = SourceListBuilder.Build([PinyinTipRow(dwFlags: 0x0)], Loaded, logs.Add);
        Assert.Empty(result);
        Assert.Contains(logs, l => l.Contains("跳过未启用 TIP") && l.Contains("dwFlags=0x00000000"));
    }

    [Fact]
    public void 无已加载匹配布局的TIP被跳过并记录日志()
    {
        var logs = new List<string>();
        var tipNoLayout = Row(0x045e, OtherClsid, Guid.NewGuid(), IntPtr.Zero, 0x1);
        var result = SourceListBuilder.Build([tipNoLayout], Loaded, logs.Add);
        Assert.Empty(result);
        Assert.Contains(logs, l => l.Contains("无已加载的匹配键盘布局"));
    }

    [Fact]
    public void 非键盘类catid被忽略()
    {
        var row = Row(0x0804, PinyinClsid, PinyinProfile, IntPtr.Zero, 0x3, catid: Guid.NewGuid());
        Assert.Empty(SourceListBuilder.Build([row], Loaded));
    }

    [Fact]
    public void 同Id去重()
    {
        var result = SourceListBuilder.Build([PinyinTipRow(), PinyinTipRow()], Loaded);
        Assert.Single(result);
    }

    [Fact]
    public void ShortLabel映射()
    {
        var jaProfile = Guid.NewGuid();
        var result = SourceListBuilder.Build(
            [PinyinTipRow(), EnLayoutRow(), Row(0x0411, OtherClsid, jaProfile, IntPtr.Zero, 0x1)], Loaded);
        Assert.Equal("中", result.Single(s => s.Id == PinyinProfile.ToString()).ShortLabel);
        Assert.Equal("EN", result.Single(s => s.Id == "hkl:04090409").ShortLabel);
        Assert.Equal("あ", result.Single(s => s.Id == jaProfile.ToString()).ShortLabel);
    }

    [Fact]
    public void 本机期望列表_拼音加EN恰好两个源()
    {
        var result = SourceListBuilder.Build([ZhLayoutRow(), PinyinTipRow(), EnLayoutRow()], Loaded);
        Assert.Equal(2, result.Count);
        Assert.Equal(PinyinProfile.ToString(), result[0].Id);
        Assert.Equal("hkl:04090409", result[1].Id);
    }
}
