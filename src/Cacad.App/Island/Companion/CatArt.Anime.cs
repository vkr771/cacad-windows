using SkiaSharp;

namespace Cacad;

/// <summary>The anime girls, drawn smooth (not pixel art): a round chibi face, big glossy eyes set low, a tiny mouth,
/// blush, and each girl's own hair and accessory. They share every reaction with the animals; each face below maps
/// a <see cref="CatFace"/> to an anime expression. Drawn in grid units (the same 16 x 11 footprint as the pixel
/// companions), scaled by px.</summary>
public static partial class CatArt
{
    private static CatSpecies Girl(string id, string name, SKColor hair, SKColor accent, SKColor iris, string[] pixels) =>
        new(id, name, Check(id, pixels), false, new("natural", "Natural", hair, Mix(hair, SKColors.Black, 0.3f), accent, Mix(hair, SKColors.White, 0.45f)), true, iris);

    /// <summary>Every row exactly 16 cells (a typo would otherwise shift a row and tear the art).</summary>
    private static string[] Check(string id, string[] rows) => rows.Select(r => r.PadRight(16, '.')[..16]).ToArray();

    private static readonly SKColor Ink = new(52, 30, 44), Lip = new(206, 86, 108), BlushC = new(255, 120, 150);

    private static SKPaint Fill(SKColor col, float a) => new() { IsAntialias = true, Color = col.WithAlpha(G.A(a * col.Alpha / 255f)) };
    private static SKPaint Line(SKColor col, float a, float w) => new()
    {
        IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = w, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
        Color = col.WithAlpha(G.A(a * col.Alpha / 255f)),
    };

    private static void DrawGirl(SKCanvas c, CatPose pose, CatFace face, float x, float y, float px, float a, double t)
    {
        c.Save();
        c.Translate(x, y);
        c.Scale(px);
        _handC = Skin;
        var hair = _sk.Body;
        // The back of the hair a shade deeper (cool-tinted on pale hair, so white hair does not turn grey).
        var shade = Light(hair) ? Mix(hair, new SKColor(150, 170, 230), 0.22f) : Mix(hair, SKColors.Black, 0.28f);
        var edge = Light(hair) ? Mix(hair, SKColors.Black, 0.35f) : _sk.Rim;   // a lit edge keeps dark hair visible

        if (pose.Worn is "headphones" or "headset") Band(c, a);
        BackHair(c, hair, shade, edge, a, t);
        // The face: round, no chin point, a soft outline.
        var faceR = SKRect.Create(2.4f, 1.6f, 11.2f, 10.0f);
        using (var p = Fill(Skin, a)) c.DrawOval(faceR, p);
        using (var p = Line(Mix(Skin, new SKColor(170, 100, 90), 0.5f), a, 0.22f)) c.DrawOval(faceR, p);
        Expression(c, face, a, t);
        Bangs(c, hair, shade, edge, a);
        Accessory(c, a, t);
        if (face is CatFace.Determined or CatFace.Grumpy) Brows(c, face, a);
        Worn(c, pose.Worn, a, t);
        if (HatId != null && pose.Worn is not ("hardhat" or "partyhat")) GirlHat(c, HatId, a, t);
        c.Restore();
    }

    // ---------- hair ----------

    private static void BackHair(SKCanvas c, SKColor hair, SKColor shade, SKColor edge, float a, double t)
    {
        using var path = BackHairPath(t);
        using (var p = Fill(shade, a)) c.DrawPath(path, p);
        using (var p = Line(edge, a, 0.35f)) c.DrawPath(path, p);
        using var inner = InnerEarsPath();
        if (inner != null) using (var p = Fill(InnerEarColor, a)) c.DrawPath(inner, p);
    }

    /// <summary>The inside of the animal ears: the accent colour, or (Kuri's pony ears) a deeper shade of her hair.</summary>
    private static SKColor InnerEarColor => _sp.Id == "kuri" ? Mix(_sk.Body, new SKColor(60, 20, 30), 0.4f) : _sk.Ear;

    /// <summary>Inner ears for the cat, fox and pony girls (null for the others).</summary>
    private static SKPath? InnerEarsPath()
    {
        if (_sp.Id is not ("neko" or "kitsu" or "kuri")) return null;
        var inner = new SKPath();
        if (_sp.Id == "neko") { Tri(inner, 3.0f, 1.4f, 3.1f, -1.4f, 5.0f, 0.4f); Tri(inner, 13.0f, 1.4f, 12.9f, -1.4f, 11.0f, 0.4f); }
        else if (_sp.Id == "kuri") { PonyEar(inner, 1, 0.55f); PonyEar(inner, -1, 0.55f); }
        else { Tri(inner, 2.4f, 1.8f, 2.0f, -2.0f, 5.0f, 0.4f); Tri(inner, 13.6f, 1.8f, 14.0f, -2.0f, 11.0f, 0.4f); }
        return inner;
    }

