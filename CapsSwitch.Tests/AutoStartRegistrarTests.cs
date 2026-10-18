using CapsSwitch.App.Config;

namespace CapsSwitch.Tests;

public class AutoStartRegistrarTests
{
    [Theory]
    [InlineData(@"C:\Tools\CapsSwitch.App.exe")]
    [InlineData(@"C:\Program Files\My Apps\CapsSwitch.App.exe")] // 含空格
    public void QuoteForRunKey_总是带引号(string path)
    {
        Assert.Equal($"\"{path}\"", AutoStartRegistrar.QuoteForRunKey(path));
    }

    [Fact]
    public void QuoteForRunKey_结果可被原始路径还原()
    {
        const string path = @"C:\Tools\CapsSwitch.App.exe";
        string quoted = AutoStartRegistrar.QuoteForRunKey(path);
        Assert.Equal(path, quoted.Trim('"'));
    }
}
