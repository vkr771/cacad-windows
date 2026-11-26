using SkiaSharp;

namespace Cacad;

/// <summary>Art styles for the animal companions. Pixel is the original grid art; the others draw the same animals
/// smooth, in grid units on the same 16 x 11 footprint, so every pose, prop, bubble and hat still fits:
///  - Toon: a flat ink silhouette, head and neck in one shape, tall pointed ears, big round white eyes with small
///    upright pupils, a tiny pale nose, long hairline whiskers and (mostly) no mouth. The eyes do all the acting.
///  - Cute: kawaii. A soft mochi head in pastel, small bead eyes set low and wide apart, a tiny mouth right between
///    them, round blush beside them, and a thick soft outline in a deeper shade of the coat.
///  - Sketch: a chalk doodle on the dark island. Line art only (a faint wash of the coat), loose strokes that don't
///    quite close and overshoot where they meet, re-drawn a few times a second, dot eyes, scribbled blush.
/// The anime girls have their own smooth art and ignore the style.</summary>
public static partial class CatArt
{
    public static readonly (string Id, string Name)[] Styles =
    {
        ("pixel", "Pixel"), ("anime", "Anime"), ("toon", "Toon"), ("cute", "Cute"), ("sketch", "Sketch"),
    };

    private static string _style = "pixel";
    private static SKColor _handC = new(255, 226, 206);
    private static int _boil;                     // sketch: which wobble the lines have this moment

    // Where the face sits, per style (grid units).
    private static float _eyeL = 5.3f, _eyeR = 10.7f, _eyeY = 7.1f, _noseY = 8.4f, _mY = 9.3f, _mk = 1;
    private static float _strokeK = 1;           // stroke width correction inside the scaled mouth space

    /// <summary>The coat as this style paints it.</summary>
    private static SKColor Coat => _style switch
    {
        // The toon cat in its own coat is ink black.
        "toon" when _sp.Id == "cat" && ReferenceEquals(_sk, _sp.Natural) => new SKColor(14, 14, 18),
        "cute" => Mix(_sk.Body, SKColors.White, Light(_sk.Body) ? 0.2f : 0.12f),
        _ => _sk.Body,
    };
    /// <summary>Whether the features sit on a dark coat (a girl's features sit on her face, which is light).</summary>
    private static bool Dark => !_girl && !Light(Coat);
    /// <summary>The chalk line of the sketch style: the coat, very pale.</summary>
    private static SKColor Chalk => _girl ? Mix(_sk.Body, new SKColor(44, 38, 48), 0.78f) : Mix(_sk.Body, new SKColor(250, 248, 242), 0.68f);   // the girls: graphite on paper
    /// <summary>Eyes, mouth and whisker lines.</summary>
    private static SKColor Feature => _style switch
    {
        "sketch" => _girl ? new SKColor(44, 38, 48) : Mix(Chalk, SKColors.White, 0.4f),
        _ when _girl => Ink,
        "cute" => Dark ? new SKColor(246, 244, 240) : Mix(_sk.Body, new SKColor(50, 26, 34), 0.8f),
        _ => Dark ? new SKColor(246, 244, 240) : new SKColor(30, 26, 32),
    };
    /// <summary>The outline that keeps the head visible on the dark island.</summary>
    private static SKColor Edge => _style switch
    {
        "cute" => Dark ? _sk.Rim : Mix(_sk.Body, new SKColor(60, 34, 42), 0.62f),
        "sketch" => Chalk,
        _ => Dark ? _sk.Rim : Mix(_sk.Body, SKColors.Black, 0.42f),
    };

    private static void Layout()
    {
        if (_girl)
        {
            switch (_style)
            {
                case "toon": _eyeL = 5.4f; _eyeR = 10.6f; _eyeY = 7.3f; _noseY = 9.0f; _mY = 9.45f; _mk = 0.8f; break;
                // Chibi: features low and close together.
                case "cute": _eyeL = 5.4f; _eyeR = 10.6f; _eyeY = 7.9f; _noseY = 8.6f; _mY = 8.75f; _mk = 0.75f; break;
                default: _eyeL = 5.4f; _eyeR = 10.6f; _eyeY = 7.4f; _noseY = 9.0f; _mY = 9.5f; _mk = 0.9f; break;
            }
            return;
        }
        switch (_style)
        {
            case "toon": _eyeL = 5.5f; _eyeR = 10.5f; _eyeY = 5.3f; _noseY = 7.15f; _mY = 7.6f; _mk = 0.7f; break;
            case "cute": _eyeL = 4.6f; _eyeR = 11.4f; _eyeY = 7.9f; _noseY = 8.0f; _mY = 8.0f; _mk = 0.8f; break;
            case "anime": _eyeL = EyeL; _eyeR = EyeR; _eyeY = EyeY; _noseY = 8.7f; _mY = 9.25f; _mk = 0.85f; break;
            default: _eyeL = 5.3f; _eyeR = 10.7f; _eyeY = 6.9f; _noseY = 8.4f; _mY = 9.1f; _mk = 0.9f; break;
        }
    }

    private static void DrawStyled(SKCanvas c, CatPose pose, CatFace face, bool ball, float x, float y, float px, float a, double t, bool earTwitch)
    {
        c.Save();
        c.Translate(x, y);
        c.Scale(px);
        _girl = false;
        Layout();
        _boil = (int)(t * 4.5) % 5;
        _handC = Mix(Coat, SKColors.White, Dark ? 0.15f : 0.05f);
        if (ball) { Loaf(c, a, t); c.Restore(); return; }

        if (pose.Worn is "headphones" or "headset") Band(c, a);
        using var head = HeadShape(earTwitch, out var inner);
        if (_sp.Id == "panda") PandaEars(c, a);
        PaintHead(c, head, a);
        if (inner != null)
        {
            if (_style == "sketch") Doodle(c, inner, Chalk, 0.6f * a, 0.16f, 0.06f);
            else using (var ip = Fill(_style == "cute" ? Mix(_sk.Ear, SKColors.White, 0.3f) : _sk.Ear, a)) c.DrawPath(inner, ip);
            inner.Dispose();
        }
        Markings(c, head, a);
        if (_sp.Id == "dog") FloppyEars(c, a, t);
        if (_sp.Whiskers) StyledWhiskers(c, a);
        StyledFace(c, face, a, t);
        c.Save();
        if (pose.Worn == "glasses") c.Translate(0, _eyeY - EyeY);
        Worn(c, pose.Worn, a, t);
        c.Restore();
        if (HatId != null && pose.Worn is not ("hardhat" or "partyhat")) GirlHat(c, HatId, a, t);
        c.Restore();
    }

