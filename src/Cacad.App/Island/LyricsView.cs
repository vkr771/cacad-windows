using SkiaSharp;

namespace Cacad;

/// <summary>An Apple Music–style Lyrics View: the song's lines as a list that scrolls to keep the line being sung in
/// place. Each line moves on its own spring, and lines below the active one start a beat later, so a change ripples
/// down the list like a wave. The active line is full size and bright, filling from dim to white as it is sung;
/// upcoming lines are dimmer, sung lines dimmer still and slightly out of focus, and focus falls off with distance.
/// Long instrumental breaks show three dots that fill one by one over the break and breathe.
/// Used large in the card (left-aligned, wrapping) and small in the pill (centred, one row per line).</summary>
public sealed class LyricsView
{
    /// <param name="Unsung">brightness of the words not sung yet on the active line.</param>
    /// <param name="Shadow">a soft dark halo under the text, so it reads over bright glass and pictures.</param>
    public sealed record Style(float Size, float Gap, bool Wrap, bool Center, float Anchor, float MaxBlur, float SideScale, float Unsung = 0.45f, bool Shadow = false);

    public static readonly Style Card = new(19.5f, 10, true, false, 0.30f, 3.2f, 0.94f);
    public static readonly Style Pill = new(12.5f, 4, false, true, 0.5f, 1.6f, 0.92f, Unsung: 0.66f, Shadow: true);

    private sealed class Row
    {
        public string[] Text = Array.Empty<string>(); // wrapped
        public SKTypeface Face = Fonts.SemiBold;
        public float Size, Height, LayoutY;           // layout
        public readonly Spring Y = new(0, (170, 24), 0.05);     // drawn offset from the anchor
        public readonly Spring Focus = new(0, (230, 30), 0.002); // 1 = the active line
        public double MoveAt;                          // when this row starts towards its new place (the wave)
        public float PendingY;
        public SKRect Hit;
    }

    private readonly Style _s;
    private LyricsService.Song? _song;
    private float _width;
    private List<Row> _rows = new();
    private int _active = -2;
    private float _manual;           // the list scrolled by hand (wheel), springing back after a pause
    private double _manualAt = -10;
    private readonly Spring _manualSpring = new(0, (120, 22), 0.1);
    private double _last;

    public LyricsView(Style s) => _s = s;

    public bool Moving { get; private set; }

    private void Layout(LyricsService.Song song, float width)
    {
        _song = song;
        _width = width;
        _rows = new List<Row>();
        float y = 0;
        foreach (var line in song.Lines)
        {
            var row = new Row();
            string text = line.Text.Length == 0 ? "" : line.Text;
            using var font = WorldText.Font(text.Length > 0 ? text : "a", _s.Size, Fonts.SemiBold);
            row.Face = font.Typeface;
            row.Size = font.Size;
            row.Text = text.Length == 0 ? new[] { "" } : _s.Wrap ? Wrap(text, font, width) : new[] { WorldText.Fit(text, font, width) };
            float lh = font.Size * 1.22f;
            row.Height = text.Length == 0 ? font.Size * 0.9f : row.Text.Length * lh;
            row.LayoutY = y;
            y += row.Height + _s.Gap;
            _rows.Add(row);
        }
        _active = -2;
    }

