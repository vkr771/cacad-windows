using System.Drawing.Drawing2D;

namespace Cacad;

/// <summary>Colours and helpers for the dark WinForms surfaces (settings, dialogs).</summary>
internal static class Ui
{
    public static readonly Color Bg = Color.FromArgb(20, 20, 22);
    public static readonly Color Card = Color.FromArgb(30, 30, 33);
    public static readonly Color Line = Color.FromArgb(44, 44, 49);
    public static readonly Color Text = Color.FromArgb(239, 237, 232);
    public static readonly Color Dim = Color.FromArgb(150, 150, 158);
    public static readonly Color Knob = Color.FromArgb(58, 58, 64);
    public static readonly Color Accent = Color.FromArgb(239, 237, 232);

    public static Font Font(float size, FontStyle style = FontStyle.Regular)
        => Fonts.UiFont(size, style) ?? new Font("Segoe UI", size, style);

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void DarkTitleBar(IntPtr hwnd)
    {
        int on = 1;
        Native.DwmSetWindowAttribute(hwnd, 20, ref on, 4);   // DWMWA_USE_IMMERSIVE_DARK_MODE
        int round = 2;
        Native.DwmSetWindowAttribute(hwnd, 33, ref round, 4); // DWMWA_WINDOW_CORNER_PREFERENCE (Win 11)
    }
}

/// <summary>A row of pill buttons, one selected. Wraps onto more lines when narrow.</summary>
internal sealed class Segmented : Control
{
    private readonly (string value, string label)[] _items;
    private readonly List<RectangleF> _rects = new();
    private int _hover = -1;
    public string Value { get; private set; }
    public event Action<string>? Changed;

    public Segmented(IEnumerable<(string, string)> items, string value)
    {
        _items = items.ToArray();
        Value = value;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Font = Ui.Font(9f);
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
    }

    public void SetValue(string v) { Value = v; Invalidate(); }