    // ---------- the head ----------

    /// <summary>The head and its ears as one outline (so the edge wraps round both), plus the inner ears.</summary>
    private static SKPath HeadShape(bool twitch, out SKPath? inner)
    {
        var face = new SKPath();
        bool toon = _style == "toon", cute = _style == "cute";
        if (toon) ToonBust(face);
        else if (cute) face.AddRoundRect(SKRect.Create(1.0f, 2.3f, 14.0f, 9.1f), 6.0f, 4.5f);   // a mochi
        else face.AddOval(SKRect.Create(1.5f, 1.9f, 13.0f, 9.4f));

        var ears = new SKPath();
        inner = toon ? null : new SKPath();     // a silhouette has no inner ears
        float tw = twitch ? 0.9f : 0;
        void Pair(Action<SKPath, float, float> draw, SKPath? target)
        {
            if (target == null) return;
            // Mirror a left-side shape to the right (the twitch only bends the left ear).
            draw(target, 1, tw);
            using var right = new SKPath();
            draw(right, -1, 0);
            right.Transform(SKMatrix.CreateTranslation(16, 0));
            target.AddPath(right);
        }
        switch (_sp.Id)
        {
            case "cat":
                if (toon) Pair((p, s, d) => Ear(p, s, 2.9f, 4.2f, 2.5f + d * 0.4f, -2.6f + d, 7.4f, 1.7f, false), ears);
                else if (cute)
                {
                    Pair((p, s, d) => Ear(p, s, 2.0f, 4.8f, 2.5f + d * 0.4f, 0.7f + d, 6.6f, 2.6f, true), ears);
                    Pair((p, s, d) => Ear(p, s, 2.9f, 4.2f, 3.1f + d * 0.4f, 1.8f + d, 5.6f, 3.0f, true), inner);
                }
                else
                {
                    Pair((p, s, d) => Ear(p, s, 1.8f, 5.0f, 2.0f + d * 0.4f, -0.6f + d, 7.0f, 2.2f, false), ears);
                    Pair((p, s, d) => Ear(p, s, 2.9f, 4.0f, 2.9f + d * 0.4f, 1.0f + d, 5.7f, 2.6f, false), inner);
                }
                break;
            case "fox":
                if (toon) Pair((p, s, d) => Ear(p, s, 2.6f, 4.4f, 1.6f + d * 0.4f, -3.0f + d, 7.6f, 1.6f, false), ears);
                else
                {
                    float k = cute ? 0.75f : 1;
                    Pair((p, s, d) => Ear(p, s, 1.4f, 5.4f, 1.0f + d * 0.4f, (cute ? 0.2f : -2.6f) + d, 7.0f, 2.2f, cute), ears);
                    Pair((p, s, d) => Ear(p, s, 2.5f, 4.4f, 2.0f + d * 0.4f, (cute ? 1.4f : -1.0f) + d, 5.6f, 2.7f, cute), inner);
                    _ = k;
                }
                break;
            case "mouse":
            {
                float ex = toon ? 3.6f : 2.7f, ey = toon ? 2.0f : cute ? 3.0f : 2.6f, r = toon ? 2.6f : cute ? 2.5f : 2.9f;
                Pair((p, s, d) => p.AddCircle(s > 0 ? ex : -ex, ey + d * 0.5f, r), ears);
                Pair((p, s, d) => p.AddCircle(s > 0 ? ex : -ex, ey + 0.1f + d * 0.5f, r * 0.6f), inner);
                break;
            }
            case "bear":
            {
                float ex = toon ? 4.0f : 3.3f, ey = toon ? 1.9f : cute ? 3.0f : 2.6f, r = cute ? 1.7f : 1.9f;
                Pair((p, s, d) => p.AddCircle(s > 0 ? ex : -ex, ey + d * 0.5f, r), ears);
                Pair((p, s, d) => p.AddCircle(s > 0 ? ex : -ex, ey + 0.1f + d * 0.5f, r * 0.52f), inner);
                break;
            }
            case "bunny":
            {
                void Long(SKPath p, float s, float d, float w, float h, float top)
                {
                    using var e = new SKPath();
                    e.AddOval(SKRect.Create(-w / 2, top, w, h));
                    e.Transform(SKMatrix.CreateRotationDegrees(-10 * s + d * 12, 0, top + h));
                    e.Transform(SKMatrix.CreateTranslation(s > 0 ? 5.2f : -5.2f, 0));
                    p.AddPath(e);
                }
                Pair((p, s, d) => Long(p, s, d, cute ? 3.0f : 2.6f, cute ? 7.0f : 8.2f, cute ? -3.2f : -4.6f), ears);
                Pair((p, s, d) => Long(p, s, d, 1.3f, cute ? 4.8f : 6.0f, cute ? -2.2f : -3.6f), inner);
                break;
            }
            default:
                inner?.Dispose();
                inner = null;
                break;
        }
        var all = face.Op(ears, SKPathOp.Union) ?? face;
        if (!ReferenceEquals(all, face)) face.Dispose();
        ears.Dispose();
        return all;
    }