    /// <summary>A pony ear: a tall leaf, leaning out, on the left (s = 1) or right (s = -1); k shrinks it about its
    /// middle (for the inside).</summary>
    private static void PonyEar(SKPath p, float s, float k)
    {
        // Centre of the leaf, then its base corners and tip relative to that.
        float cx = 4.0f, cy = 0.2f;
        float X(float dx) => s > 0 ? cx + dx * k : 16 - (cx + dx * k);
        float Y(float dy) => cy + dy * k;
        p.MoveTo(X(-0.6f), Y(2.6f));
        p.QuadTo(X(-2.6f), Y(-0.4f), X(-1.4f), Y(-3.2f));
        p.QuadTo(X(1.4f), Y(-1.8f), X(2.2f), Y(0.8f));
        p.Close();
    }

    /// <summary>The hair behind the face (and the cat or fox ears), each girl's own cut.</summary>
    private static SKPath BackHairPath(double t)
    {
        var path = new SKPath();
        float sway = MathF.Sin((float)t * 1.4f) * 0.25f;
        switch (_sp.Id)
        {
            case "hana": // long and straight, past the shoulders
                path.AddOval(SKRect.Create(1.1f, -1.2f, 13.8f, 12.4f));
                path.AddOval(SKRect.Create(0.6f + sway, 3.0f, 4.6f, 10.6f));
                path.AddOval(SKRect.Create(10.8f + sway, 3.0f, 4.6f, 10.6f));
                break;
            case "neko": // a bob, with cat ears
                path.AddOval(SKRect.Create(1.3f, -0.8f, 13.4f, 12.0f));
                Tri(path, 2.0f, 2.2f, 2.6f, -2.6f, 6.0f, 0.2f);
                Tri(path, 14.0f, 2.2f, 13.4f, -2.6f, 10.0f, 0.2f);
                break;
            case "yuki": // twin tails tied high
                path.AddOval(SKRect.Create(1.4f, -0.9f, 13.2f, 11.6f));
                path.AddOval(SKRect.Create(-1.6f + sway, 3.2f, 3.4f, 9.0f));
                path.AddOval(SKRect.Create(14.2f + sway, 3.2f, 3.4f, 9.0f));
                break;
            case "kitsu": // fluffy, with big fox ears
                path.AddOval(SKRect.Create(1.0f, -0.8f, 14.0f, 12.2f));
                Tri(path, 1.4f, 2.6f, 1.2f, -3.4f, 6.0f, 0.0f);
                Tri(path, 14.6f, 2.6f, 14.8f, -3.4f, 10.0f, 0.0f);
                break;
            case "kuri": // wavy, to the shoulders, curling out at the ends; pony ears
                path.AddOval(SKRect.Create(1.2f, -0.9f, 13.6f, 12.0f));
                path.AddOval(SKRect.Create(0.4f + sway, 3.8f, 4.4f, 8.6f));
                path.AddOval(SKRect.Create(11.2f + sway, 3.8f, 4.4f, 8.6f));
                path.AddCircle(1.0f + sway, 11.0f, 1.5f);
                path.AddCircle(15.0f + sway, 11.0f, 1.5f);
                PonyEar(path, 1, 1);
                PonyEar(path, -1, 1);
                break;
            default: // hoshi: short, with a side ponytail
                path.AddOval(SKRect.Create(1.4f, -0.9f, 13.2f, 11.4f));
                path.AddOval(SKRect.Create(13.6f + sway, 2.6f, 3.0f, 7.6f));
                break;
        }
        // One outline round the whole mass (the ovals overlap).
        var merged = path.Simplify();
        if (merged == null) return path;
        path.Dispose();
        return merged;
    }

    private static void Tri(SKPath p, float x1, float y1, float x2, float y2, float x3, float y3)
    {
        p.MoveTo(x1, y1); p.LineTo(x2, y2); p.LineTo(x3, y3); p.Close();
    }

    /// <summary>The fringe over the forehead, cut to each girl's style, with a shine band across the crown.</summary>
    private static void Bangs(SKCanvas c, SKColor hair, SKColor shade, SKColor edge, float a)
    {
        using var path = BangsPath();
        using (var p = Fill(hair, a)) c.DrawPath(path, p);
        using (var p = Line(edge, a, 0.3f)) c.DrawPath(path, p);
        // The shine band (an "angel ring") across the crown.
        using (var p = Line(Mix(hair, SKColors.White, 0.5f), 0.8f * a, 0.55f))
        {
            using var ring = new SKPath();
            ring.AddArc(SKRect.Create(3.2f, 0.4f, 9.6f, 5.0f), 200, 140);
            c.DrawPath(ring, p);
        }
    }

