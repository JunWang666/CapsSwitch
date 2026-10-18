using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CapsSwitch.App.Interop;
using CapsSwitch.App.Logging;

namespace CapsSwitch.App.Overlay;

/// <summary>
/// 无焦点置顶浮窗:WS_EX_NOACTIVATE | TOOLWINDOW | LAYERED | TRANSPARENT(鼠标穿透),
/// 不出现在任务栏/Alt+Tab。
/// 呈现:圆角背景 + 文字用 GDI+ 画到 32bpp 位图(抗锯齿、逐像素 alpha,含整体 0.78 透明度),
/// 经 UpdateLayeredWindow(ULW_ALPHA) 呈现 —— 替代旧 Region 方案(1-bit 裁剪,圆角锯齿严重)。
/// 两种模式:SetBadge(紧凑单标签)/ SetList(循环列表,活动行强调色高亮)。
/// 尺寸按 96 DPI 逻辑值设计,HudMetrics 换算物理像素;DPI 变化整图重绘。
/// </summary>
public sealed class InputHudForm : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;
    private const int WmMouseActivate = 0x0021;
    private const int WmEraseBackground = 0x0014;
    private const int MaNoActivate = 0x0003;
    private const int UlwAlpha = 0x00000002;

    private static readonly Color Accent = Color.FromArgb(0, 120, 215);

    private const int FadeInMs = 90;
    private const int FadeOutMs = 120;
    private const int FadeStepMs = 15;

    private int _backAlpha = (int)Math.Round(255 * HudMetrics.OpacityValue); // 整体透明度折进背景 alpha
    private string _badgeText = string.Empty;
    private IReadOnlyList<(string Label, string Name)> _listItems = [];
    private int _listActiveIndex = -1;
    private bool _isListMode;

    // 位图缓存 + 淡入淡出(ULW 逐像素 alpha,动画只调 SourceConstantAlpha 整体系数)
    private Bitmap? _cache;
    private byte _alpha = 255;
    private System.Windows.Forms.Timer? _fadeTimer;
    private long _fadeStartMs;
    private int _fadeFrom, _fadeTo, _fadeDuration;
    private Action? _fadeDone;

    public InputHudForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(24, 24, 24);
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None; // 尺寸由 HudMetrics 手动换算
        Text = "CapsSwitchHUD"; // 供自动化定位(无标题栏,不可见)

        _ = Handle; // 强制创建句柄以读取 DeviceDpi
        Redraw();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExNoActivate | WsExToolWindow | WsExLayered | WsExTransparent;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmMouseActivate)
        {
            m.Result = MaNoActivate;
            return;
        }
        if (m.Msg == WmEraseBackground)
        {
            m.Result = new IntPtr(1); // 背景完全由 ULW 位图承担
            return;
        }
        base.WndProc(ref m);
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // 不绘制任何背景,避免 ULW 更新前的直角闪帧
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Redraw();
    }

    private int PadH => HudMetrics.Scale(HudMetrics.LogicalPadHorizontal, DeviceDpi);
    private int PadV => HudMetrics.Scale(HudMetrics.LogicalPadVertical, DeviceDpi);

    // ── 模式 ──

    /// <summary>紧凑徽章模式。</summary>
    public void SetBadge(string text)
    {
        _isListMode = false;
        _badgeText = text;
        Redraw();
    }

    /// <summary>循环列表模式:activeIndex 行强调色高亮。</summary>
    public void SetList(IReadOnlyList<(string Label, string Name)> items, int activeIndex)
    {
        _isListMode = true;
        _listItems = items;
        _listActiveIndex = activeIndex;
        Redraw();
    }

    /// <summary>只移动高亮(列表已显示时连按推进,不闪烁)。</summary>
    public void SetListActive(int activeIndex)
    {
        if (!_isListMode)
            return;
        _listActiveIndex = activeIndex;
        Redraw();
    }

    // ── 度量与绘制 ──

    private Font UiFont => new("Segoe UI", HudMetrics.LogicalFontPt, FontStyle.Regular, GraphicsUnit.Point);

    private Size MeasureContent(Graphics g, Font font)
    {
        if (!_isListMode)
        {
            var textSize = g.MeasureString(_badgeText.Length == 0 ? " " : _badgeText, font);
            return new Size((int)Math.Ceiling(textSize.Width) + PadH * 2, (int)Math.Ceiling(textSize.Height) + PadV * 2);
        }
        int labelW = 0, nameW = 0;
        float rowH = g.MeasureString("中", font).Height + HudMetrics.Scale(4, DeviceDpi);
        foreach (var (label, name) in _listItems)
        {
            labelW = Math.Max(labelW, (int)Math.Ceiling(g.MeasureString(label, font).Width));
            nameW = Math.Max(nameW, (int)Math.Ceiling(g.MeasureString(name, font).Width));
        }
        int colGap = PadH;
        int w = PadH * 2 + labelW + colGap + nameW;
        int h = PadV * 2 + (int)(rowH * Math.Max(1, _listItems.Count));
        return new Size(Math.Max(1, w), Math.Max(1, h));
    }

    private void RenderContent(Graphics g, Font font)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedRect(bounds, HudMetrics.Scale(HudMetrics.LogicalCornerRadius, DeviceDpi));
        using var bg = new SolidBrush(Color.FromArgb(_backAlpha, 24, 24, 24));
        g.FillPath(bg, path);
        g.Clip = new Region(path); // 文字也裁剪进圆角

        using var white = new SolidBrush(Color.White);
        using var dim = new SolidBrush(Color.FromArgb(235, 235, 235));

        if (!_isListMode)
        {
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(_badgeText, font, white, new RectangleF(0, 0, Width, Height), fmt);
            return;
        }

        int labelColW = 0;
        foreach (var (label, _) in _listItems)
            labelColW = Math.Max(labelColW, (int)Math.Ceiling(g.MeasureString(label, font).Width));
        float rowH = g.MeasureString("中", font).Height + HudMetrics.Scale(4, DeviceDpi);
        using var accent = new SolidBrush(Color.FromArgb(_backAlpha, Accent));
        float y = PadV;
        for (int i = 0; i < _listItems.Count; i++)
        {
            bool active = i == _listActiveIndex;
            if (active)
                g.FillRectangle(accent, new RectangleF(0, y, Width, rowH));
            var brush = active ? white : dim;
            g.DrawString(_listItems[i].Label, font, brush, new PointF(PadH, y + HudMetrics.Scale(2, DeviceDpi)));
            g.DrawString(_listItems[i].Name, font, brush, new PointF(PadH + labelColW + PadH, y + HudMetrics.Scale(2, DeviceDpi)));
            y += rowH;
        }
    }

    /// <summary>重绘位图并经 UpdateLayeredWindow 呈现;失败只记日志,不抛。</summary>
    private void Redraw()
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        try
        {
            using var font = UiFont;
            using (var measureBmp = new Bitmap(1, 1))
            using (var mg = Graphics.FromImage(measureBmp))
            {
                var contentSize = MeasureContent(mg, font);
                if (contentSize != Size)
                    Size = contentSize;
            }

            var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
                RenderContent(g, font);

            _cache?.Dispose();
            _cache = bmp;
            ApplyLayered(_alpha);
        }
        catch (Exception ex)
        {
            Logger.Verbose($"[InputHudForm] 重绘失败(忽略): {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>淡入(90ms)。已在淡出则取消并从当前 alpha 起淡入。</summary>
    public void FadeIn()
    {
        if (!Visible)
        {
            _alpha = 0;
            Show();
        }
        StartFade(_alpha, 255, FadeInMs, null);
    }

    /// <summary>淡出(120ms),完成后回调(UI 线程,此时已 Hide)。</summary>
    public void FadeOut(Action? onHidden)
    {
        if (!Visible)
        {
            onHidden?.Invoke();
            return;
        }
        StartFade(_alpha, 0, FadeOutMs, () =>
        {
            Hide();
            onHidden?.Invoke();
        });
    }

    private void StartFade(int from, int to, int durationMs, Action? done)
    {
        _fadeFrom = from;
        _fadeTo = to;
        _fadeDuration = durationMs;
        _fadeDone = done;
        _fadeStartMs = Environment.TickCount64;
        _fadeTimer ??= new System.Windows.Forms.Timer { Interval = FadeStepMs };
        _fadeTimer.Stop();
        _fadeTimer.Tick -= FadeTick;
        _fadeTimer.Tick += FadeTick;
        _fadeTimer.Start();
        ApplyLayered((byte)from);
    }

    private void FadeTick(object? sender, EventArgs e)
    {
        double t = Math.Min(1.0, (Environment.TickCount64 - _fadeStartMs) / (double)_fadeDuration);
        int alpha = (int)(_fadeFrom + (_fadeTo - _fadeFrom) * t);
        _alpha = (byte)Math.Clamp(alpha, 0, 255);
        ApplyLayered(_alpha);
        if (t < 1.0)
            return;
        _fadeTimer?.Stop();
        _fadeTimer!.Tick -= FadeTick;
        var done = _fadeDone;
        _fadeDone = null;
        if (_fadeTo == 0 && Visible)
            Hide();
        done?.Invoke();
    }

    private void ApplyLayered(byte alpha)
    {
        var bmp = _cache;
        if (bmp is null)
            return;
        var bi = new Win32Native.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<Win32Native.BITMAPINFOHEADER>(),
            biWidth = bmp.Width,
            biHeight = -bmp.Height, // 自顶向下,与 GDI+ Scan0 一致
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0, // BI_RGB
        };

        IntPtr screenDc = Win32Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Win32Native.CreateCompatibleDC(screenDc);
        IntPtr hBmp = Win32Native.CreateDIBSection(memDc, ref bi, 0, out IntPtr bits, IntPtr.Zero, 0);
        if (hBmp == IntPtr.Zero || bits == IntPtr.Zero)
        {
            Logger.Verbose("[InputHudForm] CreateDIBSection 失败");
            Win32Native.DeleteDC(memDc);
            Win32Native.ReleaseDC(IntPtr.Zero, screenDc);
            return;
        }
        IntPtr oldBmp = Win32Native.SelectObject(memDc, hBmp);
        try
        {
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            Win32Native.RtlMoveMemory(bits, data.Scan0, (nuint)(data.Stride * data.Height));
            bmp.UnlockBits(data);

            var dst = new Win32Native.POINT { X = Left, Y = Top };
            var src = new Win32Native.POINT { X = 0, Y = 0 };
            var size = new Win32Native.SIZE { cx = bmp.Width, cy = bmp.Height };
            var blend = new Win32Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
            if (!Win32Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, UlwAlpha))
                Logger.Verbose($"[InputHudForm] UpdateLayeredWindow 失败 LastError=0x{Marshal.GetLastWin32Error():x8}");
        }
        finally
        {
            Win32Native.SelectObject(memDc, oldBmp);
            Win32Native.DeleteObject(hBmp);
            Win32Native.DeleteDC(memDc);
            Win32Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>把窗口放到光标下方(底部放不下翻到上方,水平左对齐,边缘避让)。</summary>
    public void PositionNear(Rectangle caretRect)
    {
        var screen = Screen.FromRectangle(caretRect);
        Location = HudPlacer.PlaceBelow(in caretRect, Size, screen.WorkingArea, HudMetrics.Scale(HudMetrics.OffsetFromCaretY, DeviceDpi));
        // ULW 位图带目标位置,位置变化后需重绘
        Redraw();
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        radius = Math.Max(1, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
        int d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