    private void Measure(Graphics g)
    {
        _rects.Clear();
        float x = 0, y = 0, h = 28;
        foreach (var (_, label) in _items)
        {
            float w = g.MeasureString(label, Font).Width + 22;
            if (x > 0 && x + w > Width) { x = 0; y += h + 6; }
            _rects.Add(new RectangleF(x, y, w, h));
            x += w + 6;
        }
        int needed = (int)Math.Ceiling(y + h) + 1;
        if (Height != needed) Height = needed;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        Measure(g);
        for (int i = 0; i < _items.Length; i++)
        {
            var r = _rects[i];
            bool on = _items[i].value == Value;
            using var path = Ui.Round(r, r.Height / 2);
            using var b = new SolidBrush(on ? Ui.Accent : i == _hover ? Color.FromArgb(52, 52, 58) : Color.FromArgb(38, 38, 42));
            g.FillPath(b, path);
            TextRenderer.DrawText(g, _items[i].label, Font, Rectangle.Round(r), on ? Color.FromArgb(14, 14, 16) : Ui.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = _rects.FindIndex(r => r.Contains(e.Location));
        if (h != _hover) { _hover = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int i = _rects.FindIndex(r => r.Contains(e.Location));
        if (i < 0 || _items[i].value == Value) return;
        Value = _items[i].value;
        Invalidate();
        Changed?.Invoke(Value);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using var g = CreateGraphics();
        Measure(g);
    }
}

internal sealed class Toggle : Control
{
    private bool _on;
    private float _k;
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };
    public event Action<bool>? Changed;

    public Toggle(bool on)
    {
        _on = on;
        _k = on ? 1 : 0;
        Size = new Size(40, 22);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        _anim.Tick += (_, _) =>
        {
            float t = _on ? 1 : 0;
            _k += (t - _k) * 0.3f;
            if (Math.Abs(_k - t) < 0.01f) { _k = t; _anim.Stop(); }
            Invalidate();
        };
    }

    public bool On
    {
        get => _on;
        set { if (_on == value) return; _on = value; _anim.Start(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        int a = (int)(255 * _k);
        using (var path = Ui.Round(r, r.Height / 2))
        {
            using var off = new SolidBrush(Ui.Knob);
            g.FillPath(off, path);
            using var on = new SolidBrush(Color.FromArgb(a, Ui.Accent));
            g.FillPath(on, path);
        }
        float d = Height - 6, x = 3 + (Width - 6 - d) * _k;
        using var knob = new SolidBrush(_k > 0.5f ? Color.FromArgb(14, 14, 16) : Ui.Text);
        g.FillEllipse(knob, x, 3, d, d);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        On = !On;
        Changed?.Invoke(_on);
    }
}

internal sealed class ValueSlider : Control
{
    private readonly int _min, _max, _step;
    private readonly Func<int, string> _format;
    private bool _drag;
    public int Value { get; private set; }
    public event Action<int>? Changed;

    public ValueSlider(int min, int max, int step, int value, Func<int, string> format)
    {
        _min = min; _max = max; _step = Math.Max(1, step); _format = format;
        Value = Math.Clamp(value, min, max);
        Size = new Size(220, 24);
        Font = Ui.Font(8.5f);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }

    private RectangleF Track => new(8, Height / 2f - 2, Width - 70, 4);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var t = Track;
        float k = (Value - _min) / (float)(_max - _min);
        using (var p = Ui.Round(t, 2)) { using var b = new SolidBrush(Ui.Knob); g.FillPath(b, p); }
        using (var p = Ui.Round(new RectangleF(t.X, t.Y, Math.Max(4, t.Width * k), t.Height), 2)) { using var b = new SolidBrush(Ui.Accent); g.FillPath(b, p); }
        float cx = t.X + t.Width * k;
        using (var b = new SolidBrush(Ui.Text)) g.FillEllipse(b, cx - 7, Height / 2f - 7, 14, 14);
        TextRenderer.DrawText(g, _format(Value), Font, new Rectangle((int)t.Right + 8, 0, Width - (int)t.Right - 8, Height), Ui.Dim,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }

    private void SetFrom(int x)
    {
        var t = Track;
        float k = Math.Clamp((x - t.X) / t.Width, 0, 1);
        int v = _min + (int)Math.Round(k * (_max - _min) / _step) * _step;
        v = Math.Clamp(v, _min, _max);
        if (v == Value) return;
        Value = v;
        Invalidate();
        Changed?.Invoke(v);
    }

    protected override void OnMouseDown(MouseEventArgs e) { _drag = true; SetFrom(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { if (_drag) SetFrom(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) => _drag = false;
}

/// <summary>A dark multi-line editor for the Notes tab. Saves on close; Esc closes too.</summary>
internal sealed class NoteEditor : Form
{
    private readonly TextBox _box;

    private NoteEditor(string text)
    {
        Text = "cacad notes";
        BackColor = Ui.Bg;
        ForeColor = Ui.Text;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 420);
        ShowInTaskbar = true;
        TopMost = true;
        KeyPreview = true;
        _box = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Ui.Card,
            ForeColor = Ui.Text,
            Font = Ui.Font(11f),
            Text = text,
        };
        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            Text = "  # heading     - [ ] checkbox     - bullet     ·  saves when you close (Esc)",
            ForeColor = Ui.Dim,
            BackColor = Ui.Bg,
            Font = Ui.Font(8.5f),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        Controls.Add(_box);
        Controls.Add(hint);
        Padding = new Padding(10);
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.DarkTitleBar(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        _box.Focus();
        _box.SelectionStart = _box.TextLength;
    }

    public static string? Edit(string text)
    {
        using var f = new NoteEditor(text);
        f.ShowDialog();
        return f._box.Text;
    }
}

/// <summary>A one-line text prompt in the island's colours.</summary>
internal sealed class InputDialog : Form
{
    private readonly TextBox _box;

    private InputDialog(string title, string placeholder, Theme theme)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(theme.Fill.Red, theme.Fill.Green, theme.Fill.Blue) is { } c && c.GetBrightness() > 0.05f ? c : Color.FromArgb(24, 24, 27);
        Size = new Size(360, 104);
        Padding = new Padding(16);
        var mon = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(mon.Left + (mon.Width - Width) / 2, mon.Top + 240);

        var label = new Label { Text = title, ForeColor = Ui.Text, Font = Ui.Font(10f, FontStyle.Bold), AutoSize = true, Location = new Point(16, 14), BackColor = Color.Transparent };
        _box = new TextBox
        {
            Location = new Point(16, 44),
            Width = Width - 32,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(44, 44, 49),
            ForeColor = Ui.Text,
            Font = Ui.Font(11f),
            PlaceholderText = placeholder,
        };
        var hint = new Label { Text = "Enter to add · Esc to cancel", ForeColor = Ui.Dim, Font = Ui.Font(8.5f), AutoSize = true, Location = new Point(16, 76), BackColor = Color.Transparent };
        Controls.AddRange(new Control[] { label, _box, hint });
        _box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; e.SuppressKeyPress = true; }
        };
        Deactivate += (_, _) => { if (DialogResult == DialogResult.None) DialogResult = DialogResult.Cancel; };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.DarkTitleBar(Handle);
        Region = Region.FromHrgn(Native.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 24, 24));
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        _box.Focus();
    }

    public static string? Ask(string title, string placeholder, Theme theme)
    {
        using var d = new InputDialog(title, placeholder, theme);
        return d.ShowDialog() == DialogResult.OK ? d._box.Text : null;
    }
}
