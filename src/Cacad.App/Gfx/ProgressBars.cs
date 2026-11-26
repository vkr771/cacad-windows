using SkiaSharp;

namespace Cacad;

/// <summary>Per-bar animation state (the comet's sparks, the song-shape waveform), so the music card and the
/// Settings previews can each run their own copy of the same bar.</summary>
public sealed class BarState
{
    internal sealed class Spark { public float X, Y, Vx, Vy, Life; }
    internal readonly List<Spark> Sparks = new();
    internal float SparkDue;
    internal float[]? Shape;
    internal string ShapeFor = "";

    public bool Busy => Sparks.Count > 0;
    public void Reset() => Sparks.Clear();
}

/// <summary>The music card's progress bar, in each of its styles.</summary>
public static class ProgressBars
{
    public static readonly (string id, string name)[] Styles =
    {
        ("wave", "Wave"), ("pixels", "Pixels"), ("comet", "Comet"), ("shape", "Song shape"), ("simple", "Simple"), ("hearts", "Hearts"),
        ("beads", "Beads"), ("segments", "Segments"), ("ribbon", "Ribbon"), ("pulse", "Heartbeat"),
    };

    /// <summary>The style a theme uses when the setting is "theme default".</summary>
    public static string ThemeDefault(string themeId) => themeId == "rose" ? "hearts" : "wave";

    private static readonly Random Rng = new();

