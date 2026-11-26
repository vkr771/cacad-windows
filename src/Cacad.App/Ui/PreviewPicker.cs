using System.Diagnostics;
using SkiaSharp;

namespace Cacad;

/// <summary>A grid of cards, each a live preview of one style (progress bar or pill visualizer), drawn with the
/// island's own renderers so the preview is exactly what you get. Click a card to choose it; hover a bar card
/// and it reacts the way the real bar does under the pointer.</summary>
internal sealed class PreviewPicker : Control
{
    public enum Kind { ProgressBar, Visualizer, Companion, Skin, Clock, Style }

    /// <summary>For companion, skin and style cards: the chosen companion, coat and art style (each card swaps in its own).</summary>
    public Func<(string Species, string Skin, string Style)>? CompanionNow { get; set; }

    private readonly Kind _kind;
    private readonly (string value, string label, string? sub)[] _items;
    private readonly Func<string> _themeDefault;
    private readonly BarState[] _bars;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<RectangleF> _rects = new();   // device px
    private double _last;
    private int _hover = -1;
    private PointF _mouse;
    private SKBitmap? _canvas;

    public string Value { get; private set; }
    public event Action<string>? Changed;

    private float S => DeviceDpi / 96f;
    private float CardW => _kind switch { Kind.ProgressBar => 162, Kind.Visualizer => 118, Kind.Clock => 162, _ => 96 };
    private const float CardH = 100, Gap = 10;

    // The preview's accent: a vivid lavender that reads on every card.
    private static readonly SKColor Demo = new(170, 140, 255);

    public PreviewPicker(Kind kind, IEnumerable<(string value, string label, string? sub)> items, string value, Func<string> themeDefault)
    {
        _kind = kind;
        _items = items.ToArray();
        _themeDefault = themeDefault;
        _bars = _items.Select(_ => new BarState()).ToArray();
        Value = value;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        _timer.Tick += (_, _) =>
        {
            // Animate only while someone can see it.
            if (FindForm() is { Visible: true, WindowState: not FormWindowState.Minimized }) Invalidate();
        };
        _timer.Start();
    }

    public void SetValue(string v) { Value = v; Invalidate(); }

    private void LayoutCards(float width)
    {
        _rects.Clear();
        float cw = CardW * S, ch = CardH * S, gap = Gap * S;
        int perRow = Math.Max(1, (int)((width + gap) / (cw + gap)));
        // Stretch the cards to fill the row exactly.
        cw = (width - gap * (perRow - 1)) / perRow;
        for (int i = 0; i < _items.Length; i++)
            _rects.Add(new RectangleF(i % perRow * (cw + gap), i / perRow * (ch + gap), cw, ch));
        int rows = (_items.Length + perRow - 1) / perRow;
        int needed = (int)Math.Ceiling(rows * ch + (rows - 1) * gap) + 1;
        if (Height != needed) Height = needed;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutCards(Width);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width <= 0 || Height <= 0) return;
        LayoutCards(Width);
        if (_canvas == null || _canvas.Width != Width || _canvas.Height != Height)
        {
            _canvas?.Dispose();
            _canvas = new SKBitmap(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        }
        double now = _clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - _last, 0.001, 0.1);
        _last = now;

