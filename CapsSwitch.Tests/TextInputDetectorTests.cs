using CapsSwitch.App.Focus;

namespace CapsSwitch.Tests;

public class TextInputDetectorTests
{
    private const string ChineseTipId = "fa550b04-5ad7-411f-a5ac-ca038ec515d7";

    [Fact]
    public void 普通Edit_可输入()
    {
        Assert.True(TextInputDetector.IsTextInput(new(UiaControlTypeIds.Edit, false, null)));
    }

    [Fact]
    public void 只读Edit_不可输入()
    {
        Assert.False(TextInputDetector.IsTextInput(new(UiaControlTypeIds.Edit, false, true)));
    }

    [Fact]
    public void 可编辑Document_有TextPattern非只读()
    {
        Assert.True(TextInputDetector.IsTextInput(new(UiaControlTypeIds.Document, true, false)));
    }

    [Fact]
    public void 只读Document_不可输入()
    {
        Assert.False(TextInputDetector.IsTextInput(new(UiaControlTypeIds.Document, true, true)));
    }

    [Fact]
    public void 无TextPattern的Document_不可输入()
    {
        Assert.False(TextInputDetector.IsTextInput(new(UiaControlTypeIds.Document, false, false)));
    }

    [Fact]
    public void 未知只读状态的Document_宽进视为可输入()
    {
        Assert.True(TextInputDetector.IsTextInput(new(UiaControlTypeIds.Document, true, null)));
    }

    [Theory]
    [InlineData(UiaControlTypeIds.Button)]
    [InlineData(UiaControlTypeIds.MenuItem)]
    [InlineData(UiaControlTypeIds.List)]
    [InlineData(UiaControlTypeIds.TreeItem)]
    [InlineData(UiaControlTypeIds.Tab)]
    [InlineData(UiaControlTypeIds.CheckBox)]
    [InlineData(UiaControlTypeIds.ComboBox)]
    [InlineData(UiaControlTypeIds.Text)]
    public void 排除类控件_即使带TextPattern也排除(int controlTypeId)
    {
        Assert.False(TextInputDetector.IsTextInput(new(controlTypeId, true, false)));
    }

    [Fact]
    public void 带TextPattern的Custom_可输入()
    {
        Assert.True(TextInputDetector.IsTextInput(new(UiaControlTypeIds.Custom, true, null)));
    }

    [Fact]
    public void 无TextPattern的Pane_不可输入()
    {
        Assert.False(TextInputDetector.IsTextInput(new(UiaControlTypeIds.Pane, false, null)));
    }
}
