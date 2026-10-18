using CapsSwitch.App.InputSources;

namespace CapsSwitch.Tests;

public class PreferredPairCycleStrategyTests
{
    private static readonly Guid PinyinGuid = new("FA550B04-5AD7-411F-A5AC-CA038EC515D7");

    private static InputSourceInfo Source(string id, int langId, string label, string name) => new()
    {
        Id = id,
        LangId = langId,
        ProfileGuid = id == PinyinGuid.ToString() ? PinyinGuid : Guid.Empty,
        Hkl = new IntPtr(langId << 16 | (langId & 0xFFFF)),
        DisplayName = name,
        ShortLabel = label,
        IsTsfTip = id != "hkl:00000409",
    };

    private static readonly InputSourceInfo Zh = Source(PinyinGuid.ToString(), 0x0804, "中", "微软拼音");
    private static readonly InputSourceInfo En = Source("hkl:00000409", 0x0409, "EN", "US Keyboard");
    private static readonly InputSourceInfo Ja = Source("a76c93d9-5523-4e90-aafa-4db112f9ac76", 0x0411, "あ", "Microsoft IME");

    private static SwitchConfig Config(params string[] ordered) => new()
    {
        PreferredChineseId = Zh.Id,
        PreferredEnglishId = En.Id,
        OrderedSourceIds = [.. ordered],
    };

    private readonly PreferredPairCycleStrategy _strategy = new();

    [Fact]
    public void 当前是首选中文_切到首选英文()
    {
        var target = _strategy.GetTarget(Zh, 0, [Zh, En], Config(Zh.Id, En.Id));
        Assert.Equal(En.Id, target?.Id);
    }

    [Fact]
    public void 当前是首选英文_切到首选中文()
    {
        var target = _strategy.GetTarget(En, 0, [Zh, En], Config(Zh.Id, En.Id));
        Assert.Equal(Zh.Id, target?.Id);
    }

    [Fact]
    public void 当前为其他源_回默认首选中文()
    {
        var target = _strategy.GetTarget(Ja, 0, [Zh, En, Ja], Config(Zh.Id, En.Id, Ja.Id));
        Assert.Equal(Zh.Id, target?.Id);
    }

    [Fact]
    public void 当前未知_回默认首选中文()
    {
        var target = _strategy.GetTarget(null, 0, [Zh, En], Config(Zh.Id, En.Id));
        Assert.Equal(Zh.Id, target?.Id);
    }

    [Fact]
    public void 三源循环_中英日依次轮换()
    {
        var sources = new List<InputSourceInfo> { Zh, En, Ja };
        var config = Config(Zh.Id, En.Id, Ja.Id);
        Assert.Equal(En.Id, _strategy.GetTarget(Zh, 0, sources, config)?.Id);
        Assert.Equal(Ja.Id, _strategy.GetTarget(null, 1, sources, config)?.Id);
        Assert.Equal(Zh.Id, _strategy.GetTarget(null, 2, sources, config)?.Id);
        Assert.Equal(En.Id, _strategy.GetTarget(null, 3, sources, config)?.Id);
    }

    [Fact]
    public void 双源列表自然循环()
    {
        var sources = new List<InputSourceInfo> { Zh, En };
        var config = Config(Zh.Id, En.Id);
        Assert.Equal(En.Id, _strategy.GetTarget(Zh, 0, sources, config)?.Id);
        Assert.Equal(Zh.Id, _strategy.GetTarget(null, 1, sources, config)?.Id);
        Assert.Equal(En.Id, _strategy.GetTarget(null, 2, sources, config)?.Id);
    }

    [Fact]
    public void 首选英文被卸载_从中文出发安全跳过()
    {
        var sources = new List<InputSourceInfo> { Zh, Ja };
        var config = Config(Zh.Id, En.Id, Ja.Id); // en 仍在配置里但已不在 sources
        var target = _strategy.GetTarget(Zh, 0, sources, config);
        Assert.NotNull(target);
        Assert.NotEqual(En.Id, target?.Id);
    }

    [Fact]
    public void 配置顺序中的源被卸载_循环时跳过()
    {
        var sources = new List<InputSourceInfo> { Zh, Ja };
        var config = Config(Zh.Id, En.Id, Ja.Id);
        Assert.Equal(Ja.Id, _strategy.GetTarget(Zh, 0, sources, config)?.Id); // zh → (en 跳过) → ja
        Assert.Equal(Zh.Id, _strategy.GetTarget(null, 1, sources, config)?.Id); // ja → 回绕 → zh
    }

    [Fact]
    public void 全部配置源被卸载_回退到可用源不抛异常()
    {
        var sources = new List<InputSourceInfo> { Ja };
        var config = Config(Zh.Id, En.Id);
        var target = _strategy.GetTarget(Zh, 0, sources, config);
        Assert.NotNull(target);
    }

    [Fact]
    public void 空源列表_返回null()
    {
        Assert.Null(_strategy.GetTarget(Zh, 0, [], Config(Zh.Id, En.Id)));
    }

    [Fact]
    public void 连按时索引0仍按首选对互切()
    {
        var sources = new List<InputSourceInfo> { Zh, En, Ja };
        var config = Config(Zh.Id, En.Id, Ja.Id);
        // 模拟:当前中,连按两次(0 和 1)
        Assert.Equal(En.Id, _strategy.GetTarget(Zh, 0, sources, config)?.Id);
        Assert.Equal(Ja.Id, _strategy.GetTarget(null, 1, sources, config)?.Id);
        // 新一轮从当前(日)按 0:当前不是首选 → 回中文
        var s2 = new PreferredPairCycleStrategy();
        Assert.Equal(Zh.Id, s2.GetTarget(Ja, 0, sources, config)?.Id);
    }
}