        using (var c = new SKCanvas(_canvas))
        {
            c.Clear(new SKColor((uint)Ui.Card.ToArgb()));
            for (int i = 0; i < _items.Length; i++) DrawCard(c, i, _rects[i], (float)now, dt);
        }
        using var bmp = new Bitmap(Width, Height, Width * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, _canvas.GetPixels());
        e.Graphics.DrawImageUnscaled(bmp, 0, 0);
    }

    private void DrawCard(SKCanvas c, int i, RectangleF rf, float t, float dt)
    {
        var (value, label, sub) = _items[i];
        bool on = value == Value, over = i == _hover;
        float s = S;
        c.Save();
        c.Translate(rf.X, rf.Y);
        c.Scale(s);
        float w = rf.Width / s, h = rf.Height / s;
        var card = SKRect.Create(0.5f, 0.5f, w - 1, h - 1);

        G.Fill(c, card, 12, over ? new SKColor(42, 42, 48) : new SKColor(34, 34, 39));

        // The stage: a dark inset, like the island itself.
        var stage = SKRect.Create(8, 8, w - 16, 58);
        using (var sh = SKShader.CreateLinearGradient(new SKPoint(0, stage.Top), new SKPoint(0, stage.Bottom),
                   new[] { new SKColor(20, 20, 24), new SKColor(11, 11, 13) }, null, SKShaderTileMode.Clamp))
        using (var p = new SKPaint { IsAntialias = true, Shader = sh })
            c.DrawRoundRect(stage, 9, 9, p);

        c.Save();
        c.ClipRoundRect(new SKRoundRect(stage, 9), SKClipOperation.Intersect, true);
        string style = value == "" ? _themeDefault() : value;
        if (_kind == Kind.ProgressBar) DrawBarPreview(c, i, style, stage, t, dt, over);
        else if (_kind == Kind.Visualizer) DrawVisualizerPreview(c, style, stage, t);
        else if (_kind == Kind.Clock) DrawClockPreview(c, i, value, stage, t);
        else DrawCompanionPreview(c, i, value, stage, t, over);
        c.Restore();

        using (var name = Fonts.Med(12))
            G.Text(c, label, 12, h - 14, name, G.Paper(on ? 1 : 0.8f));
        if (sub != null)
            using (var small = Fonts.Sub(10.5f))
            {
                float lw;
                using (var name = Fonts.Med(12)) lw = G.Measure(name, label);
                G.Text(c, sub, 12 + lw + 6, h - 14, small, G.Paper(0.45f));
            }

        if (on)
        {
            G.Stroke(c, SKRect.Create(1, 1, w - 2, h - 2), 11.5f, G.Paper(0.92f), 2);
            G.Circle(c, w - 16, h - 18, 7.5f, G.Paper(0.95f));
            Icons.Draw(c, "check", w - 16, h - 18, 10, G.Ink, 3f);
        }
        c.Restore();
    }

    private void DrawBarPreview(SKCanvas c, int i, string style, SKRect stage, float t, float dt, bool over)
    {
        float left = stage.Left + 14, width = stage.Width - 28, cy = stage.Top + 25;
        // The song plays on a loop, each card a little out of step so the row does not move in unison.
        float prog = 0.06f + 0.88f * (float)((t * 0.07 + i * 0.13) % 1.0);
        float hover = over ? 1 : 0;
        float hx = over ? _mouse.X / S - (_rects[i].X / S) - left : -1;
        ProgressBars.Draw(c, _bars[i], style, left, width, cy, Demo, prog, 1, hover, hx, t, 1, true, dt, "cacad preview " + style);
        using var mono = Fonts.Mono(9);
        float total = 187, pos = total * prog;
        G.Text(c, G.FormatTime(pos), left, stage.Bottom - 8, mono, G.Paper(0.45f));
        G.Text(c, "-" + G.FormatTime(total - pos), left + width, stage.Bottom - 8, mono, G.Paper(0.45f), SKTextAlign.Right);
    }

    private static SKImage? _cover;

    /// <summary>A made-up cover for the previews: a sunset gradient with a sun.</summary>
    private static SKImage Cover()
    {
        if (_cover != null) return _cover;
        using var surface = SKSurface.Create(new SKImageInfo(48, 48, SKColorType.Bgra8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        using (var sh = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 48),
                   new[] { new SKColor(255, 120, 80), new SKColor(214, 46, 120), new SKColor(70, 20, 110) }, null, SKShaderTileMode.Clamp))
        using (var p = new SKPaint { Shader = sh })
            c.DrawRect(0, 0, 48, 48, p);
        using (var sun = new SKPaint { IsAntialias = true, Color = new SKColor(255, 214, 110) })
            c.DrawCircle(24, 30, 11, sun);
        using (var sea = new SKPaint { Color = new SKColor(40, 12, 70) })
            c.DrawRect(0, 34, 48, 14, sea);
        return _cover = surface.Snapshot();
    }

    private static void DrawVisualizerPreview(SKCanvas c, string style, SKRect stage, float t)
    {
        // A small resting pill, as it sits at the top of the screen, a little enlarged.
        const float k = 1.25f;
        float pw = 84, ph = 30;
        c.Save();
        c.Translate(stage.MidX, stage.MidY);
        c.Scale(k);
        var pill = SKRect.Create(-pw / 2, -ph / 2, pw, ph);
        G.Fill(c, pill, ph / 2, new SKColor(6, 6, 8));
        G.Stroke(c, pill, ph / 2, G.White(0.08f), 1);
        G.Image(c, Cover(), SKRect.Create(pill.Left + 5, -10, 20, 20), 1, 5);
        Visualizers.Draw(c, style, pill.Right - 9, 0, Demo, t);
        c.Restore();
    }

    /// <summary>A companion card: the chosen companion in the chosen coat, blinking now and then; it cheers when
    /// you hover it. For companion cards the other half (the coat) comes from <c>_themeDefault</c>, and vice versa.</summary>
    private void DrawCompanionPreview(SKCanvas c, int i, string value, SKRect stage, float t, bool over)
    {
        var now = CompanionNow?.Invoke() ?? ("cat", "natural", "pixel");
        string species = _kind == Kind.Companion ? value : now.Species;
        string skin = _kind == Kind.Skin ? value : now.Skin;
        string style = _kind == Kind.Style ? value : now.Style;


        const float px = 3.4f;
        bool blink = (t + i * 0.9f) % 3.7f < 0.14f;
        var pose = over ? new CatPose(CatFace.Happy, Motion: "jump") : new CatPose(CatFace.Open);
        float x = stage.MidX - 8 * px, y = stage.MidY - 5.5f * px + 1;
        CatArt.Draw(c, pose, x, y, px, 1, t + i, 0.5f, 0, blink, false, species, skin, style);
    }

    private readonly Dictionary<int, ClockFaces> _clocks = new();

    /// <summary>A clock card: the face as it sits in the pill, live.</summary>
    private void DrawClockPreview(SKCanvas c, int i, string style, SKRect stage, float t)
    {
        if (!_clocks.TryGetValue(i, out var faces)) _clocks[i] = faces = new ClockFaces();
        const float ph = 34;
        float fw = ClockFaces.Width(style, ph, false), pw = Math.Min(stage.Width - 12, fw + 34);
        c.Save();
        c.Translate(stage.MidX, stage.MidY - ph / 2);
        var pill = SKRect.Create(-pw / 2, 0, pw, ph);
        G.Fill(c, pill, ph / 2, new SKColor(6, 6, 8));
        G.Stroke(c, pill, ph / 2, G.White(0.08f), 1);
        c.ClipRoundRect(new SKRoundRect(pill, ph / 2), SKClipOperation.Intersect, true);
        faces.DrawPill(c, style, pw, ph, 1, Themes.Get(_themeDefault()), false, t, 1 / 30f);
        c.Restore();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        _mouse = e.Location;
        int h = _rects.FindIndex(r => r.Contains(e.Location));
        if (h != _hover) { _hover = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int i = _rects.FindIndex(r => r.Contains(e.Location));
        if (i < 0 || _items[i].value == Value) return;
        Value = _items[i].value;
        Invalidate();
        Changed?.Invoke(Value);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _canvas?.Dispose();
        }
        base.Dispose(disposing);
    }
}