    /// <summary>The toon silhouette: a wide round head that narrows into the neck and widens again into the
    /// shoulders, all one shape.</summary>
    private static void ToonBust(SKPath p)
    {
        p.MoveTo(8.0f, 1.3f);
        p.CubicTo(11.6f, 1.3f, 13.6f, 3.0f, 13.6f, 5.4f);          // crown to the right cheek
        p.CubicTo(13.6f, 7.2f, 12.4f, 8.2f, 11.0f, 8.7f);          // cheek in to the jaw
        p.CubicTo(10.6f, 9.6f, 11.4f, 10.8f, 12.6f, 11.8f);        // neck out to the shoulder
        p.LineTo(3.4f, 11.8f);
        p.CubicTo(4.6f, 10.8f, 5.4f, 9.6f, 5.0f, 8.7f);
        p.CubicTo(3.6f, 8.2f, 2.4f, 7.2f, 2.4f, 5.4f);
        p.CubicTo(2.4f, 3.0f, 4.4f, 1.3f, 8.0f, 1.3f);
        p.Close();
    }

    /// <summary>A pointed ear from base (bx1, by1) to its tip and back to (bx2, by2), mirrored when s is -1.</summary>
    private static void Ear(SKPath p, float s, float bx1, float by1, float tx, float ty, float bx2, float by2, bool round)
    {
        float X(float v) => s > 0 ? v : -v;
        p.MoveTo(X(bx1), by1);
        if (round)
        {
            p.QuadTo(X(tx - 0.6f), ty, X(tx + 0.9f), ty + 0.3f);
            p.QuadTo(X(tx + 1.8f), ty + 0.6f, X(bx2), by2);
        }
        else
        {
            p.QuadTo(X(bx1 - 0.15f), (by1 + ty) / 2, X(tx), ty);
            p.QuadTo(X((tx + bx2) / 2 + 0.5f), (ty + by2) / 2 - 0.1f, X(bx2), by2);
        }
        p.Close();
    }

    private static void PandaEars(SKCanvas c, float a)
    {
        float ex = _style == "toon" ? 4.0f : 3.0f, ey = _style == "toon" ? 2.0f : _style == "cute" ? 3.1f : 2.8f, r = _style == "cute" ? 1.9f : 2.1f;
        using var path = new SKPath();
        path.AddCircle(ex, ey, r);
        path.AddCircle(16 - ex, ey, r);
        if (_style == "sketch") { using (var f = Fill(Chalk.WithAlpha(90), a)) c.DrawPath(path, f); SketchLine(c, path, Chalk, a, 0.26f); return; }
        using (var p = Fill(_sk.Mark, a)) c.DrawPath(path, p);
        using (var o = Line(Edge, a, _style == "cute" ? 0.45f : 0.28f)) c.DrawPath(path, o);
    }

    private static void PaintHead(SKCanvas c, SKPath head, float a)
    {
        var b = head.Bounds;
        switch (_style)
        {
            case "toon":
                // Flat ink, no shading; a hairline lit edge so it reads on the dark island.
                using (var p = Fill(Coat, a)) c.DrawPath(head, p);
                using (var p = Line(Edge, 0.85f * a, 0.24f)) c.DrawPath(head, p);
                break;
            case "cute":
                using (var p = Fill(Coat, a)) c.DrawPath(head, p);
                // A soft shine on the crown, then the thick soft outline.
                using (var p = Fill(SKColors.White.WithAlpha(Dark ? (byte)40 : (byte)80), a)) c.DrawOval(SKRect.Create(b.Left + 2.4f, b.Bottom - 8.2f, 3.2f, 1.2f), p);
                using (var p = Line(Edge, a, 0.55f)) c.DrawPath(head, p);
                break;
            case "anime":
            {
                // Cel shading: the coat, one hard-edged shadow down the lower right, a bright rim of light on the
                // upper left, and a thin dark line.
                using (var p = Fill(Coat, a)) c.DrawPath(head, p);
                c.Save();
                c.ClipPath(head, SKClipOperation.Intersect, true);
                using (var p = Fill(Mix(Coat, new SKColor(40, 20, 70), Dark ? 0.3f : 0.14f), a)) c.DrawOval(SKRect.Create(8.6f, 6.4f, 9.0f, 7.0f), p);   // the lower right of the face only (bounds include the ears)
                using (var p = Line(Mix(Coat, SKColors.White, 0.55f), 0.7f * a, 0.45f)) c.DrawArc(SKRect.Create(b.Left + 0.6f, b.Top + 0.6f, b.Width - 1.2f, b.Height - 1.2f), 190, 70, false, p);
                c.Restore();
                using (var p = Line(Edge, a, 0.3f)) c.DrawPath(head, p);
                break;
            }
            default: // sketch
            {
                using (var p = Fill(Coat.WithAlpha(85), a)) c.DrawPath(head, p);
                // Pencil hatching down the shadow side.
                c.Save();
                c.ClipPath(head, SKClipOperation.Intersect, true);
                c.ClipRect(SKRect.Create(b.MidX + 2.2f, b.Top + 2.5f, b.Width, b.Height));
                using (var hatch = new SKPaint
                       {
                           IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.1f,
                           Color = Chalk.WithAlpha(G.A(0.35f * a)),
                           PathEffect = SKPathEffect.Create2DLine(0.1f, SKMatrix.CreateScale(0.75f, 0.75f).PostConcat(SKMatrix.CreateRotationDegrees(55))),
                       })
                    c.DrawRect(b, hatch);
                c.Restore();
                SketchLine(c, head, Chalk, a, 0.4f);
                break;
            }
        }
    }

