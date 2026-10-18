using System.Drawing.Drawing2D;
using CapsSwitch.App.Config;
using CapsSwitch.App.InputSources;
using CapsSwitch.App.Overlay;

namespace CapsSwitch.App.UI;

/// <summary>
/// 托盘 ApplicationContext:NotifyIcon + 右键菜单(暂停切换/HUD 开关/设置/退出)。
/// 托盘图标代码生成(64x64 深色圆角方底 + 白色文字,随当前输入源换字)。
/// </summary>
public sealed class TrayContext : ApplicationContext
{
    private readonly Action<string> _log;
    private readonly AppHost _host;
    private readonly SettingsStore _store;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _toggleHotkeysItem;
    private readonly ToolStripMenuItem _toggleHudItem;
    private string _iconLabel = "?";

    public TrayContext(Action<string> log, AppHost host, SettingsStore store, bool showBalloon)
    {
        _log = log;
        _host = host;
        _store = store;

        _toggleHotkeysItem = new ToolStripMenuItem();
        _toggleHotkeysItem.Click += (_, _) => ToggleHotkeys();
        _toggleHudItem = new ToolStripMenuItem();
        _toggleHudItem.Click += (_, _) => ToggleHud();

        var settingsItem = new ToolStripMenuItem("设置…");
        settingsItem.Click += (_, _) => OpenSettings();
        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitFromMenu();

        _menu = new ContextMenuStrip { Renderer = new FlatMenuRenderer(), ShowImageMargin = false };
        _menu.Items.Add(_toggleHotkeysItem);
        _menu.Items.Add(_toggleHudItem);
        _menu.Items.Add(settingsItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);
        foreach (ToolStripItem item in _menu.Items)
            item.Padding = new Padding(12, 5, 12, 5);

        _notifyIcon = new NotifyIcon
        {
            Icon = RenderIcon(_iconLabel),
            Visible = true,
            Text = "CapsSwitch",
            // 注意:不把菜单赋给 ContextMenuStrip 属性,右键弹出完全由 MouseClick 显式控制,
            // 菜单行为不依赖 shell 默认右键处理
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                RefreshMenuLabels();
                _menu.Show(Cursor.Position);
            }
            else if (e.Button == MouseButtons.Left)
            {
                OpenSettings();
            }
        };

        _host.Monitor.Changed += OnInputStateChanged;
        RefreshMenuLabels();

        if (showBalloon)
        {
            _notifyIcon.BalloonTipTitle = "CapsSwitch 已启动";
            _notifyIcon.BalloonTipText = "短按 Caps 切换输入源;右键托盘图标查看更多选项。";
            _notifyIcon.ShowBalloonTip(3000);
        }
    }

    private void RefreshMenuLabels()
    {
        bool hotkeys = _host.HotkeysEnabled;
        _toggleHotkeysItem.Text = hotkeys ? "暂停快捷键切换" : "启用快捷键切换";
        _toggleHotkeysItem.Checked = !hotkeys;
        bool hud = _host.Settings.HudEnabled;
        _toggleHudItem.Text = hud ? "暂停输入焦点提示" : "启用输入焦点提示";
        _toggleHudItem.Checked = !hud;
    }

    private void ToggleHotkeys()
    {
        _host.HotkeysEnabled = !_host.HotkeysEnabled;
        RefreshMenuLabels();
    }

    private void ToggleHud()
    {
        _host.SetHudEnabled(!_host.Settings.HudEnabled);
        _store.Save(_host.Settings);
        RefreshMenuLabels();
    }

    private void OnInputStateChanged(InputSourceState state)
    {
        // Monitor 线程回调 → 封送到 UI 线程(以菜单控件为同步锚)
        if (_menu.InvokeRequired)
        {
            _menu.BeginInvoke(() => OnInputStateChanged(state));
            return;
        }
        var sources = _host.Service.GetSources();
        var matched = sources.FirstOrDefault(s => s.Hkl == state.Hkl)
            ?? sources.FirstOrDefault(s => (s.Hkl.ToInt64() & 0xFFFF) == (state.Hkl.ToInt64() & 0xFFFF));
        string? label = HudTextMapper.Map(matched, state.NativeMode, _host.Settings.PreferredChineseId);
        UpdateIconLabel(label);
    }

    private void UpdateIconLabel(string? label)
    {
        if (label is null || label == _iconLabel)
            return;
        _iconLabel = label;
        var old = _notifyIcon.Icon;
        _notifyIcon.Icon = RenderIcon(label);
        old?.Dispose();
    }

    /// <summary>扁平菜单渲染:白底、悬停浅灰、选中浅蓝灰、文字深灰、分隔线浅灰,无立体边框。</summary>
    private sealed class FlatMenuRenderer : ToolStripProfessionalRenderer
    {
        public FlatMenuRenderer() : base(new FlatColorTable()) { }
    }

    private sealed class FlatColorTable : ProfessionalColorTable
    {
        private static readonly Color Bg = Color.White;
        private static readonly Color Hover = Color.FromArgb(240, 240, 240);
        private static readonly Color Checked = Color.FromArgb(220, 232, 246);
        private static readonly Color Sep = Color.FromArgb(225, 225, 225);

        public override Color ToolStripDropDownBackground => Bg;
        public override Color MenuBorder => Bg;
        public override Color MenuItemBorder => Bg;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Checked;
        public override Color MenuItemPressedGradientEnd => Checked;
        public override Color ButtonSelectedGradientBegin => Hover;
        public override Color ButtonSelectedGradientEnd => Hover;
        public override Color SeparatorDark => Sep;
        public override Color SeparatorLight => Sep;
        public override Color CheckBackground => Checked;
        public override Color CheckSelectedBackground => Checked;
        public override Color CheckPressedBackground => Checked;
        public override Color ImageMarginGradientBegin => Bg;
        public override Color ImageMarginGradientMiddle => Bg;
        public override Color ImageMarginGradientEnd => Bg;
    }

    /// <summary>代码生成托盘图标:64x64 深色圆角方底 + 白色粗体文字(无资源文件)。</summary>
    private static Icon RenderIcon(string label)
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRect(new Rectangle(2, 2, 60, 60), 12);
            using var brush = new SolidBrush(Color.FromArgb(45, 45, 48));
            g.FillPath(brush, path);
            using var font = new Font("Segoe UI", label.Length > 1 ? 20F : 30F, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(label, font, textBrush, new RectangleF(0, 0, 64, 64), fmt);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void OpenSettings()
    {
        SettingsForm.OpenSingleton(_log, _host, _store);
    }

    private void ExitFromMenu()
    {
        _log("[App] 托盘退出");
        // NotifyIcon 必须在消息泵停止前移除,否则托盘图标残留
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _host.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _host.Monitor.Changed -= OnInputStateChanged;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }
}