    /// <param name="hover">0..1, how much the pointer is over the bar (it grows a little).</param>
    /// <param name="hx">pointer x from <paramref name="left"/>, or -1 when it is away.</param>
    /// <param name="playK">0 paused .. 1 playing: the bar's motion follows it.</param>
    /// <param name="seed">gives "song shape" a steady waveform per track.</param>
    public static void Draw(SKCanvas c, BarState st, string style, float left, float width, float cy, SKColor strong, float prog,
        float a, float hover, float hx, float sec, float playK, bool playing, float dt, string seed)
    {
        float head = left + width * prog, pk = playK;
        var color = strong.WithAlpha(G.A(a));

        switch (style)
        {
            case "simple":
            {
                float h = 4 + 4 * hover;
                G.Pill(c, left, cy - h / 2, width, h, G.White(0.15f * a));
                if (prog > 0) G.Pill(c, left, cy - h / 2, Math.Max(h, head - left), h, color);
                break;
            }
            case "pixels":
            {
                float step = 4.5f, px = 3;
                int n = (int)(width / step) + 1, headK = -1, headStack = 1;
                using var paint = new SKPaint();
                for (int k = 0; k < n; k++)
                {
                    float x = left + k * step, cx = x + px / 2;
                    bool lit = cx <= head;
                    float near = hx >= 0 ? MathF.Exp(-MathF.Pow((cx - left - hx) / 15, 2)) * hover : 0;
                    int stack = 1 + (int)MathF.Round(near * 2.4f);
                    if (lit && cx + step > head) { headK = k; headStack = stack; continue; }
                    float lvl = lit ? 0.62f + 0.38f * (0.5f + 0.5f * MathF.Sin(sec * 5 - k * 0.45f)) * pk + 0.2f * (1 - pk) : 0.17f + near * 0.3f;
                    paint.Color = lit ? strong.WithAlpha(G.A(Math.Min(1, lvl) * a)) : G.White(Math.Min(1, lvl) * a);
                    for (int p = 0; p < stack; p++) c.DrawRect(x, cy + px / 2 - px - p * (px + 1.5f), px, px, paint);
                }
                if (headK >= 0)
                {
                    float hx2 = left + headK * step - 0.5f;
                    float glow = 7 + 4 * (0.5f + 0.5f * MathF.Sin(sec * 4)) * pk;
                    using var gp = new SKPaint { Color = G.Paper(a), ImageFilter = SKImageFilter.CreateDropShadow(0, 0, glow / 2, glow / 2, color) };
                    for (int q = 0; q < headStack; q++) c.DrawRect(hx2, cy + 2 - 4 - q * 4.5f, 4, 4, gp);
                }
                break;
            }
            case "comet":
            {
                float ch = 3 + 3 * hover;
                G.Pill(c, left, cy - ch / 2, width, ch, G.White(0.12f * a));
                if (head > left + 1)
                {
                    using var sh = SKShader.CreateLinearGradient(new SKPoint(left, 0), new SKPoint(head, 0),
                        new[] { strong.WithAlpha(G.A(0.08f * a)), strong.WithAlpha(G.A(0.56f * a)), color }, new[] { 0f, 0.75f, 1f }, SKShaderTileMode.Clamp);
                    using var p = new SKPaint { IsAntialias = true, Shader = sh };
                    c.DrawRoundRect(SKRect.Create(left, cy - ch / 2, head - left, ch), ch / 2, ch / 2, p);
                }
                if (playing)
                {
                    st.SparkDue += dt * 24;
                    while (st.SparkDue > 1)
                    {
                        st.SparkDue--;
                        st.Sparks.Add(new BarState.Spark { X = head - left, Vx = -(8 + (float)Rng.NextDouble() * 24), Vy = ((float)Rng.NextDouble() - 0.5f) * 18, Life = 1 });
                    }
                }
                st.Sparks.RemoveAll(s => (s.Life -= dt * 1.5f) <= 0);
                using (var sp = new SKPaint())
                    foreach (var s in st.Sparks)
                    {
                        s.X += s.Vx * dt; s.Y += s.Vy * dt; s.Vy *= 0.95f;
                        float z = 0.5f + 1.6f * s.Life;
                        sp.Color = strong.WithAlpha(G.A(s.Life * 0.9f * a));
                        c.DrawRect(left + s.X - z / 2, cy + s.Y - z / 2, z, z, sp);
                    }
                float beat = MathF.Pow(Math.Max(0, MathF.Sin(sec * MathF.PI * 2)), 10) * pk, r = (3.4f + 1.6f * hover) * (1 + 0.3f * beat);
                using (var hp = new SKPaint { IsAntialias = true, Color = G.White(a), ImageFilter = SKImageFilter.CreateDropShadow(0, 0, (10 + 8 * beat) / 2, (10 + 8 * beat) / 2, color) })
                    c.DrawCircle(head, cy, r, hp);
                break;
            }
            case "shape":
            {
                var bars = SongShape(st, seed);
                int cnt = bars.Length;
                float stp = width / cnt, bw = Math.Max(1.6f, stp - 1.4f), reach = hx >= 0 ? left + hx : -1;
                for (int b = 0; b < cnt; b++)
                {
                    float bx = left + b * stp, bcx = bx + bw / 2;
                    float bounce = Math.Max(0, 1 - Math.Abs(bcx - head) / 9) * 0.4f * Math.Abs(MathF.Sin(sec * 9 + b)) * pk;
                    float bh = Math.Max(2.2f, (bars[b] + bounce) * (12 + 4 * hover));
                    G.Pill(c, bx, cy - bh / 2, bw, bh, bcx <= head ? color : bcx <= reach ? strong.WithAlpha(G.A(0.44f * a)) : G.White(0.22f * a));
                }
                break;
            }
            case "hearts":
            {
                int n2 = Math.Max(2, (int)(width / 11));
                float gap = width / (n2 - 1);
                int hi = (int)MathF.Round(prog * (n2 - 1));
                for (int j = 0; j < n2; j++)
                {
                    float x2 = left + j * gap, nr = hx >= 0 ? MathF.Exp(-MathF.Pow((x2 - left - hx) / 16, 2)) * hover : 0, sc = 1 + nr * 0.6f;
                    if (j == hi) { float bt = 1 + 0.18f * Math.Max(0, MathF.Sin(sec * 7.5f)) * pk; Ambient.Heart(c, x2, cy, 10.5f * bt * sc, G.Paper(a), true); continue; }
                    if (j < hi) Ambient.Heart(c, x2, cy + MathF.Sin(sec * 4 - j * 0.5f) * 1.4f * pk, 7.5f * sc, color, true);
                    else Ambient.Heart(c, x2, cy, 7.5f * sc, G.White((0.25f + 0.25f * nr) * a), false);
                }
                break;
            }
            case "beads":
            {
                // A string of beads: played ones in colour, breathing in a slow wave; the one being played is a
                // bright pearl; beads near the pointer swell like a magnifier.
                int n = Math.Max(4, (int)(width / 7.5f));
                float gap = width / (n - 1);
                int at = (int)MathF.Round(prog * (n - 1));
                for (int j = 0; j < n; j++)
                {
                    float x = left + j * gap;
                    float near = hx >= 0 ? MathF.Exp(-MathF.Pow((x - left - hx) / 14, 2)) * hover : 0;
                    float r = 1.9f * (1 + near * 0.9f);
                    if (j == at) { G.Circle(c, x, cy, 4.2f + hover, G.Paper(a)); continue; }
                    if (j < at) G.Circle(c, x, cy, r * (1 + 0.25f * MathF.Sin(sec * 3 - j * 0.6f) * pk), color);
                    else G.Circle(c, x, cy, r * 0.85f, G.White((0.2f + 0.25f * near) * a));
                }
                break;
            }
            case "segments":
            {
                // Short rounded segments like a level meter; the current one fills as it plays.
                int n = Math.Max(6, (int)(width / 12));
                float gap = 2.5f, sw = (width - gap * (n - 1)) / n, sh = 5 + 3 * hover;
                float fill = prog * n;
                for (int j = 0; j < n; j++)
                {
                    float x = left + j * (sw + gap);
                    float near = hx >= 0 ? MathF.Exp(-MathF.Pow((x + sw / 2 - left - hx) / 18, 2)) * hover : 0;
                    float lift = near * 2.5f;
                    var r = SKRect.Create(x, cy - sh / 2 - lift, sw, sh + lift * 2);
                    G.Fill(c, r, sh / 2, G.White((0.13f + 0.12f * near) * a));
                    float part = Math.Clamp(fill - j, 0, 1);
                    if (part > 0)
                    {
                        c.Save();
                        c.ClipRect(SKRect.Create(x, r.Top - 1, sw * part, r.Height + 2));
                        G.Fill(c, r, sh / 2, j == (int)fill ? strong.WithAlpha(G.A((0.65f + 0.35f * MathF.Sin(sec * 6) * pk) * a)) : color);
                        c.Restore();
                    }
                }
                break;
            }
            case "ribbon":
            {
                // A thick ribbon of colour with light running along it, ending in a soft glow, no knob.
                float rh = 6 + 3 * hover;
                G.Pill(c, left, cy - rh / 2, width, rh, G.White(0.1f * a));
                if (head > left + 1)
                {
                    var lighter = new SKColor((byte)Math.Min(255, strong.Red + 90), (byte)Math.Min(255, strong.Green + 90), (byte)Math.Min(255, strong.Blue + 90));
                    using (var sh = SKShader.CreateLinearGradient(new SKPoint(left, 0), new SKPoint(head, 0),
                               new[] { strong.WithAlpha(G.A(0.35f * a)), color, lighter.WithAlpha(G.A(a)) }, new[] { 0f, 0.7f, 1f }, SKShaderTileMode.Clamp))
                    using (var p = new SKPaint { IsAntialias = true, Shader = sh })
                        c.DrawRoundRect(SKRect.Create(left, cy - rh / 2, head - left, rh), rh / 2, rh / 2, p);
                    // The shimmer: a bright band sweeping along the played part.
                    float sweep = left + ((sec * 70) % Math.Max(40, head - left + 40)) - 20;
                    c.Save();
                    c.ClipRoundRect(new SKRoundRect(SKRect.Create(left, cy - rh / 2, head - left, rh), rh / 2), SKClipOperation.Intersect, true);
                    using (var sh = SKShader.CreateLinearGradient(new SKPoint(sweep - 20, 0), new SKPoint(sweep + 20, 0),
                               new[] { G.White(0), G.White(0.45f * a * pk), G.White(0) }, null, SKShaderTileMode.Clamp))
                    using (var p = new SKPaint { Shader = sh })
                        c.DrawRect(SKRect.Create(left, cy - rh, head - left, rh * 2), p);
                    c.Restore();
                    using var glow = new SKPaint { IsAntialias = true, Color = lighter.WithAlpha(G.A(0.8f * a)), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5) };
                    c.DrawCircle(head - rh / 2, cy, rh * 0.8f, glow);
                }
                break;
            }
            case "pulse":
            {
                // A heart monitor: the played part is a trace with a beat every so often; the trace's pen is the head.
                float lw = 2.1f + hover;
                using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = lw, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
                p.Color = G.White(0.16f * a);
                if (head < left + width) c.DrawLine(head, cy, left + width, cy, p);
                if (head > left + 1)
                {
                    using var path = new SKPath();
                    path.MoveTo(left, cy);
                    const float period = 34;
                    float scroll = (sec * 26 * pk) % period;
                    for (float x = left; x <= head; x += 1)
                    {
                        float u = ((x - left + scroll) % period) / period;
                        // One beat per period: a small bump, a sharp spike up and down, a small dip.
                        float y = u switch
                        {
                            < 0.40f => 0,
                            < 0.46f => -2 * MathF.Sin((u - 0.40f) / 0.06f * MathF.PI),
                            < 0.50f => 0,
                            < 0.53f => -(u - 0.50f) / 0.03f * 8,
                            < 0.57f => -8 + (u - 0.53f) / 0.04f * 12,
                            < 0.60f => 4 - (u - 0.57f) / 0.03f * 4,
                            _ => 0,
                        };
                        // The trace calms near the start, so it reads as the song's past.
                        float fade = 1.25f * Math.Clamp((x - left) / 30, 0, 1);
                        path.LineTo(x, cy + y * fade);
                    }
                    using (var sh = SKShader.CreateLinearGradient(new SKPoint(left, 0), new SKPoint(head, 0),
                               new[] { strong.WithAlpha(G.A(0.5f * a)), color }, null, SKShaderTileMode.Clamp))
                    {
                        p.Color = SKColors.White;
                        p.Shader = sh;
                        c.DrawPath(path, p);
                        p.Shader = null;
                    }
                }
                using (var glow = new SKPaint { IsAntialias = true, Color = color, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3) })
                    c.DrawCircle(head, cy, 4 + hover, glow);
                G.Circle(c, head, cy, 2.6f + hover * 0.6f, G.Paper(a));
                break;
            }
            default: // wave: the played part ripples while playing and lies flat when paused
            {
                float lw = 2.6f + 1.4f * hover, amp = 2.8f * pk;
                using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = lw, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
                if (head + 7 < left + width)
                {
                    p.Color = G.White(0.18f * a);
                    c.DrawLine(head + 7, cy, left + width, cy, p);
                }
                if (head > left + 1)
                {
                    p.Color = color;
                    using var path = new SKPath();
                    for (float wx = left; wx <= head; wx += 0.75f)
                    {
                        float tp = Math.Min(1, (head - wx) / 10);
                        float wy = cy + MathF.Sin((wx - left) / 20 * MathF.PI * 2 - sec * 4.4f) * amp * tp;
                        if (wx == left) path.MoveTo(wx, wy); else path.LineTo(wx, wy);
                    }
                    path.LineTo(head, cy);
                    c.DrawPath(path, p);
                }
                float hh = 11 + 3 * hover, hw = 3.4f + hover;
                G.Pill(c, head - hw / 2, cy - hh / 2, hw, hh, G.Paper(a));
                break;
            }
        }
    }

    /// <summary>A made-up but steady waveform per title.</summary>
    private static float[] SongShape(BarState st, string seed)
    {
        if (st.Shape != null && st.ShapeFor == seed) return st.Shape;
        uint x = 7;
        foreach (char ch in seed) x = x * 31 + ch;
        float Next() { x = x * 1664525 + 1013904223; return x / 4294967296f; }
        var outp = new float[96];
        float v = 0.5f;
        for (int k = 0; k < 96; k++)
        {
            v = Math.Clamp(v + (Next() - 0.5f) * 0.45f, 0.15f, 1);
            float env = Math.Min(1, Math.Min((k + 2) / 10f, (96 - k) / 8f + 0.2f));
            outp[k] = Math.Max(0.14f, v * (0.6f + 0.4f * Next()) * env);
        }
        st.ShapeFor = seed;
        return st.Shape = outp;
    }
}