    /// <summary>A loose hand-drawn outline: each contour drawn as one stroke that stops a little short, plus a
    /// second, lighter pass that crosses the join and overshoots it; both wobble, and the wobble changes a few times
    /// a second (line boil).</summary>
    private static void SketchLine(SKCanvas c, SKPath path, SKColor col, float a, float w)
    {
        using var pm = new SKPathMeasure(path, false);
        int n = 0;
        do
        {
            float len = pm.Length;
            if (len < 0.2f) continue;
            float start = len * ((n * 0.37f + _boil * 0.11f) % 1 * 0.05f + 0.01f);
            using (var main = new SKPath())
            using (var p = Line(col, 0.95f * a, w))
            using (var fx = SKPathEffect.CreateDiscrete(0.8f, 0.12f, (uint)(_boil * 13 + n + 1)))
            {
                pm.GetSegment(start, len * 0.97f, main, true);
                p.PathEffect = fx;
                c.DrawPath(main, p);
            }
            using (var join = new SKPath())
            using (var p = Line(col, 0.55f * a, w * 0.65f))
            using (var fx = SKPathEffect.CreateDiscrete(0.7f, 0.1f, (uint)(_boil * 7 + n + 40)))
            {
                pm.GetSegment(len * 0.9f, len, join, true);
                using var tail = new SKPath();
                pm.GetSegment(0, len * 0.08f, tail, true);
                join.AddPath(tail);
                join.Transform(SKMatrix.CreateTranslation(0.12f, -0.1f));
                p.PathEffect = fx;
                c.DrawPath(join, p);
            }
            n++;
        } while (pm.NextContour());
    }

    /// <summary>A small hand-drawn feature (an eye, a mouth): the path with a slight wobble.</summary>
    private static void Doodle(SKCanvas c, SKPath path, SKColor col, float a, float w, float dev = 0.07f)
    {
        using var p = Line(col, a, w);
        using var fx = SKPathEffect.CreateDiscrete(0.5f, dev, (uint)(_boil * 5 + 3));
        p.PathEffect = fx;
        c.DrawPath(path, p);
    }

    private static void Markings(SKCanvas c, SKPath head, float a)
    {
        using var path = new SKPath();
        switch (_sp.Id)
        {
            case "panda":
                foreach (float s in new[] { 1f, -1f })
                {
                    using var e = new SKPath();
                    e.AddOval(SKRect.Create(-1.6f, -2.0f, 3.2f, 4.0f));
                    e.Transform(SKMatrix.CreateRotationDegrees(28 * s).PostConcat(SKMatrix.CreateTranslation(s > 0 ? _eyeL - 0.2f : _eyeR + 0.2f, _eyeY + 0.2f)));
                    path.AddPath(e);
                }
                break;
            case "bear":
                path.AddOval(SKRect.Create(5.3f, _noseY - 0.9f, 5.4f, 3.4f));
                break;
            case "dog":
                path.AddOval(SKRect.Create(5.4f, _noseY - 0.6f, 5.2f, 3.0f));
                break;
            case "fox":
            {
                // White cheeks (and, on the toon, a white chest).
                float cy = _style == "toon" ? 6.4f : 7.2f;
                path.MoveTo(0.5f, cy);
                path.QuadTo(4.6f, cy + 0.4f, 8.0f, cy + 1.8f);
                path.QuadTo(11.4f, cy + 0.4f, 15.5f, cy);
                path.LineTo(15.5f, 12.5f); path.LineTo(0.5f, 12.5f); path.Close();
                break;
            }
            default: return;
        }
        c.Save();
        c.ClipPath(head, SKClipOperation.Intersect, true);
        if (_style == "sketch")
        {
            using (var f = Fill(Chalk.WithAlpha(_sp.Id == "panda" ? (byte)80 : (byte)35), a)) c.DrawPath(path, f);
            if (_sp.Id != "fox") Doodle(c, path, Chalk, 0.8f * a, 0.18f, 0.08f);
        }
        else
        {
            var col = _sp.Id is "dog" ? Mix(Coat, SKColors.White, 0.35f) : _style == "cute" ? Mix(_sk.Mark, SKColors.White, Light(_sk.Mark) ? 0.1f : 0.0f) : _sk.Mark;
            using var m = Fill(col, a);
            c.DrawPath(path, m);
        }
        c.Restore();
    }

    private static void FloppyEars(SKCanvas c, float a, double t)
    {
        float sway = MathF.Sin((float)t * 1.6f) * 3;
        float ex = _style == "toon" ? 3.2f : _style == "cute" ? 2.2f : 2.6f, ey = _style == "toon" ? 1.9f : _style == "cute" ? 3.0f : 2.4f;
        foreach (float s in new[] { 1f, -1f })
        {
            c.Save();
            c.Translate(s > 0 ? ex : 16 - ex, ey);
            c.RotateDegrees(s * (16 + sway));
            var r = SKRect.Create(-1.7f, -0.4f, 3.4f, _style == "cute" ? 5.6f : 7.0f);
            using var path = new SKPath();
            path.AddRoundRect(r, 1.7f, 1.7f);
            if (_style == "sketch") { using (var f = Fill(Chalk.WithAlpha(50), a)) c.DrawPath(path, f); SketchLine(c, path, Chalk, a, 0.24f); }
            else
            {
                using (var p = Fill(_style == "cute" ? Mix(_sk.Mark, SKColors.White, 0.15f) : _sk.Mark, a)) c.DrawPath(path, p);
                using (var p = Line(Edge, a, _style == "cute" ? 0.5f : 0.24f)) c.DrawPath(path, p);
            }
            c.Restore();
        }
    }