    /// <summary>The fringe: the crown of the head down to each girl's cut.</summary>
    private static SKPath BangsPath()
    {
        // The lower edge of the fringe, right to left.
        (float x, float y)[] cut = _sp.Id switch
        {
            "hana" => new[] { (13.5f, 9.0f), (12.9f, 9.9f), (12.3f, 4.8f), (10.2f, 4.4f), (8.0f, 4.7f), (5.8f, 4.4f), (3.7f, 4.8f), (3.1f, 9.9f), (2.5f, 9.0f) },   // hime cut
            "neko" => new[] { (13.8f, 7.6f), (12.6f, 4.4f), (11.2f, 5.2f), (10.0f, 3.8f), (8.0f, 5.0f), (6.0f, 3.8f), (4.8f, 5.2f), (3.4f, 4.4f), (2.2f, 7.6f) },
            "yuki" => new[] { (13.8f, 6.8f), (12.4f, 4.2f), (10.6f, 4.8f), (9.2f, 3.6f), (8.0f, 4.6f), (6.8f, 3.6f), (5.4f, 4.8f), (3.6f, 4.2f), (2.2f, 6.8f) },
            "kitsu" => new[] { (14.2f, 8.2f), (13.2f, 5.0f), (12.4f, 5.6f), (11.4f, 3.6f), (10.0f, 5.2f), (8.6f, 3.4f), (7.2f, 5.0f), (5.8f, 3.4f), (4.6f, 5.4f), (3.4f, 4.2f), (1.8f, 8.2f) },
            "kuri" => new[] { (14.0f, 8.4f), (13.0f, 4.8f), (11.8f, 5.4f), (10.6f, 3.8f), (9.2f, 5.0f), (7.8f, 3.2f), (6.8f, 4.9f), (5.4f, 3.6f), (4.2f, 5.2f), (3.2f, 4.4f), (2.0f, 8.4f) },   // soft, parted tufts
            _ => new[] { (13.8f, 6.4f), (12.8f, 4.0f), (10.2f, 5.0f), (7.6f, 3.8f), (5.2f, 4.8f), (3.0f, 3.8f), (2.2f, 6.4f) },   // swept
        };
        var path = new SKPath();
        path.MoveTo(cut[^1].x, cut[^1].y);
        path.CubicTo(1.0f, 1.0f, 4.0f, -0.6f, 8.0f, -0.6f);
        path.CubicTo(12.0f, -0.6f, 15.0f, 1.0f, cut[0].x, cut[0].y);
        foreach (var (px, py) in cut.Skip(1)) path.LineTo(px, py);
        path.Close();
        return path;
    }

    private static void Accessory(SKCanvas c, float a, double t)
    {
        using var p = Fill(_sk.Ear, a);
        using var o = Line(Mix(_sk.Ear, SKColors.Black, 0.35f), a, 0.2f);
        switch (_sp.Id)
        {
            case "hana": // a bow on top
                Bow(c, 11.6f, 0.4f, 1.0f, p, o);
                break;
            case "yuki": // ribbons where the tails are tied
                Bow(c, 1.6f, 3.4f, 0.7f, p, o);
                Bow(c, 14.4f, 3.4f, 0.7f, p, o);
                break;
            case "kuri": // a beret (unless she has a hat on) and a little horseshoe clip
            {
                if (HatId == null)
                {
                    c.Save();
                    c.RotateDegrees(-8, 8, 0);
                    var beret = SKRect.Create(4.0f, -1.6f, 8.0f, 3.0f);
                    c.DrawOval(beret, p);
                    c.DrawOval(beret, o);
                    using (var band = Fill(Mix(_sk.Ear, SKColors.White, 0.55f), a)) c.DrawRoundRect(SKRect.Create(4.6f, 0.6f, 6.8f, 0.8f), 0.4f, 0.4f, band);
                    using (var shine = Fill(SKColors.White.WithAlpha(90), a)) c.DrawOval(SKRect.Create(5.4f, -1.1f, 2.6f, 0.8f), shine);
                    c.DrawCircle(8.2f, -1.7f, 0.4f, p);
                    c.Restore();
                }
                using var shoe = Line(Palette['y'], a, 0.4f);
                c.DrawArc(SKRect.Create(11.6f, 3.4f, 1.6f, 1.6f), 150, 240, false, shoe);
                break;
            }
            case "hoshi": // a star clip
            {
                using var star = new SKPath();
                for (int i = 0; i < 10; i++)
                {
                    float r = i % 2 == 0 ? 1.3f : 0.55f, ang = -MathF.PI / 2 + i * MathF.PI / 5;
                    float sx = 3.8f + r * MathF.Cos(ang), sy = 2.6f + r * MathF.Sin(ang);
                    if (i == 0) star.MoveTo(sx, sy); else star.LineTo(sx, sy);
                }
                star.Close();
                c.DrawPath(star, p);
                c.DrawPath(star, o);
                break;
            }
        }
    }