/// <summary>What plays beside the artwork on the resting pill, in each style. Drawn right-aligned at
/// <c>right</c>, about 24 DIPs wide, centred on <c>cy</c>.</summary>
public static class Visualizers
{
    public static readonly (string id, string name)[] Styles =
    {
        ("pixels", "Pixels"), ("bars", "Bars"), ("line", "Line"), ("dots", "Dots"),
        ("mirror", "Mirror"), ("ring", "Ring"), ("blob", "Blob"), ("peaks", "Peaks"), ("heart", "Heart"),
    };

    /// <summary>The real sound, per column (0..1), or null when none is arriving. Set by the island.</summary>
    public static Func<int, float?>? Live;

    /// <summary>The Peaks style's floating caps (they fall slowly back to their bars).</summary>
    private static readonly float[] PeakCap = new float[5];

    /// <summary>The level of one column: the real sound's band when it is arriving, otherwise a lively,
    /// steady pattern.</summary>
    private static float Level(float t, int i)
    {
        if (Live?.Invoke(i) is { } real) return 0.12f + 0.88f * real;
        float ph = t * (1.6f + i * 0.37f) + i * 1.3f;
        return 0.35f + 0.65f * (0.5f + 0.5f * MathF.Sin(ph) * MathF.Cos(ph * 0.53f));
    }

