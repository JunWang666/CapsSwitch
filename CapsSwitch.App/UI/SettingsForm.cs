using CapsSwitch.App.Config;
using CapsSwitch.App.InputSources;

namespace CapsSwitch.App.UI;

/// <summary>
/// 设置窗口(纯 WinForms)。逻辑像素绝对布局 + AutoScaleMode=Dpi,任意缩放下布局比例一致,
/// 保存/取消/自启勾选保证在窗口内可见(Phase 3 首版 AutoSize 布局在 150% DPI 下裁剪,已弃用)。
/// UI 线程单例:重复打开只激活已有窗口。保存 → 写 settings.json + AppHost.UpdateSettings 立即生效。
/// </summary>
public sealed class SettingsForm : Form
{
    private static SettingsForm? _instance;

    private readonly Action<string> _log;
    private readonly AppHost _host;
    private readonly SettingsStore _store;
    private readonly IReadOnlyList<InputSourceInfo> _sources;

    private readonly ComboBox _chineseCombo;
    private readonly ComboBox _englishCombo;
    private readonly ListBox _orderList;
    private readonly NumericUpDown _longPressMs;
    private readonly NumericUpDown _sequenceGapMs;
    private readonly NumericUpDown _hudDurationMs;
    private readonly NumericUpDown _hudFocusCooldown;
    private readonly CheckBox _hudEnabled;
    private readonly CheckBox _autoStart;

    public static void OpenSingleton(Action<string> log, AppHost host, SettingsStore store)
    {
        if (_instance is not null && !_instance.IsDisposed)
        {
            _instance.Activate();
            return;
        }
        _instance = new SettingsForm(log, host, store);
        _instance.FormClosed += (_, _) => _instance = null;
        _instance.Show();
    }