    private static void Bow(SKCanvas c, float cx, float cy, float k, SKPaint fill, SKPaint outline)
    {
        using var bow = new SKPath();
        Tri(bow, cx, cy, cx - 2.0f * k, cy - 1.2f * k, cx - 2.0f * k, cy + 1.2f * k);
        Tri(bow, cx, cy, cx + 2.0f * k, cy - 1.2f * k, cx + 2.0f * k, cy + 1.2f * k);
        c.DrawPath(bow, fill);
        c.DrawPath(bow, outline);
        c.DrawCircle(cx, cy, 0.55f * k, fill);
        c.DrawCircle(cx, cy, 0.55f * k, outline);
    }

    // ---------- the face ----------

    private const float EyeL = 5.3f, EyeR = 10.7f, EyeY = 7.1f;

    private static void Expression(SKCanvas c, CatFace face, float a, double t)
    {
        void Blush(float k = 1)
        {
            using var b = Fill(BlushC.WithAlpha(110), a * k);
            c.DrawOval(SKRect.Create(2.9f, 8.5f, 2.2f, 1.0f), b);
            c.DrawOval(SKRect.Create(10.9f, 8.5f, 2.2f, 1.0f), b);
        }
        switch (face)
        {
            case CatFace.Open:
                Blush(); Eye(c, EyeL, a, false); Eye(c, EyeR, a, false); Smile(c, a); break;
            case CatFace.Happy:
                Blush(1.3f); Arc(c, EyeL, a, true); Arc(c, EyeR, a, true); OpenMouth(c, a, 1.0f, true); break;
            case CatFace.Closed:
                Blush(0.8f); Arc(c, EyeL, a, false); Arc(c, EyeR, a, false); Smile(c, a, 0.6f); break;
            case CatFace.Determined:
                Eye(c, EyeL, a, false, lid: 0.25f); Eye(c, EyeR, a, false, lid: 0.25f); Flat(c, a); break;
            case CatFace.XEyes:
                Blush(); Cross(c, EyeL, a); Cross(c, EyeR, a); Wavy(c, a); break;
            case CatFace.Spiral:
                Spiral(c, EyeL, a, t); Spiral(c, EyeR, a, t); Wavy(c, a); break;
            case CatFace.Surprised:
                Eye(c, EyeL, a, true); Eye(c, EyeR, a, true); OpenMouth(c, a, 0.7f, false); break;
            case CatFace.Grumpy:
                Blush(1.4f); Eye(c, EyeL, a, false, lid: 0.5f); Eye(c, EyeR, a, false, lid: 0.5f); Pout(c, a); break;
            case CatFace.Love:
                Blush(1.5f); Heart(c, EyeL, EyeY, 1.4f, a); Heart(c, EyeR, EyeY, 1.4f, a); Smile(c, a); break;
            case CatFace.Droopy:
                Blush(0.6f); Eye(c, EyeL, a, false, lid: 0.55f); Eye(c, EyeR, a, false, lid: 0.55f); Smile(c, a, 0.4f); break;
            case CatFace.Wink:
                Blush(1.2f); Eye(c, EyeL, a, false); Arc(c, EyeR, a, true); OpenMouth(c, a, 0.8f, true); break;
            case CatFace.Sing:
                Blush(1.2f); Arc(c, EyeL, a, true); Arc(c, EyeR, a, true);
                if (SingOpen < 0.25f) Smile(c, a, 0.5f);
                else OpenMouth(c, a, 0.45f + 0.75f * SingOpen, false);
                break;
            case CatFace.Yawn:
                Arc(c, EyeL, a, false); Arc(c, EyeR, a, false); OpenMouth(c, a, 1.3f, false);
                using (var tear = Fill(new SKColor(150, 210, 255), a)) c.DrawCircle(EyeR + 1.5f, EyeY + 0.6f, 0.38f, tear);
                break;
            case CatFace.Shades:
                Blush(0.7f); Shades(c, a); Smile(c, a); break;
        }
    }