    public static void Draw(SKCanvas c, string style, float right, float cy, SKColor tint, float t)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = tint };
        switch (style)
        {
            case "bars":
                for (int i = 0; i < 4; i++)
                {
                    float bh = Math.Max(3, 16 * Level(t, i)), bx = right - (4 - i) * 6;
                    c.DrawRoundRect(SKRect.Create(bx, cy - bh / 2, 3, bh), 1.5f, 1.5f, paint);
                }
                break;
            case "line":
            {
                // An oscilloscope trace: a few summed waves whose loudness follows the levels.
                using var path = new SKPath();
                float w = 24, x0 = right - w;
                for (int k = 0; k <= 24; k++)
                {
                    float x = x0 + w * k / 24f, u = k / 24f;
                    float env = MathF.Sin(u * MathF.PI);
                    float y = cy + env * 6.5f * (MathF.Sin(u * 13 + t * 9) * Level(t, 0) * 0.6f + MathF.Sin(u * 23 - t * 13) * Level(t, 2) * 0.4f);
                    if (k == 0) path.MoveTo(x, y); else path.LineTo(x, y);
                }
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1.8f;
                paint.StrokeCap = SKStrokeCap.Round;
                paint.StrokeJoin = SKStrokeJoin.Round;
                c.DrawPath(path, paint);
                break;
            }
            case "dots":
                // Four dots bouncing to the beat, each with a fading echo below it.
                for (int i = 0; i < 4; i++)
                {
                    float lvl = Level(t, i), x = right - (4 - i) * 6 + 1.5f, y = cy + 6 - lvl * 12;
                    paint.Color = tint.WithAlpha((byte)(tint.Alpha * 0.25f));
                    c.DrawCircle(x, cy + 6, 1.4f, paint);
                    paint.Color = tint;
                    c.DrawCircle(x, y, 2.1f, paint);
                }
                break;
            case "mirror":
            {
                // Seven thin bars mirrored about the middle line, tallest in the centre.
                for (int i = 0; i < 7; i++)
                {
                    float lvl = Level(t, Math.Min(3, Math.Abs(i - 3))) * (1 - Math.Abs(i - 3) * 0.12f);
                    float bh = Math.Max(2, 18 * lvl), bx = right - 24 + i * 3.5f;
                    c.DrawRoundRect(SKRect.Create(bx, cy - bh / 2, 2, bh), 1, 1, paint);
                }
                break;
            }
            case "ring":
            {
                // A ring that swells with the bass, sending out echoes on the beat.
                float cx = right - 11, bass = Level(t, 0), r = 4 + 3.5f * bass;
                paint.Style = SKPaintStyle.Stroke;
                for (int k = 0; k < 2; k++)
                {
                    float life = (float)((t * 1.2 + k * 0.5) % 1.0);
                    paint.StrokeWidth = 1.2f;
                    paint.Color = tint.WithAlpha((byte)(tint.Alpha * (1 - life) * 0.6f));
                    c.DrawCircle(cx, cy, r + life * 7, paint);
                }
                paint.StrokeWidth = 2.2f;
                paint.Color = tint;
                c.DrawCircle(cx, cy, r, paint);
                paint.Style = SKPaintStyle.Fill;
                c.DrawCircle(cx, cy, 1.6f + Level(t, 3) * 1.4f, paint);
                break;
            }
            case "blob":
            {
                // A drop of liquid, its edge pushed out by each band in turn, slowly turning.
                float cx = right - 11, rad = 8f;
                using var path = new SKPath();
                for (int k = 0; k <= 48; k++)
                {
                    float ang = k / 48f * MathF.PI * 2;
                    float bump = 0;
                    for (int b = 0; b < 4; b++)
                        bump += Level(t, b) * 0.45f * MathF.Pow(Math.Max(0, MathF.Cos(ang - b * MathF.PI / 2 - t * 0.9f)), 2);
                    float rr = rad * (0.62f + bump);
                    float x = cx + MathF.Cos(ang) * rr, y = cy + MathF.Sin(ang) * rr;
                    if (k == 0) path.MoveTo(x, y); else path.LineTo(x, y);
                }
                path.Close();
                using (var g = SKShader.CreateRadialGradient(new SKPoint(cx - 2, cy - 2), rad * 1.4f,
                           new[] { new SKColor(255, 255, 255, tint.Alpha), tint }, new[] { 0f, 0.6f }, SKShaderTileMode.Clamp))
                {
                    paint.Shader = g;
                    c.DrawPath(path, paint);
                    paint.Shader = null;
                }
                break;
            }
            case "peaks":
            {
                // An equaliser: bars with caps that are knocked up and float back down.
                for (int i = 0; i < 5; i++)
                {
                    float lvl = Level(t, Math.Min(3, i * 3 / 4)), bh = Math.Max(2, 15 * lvl), bx = right - (5 - i) * 4.8f;
                    float bottom = cy + 8;
                    c.DrawRect(bx, bottom - bh, 3, bh, paint);
                    PeakCap[i] = Math.Max(PeakCap[i] - 0.35f, bh);
                    paint.Color = tint.WithAlpha((byte)(tint.Alpha * 0.85f));
                    c.DrawRect(bx, bottom - PeakCap[i] - 2.5f, 3, 1.4f, paint);
                    paint.Color = tint;
                }
                break;
            }
            case "heart":
            {
                // A heart beating with the bass.
                float s = 11 + 5 * Level(t, 0);
                Ambient.Heart(c, right - 11, cy, s, tint, true);
                break;
            }
            default: // pixels: little stacks of squares
                for (int i = 0; i < 4; i++)
                {
                    int count = Math.Clamp((int)MathF.Round(Level(t, i) * 4), 1, 4);
                    float x = right - (4 - i) * 5.5f, bottom = cy + (4 * 4.5f - 1.5f) / 2;
                    for (int p = 0; p < count; p++) c.DrawRect(x, bottom - (p + 1) * 3 - p * 1.5f, 3, 3, paint);
                }
                break;
        }
    }
}
