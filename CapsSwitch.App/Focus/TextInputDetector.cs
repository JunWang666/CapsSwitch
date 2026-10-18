namespace CapsSwitch.App.Focus;

/// <summary>文本输入控件的简单描述(由 UIA 查询填充,判定逻辑保持纯函数可单测)。</summary>
public readonly record struct TextControlDescription
{
    public int ControlTypeId { get; init; }
    public bool HasTextPattern { get; init; }
    /// <summary>true=只读;null=未知(宽进)。</summary>
    public bool? IsReadOnly { get; init; }

    public TextControlDescription(int controlTypeId, bool hasTextPattern, bool? isReadOnly)
    {
        ControlTypeId = controlTypeId;
        HasTextPattern = hasTextPattern;
        IsReadOnly = isReadOnly;
    }
}

/// <summary>
/// ControlTypeId 常量(UIA,摘自 uiautomationclient.h)。
/// </summary>
public static class UiaControlTypeIds
{
    public const int Button = 50000;
    public const int Calendar = 50001;
    public const int CheckBox = 50002;
    public const int ComboBox = 50003;
    public const int Edit = 50004;
    public const int Hyperlink = 50005;
    public const int ListItem = 50007;
    public const int List = 50008;
    public const int Menu = 50009;
    public const int MenuBar = 50010;
    public const int MenuItem = 50011;
    public const int RadioButton = 50013;
    public const int ScrollBar = 50014;
    public const int Slider = 50015;
    public const int Spinner = 50016;
    public const int Tab = 50018;
    public const int TabItem = 50019;
    public const int Text = 50020;
    public const int ToolBar = 50021;
    public const int Tree = 50023;
    public const int TreeItem = 50024;
    public const int Custom = 50025;
    public const int Group = 50026;
    public const int DataGrid = 50028;
    public const int DataItem = 50029;
    public const int Document = 50030;
    public const int SplitButton = 50031;
    public const int Pane = 50033;
}

/// <summary>
/// 判定焦点元素是否"可编辑文本输入控件"(纯逻辑)。
/// 宽进:只读状态取不到时视为可编辑(显示比漏显示好);
/// 按钮/菜单/列表/树/选项卡等即使带 TextPattern 也直接排除。
/// </summary>
public static class TextInputDetector
{
    private static readonly HashSet<int> AlwaysExclude = new()
    {
        UiaControlTypeIds.Button, UiaControlTypeIds.CheckBox, UiaControlTypeIds.ComboBox,
        UiaControlTypeIds.ListItem, UiaControlTypeIds.List, UiaControlTypeIds.Menu,
        UiaControlTypeIds.MenuBar, UiaControlTypeIds.MenuItem, UiaControlTypeIds.RadioButton,
        UiaControlTypeIds.ScrollBar, UiaControlTypeIds.Slider, UiaControlTypeIds.Spinner,
        UiaControlTypeIds.Tab, UiaControlTypeIds.TabItem, UiaControlTypeIds.Text,
        UiaControlTypeIds.ToolBar, UiaControlTypeIds.Tree, UiaControlTypeIds.TreeItem,
        UiaControlTypeIds.DataGrid, UiaControlTypeIds.DataItem, UiaControlTypeIds.SplitButton,
        UiaControlTypeIds.Hyperlink, UiaControlTypeIds.Calendar,
    };

    public static bool IsTextInput(in TextControlDescription d)
    {
        if (AlwaysExclude.Contains(d.ControlTypeId))
            return false;

        return d.ControlTypeId switch
        {
            // Edit:常规文本框,除非明确只读
            UiaControlTypeIds.Edit => d.IsReadOnly != true,
            // Document:需可编辑(有 TextPattern 近似 IsContentEditable)且非只读
            UiaControlTypeIds.Document => d.HasTextPattern && d.IsReadOnly != true,
            // 其他(Custom/Pane/Group 等):有 TextPattern 且非只读
            _ => d.HasTextPattern && d.IsReadOnly != true,
        };
    }
}