    /// <summary>A big glossy anime eye: a lash line on top, an iris darker at the top, a pupil and two highlights.</summary>
    private static void Eye(SKCanvas c, float cx, float a, bool small, float lid = 0)
    {
        var iris = _sp.Anime ? _sp.Iris : AnimalIris;
        var box = SKRect.Create(cx - 1.35f, EyeY - 1.75f, 2.7f, 3.5f);
        using (var white = Fill(SKColors.White, a)) c.DrawOval(box, white);
        float ir = small ? 0.65f : 1.15f;
        var irisBox = SKRect.Create(cx - ir, EyeY - (small ? 0.6f : 1.45f) + (small ? 0.2f : 0.15f), ir * 2, small ? 1.3f : 3.0f);
        using (var grad = SKShader.CreateLinearGradient(new SKPoint(0, irisBox.Top), new SKPoint(0, irisBox.Bottom),
                   new[] { Mix(iris, SKColors.Black, 0.55f), iris, Mix(iris, SKColors.White, 0.35f) }, null, SKShaderTileMode.Clamp))
        using (var p = new SKPaint { IsAntialias = true, Shader = grad, Color = SKColors.White.WithAlpha(G.A(a)) })
        {
            c.Save();
            c.ClipPath(OvalPath(box), SKClipOperation.Intersect, true);
            c.DrawOval(irisBox, p);
            using (var pupil = Fill(Mix(iris, SKColors.Black, 0.75f), a))
                c.DrawOval(SKRect.Create(cx - ir * 0.45f, irisBox.MidY - irisBox.Height * 0.28f, ir * 0.9f, irisBox.Height * 0.5f), pupil);
            using (var hi = Fill(SKColors.White, a))
            {
                c.DrawCircle(cx - ir * 0.38f, irisBox.Top + irisBox.Height * 0.3f, small ? 0.25f : 0.48f, hi);
                if (!small) c.DrawCircle(cx + ir * 0.4f, irisBox.Top + irisBox.Height * 0.75f, 0.2f, hi);
            }
            // Half-closed (sleepy, grumpy, focused): the lid comes down over the top.
            if (lid > 0) using (var skin = Fill(_sp.Anime ? Skin : Coat, a)) c.DrawRect(box.Left - 0.2f, box.Top - 0.2f, box.Width + 0.4f, box.Height * lid + 0.2f, skin);
            c.Restore();
        }
        // The upper lash line, thicker at the outer corner, with a flick.
        float top = box.Top + box.Height * lid;
        bool left = cx < 8;
        using var lash = Line(Ink, a, 0.5f);
        using var path = new SKPath();
        if (lid > 0) { path.MoveTo(box.Left - 0.1f, top + 0.05f); path.LineTo(box.Right + 0.1f, top + 0.05f); }
        else path.AddArc(SKRect.Create(box.Left - 0.1f, box.Top, box.Width + 0.2f, box.Height * 0.9f), 195, 150);
        c.DrawPath(path, lash);
        float ox = left ? box.Left : box.Right, dir = left ? -1 : 1;
        c.DrawLine(ox, top + 0.5f, ox + dir * 0.6f, top - 0.05f, lash);
    }

    private static SKPath OvalPath(SKRect r) { var p = new SKPath(); p.AddOval(r); return p; }

    /// <summary>Closed eyes: ^ when happy, a soft downward curve when calm or asleep.</summary>
    private static void Arc(SKCanvas c, float cx, float a, bool happy)
    {
        using var p = Line(Ink, a, 0.45f);
        using var path = new SKPath();
        if (happy) { path.MoveTo(cx - 1.2f, EyeY + 0.6f); path.QuadTo(cx, EyeY - 1.1f, cx + 1.2f, EyeY + 0.6f); }
        else { path.MoveTo(cx - 1.2f, EyeY + 0.1f); path.QuadTo(cx, EyeY + 1.2f, cx + 1.2f, EyeY + 0.1f); }
        c.DrawPath(path, p);
    }

    private static void Cross(SKCanvas c, float cx, float a)
    {
        using var p = Line(Ink, a, 0.42f);
        // > < : each eye points in toward the nose.
        float d = cx < 8 ? 1 : -1;
        c.DrawLine(cx - d * 0.9f, EyeY - 0.9f, cx + d * 0.8f, EyeY, p);
        c.DrawLine(cx + d * 0.8f, EyeY, cx - d * 0.9f, EyeY + 0.9f, p);
    }