    private static void StyledWhiskers(SKCanvas c, float a)
    {
        using var path = new SKPath();
        switch (_style)
        {
            case "toon":
                // Three long hairlines a side, fanning out from just inside the cheeks.
                for (int i = 0; i < 3; i++)
                {
                    float y0 = 6.5f + i * 0.55f, y1 = 5.9f + i * 1.15f;
                    path.MoveTo(3.4f, y0); path.LineTo(-2.5f, y1);
                    path.MoveTo(12.6f, y0); path.LineTo(18.5f, y1);
                }
                using (var p = Line(Feature, 0.9f * a, 0.13f)) c.DrawPath(path, p);
                break;
            case "anime":
                for (int i = 0; i < 2; i++)
                {
                    float y0 = 8.7f + i * 0.75f;
                    path.MoveTo(3.0f, y0); path.QuadTo(0.6f, y0 - 0.3f, -2.4f, y0 - 0.6f + i * 1.2f);
                    path.MoveTo(13.0f, y0); path.QuadTo(15.4f, y0 - 0.3f, 18.4f, y0 - 0.6f + i * 1.2f);
                }
                using (var p = Line(Feature, 0.7f * a, 0.15f)) c.DrawPath(path, p);
                break;
            case "cute":
                // Two short stubs a side, outside the head.
                for (int i = 0; i < 2; i++)
                {
                    float y0 = 8.2f + i * 0.9f;
                    path.MoveTo(0.6f, y0); path.LineTo(-1.2f, y0 - 0.4f + i * 0.8f);
                    path.MoveTo(15.4f, y0); path.LineTo(17.2f, y0 - 0.4f + i * 0.8f);
                }
                using (var p = Line(Edge, a, 0.32f)) c.DrawPath(path, p);
                break;
            default:
                for (int i = 0; i < 2; i++)
                {
                    float y0 = 8.6f + i * 0.8f;
                    path.MoveTo(3.0f, y0); path.QuadTo(0.6f, y0 - 0.3f, -2.4f, y0 - 0.6f + i * 1.2f);
                    path.MoveTo(13.0f, y0); path.QuadTo(15.4f, y0 - 0.3f, 18.4f, y0 - 0.6f + i * 1.2f);
                }
                Doodle(c, path, Chalk, 0.85f * a, 0.22f, 0.1f);
                break;
        }
    }

    // ---------- the face ----------

    private static void StyledFace(SKCanvas c, CatFace face, float a, double t)
    {
        void Blush(float k = 1)
        {
            float by = _style == "cute" ? _eyeY + 0.5f : _eyeY + 1.9f;
            if (_style == "toon")
            {
                if (k < 1.3f) return;                              // the toon only blushes when it means it
                using var tb = Fill(new SKColor(255, 110, 140, 170), a);
                c.DrawOval(SKRect.Create(_eyeL - 1.6f, by - 0.3f, 1.6f, 0.7f), tb);
                c.DrawOval(SKRect.Create(_eyeR, by - 0.3f, 1.6f, 0.7f), tb);
                return;
            }
            if (_style == "sketch")
            {
                using var s = Line(new SKColor(255, 130, 160), a * Math.Min(1, 0.6f + k * 0.3f), 0.16f);
                foreach (float bx in new[] { _eyeL - 2.0f, _eyeR + 0.6f })
                    for (int i = 0; i < 3; i++) c.DrawLine(bx + i * 0.5f, by + 0.4f, bx + 0.45f + i * 0.5f, by - 0.4f, s);
                return;
            }
            if (_style == "anime")
            {
                // Soft blush under the eyes, with the little diagonal strokes anime uses.
                using var ab = Fill(new SKColor(255, 120, 150, (byte)Math.Min(255, 110 * k)), a);
                using var hl = Line(new SKColor(255, 90, 130), 0.7f * a * Math.Min(1, k), 0.14f);
                foreach (float bx in new[] { _eyeL - 1.2f, _eyeR - 1.2f })
                {
                    c.DrawOval(SKRect.Create(bx, _eyeY + 1.7f, 2.4f, 0.9f), ab);
                    for (int i = 0; i < 3; i++) c.DrawLine(bx + 0.5f + i * 0.6f, _eyeY + 2.4f, bx + 0.8f + i * 0.6f, _eyeY + 1.8f, hl);
                }
                return;
            }
            // Cute: round blush just outside and below each eye.
            using var b = Fill(new SKColor(255, 120, 150, (byte)Math.Min(255, 150 * k)), a);
            c.DrawOval(SKRect.Create(_eyeL - 2.9f, by, 2.1f, 1.2f), b);
            c.DrawOval(SKRect.Create(_eyeR + 0.8f, by, 2.1f, 1.2f), b);
        }
        bool toon = _style == "toon";
        switch (face)
        {
            case CatFace.Open: Blush(); Eyes(1, 0, false); Nose(c, a); if (!toon) Mouth(() => SSmile(c, a, 1)); break;
            case CatFace.Happy: Blush(1.3f); SArc(c, _eyeL, a, true); SArc(c, _eyeR, a, true); Nose(c, a); Mouth(() => SOpen(c, a, 1.0f, true)); break;
            case CatFace.Closed: Blush(0.8f); SArc(c, _eyeL, a, false); SArc(c, _eyeR, a, false); Nose(c, a); if (!toon) Mouth(() => SSmile(c, a, 0.6f)); break;
            case CatFace.Determined: Eyes(1, 0.32f, false); SBrows(c, face, a); Nose(c, a); Mouth(() => SFlat(c, a)); break;
            case CatFace.XEyes: Blush(); SCross(c, _eyeL, a); SCross(c, _eyeR, a); Nose(c, a); Mouth(() => SWavy(c, a)); break;
            case CatFace.Spiral: SSpiral(c, _eyeL, a, t); SSpiral(c, _eyeR, a, t); Nose(c, a); Mouth(() => SWavy(c, a)); break;
            case CatFace.Surprised: Eyes(1.15f, 0, true); Nose(c, a); Mouth(() => SOpen(c, a, 0.8f, false)); break;
            case CatFace.Grumpy: Blush(1.4f); Eyes(1, 0.5f, false); SBrows(c, face, a); Nose(c, a); Mouth(() => SPout(c, a)); Anger(c, a); break;
            case CatFace.Love:
            {
                Blush(1.5f);
                float hs = _style == "cute" ? 1.0f : 1.3f;
                Heart(c, _eyeL, _eyeY, hs, a); Heart(c, _eyeR, _eyeY, hs, a); Nose(c, a); Mouth(() => SSmile(c, a, 1));
                break;
            }
            case CatFace.Droopy: Blush(0.6f); Eyes(1, 0.58f, false); Nose(c, a); if (!toon) Mouth(() => SSmile(c, a, 0.4f)); break;
            case CatFace.Wink: Blush(1.2f); StyledEye(c, _eyeL, a, 1, 0, t, false); SArc(c, _eyeR, a, true); Nose(c, a); Mouth(() => SOpen(c, a, 0.8f, true)); break;
            case CatFace.Sing:
                Blush(1.2f); SArc(c, _eyeL, a, true); SArc(c, _eyeR, a, true); Nose(c, a);
                if (SingOpen < 0.25f) Mouth(() => SSmile(c, a, 0.5f)); else Mouth(() => SOpen(c, a, 0.45f + 0.75f * SingOpen, false));
                break;
            case CatFace.Yawn:
                SArc(c, _eyeL, a, false); SArc(c, _eyeR, a, false); Nose(c, a); Mouth(() => SOpen(c, a, 1.3f, false));
                using (var tear = Fill(new SKColor(150, 210, 255), a)) c.DrawCircle(_eyeR + 1.4f, _eyeY + 0.6f, 0.34f, tear);
                break;
            case CatFace.Shades:
                Blush(0.7f);
                c.Save(); c.Translate(0, _eyeY - EyeY); Shades(c, a); c.Restore();
                Nose(c, a); Mouth(() => SSmile(c, a, 1));
                break;
        }

        void Eyes(float k, float lid, bool tiny) { StyledEye(c, _eyeL, a, k, lid, t, tiny); StyledEye(c, _eyeR, a, k, lid, t, tiny); }
        // Mouths are drawn in the anime face's space (top at y 9.3) and moved and scaled to this style's mouth.
        void Mouth(Action draw)
        {
            c.Save();
            c.Translate(8, _mY); c.Scale(_mk); c.Translate(-8, -9.3f);
            _strokeK = 1 / _mk;
            draw();
            _strokeK = 1;
            c.Restore();
        }
    }