    private static string[] Wrap(string text, SKFont font, float width)
    {
        var lines = new List<string>();
        string cur = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string tryLine = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length > 0 && WorldText.Measure(font, tryLine) > width) { lines.Add(cur); cur = word; }
            else cur = tryLine;
        }
        if (cur.Length > 0) lines.Add(cur);
        return lines.Count == 0 ? new[] { "" } : lines.ToArray();
    }

    /// <summary>Scroll by hand; the view returns to the sung line two and a half seconds after the last nudge.</summary>
    public void Scroll(float dy, double now)
    {
        _manual += dy;
        _manualAt = now;
        _manualSpring.Snap(_manual);
    }

    /// <summary>The line under a point (from the last frame), to jump the song there.</summary>
    public LyricsService.Line? LineAt(SKPoint p)
    {
        if (_song == null) return null;
        for (int i = 0; i < _rows.Count; i++)
            if (_rows[i].Hit.Contains(p) && _song.Lines[i].Text.Length > 0) return _song.Lines[i];
        return null;
    }

    /// <summary>Draws the view into <paramref name="box"/>. <paramref name="pos"/> is the song position in seconds;
    /// <paramref name="now"/> the island's clock.</summary>
    public void Draw(SKCanvas c, LyricsService.Song song, SKRect box, double pos, double now, float a)
    {
        float inner = _s.Center ? box.Width : box.Width - 8;
        if (!ReferenceEquals(song, _song) || Math.Abs(inner - _width) > 0.5f) Layout(song, inner);
        double dt = Math.Clamp(now - _last, 0, 0.1);
        _last = now;

        var (idx, line) = LyricsService.At(song, pos);
        int active = Math.Max(0, idx);
        if (active != _active)
        {
            bool first = _active == -2;
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                r.PendingY = r.LayoutY - _rows[active].LayoutY;
                // The wave: rows from the new line down start moving one after another; rows above move at once.
                r.MoveAt = now + (first ? 0 : Math.Clamp(i - active + 1, 0, 8) * 0.045);
                if (first) r.Y.Snap(r.PendingY);
                r.Focus.To(i == active ? 1 : 0);
                if (first) r.Focus.Snap(i == active ? 1 : 0);
            }
            _active = active;
        }

        // Hand scrolling eases back to the sung line after a pause.
        if (now - _manualAt > 2.5) { _manualSpring.To(0); _manual = (float)_manualSpring.Value; }
        bool moving = _manualSpring.Step(dt);
        if (now - _manualAt > 2.5) _manual = (float)_manualSpring.Value;

        float anchorY = box.Top + box.Height * _s.Anchor - (_s.Center ? _rows[active].Height / 2 : 0);
        var paper = G.PaperBase;

        c.Save();
        c.ClipRect(box);
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            if (now >= r.MoveAt && r.Y.Target != r.PendingY) r.Y.To(r.PendingY);
            moving |= r.Y.Step(dt) | r.Focus.Step(dt);
            float top = anchorY + (float)r.Y.Value + _manual;
            if (top > box.Bottom + 4 || top + r.Height < box.Top - 4) { r.Hit = SKRect.Empty; continue; }

            float f = (float)Math.Clamp(r.Focus.Value, 0, 1);
            int d = i - active;
            // Upcoming lines are a little brighter than sung ones; both fall off with distance.
            float rest = d > 0 ? 0.42f - 0.05f * Math.Min(d - 1, 4) : 0.30f - 0.03f * Math.Min(-d - 1, 4);
            float alpha = rest + (1 - rest) * f;
            float blur = (1 - f) * Math.Min(_s.MaxBlur, (Math.Abs(d) - (d > 0 ? 0.6f : 0)) * _s.MaxBlur / 3f);
            float scale = _s.SideScale + (1 - _s.SideScale) * f;

            // Edge fade: rows fade out as they reach the top and bottom of the box.
            float edge = Math.Min(top + r.Height / 2 - box.Top, box.Bottom - (top + r.Height / 2));
            float fadeZone = _s.Center ? box.Height * 0.5f : 28;
            alpha *= Math.Clamp(edge / fadeZone + (_s.Center ? 0.15f : 0.2f), 0, 1);
            if (alpha * a < 0.01f) { r.Hit = SKRect.Empty; continue; }

            using var font = new SKFont(r.Face, r.Size);
            float lh = r.Size * 1.22f;
            float px = _s.Center ? box.MidX : box.Left;
            r.Hit = SKRect.Create(box.Left, top, box.Width, r.Height);

            c.Save();
            // Scale about the line's leading edge (card) or centre (pill), as the list does.
            c.Translate(px, top + r.Height / 2);
            c.Scale(scale);
            c.Translate(-px, -(top + r.Height / 2));
            using var filter = blur > 0.2f ? SKImageFilter.CreateBlur(blur, blur) : null;
            using var layer = new SKPaint { ImageFilter = filter, Color = SKColors.White.WithAlpha(G.A(alpha * a)) };
            var bounds = SKRect.Create(box.Left - 10, top - 8, box.Width + 20, r.Height + 16);
            c.SaveLayer(bounds, layer);

            if (song.Lines[i].Text.Length == 0)
                Interlude(c, song, i, px, top + r.Height / 2, pos, now, f, _s.Center, _s.Size);
            else
            {
                // The active line fills from dim to white as it is sung.
                float fill = i == active && line != null ? Sung(song, i, pos) : i < active ? 1 : 0;
                for (int k = 0; k < r.Text.Length; k++)
                {
                    string s = r.Text[k];
                    float tw = WorldText.Measure(font, s);
                    float x = _s.Center ? px - tw / 2 : px;
                    float y = top + k * lh + r.Size * 0.95f;
                    if (_s.Shadow) WorldText.Shadow(c, s, x, y, font);
                    if (i != active) { WorldText.Draw(c, s, x, y, font, paper); continue; }
                    // Which part of the wipe this wrapped row covers.
                    float k0 = (float)k / r.Text.Length, k1 = (float)(k + 1) / r.Text.Length;
                    float rowFill = Math.Clamp((fill - k0) / (k1 - k0), 0, 1);
                    WorldText.Draw(c, s, x, y, font, paper.WithAlpha(G.A(_s.Unsung + (1 - _s.Unsung) * (1 - f))));
                    if (rowFill <= 0) continue;
                    var rowBox = SKRect.Create(x - 4, y - r.Size * 1.1f, tw + 8, r.Size * 1.5f);
                    c.SaveLayer(rowBox, null);
                    WorldText.Draw(c, s, x, y, font, paper);
                    float soft = Math.Min(22, tw * 0.25f + 4), edgeX = x - soft + rowFill * (tw + soft * 2);
                    using var mask = SKShader.CreateLinearGradient(new SKPoint(edgeX - soft, 0), new SKPoint(edgeX, 0),
                        new[] { SKColors.White, SKColors.Transparent }, null, SKShaderTileMode.Clamp);
                    using var mp = new SKPaint { Shader = mask, BlendMode = SKBlendMode.DstIn };
                    c.DrawRect(rowBox, mp);
                    c.Restore();
                }
            }
            c.Restore();
            c.Restore();
        }
        c.Restore();
        Moving = moving;
    }

    /// <summary>How much of line i has been sung, 0..1. LRC gives only when a line starts, so the singing is spread
    /// over the time before the next line, but no longer than its words take (a long gap after a short line is a
    /// held note or a pause).</summary>
    public static float Sung(LyricsService.Song song, int i, double pos)
    {
        var line = song.Lines[i];
        double next = i + 1 < song.Lines.Count ? song.Lines[i + 1].At : line.At + 5;
        double dur = Math.Clamp(Math.Min((next - line.At) * 0.9, 0.5 + line.Text.Length * 0.085), 0.4, 12);
        return (float)Math.Clamp((pos + 0.25 - line.At) / dur, 0, 1);
    }

    /// <summary>An instrumental break: three dots that fill in turn over the break, the group breathing; it swells
    /// and fades just before the singing comes back.</summary>
    private static void Interlude(SKCanvas c, LyricsService.Song song, int i, float x, float cy, double pos, double now, float focus, bool center, float size)
    {
        var line = song.Lines[i];
        double end = i + 1 < song.Lines.Count ? song.Lines[i + 1].At : line.At + 4;
        float p = (float)Math.Clamp((pos - line.At) / Math.Max(0.5, end - line.At), 0, 1);
        float breathe = 1 + 0.08f * MathF.Sin((float)now * 2.6f) * focus;
        float exit = p > 0.92f ? 1 + (p - 0.92f) / 0.08f * 0.25f : 1;
        float exitA = p > 0.92f ? 1 - (p - 0.92f) / 0.08f : 1;
        float r = size * 0.17f, gap = size * 0.56f;
        float x0 = center ? x - gap : x + r;
        c.Save();
        c.Translate(x0 + gap, cy);
        c.Scale(breathe * exit);
        c.Translate(-(x0 + gap), -cy);
        for (int k = 0; k < 3; k++)
        {
            float lit = Math.Clamp(p * 3 - k, 0, 1);
            using var paint = new SKPaint { IsAntialias = true, Color = G.Paper((0.3f + 0.7f * lit) * exitA) };
            c.DrawCircle(x0 + k * gap, cy, r, paint);
        }
        c.Restore();
    }
}