    private static void Spiral(SKCanvas c, float cx, float a, double t)
    {
        using var p = Line(Ink, a, 0.3f);
        using var path = new SKPath();
        for (int i = 0; i <= 40; i++)
        {
            float k = i / 40f, ang = k * 4.2f * MathF.PI + (float)t * 4, r = 0.15f + k * 1.15f;
            float sx = cx + r * MathF.Cos(ang), sy = EyeY + r * MathF.Sin(ang);
            if (i == 0) path.MoveTo(sx, sy); else path.LineTo(sx, sy);
        }
        c.DrawPath(path, p);
    }

    private static void Heart(SKCanvas c, float cx, float cy, float s, float a)
    {
        using var path = new SKPath();
        path.MoveTo(cx, cy + 0.9f * s);
        path.CubicTo(cx - 1.3f * s, cy, cx - 0.9f * s, cy - 1.0f * s, cx, cy - 0.35f * s);
        path.CubicTo(cx + 0.9f * s, cy - 1.0f * s, cx + 1.3f * s, cy, cx, cy + 0.9f * s);
        using var p = Fill(new SKColor(255, 80, 130), a);
        c.DrawPath(path, p);
        using var hi = Fill(SKColors.White, 0.8f * a);
        c.DrawCircle(cx - 0.4f * s, cy - 0.3f * s, 0.18f * s, hi);
    }

    private static void Shades(SKCanvas c, float a)
    {
        using var p = Fill(new SKColor(20, 20, 26), a);
        c.DrawRoundRect(SKRect.Create(3.4f, 5.9f, 3.9f, 2.4f), 1.0f, 1.0f, p);
        c.DrawRoundRect(SKRect.Create(8.7f, 5.9f, 3.9f, 2.4f), 1.0f, 1.0f, p);
        using var bridge = Line(new SKColor(20, 20, 26), a, 0.4f);
        c.DrawLine(7.2f, 6.5f, 8.8f, 6.5f, bridge);
        using var glint = Line(SKColors.White, 0.8f * a, 0.3f);
        c.DrawLine(4.2f, 7.4f, 5.2f, 6.4f, glint);
        c.DrawLine(9.5f, 7.4f, 10.5f, 6.4f, glint);
    }

    private static void Smile(SKCanvas c, float a, float k = 1)
    {
        // A small cat-like "ω" smile.
        using var p = Line(Lip, a, 0.3f);
        using var path = new SKPath();
        path.MoveTo(7.0f, 9.5f);
        path.QuadTo(7.5f, 9.5f + 0.7f * k, 8.0f, 9.6f);
        path.QuadTo(8.5f, 9.5f + 0.7f * k, 9.0f, 9.5f);
        c.DrawPath(path, p);
    }

    private static void OpenMouth(SKCanvas c, float a, float k, bool smile)
    {
        using var path = new SKPath();
        if (smile)
        {
            // A "D" smile: flat top, round bottom, a little tongue.
            path.MoveTo(7.0f * 1 - (k - 1), 9.3f);
            path.LineTo(9.0f + (k - 1), 9.3f);
            path.QuadTo(8.0f, 11.2f + (k - 1), 7.0f - (k - 1), 9.3f);
        }
        else path.AddOval(SKRect.Create(8.0f - 0.55f * k, 9.3f, 1.1f * k, 1.3f * k));
        using (var p = Fill(new SKColor(150, 40, 60), a)) c.DrawPath(path, p);
        using (var tongue = Fill(new SKColor(255, 130, 150), a))
        {
            c.Save(); c.ClipPath(path, SKClipOperation.Intersect, true);
            c.DrawOval(SKRect.Create(7.3f, 10.0f, 1.4f, 1.2f), tongue);
            c.Restore();
        }
    }

    private static void Flat(SKCanvas c, float a)
    {
        using var p = Line(Lip, a, 0.32f);
        c.DrawLine(7.3f, 9.7f, 8.7f, 9.7f, p);
    }

    private static void Wavy(SKCanvas c, float a)
    {
        using var p = Line(Lip, a, 0.3f);
        using var path = new SKPath();
        path.MoveTo(6.8f, 9.8f);
        path.QuadTo(7.2f, 9.3f, 7.6f, 9.8f); path.QuadTo(8.0f, 10.3f, 8.4f, 9.8f); path.QuadTo(8.8f, 9.3f, 9.2f, 9.8f);
        c.DrawPath(path, p);
    }