    /// <summary>An open eye in the current style. <paramref name="lid"/> 0..1 closes it from the top.</summary>
    private static void StyledEye(SKCanvas c, float cx, float a, float k, float lid, double t, bool tiny)
    {
        // Anime: the big glossy eye the girls have, with an iris to suit the coat.
        if (_style == "anime") { Eye(c, cx, a, tiny, lid); return; }
        // The pupils glance around now and then.
        float wave = MathF.Sin((float)t * 0.7f);
        float look = wave > 0.6f ? 0.3f : wave < -0.75f ? -0.3f : 0;
        bool left = cx < 8;
        SKRect box;
        using var shape = new SKPath();
        switch (_style)
        {
            case "toon":
                box = SKRect.Create(cx - 1.35f * k, _eyeY - 1.6f * k, 2.7f * k, 3.2f * k);
                break;
            case "cute":
                box = SKRect.Create(cx - 0.95f * k, _eyeY - 1.1f * k, 1.9f * k, 2.2f * k);
                break;
            default:
                box = tiny ? SKRect.Create(cx - 1.0f, _eyeY - 1.0f, 2.0f, 2.0f) : SKRect.Create(cx - 0.78f, _eyeY - 0.85f, 1.56f, 1.7f);
                break;
        }
        shape.AddOval(box);

        c.Save();
        c.ClipPath(shape, SKClipOperation.Intersect, true);
        float lidY = box.Top + box.Height * lid;
        if (lid > 0) c.ClipRect(SKRect.Create(box.Left - 1, lidY, box.Width + 2, box.Height + 2));
        switch (_style)
        {
            case "toon":
            {
                using (var w = Fill(new SKColor(250, 250, 246), a)) c.DrawPath(shape, w);
                // A small upright pupil, set a little toward the nose.
                float pw = (tiny ? 0.38f : 0.52f) * (_girl ? 1.5f : 1), ph = (tiny ? 0.75f : 1.15f) * (_girl ? 1.3f : 1);
                using var pupil = Fill(_girl ? Mix(_sp.Iris, SKColors.Black, 0.45f) : new SKColor(12, 10, 14), a);
                c.DrawOval(SKRect.Create(cx - pw / 2 + look + (left ? 0.25f : -0.25f), _eyeY - ph / 2 + 0.15f, pw, ph), pupil);
                break;
            }
            case "cute":
            {
                using (var bead = Fill(_girl ? Mix(_sp.Iris, SKColors.Black, 0.62f) : new SKColor(34, 24, 36), a)) c.DrawPath(shape, bead);
                using var hi = Fill(SKColors.White, a);
                c.DrawCircle(box.Left + box.Width * 0.36f, box.Top + box.Height * 0.32f, box.Width * 0.25f, hi);
                c.DrawCircle(box.Left + box.Width * 0.7f, box.Top + box.Height * 0.72f, box.Width * 0.11f, hi);
                break;
            }
            default:
            {
                // Dot eyes; wide open, a ring with a dot.
                using var ink = Fill(Feature, a);
                if (tiny) c.DrawCircle(cx + look * 0.5f, _eyeY + 0.1f, 0.38f, ink);
                else c.DrawPath(shape, ink);
                break;
            }
        }
        c.Restore();

        // A girl's lash: one flick off the outer corner.
        if (_girl)
        {
            float top = box.Top + box.Height * lid, ox = left ? box.Left : box.Right, dir = left ? -1 : 1;
            using var lash = new SKPath();
            lash.MoveTo(ox + dir * -0.1f, top + box.Height * 0.3f);
            lash.LineTo(ox + dir * 0.55f, top - 0.1f);
            if (_style == "sketch") Doodle(c, lash, Feature, a, 0.22f, 0.03f);
            else using (var p = Line(Ink, a, _style == "cute" ? 0.3f : 0.26f)) c.DrawPath(lash, p);
            if (_style == "toon")
                using (var p = Line(Ink, a, 0.28f))
                {
                    // The toon's upper lid line, so the white eye reads on a light face.
                    using var arc = new SKPath();
                    arc.AddArc(SKRect.Create(box.Left, box.Top + box.Height * lid, box.Width, box.Height * (1 - lid) + 0.01f), 200, 140);
                    c.DrawPath(arc, p);
                }
        }

        // Outlines and lids.
        switch (_style)
        {
            case "toon":
                if (!Dark) using (var o = Line(Feature, a, 0.18f)) c.DrawPath(shape, o);
                if (lid > 0) using (var o = Line(Dark ? new SKColor(250, 250, 246) : Feature, a, 0.26f)) c.DrawLine(box.Left + 0.1f, lidY, box.Right - 0.1f, lidY, o);
                break;
            case "cute":
                if (Dark) using (var o = Line(_sk.Rim, 0.8f * a, 0.14f)) c.DrawPath(shape, o);
                if (lid > 0) using (var o = Line(Feature, a, 0.3f)) c.DrawLine(box.Left - 0.15f, lidY, box.Right + 0.15f, lidY, o);
                break;
            default:
                if (tiny) Doodle(c, shape, Feature, a, 0.2f, 0.05f);
                if (lid > 0)
                {
                    using var lidPath = new SKPath();
                    lidPath.MoveTo(box.Left - 0.4f, lidY); lidPath.LineTo(box.Right + 0.4f, lidY);
                    Doodle(c, lidPath, Feature, a, 0.22f, 0.04f);
                }
                break;
        }
    }

