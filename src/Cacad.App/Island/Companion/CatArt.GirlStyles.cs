using SkiaSharp;

namespace Cacad;

/// <summary>The anime girls in the animal art styles ("Pixel" keeps their own anime look):
///  - Toon: flat colour blocks, no shading or shine; hair as one bold silhouette with a hairline edge; big white
///    eyes with small upright pupils and a single lash flick; a tiny nose dot.
///  - Cute: chibi kawaii. A rounder, wider face with the features set low and close: bead eyes tinted with her own
///    eye colour, a tiny mouth between them, round blush; pastel hair in soft lumps; a thick soft outline.
///  - Sketch: pencil on paper. A paper face, a light wash of hair colour, loose graphite outlines that overshoot, a few strokes of fringe,
///    dot eyes with a lash, scribbled blush.
/// Hair shapes, accessories, worn things and hats are the girls' own (CatArt.Anime.cs).</summary>
public static partial class CatArt
{
    private static bool _girl;

    /// <summary>The animals' iris in the anime style: gold on a dark coat, green on a pale one, warm brown for the
    /// dog, bear and panda.</summary>
    private static SKColor AnimalIris => _sp.Id is "dog" or "bear" or "panda" ? new SKColor(150, 96, 56) : !Light(_sk.Body) ? new SKColor(236, 186, 60) : new SKColor(96, 184, 120);

    private static void DrawGirlStyled(SKCanvas c, CatPose pose, CatFace face, float x, float y, float px, float a, double t)
    {
        c.Save();
        c.Translate(x, y);
        c.Scale(px);
        _girl = true;
        Layout();
        _boil = (int)(t * 4.5) % 5;
        var skin = GirlSkin;
        _handC = skin;

        if (pose.Worn is "headphones" or "headset") Band(c, a);

        // The hair behind, and her cat or fox ears.
        using (var back = BackHairPath(t)) PaintHair(c, back, a, true);
        using (var inner = InnerEarsPath())
            if (inner != null)
            {
                if (_style == "sketch") Doodle(c, inner, Chalk, 0.6f * a, 0.16f, 0.05f);
                else using (var p = Fill(_style == "cute" ? Mix(InnerEarColor, SKColors.White, 0.25f) : InnerEarColor, a)) c.DrawPath(inner, p);
            }

        // The face: rounder and a little wider on the chibi.
        using (var faceP = new SKPath())
        {
            faceP.AddOval(_style == "cute" ? SKRect.Create(2.0f, 1.8f, 12.0f, 10.0f) : SKRect.Create(2.4f, 1.6f, 11.2f, 10.0f));
            switch (_style)
            {
                case "toon":
                    using (var p = Fill(skin, a)) c.DrawPath(faceP, p);
                    using (var p = Line(Mix(skin, new SKColor(120, 70, 70), 0.55f), a, 0.2f)) c.DrawPath(faceP, p);
                    break;
                case "cute":
                    using (var p = Fill(skin, a)) c.DrawPath(faceP, p);
                    using (var p = Line(Mix(skin, new SKColor(150, 70, 80), 0.6f), a, 0.45f)) c.DrawPath(faceP, p);
                    break;
                default:
                    // Paper: opaque, so the hair lines behind do not show through.
                    using (var p = Fill(new SKColor(252, 246, 238), a)) c.DrawPath(faceP, p);
                    SketchLine(c, faceP, Chalk, a, 0.3f);
                    break;
            }
        }

        StyledFace(c, face, a, t);
        using (var bangs = BangsPath()) PaintHair(c, bangs, a, false);
        Accessory(c, a, t);
        c.Save();
        if (pose.Worn == "glasses") c.Translate(0, _eyeY - EyeY);
        Worn(c, pose.Worn, a, t);
        c.Restore();
        if (HatId != null && pose.Worn is not ("hardhat" or "partyhat")) GirlHat(c, HatId, a, t);
        _girl = false;
        c.Restore();
    }

    private static SKColor GirlSkin => _style switch
    {
        "cute" => new SKColor(255, 232, 220),
        "toon" => new SKColor(255, 222, 200),
        _ => new SKColor(255, 226, 206),
    };

    /// <summary>The hair, painted in the style: the back a shade deeper than the fringe.</summary>
    private static void PaintHair(SKCanvas c, SKPath path, float a, bool back)
    {
        var hair = _sk.Body;
        switch (_style)
        {
            case "toon":
            {
                // Flat blocks: one colour each, no shine.
                var fill = back ? Mix(hair, SKColors.Black, Light(hair) ? 0.12f : 0.22f) : hair;
                var edge = Light(hair) ? Mix(hair, SKColors.Black, 0.45f) : _sk.Rim;
                using (var p = Fill(fill, a)) c.DrawPath(path, p);
                using (var p = Line(edge, 0.9f * a, 0.22f)) c.DrawPath(path, p);
                break;
            }
            case "cute":
            {
                var pastel = Mix(hair, SKColors.White, Light(hair) ? 0.12f : 0.25f);
                var fill = back ? Mix(pastel, new SKColor(120, 90, 140), 0.14f) : pastel;
                using (var p = Fill(fill, a)) c.DrawPath(path, p);
                using (var p = Line(Mix(hair, new SKColor(60, 34, 50), 0.6f), a, 0.45f)) c.DrawPath(path, p);
                if (!back)
                    // Two soft shine lumps on the crown, not a ring.
                    using (var p = Fill(SKColors.White.WithAlpha(110), a))
                    {
                        c.DrawOval(SKRect.Create(4.4f, 0.9f, 2.0f, 0.9f), p);
                        c.DrawOval(SKRect.Create(7.0f, 0.6f, 1.2f, 0.7f), p);
                    }
                break;
            }
            default:
            {
                using (var p = Fill(Mix(hair, new SKColor(252, 246, 238), back ? 0.45f : 0.6f), a)) c.DrawPath(path, p);
                SketchLine(c, path, Chalk, a, 0.36f);
                if (!back)
                {
                    // A few strokes of fringe, falling from the crown.
                    using var strands = new SKPath();
                    strands.MoveTo(6.0f, 0.6f); strands.QuadTo(5.4f, 2.4f, 5.6f, 4.0f);
                    strands.MoveTo(9.4f, 0.4f); strands.QuadTo(10.2f, 2.2f, 10.0f, 3.9f);
                    strands.MoveTo(4.0f, 1.6f); strands.QuadTo(3.4f, 3.0f, 3.5f, 4.2f);
                    Doodle(c, strands, Chalk, 0.7f * a, 0.2f, 0.06f);
                }
                break;
            }
        }
    }
}