    private static void Pout(SKCanvas c, float a)
    {
        using var p = Line(Lip, a, 0.32f);
        using var path = new SKPath();
        path.MoveTo(7.3f, 10.0f); path.QuadTo(8.0f, 9.3f, 8.7f, 10.0f);
        c.DrawPath(path, p);
        // A puffed cheek and an anger mark.
        using var mark = Line(new SKColor(230, 60, 80), a, 0.3f);
        c.DrawLine(12.4f, 2.4f, 12.4f, 3.6f, mark); c.DrawLine(11.8f, 3.0f, 13.0f, 3.0f, mark);
    }

    private static void Brows(SKCanvas c, CatFace face, float a)
    {
        using var p = Line(Mix(_sk.Body, SKColors.Black, 0.45f), a, 0.45f);
        if (face == CatFace.Determined) { c.DrawLine(3.9f, 4.7f, 6.4f, 5.3f, p); c.DrawLine(12.1f, 4.7f, 9.6f, 5.3f, p); }
        else { c.DrawLine(3.9f, 5.1f, 6.4f, 4.7f, p); c.DrawLine(12.1f, 5.1f, 9.6f, 4.7f, p); }
    }

    // ---------- things she wears ----------

    private static void Band(SKCanvas c, float a)
    {
        using var p = Line(new SKColor(110, 112, 124), a, 0.9f);
        c.DrawArc(SKRect.Create(1.2f, -2.0f, 13.6f, 13.0f), 180, 180, false, p);
    }

    private static void Worn(SKCanvas c, string? worn, float a, double t)
    {
        if (worn == null) return;
        var cup = new SKColor(70, 72, 82);
        switch (worn)
        {
            case "headphones":
            case "headset":
            case "neckphones":
            {
                float top = worn == "neckphones" ? 9.6f : 4.6f;
                var pad = Palette[worn == "headset" ? 'c' : 'v'];
                using var cp = Fill(cup, a);
                using var pp = Fill(pad, a);
                c.DrawRoundRect(SKRect.Create(0.2f, top, 2.4f, 4.0f), 1.0f, 1.0f, cp);
                c.DrawRoundRect(SKRect.Create(13.4f, top, 2.4f, 4.0f), 1.0f, 1.0f, cp);
                c.DrawRoundRect(SKRect.Create(1.6f, top + 0.6f, 1.0f, 2.8f), 0.5f, 0.5f, pp);
                c.DrawRoundRect(SKRect.Create(13.4f, top + 0.6f, 1.0f, 2.8f), 0.5f, 0.5f, pp);
                if (worn == "neckphones")
                {
                    using var band = Line(new SKColor(110, 112, 124), a, 0.8f);
                    c.DrawArc(SKRect.Create(1.4f, 6.0f, 13.2f, 7.4f), 20, 140, false, band);
                }
                if (worn == "headset")
                {
                    using var boom = Line(Palette['s'], a, 0.4f);
                    using var path = new SKPath();
                    path.MoveTo(1.6f, 8.4f); path.QuadTo(3.0f, 11.0f, 6.2f, 10.4f);
                    c.DrawPath(path, boom);
                    using var mic = Fill(Palette['d'], a);
                    c.DrawCircle(6.4f, 10.4f, 0.5f, mic);
                }
                break;
            }
            case "hardhat":
            {
                using var path = new SKPath();
                path.AddArc(SKRect.Create(2.0f, -2.2f, 12.0f, 7.6f), 180, 180);
                path.Close();
                using (var p = Fill(Palette['y'], a)) c.DrawPath(path, p);
                using (var p = Fill(Palette['o'], a)) c.DrawRoundRect(SKRect.Create(1.0f, 1.3f, 14.0f, 1.0f), 0.5f, 0.5f, p);
                using (var p = Line(Mix(Palette['y'], SKColors.White, 0.5f), a, 0.4f)) c.DrawLine(8f, -2.0f, 8f, 1.2f, p);
                break;
            }
            case "glasses":
            {
                using var p = Line(new SKColor(250, 250, 250, 230), a, 0.32f);
                c.DrawCircle(EyeL, EyeY, 1.9f, p);
                c.DrawCircle(EyeR, EyeY, 1.9f, p);
                c.DrawLine(EyeL + 1.9f, EyeY - 0.3f, EyeR - 1.9f, EyeY - 0.3f, p);
                break;
            }
            case "partyhat":
            {
                using var path = new SKPath();
                Tri(path, 6.2f, 0.8f, 9.8f, 0.8f, 8.6f, -4.6f);
                using (var p = Fill(Palette['p'], a)) c.DrawPath(path, p);
                using (var p = Fill(Palette['y'], a))
                {
                    c.DrawCircle(8.6f, -4.8f, 0.7f, p);
                    c.DrawCircle(7.6f, -0.6f, 0.35f, p);
                    c.DrawCircle(8.8f, -2.2f, 0.35f, p);
                }
                break;
            }
            case "tie":
            {
                // A tiny bow at the collar instead.
                using var p = Fill(Palette['r'], a);
                using var o = Line(Mix(Palette['r'], SKColors.Black, 0.3f), a, 0.15f);
                Bow(c, 8f, 11.9f, 0.6f, p, o);
                break;
            }
            case "pawmouth":
                Hand(c, 8.0f, 10.0f, a);
                break;
            case "pawears":
                Hand(c, 1.3f, 6.4f, a);
                Hand(c, 14.7f, 6.4f, a);
                break;
            case "pawsup":
            {
                float lift = MathF.Abs(MathF.Sin((float)t * 4)) * 0.8f;
                Hand(c, 4.4f, 11.2f - lift, a);
                Hand(c, 11.6f, 11.2f - lift, a);
                break;
            }
        }
    }