    private SettingsForm(Action<string> log, AppHost host, SettingsStore store)
    {
        _log = log;
        _host = host;
        _store = store;

        Text = "CapsSwitch 设置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        // 绝对逻辑布局 + 手动 DPI 缩放(WinForms 的 AutoScaleMode 不缩放运行时设置的 ClientSize,
        // Phase 3 首版因此子控件放大但窗口没放大 → 内容被裁剪)
        AutoScaleMode = AutoScaleMode.None;
        const int LogicalWidth = 440, LogicalHeight = 556;

        _sources = host.Service.GetSources();
        var settings = host.Settings;

        // 打开窗口时剔除已卸载的源(配置回退,不崩)
        var alive = new HashSet<string>(_sources.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);
        var ordered = settings.OrderedSourceIds.Where(alive.Contains).ToList();
        foreach (var s in _sources)
            if (!ordered.Contains(s.Id))
                ordered.Add(s.Id);
        string? chineseId = settings.PreferredChineseId is not null && alive.Contains(settings.PreferredChineseId)
            ? settings.PreferredChineseId
            : _sources.FirstOrDefault(s => s.ProfileGuid == InputSourceService.MicrosoftPinyinProfileGuid)?.Id
            ?? _sources.FirstOrDefault()?.Id;
        string? englishId = settings.PreferredEnglishId is not null && alive.Contains(settings.PreferredEnglishId)
            ? settings.PreferredEnglishId
            : _sources.FirstOrDefault(s => s.Id == "hkl:00000409")?.Id
            ?? _sources.Skip(1).FirstOrDefault()?.Id;

        // ── 顶部说明 ──
        Controls.Add(new Label
        {
            Text = "修改保存后立即生效。",
            ForeColor = Color.Gray,
            AutoSize = false,
            Bounds = new Rectangle(12, 10, 416, 18),
        });

        // ── 输入源 ──
        var gbSrc = new GroupBox { Text = "输入源", Bounds = new Rectangle(12, 34, 416, 216) };
        _chineseCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(150, 22, 252, 23) };
        _englishCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(150, 52, 252, 23) };
        FillCombo(_chineseCombo, chineseId);
        FillCombo(_englishCombo, englishId);
        gbSrc.Controls.Add(LabelAt(14, 25, "主输入源"));
        gbSrc.Controls.Add(_chineseCombo);
        gbSrc.Controls.Add(LabelAt(14, 55, "副输入源"));
        gbSrc.Controls.Add(_englishCombo);
        gbSrc.Controls.Add(LabelAt(14, 88, "循环顺序"));
        _orderList = new ListBox { Bounds = new Rectangle(150, 84, 200, 118) };
        _orderList.Items.AddRange(ordered.Select(FindSource).Where(s => s is not null).Select(s => Describe(s!)).ToArray());
        gbSrc.Controls.Add(_orderList);
        var up = FlatButton("上移", 44);
        up.Bounds = new Rectangle(356, 84, 44, 28);
        up.Click += (_, _) => MoveOrder(-1);
        var down = FlatButton("下移", 44);
        down.Bounds = new Rectangle(356, 116, 44, 28);
        down.Click += (_, _) => MoveOrder(+1);
        gbSrc.Controls.Add(up);
        gbSrc.Controls.Add(down);
        Controls.Add(gbSrc);

        // ── 触发参数 ──
        var gbTrig = new GroupBox { Text = "触发参数", Bounds = new Rectangle(12, 258, 416, 178) };
        _longPressMs = new NumericUpDown { Minimum = 100, Maximum = 2000, Bounds = new Rectangle(170, 22, 90, 23) };
        _sequenceGapMs = new NumericUpDown { Minimum = 100, Maximum = 2000, Bounds = new Rectangle(170, 52, 90, 23) };
        _hudDurationMs = new NumericUpDown { Minimum = 200, Maximum = 10000, Increment = 100, Bounds = new Rectangle(170, 82, 90, 23) };
        _longPressMs.Value = Math.Clamp(settings.LongPressThresholdMs, 100, 2000);
        _sequenceGapMs.Value = Math.Clamp(settings.SequenceGapMs, 100, 2000);
        _hudDurationMs.Value = Math.Clamp(settings.HudDurationMs, 200, 10000);
        gbTrig.Controls.Add(LabelAt(14, 25, "长按阈值(毫秒)"));
        gbTrig.Controls.Add(_longPressMs);
        gbTrig.Controls.Add(LabelAt(14, 55, "连续按键间隔(毫秒)"));
        gbTrig.Controls.Add(_sequenceGapMs);
        gbTrig.Controls.Add(LabelAt(14, 85, "HUD 显示时长(毫秒)"));
        gbTrig.Controls.Add(_hudDurationMs);
        _hudFocusCooldown = new NumericUpDown { Minimum = 0, Maximum = 3600, Increment = 5, Bounds = new Rectangle(170, 112, 90, 23) };
        _hudFocusCooldown.Value = Math.Clamp(settings.HudFocusCooldownSeconds, 0, 3600);
        gbTrig.Controls.Add(LabelAt(14, 115, "聚焦冷却(秒,0=关)"));
        gbTrig.Controls.Add(_hudFocusCooldown);
        _hudEnabled = new CheckBox { Text = "启用输入焦点提示(HUD)", AutoSize = true, Bounds = new Rectangle(170, 142, 230, 20) };
        _hudEnabled.Checked = settings.HudEnabled;
        gbTrig.Controls.Add(_hudEnabled);
        Controls.Add(gbTrig);

        // ── 启动 ──
        var gbBoot = new GroupBox { Text = "启动", Bounds = new Rectangle(12, 444, 416, 56) };
        _autoStart = new CheckBox { Text = "开机自启动(当前用户)", AutoSize = true, Bounds = new Rectangle(20, 22, 240, 20) };
        _autoStart.Checked = settings.AutoStart;
        gbBoot.Controls.Add(_autoStart);
        Controls.Add(gbBoot);

        // ── 按钮 ──
        var save = AccentButton("保存", 92);
        save.Bounds = new Rectangle(328, 512, 92, 28);
        save.Click += (_, _) => SaveAndClose();
        var cancel = FlatButton("取消", 92);
        cancel.Bounds = new Rectangle(228, 512, 92, 28);
        cancel.Click += (_, _) => Close();
        Controls.Add(save);
        Controls.Add(cancel);

        // 手动 DPI 缩放:所有 Bounds 为 96 DPI 逻辑值,统一换算为物理像素
        _ = Handle; // 强制创建句柄以读取 DeviceDpi
        float scale = DeviceDpi / 96f;
        ScaleTree(this, scale);
        ClientSize = new Size((int)(LogicalWidth * scale), (int)(LogicalHeight * scale));
    }

    private static void ScaleTree(Control parent, float scale)
    {
        foreach (Control child in parent.Controls)
        {
            child.Bounds = new Rectangle(
                (int)(child.Left * scale), (int)(child.Top * scale),
                (int)(child.Width * scale), (int)(child.Height * scale));
            // 字号为 Point 单位,GDI 按物理尺寸渲染,无需缩放
            ScaleTree(child, scale);
        }
    }

    private static Label LabelAt(int x, int y, string text) =>
        new() { Text = text, AutoSize = false, Bounds = new Rectangle(x, y, 132, 18) };

    private static Button FlatButton(string text, int width) => new()
    {
        Text = text,
        Width = width,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.White,
        ForeColor = Color.FromArgb(32, 32, 32),
        Height = 28,
    };

    private static Button AccentButton(string text, int width)
    {
        var b = FlatButton(text, width);
        b.BackColor = Color.FromArgb(0, 120, 215);
        b.ForeColor = Color.White;
        b.FlatAppearance.BorderColor = Color.FromArgb(0, 120, 215);
        return b;
    }

    private InputSourceInfo? FindSource(string id) =>
        _sources.FirstOrDefault(s => s.Id == id);

    private static string Describe(InputSourceInfo s) => $"[{s.ShortLabel}] {s.DisplayName}";

    private void FillCombo(ComboBox combo, string? selectedId)
    {
        combo.Items.Clear();
        foreach (var s in _sources)
            combo.Items.Add(new SourceItem(s));
        combo.DisplayMember = nameof(SourceItem.Display);
        combo.ValueMember = nameof(SourceItem.Id);
        int idx = -1;
        for (int i = 0; i < _sources.Count; i++)
            if (_sources[i].Id == selectedId) { idx = i; break; }
        combo.SelectedIndex = idx >= 0 ? idx : (_sources.Count > 0 ? 0 : -1);
    }

    private void MoveOrder(int delta)
    {
        int idx = _orderList.SelectedIndex;
        int next = idx + delta;
        if (idx < 0 || next < 0 || next >= _orderList.Items.Count)
            return;
        (_orderList.Items[idx], _orderList.Items[next]) = (_orderList.Items[next], _orderList.Items[idx]);
        _orderList.SelectedIndex = next;
    }

    private void SaveAndClose()
    {
        try
        {
            var settings = _host.Settings;
            settings.PreferredChineseId = (_chineseCombo.SelectedItem as SourceItem)?.Id;
            settings.PreferredEnglishId = (_englishCombo.SelectedItem as SourceItem)?.Id;
            var ids = new List<string>();
            foreach (var item in _orderList.Items.Cast<string>())
                ids.Add(IdFromDisplay(item));
            settings.OrderedSourceIds = ids;
            settings.LongPressThresholdMs = (int)_longPressMs.Value;
            settings.SequenceGapMs = (int)_sequenceGapMs.Value;
            settings.HudDurationMs = (int)_hudDurationMs.Value;
            settings.HudFocusCooldownSeconds = (int)_hudFocusCooldown.Value;
            settings.HudEnabled = _hudEnabled.Checked;
            settings.AutoStart = _autoStart.Checked;
            _store.Save(settings);
            _host.UpdateSettings(settings);
            _log("[Settings] 设置已保存并生效");
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存失败:{ex.Message}", "CapsSwitch", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string IdFromDisplay(string display)
    {
        var matched = _sources.FirstOrDefault(s => Describe(s) == display);
        return matched?.Id ?? display;
    }

    private sealed class SourceItem
    {
        public string Id { get; }
        public string Display { get; }
        public SourceItem(InputSourceInfo s) { Id = s.Id; Display = Describe(s); }
        public override string ToString() => Display;
    }
}
