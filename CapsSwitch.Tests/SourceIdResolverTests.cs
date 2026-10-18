using CapsSwitch.App.Config;
using CapsSwitch.App.InputSources;

namespace CapsSwitch.Tests;

public class SourceIdResolverTests
{
    private static readonly Guid PinyinGuid = new("FA550B04-5AD7-411F-A5AC-CA038EC515D7");

    private static InputSourceInfo Tip(string id, int langId, long hkl) => new()
    {
        Id = id,
        LangId = langId,
        ProfileGuid = id == PinyinGuid.ToString() ? PinyinGuid : Guid.NewGuid(),
        Hkl = new IntPtr(hkl),
        DisplayName = id,
        ShortLabel = "中",
        IsTsfTip = true,
    };

    private static InputSourceInfo Layout(string id, int langId, long hkl) => new()
    {
        Id = id,
        LangId = langId,
        Hkl = new IntPtr(hkl),
        DisplayName = id,
        ShortLabel = "EN",
        IsTsfTip = false,
    };

    private static readonly IReadOnlyList<InputSourceInfo> Sources =
    [
        Tip(PinyinGuid.ToString(), 0x0804, 0x08040804),
        Layout("hkl:04090409", 0x0409, 0x04090409),
    ];

    [Fact]
    public void 精确匹配_原样返回()
    {
        Assert.Equal(PinyinGuid.ToString(), SourceIdResolver.Normalize(PinyinGuid.ToString(), Sources));
        Assert.Equal("hkl:04090409", SourceIdResolver.Normalize("hkl:04090409", Sources));
    }

    [Fact]
    public void 悬空的hkl配置Id_按HKL等价命中拼音TIP并归一化()
    {
        // 污染场景:配置写的是 hkl:08040804(伪布局),恢复后同 HKL 的是拼音 TIP
        Assert.Equal(PinyinGuid.ToString(), SourceIdResolver.Normalize("hkl:08040804", Sources));
    }

    [Fact]
    public void 不存在的HKL_返回null走回退()
    {
        Assert.Null(SourceIdResolver.Normalize("hkl:0abc0abc", Sources));
    }

    [Fact]
    public void 非hkl形式的未知Id_返回null()
    {
        Assert.Null(SourceIdResolver.Normalize("deadbeef-0000-0000-0000-000000000000", Sources));
    }

    [Fact]
    public void null输入_返回null()
    {
        Assert.Null(SourceIdResolver.Normalize(null, Sources));
    }

    [Fact]
    public void 同HKL多源_优先TIP()
    {
        // 真实场景:伪布局被 SourceListBuilder 丢弃,但保险起见——同 HKL 有 TIP 和布局时优先 TIP
        var mixed = new List<InputSourceInfo>(Sources)
        {
            Layout("custom-zh-layout", 0x0804, 0x08040804), // 同 HKL,不同 Id
        };
        Assert.Equal(PinyinGuid.ToString(), SourceIdResolver.Normalize("hkl:08040804", mixed));
        // 精确匹配永远优先于 HKL 匹配
        Assert.Equal("custom-zh-layout", SourceIdResolver.Normalize("custom-zh-layout", mixed));
    }

    [Theory]
    [InlineData("hkl:08040804", true)]
    [InlineData("hkl:ZZZZZZZZ", false)]
    [InlineData("hkl:", false)]
    [InlineData("tip:08040804", false)]
    public void TryParseHklId_格式校验(string id, bool expected)
    {
        Assert.Equal(expected, SourceIdResolver.TryParseHklId(id, out _));
    }

    [Fact]
    public void TryParseHklId_值正确()
    {
        Assert.True(SourceIdResolver.TryParseHklId("hkl:08040804", out long hkl));
        Assert.Equal(0x08040804, hkl);
    }
}

public class SettingsNormalizationTests
{
    private static readonly Guid PinyinGuid = new("FA550B04-5AD7-411F-A5AC-CA038EC515D7");

    private static readonly IReadOnlyList<InputSourceInfo> Sources =
    [
        new InputSourceInfo
        {
            Id = PinyinGuid.ToString(), LangId = 0x0804, ProfileGuid = PinyinGuid,
            Hkl = new IntPtr(0x08040804), DisplayName = "Microsoft Pinyin", ShortLabel = "中", IsTsfTip = true,
        },
        new InputSourceInfo
        {
            Id = "hkl:04090409", LangId = 0x0409, Hkl = new IntPtr(0x04090409),
            DisplayName = "English (US) Keyboard", ShortLabel = "EN", IsTsfTip = false,
        },
    ];

    [Fact]
    public void 污染配置_归一化修复并报告变化()
    {
        var store = new SettingsStore(_ => { });
        var settings = new AppSettings
        {
            // 冷启动 TIP 丢失期的污染形态
            PreferredChineseId = "hkl:08040804",
            OrderedSourceIds = ["hkl:08040804", "ghost-id", "hkl:04090409"],
        };

        bool changed = store.NormalizeInPlace(settings, Sources);

        Assert.True(changed);
        Assert.Equal(PinyinGuid.ToString(), settings.PreferredChineseId); // hkl:08040804 → 拼音 TIP
        Assert.Equal("hkl:04090409", settings.PreferredEnglishId);        // 回退检测命中 EN
        Assert.Equal([PinyinGuid.ToString(), "hkl:04090409"], settings.OrderedSourceIds); // ghost-id 剔除
    }

    [Fact]
    public void 归一化幂等_第二次调用无变化()
    {
        var store = new SettingsStore(_ => { });
        var settings = new AppSettings
        {
            PreferredChineseId = "hkl:08040804",
            OrderedSourceIds = ["hkl:08040804"],
        };
        Assert.True(store.NormalizeInPlace(settings, Sources));
        Assert.False(store.NormalizeInPlace(settings, Sources));
    }

    [Fact]
    public void 干净配置_归一化无变化()
    {
        var store = new SettingsStore(_ => { });
        var settings = new AppSettings
        {
            PreferredChineseId = PinyinGuid.ToString(),
            PreferredEnglishId = "hkl:04090409",
            OrderedSourceIds = [PinyinGuid.ToString(), "hkl:04090409"],
        };
        Assert.False(store.NormalizeInPlace(settings, Sources));
    }
}