    private static float FeatureW => _style switch { "cute" => 0.5f, "toon" => 0.3f, "anime" => 0.32f, _ => 0.38f };

    private static void Stroke(SKCanvas c, SKPath path, float a, float? w = null, SKColor? col = null)
    {
        var color = col ?? Feature;
        float width = (w ?? FeatureW) * _strokeK;
        if (_style == "sketch") { Doodle(c, path, color, a, width, 0.05f * _strokeK); return; }
        using var p = Line(color, a, width);
        c.DrawPath(path, p);
    }

    private static void SArc(SKCanvas c, float cx, float a, bool happy)
    {
        using var path = new SKPath();
        float hw = _style switch { "cute" => 1.0f, "toon" => 1.2f, _ => 0.9f }, hh = _style == "cute" ? 0.8f : 1.0f, y = _eyeY;
        if (happy) { path.MoveTo(cx - hw, y + 0.5f * hh); path.QuadTo(cx, y - 1.1f * hh, cx + hw, y + 0.5f * hh); }
        else { path.MoveTo(cx - hw, y); path.QuadTo(cx, y + 1.1f * hh, cx + hw, y); }
        Stroke(c, path, a, FeatureW + 0.06f);
    }

    private static void SCross(SKCanvas c, float cx, float a)
    {
        using var path = new SKPath();
        float d = cx < 8 ? 1 : -1, s = _style == "cute" ? 0.7f : 0.9f;
        path.MoveTo(cx - d * s, _eyeY - s); path.LineTo(cx + d * s * 0.9f, _eyeY); path.LineTo(cx - d * s, _eyeY + s);
        Stroke(c, path, a, FeatureW + 0.06f);
    }

    private static void SSpiral(SKCanvas c, float cx, float a, double t)
    {
        using var path = new SKPath();
        float rr = _style == "cute" ? 0.85f : 1.15f;
        for (int i = 0; i <= 40; i++)
        {
            float k = i / 40f, ang = k * 4.2f * MathF.PI + (float)t * 4, r = 0.12f + k * rr;
            float sx = cx + r * MathF.Cos(ang), sy = _eyeY + r * MathF.Sin(ang);
            if (i == 0) path.MoveTo(sx, sy); else path.LineTo(sx, sy);
        }
        Stroke(c, path, a, 0.22f);
    }

    private static void SBrows(SKCanvas c, CatFace face, float a)
    {
        using var path = new SKPath();
        float y = _eyeY - (_style == "toon" ? 2.2f : 1.6f), l = _eyeL, r = _eyeR;
        if (face == CatFace.Determined) { path.MoveTo(l - 1.3f, y - 0.4f); path.LineTo(l + 1.1f, y + 0.3f); path.MoveTo(r + 1.3f, y - 0.4f); path.LineTo(r - 1.1f, y + 0.3f); }
        else { path.MoveTo(l - 1.3f, y + 0.2f); path.LineTo(l + 1.1f, y - 0.3f); path.MoveTo(r + 1.3f, y + 0.2f); path.LineTo(r - 1.1f, y - 0.3f); }
        Stroke(c, path, a, FeatureW + 0.08f);
    }

    private static void Anger(SKCanvas c, float a)
    {
        using var mark = Line(new SKColor(240, 70, 90), a, 0.3f);
        float mx = 12.8f, my = _style == "toon" ? 2.4f : 3.6f;
        c.DrawLine(mx, my - 0.6f, mx, my + 0.6f, mark); c.DrawLine(mx - 0.6f, my, mx + 0.6f, my, mark);
    }

    private static void Nose(SKCanvas c, float a)
    {
        if (_girl)
        {
            // A girl's nose is a dot on the toon and the sketch, and left out on the chibi.
            if (_style == "cute") return;
            using var dot = Fill(_style == "sketch" ? Feature : Mix(GirlSkin, new SKColor(150, 70, 70), 0.5f), 0.8f * a);
            c.DrawCircle(8.0f, _noseY - 0.25f, 0.16f, dot);
            return;
        }
        switch (_style)
        {
            case "toon":
                // The tiny pale nub of a silhouette cat.
                using (var p = Fill(Dark ? new SKColor(246, 240, 238) : new SKColor(50, 34, 40), a)) c.DrawOval(SKRect.Create(7.62f, _noseY - 0.18f, 0.76f, 0.38f), p);
                break;
            case "anime":
            {
                // A small soft triangle with a glint.
                using var path = new SKPath();
                path.MoveTo(7.45f, _noseY - 0.35f); path.LineTo(8.55f, _noseY - 0.35f); path.QuadTo(8.1f, _noseY + 0.35f, 8.0f, _noseY + 0.35f); path.QuadTo(7.9f, _noseY + 0.35f, 7.45f, _noseY - 0.35f); path.Close();
                using (var p = Fill(_sp.Id is "dog" or "bear" or "panda" ? new SKColor(44, 32, 38) : new SKColor(240, 130, 150), a)) c.DrawPath(path, p);
                using (var hi = Fill(SKColors.White.WithAlpha(180), a)) c.DrawCircle(7.85f, _noseY - 0.18f, 0.14f, hi);
                break;
            }
            case "cute":
                // Kawaii faces mostly skip the nose; the round-nosed animals keep a tiny dark button.
                if (_sp.Id is "dog" or "bear" or "panda")
                    using (var p = Fill(new SKColor(44, 30, 36), a)) c.DrawOval(SKRect.Create(7.55f, _noseY - 0.55f, 0.9f, 0.55f), p);
                break;
            default:
            {
                using var path = new SKPath();
                path.MoveTo(7.5f, _noseY - 0.3f); path.LineTo(8.5f, _noseY - 0.3f); path.LineTo(8.0f, _noseY + 0.3f); path.Close();
                using (var p = Fill(new SKColor(255, 150, 170, 150), a)) c.DrawPath(path, p);
                Doodle(c, path, Feature, a, 0.16f, 0.03f);
                break;
            }
        }
    }