    /// <summary>The unlockable hats, drawn smooth for the girls.</summary>
    private static void GirlHat(SKCanvas c, string hat, float a, double t)
    {
        switch (hat)
        {
            case "flower":
            {
                using var petal = Fill(Palette['p'], a);
                for (int i = 0; i < 5; i++)
                {
                    float ang = i * MathF.PI * 2 / 5 - MathF.PI / 2;
                    c.DrawCircle(12.6f + 0.9f * MathF.Cos(ang), 1.4f + 0.9f * MathF.Sin(ang), 0.7f, petal);
                }
                using var mid = Fill(Palette['y'], a);
                c.DrawCircle(12.6f, 1.4f, 0.5f, mid);
                break;
            }
            case "beanie":
            {
                using var path = new SKPath();
                path.AddArc(SKRect.Create(2.6f, -2.6f, 10.8f, 7.2f), 180, 180);
                path.Close();
                using (var p = Fill(Palette['r'], a)) c.DrawPath(path, p);
                using (var p = Fill(SKColors.White, a))
                {
                    c.DrawRoundRect(SKRect.Create(2.2f, 0.4f, 11.6f, 1.6f), 0.8f, 0.8f, p);
                    c.DrawCircle(8, -2.8f, 0.9f, p);
                }
                break;
            }
            case "bow":
            {
                using var p = Fill(Palette['p'], a);
                using var o = Line(Mix(Palette['p'], SKColors.Black, 0.3f), a, 0.18f);
                Bow(c, 8, -0.6f, 1.6f, p, o);
                break;
            }
            case "crown":
            {
                using var path = new SKPath();
                path.MoveTo(4.6f, 0.6f); path.LineTo(4.6f, -2.2f); path.LineTo(6.3f, -0.8f); path.LineTo(8f, -3.0f);
                path.LineTo(9.7f, -0.8f); path.LineTo(11.4f, -2.2f); path.LineTo(11.4f, 0.6f); path.Close();
                using (var p = Fill(Palette['y'], a)) c.DrawPath(path, p);
                using (var o = Line(Palette['o'], a, 0.2f)) c.DrawPath(path, o);
                using (var gem = Fill(Palette['r'], a)) c.DrawCircle(8f, -0.4f, 0.45f, gem);
                break;
            }
            case "halo":
            {
                float bob = MathF.Sin((float)t * 2) * 0.35f;
                using var ring = Line(Palette['y'], a, 0.55f);
                c.DrawOval(SKRect.Create(4.2f, -3.4f + bob, 7.6f, 1.8f), ring);
                break;
            }
            case "wizard":
            {
                using var path = new SKPath();
                Tri(path, 3.6f, 0.4f, 12.4f, 0.4f, 9.2f, -6.0f);
                using (var p = Fill(Palette['v'], a)) c.DrawPath(path, p);
                using (var p = Fill(Mix(Palette['v'], SKColors.Black, 0.25f), a)) c.DrawRoundRect(SKRect.Create(2.6f, -0.2f, 10.8f, 1.2f), 0.6f, 0.6f, p);
                using (var star = Fill(Palette['y'], a)) c.DrawCircle(8.4f, -2.6f, 0.55f, star);
                break;
            }
        }
    }

    private static void Hand(SKCanvas c, float cx, float cy, float a)
    {
        using var p = Fill(_handC, a);
        using var o = Line(_sp.Anime ? Mix(Skin, new SKColor(170, 100, 90), 0.55f) : Edge, a, 0.22f);
        c.DrawOval(SKRect.Create(cx - 1.2f, cy - 1.0f, 2.4f, 2.0f), p);
        c.DrawOval(SKRect.Create(cx - 1.2f, cy - 1.0f, 2.4f, 2.0f), o);
    }
}
