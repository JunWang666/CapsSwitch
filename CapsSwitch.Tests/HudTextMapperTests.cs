using CapsSwitch.App.InputSources;
using CapsSwitch.App.Overlay;

namespace CapsSwitch.Tests;

public class HudTextMapperTests
{
    private const string ChineseTipId = "fa550b04-5ad7-411f-a5ac-ca038ec515d7";

    private static InputSourceInfo Source(string id, int langId) => new()
    {
        Id = id,
        LangId = langId,
        Hkl = new IntPtr(langId << 16 | (langId & 0xFFFF)),
        DisplayName = id,
        ShortLabel = InputSourceInfo.ShortLabelFor(langId),
        IsTsfTip = !id.StartsWith("hkl:", StringComparison.Ordinal),
    };

    [Fact]
    public void 中文TIP中文模式_中()
    {
        Assert.Equal("中", HudTextMapper.Map(Source(ChineseTipId, 0x0804), true, ChineseTipId));
    }

    [Fact]
    public void 中文TIP英文模式_A()
    {
        Assert.Equal("A", HudTextMapper.Map(Source(ChineseTipId, 0x0804), false, ChineseTipId));
    }

    [Fact]
    public void 英语键盘_EN()
    {
        Assert.Equal("EN", HudTextMapper.Map(Source("hkl:04090409", 0x0409), false, ChineseTipId));
    }

    [Fact]
    public void 日语IME_平假名()
    {
        Assert.Equal("あ", HudTextMapper.Map(Source("a76c93d9-5523-4e90-aafa-4db112f9ac76", 0x0411), true, ChineseTipId));
    }

    [Fact]
    public void 其他语言_语言代码大写()
    {
        Assert.Equal("KO", HudTextMapper.Map(Source("some-tip", 0x0412), true, ChineseTipId));
    }

    [Fact]
    public void 未知源_不显示()
    {
        Assert.Null(HudTextMapper.Map(null, false, ChineseTipId));
    }

    [Fact]
    public void 非首选中文源_按ShortLabel显示中()
    {
        Assert.Equal("中", HudTextMapper.Map(Source("other-zh-tip", 0x0804), true, ChineseTipId));
    }
}