    private static void SSmile(SKCanvas c, float a, float k)
    {
        using var path = new SKPath();
        path.MoveTo(6.9f, 9.3f);
        path.QuadTo(7.45f, 9.3f + 0.75f * k, 8.0f, 9.35f);
        path.QuadTo(8.55f, 9.3f + 0.75f * k, 9.1f, 9.3f);
        Stroke(c, path, a);
    }

    private static void SFlat(SKCanvas c, float a)
    {
        using var path = new SKPath();
        path.MoveTo(7.3f, 9.7f); path.LineTo(8.7f, 9.7f);
        Stroke(c, path, a);
    }

    private static void SWavy(SKCanvas c, float a)
    {
        using var path = new SKPath();
        path.MoveTo(6.8f, 9.8f);
        path.QuadTo(7.2f, 9.3f, 7.6f, 9.8f); path.QuadTo(8.0f, 10.3f, 8.4f, 9.8f); path.QuadTo(8.8f, 9.3f, 9.2f, 9.8f);
        Stroke(c, path, a, 0.22f);
    }

    private static void SPout(SKCanvas c, float a)
    {
        using var path = new SKPath();
        path.MoveTo(7.3f, 10.0f); path.QuadTo(8.0f, 9.3f, 8.7f, 10.0f);
        Stroke(c, path, a);
    }

    private static void SOpen(SKCanvas c, float a, float k, bool smile)
    {
        using var path = new SKPath();
        float top = 9.35f;
        if (smile)
        {
            path.MoveTo(7.0f - (k - 1), top);
            path.LineTo(9.0f + (k - 1), top);
            path.QuadTo(8.0f, top + 1.9f + (k - 1), 7.0f - (k - 1), top);
            path.Close();
        }
        else path.AddOval(SKRect.Create(8.0f - 0.55f * k, top, 1.1f * k, 1.3f * k));
        if (_style == "sketch")
        {
            using (var f = Fill(new SKColor(255, 140, 160, 90), a)) c.DrawPath(path, f);
            Doodle(c, path, Feature, a, 0.2f * _strokeK, 0.04f);
            return;
        }
        using (var p = Fill(new SKColor(110, 28, 44), a)) c.DrawPath(path, p);
        using (var tongue = Fill(new SKColor(255, 128, 150), a))
        {
            c.Save(); c.ClipPath(path, SKClipOperation.Intersect, true);
            c.DrawOval(SKRect.Create(7.3f, top + 0.7f, 1.4f, 1.2f), tongue);
            c.Restore();
        }
        if (Dark || _style == "cute") using (var o = Line(_style == "cute" ? Edge : _sk.Rim, 0.8f * a, 0.16f * _strokeK)) c.DrawPath(path, o);
    }

    // ---------- curled up asleep ----------

    private static void Loaf(SKCanvas c, float a, double t)
    {
        using var path = new SKPath();
        path.AddRoundRect(SKRect.Create(1.0f, 4.2f, 14.0f, 6.8f), 3.4f, 3.4f);
        if (_sp.Id is "cat" or "fox")
        {
            using var ears = new SKPath();
            bool round = _style == "cute";
            float tip = _style == "toon" ? 1.2f : 2.2f;
            Ear(ears, 1, 2.8f, 5.0f, 3.0f, tip, 6.2f, 4.4f, round);
            Ear(ears, 1, 7.2f, 4.4f, 8.8f, tip - 0.3f, 10.4f, 4.4f, round);
            using var u = path.Op(ears, SKPathOp.Union);
            if (u != null) { path.Reset(); path.AddPath(u); }
        }
        else if (_sp.Id is "bear" or "mouse" or "panda")
        {
            using var ears = new SKPath();
            ears.AddCircle(4.2f, 4.6f, 1.4f);
            ears.AddCircle(9.2f, 4.4f, 1.4f);
            using var u = path.Op(ears, SKPathOp.Union);
            if (u != null) { path.Reset(); path.AddPath(u); }
        }
        PaintHead(c, path, a);
        // The tail wrapped round the front.
        using (var tail = new SKPath())
        {
            tail.MoveTo(14.4f, 8.4f); tail.QuadTo(12.6f, 11.6f, 6.0f, 10.4f);
            var col = _style == "sketch" ? Chalk : _style == "cute" ? Edge : Mix(Coat, Dark ? SKColors.White : SKColors.Black, 0.22f);
            Stroke(c, tail, a, _style == "sketch" ? 0.24f : 0.6f, col);
        }
        // Sleeping eyes, low on the loaf.
        using var eyes = new SKPath();
        eyes.MoveTo(3.4f, 7.4f); eyes.QuadTo(4.4f, 8.1f, 5.4f, 7.4f);
        eyes.MoveTo(7.2f, 7.4f); eyes.QuadTo(8.2f, 8.1f, 9.2f, 7.4f);
        Stroke(c, eyes, a, null, _style == "toon" && Dark ? new SKColor(246, 244, 240) : null);
    }
}
