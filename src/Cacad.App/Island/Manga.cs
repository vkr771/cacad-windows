using SkiaSharp;

namespace Cacad;

/// <summary>The Manga surface: a page of ink, read in negative so white content stays readable. Built from the tools
/// manga artists draw with:
///  - screentone: halftone dots on a 45° grid, big dots in the darkest corner shrinking into small dots further out
///    (the way printed tone grades), rather than a smooth gradient;
///  - focus lines (shuchūsen): tapered ink strokes rushing in from the edges towards the middle and stopping short of
///    it, so the island's content sits in the calm eye of the panel. They are redrawn a few times a second, like the
///    "boiling" speed lines of anime;
///  - kira-kira sparkles: little four-point stars that twinkle in the corners.
/// Everything is drawn crisp in island DIPs (no shader), so dots and hairlines stay sharp.</summary>
public static class Manga
{
    /// <summary>Seconds between frames: the focus lines jitter at about 6 per second.</summary>
    public const double Interval = 1 / 6.0;

    private static SKImage? _bigDots, _smallDots;

    /// <summary>The big halftone dot tile (white dots, 16 px a cell), for screentone elsewhere (the Comic clock).</summary>
    public static SKImage DotsTile => _bigDots ??= DotTile(4.6f);

    private static SKImage DotTile(float radius)
    {
        const int n = 16;
        using var surface = SKSurface.Create(new SKImageInfo(n, n, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        using var p = new SKPaint { IsAntialias = true, Color = SKColors.White };
        surface.Canvas.DrawCircle(n / 2f, n / 2f, radius, p);
        return surface.Snapshot();
    }

    private static float Hash(int n)
    {
        uint x = unchecked((uint)n * 2654435761u);
        x ^= x >> 15; x = unchecked(x * 2246822519u); x ^= x >> 13;
        return (x & 0xFFFFFF) / (float)0x1000000;
    }

    /// <summary>Canvas in island DIPs, x = 0 at the centre, y from 0 to <paramref name="h"/>, clipped to the shape.</summary>
    public static void Draw(SKCanvas c, float w, float h, double time, float open)
    {
        var area = SKRect.Create(-w / 2 - 2, -2, w + 4, h + 4);
        // The page: ink, a touch lighter at the top like a printed black.
        using (var sh = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, h), new[] { new SKColor(20, 19, 23), new SKColor(9, 9, 11) }, null, SKShaderTileMode.Clamp))
        using (var p = new SKPaint { Shader = sh })
            c.DrawRect(area, p);

        Tone(c, w, h, open);
        FocusLines(c, w, h, time, open);
        Sparkles(c, w, h, time, open);
    }

    /// <summary>Screentone graded from the lower right: big dots near the corner, small dots beyond, then none.</summary>
    private static void Tone(SKCanvas c, float w, float h, float open)
    {
        _bigDots ??= DotTile(4.6f);
        _smallDots ??= DotTile(2.3f);
        float pitch = 4.2f;
        var local = SKMatrix.CreateScale(pitch / 16f, pitch / 16f).PostConcat(SKMatrix.CreateRotationDegrees(45));
        var corner = new SKPoint(w / 2, h);
        float reach = MathF.Max(w * 0.55f, h * 1.3f);
        float strength = 0.22f - 0.06f * open;
        void Layer(SKImage tile, float inner, float outer, float alpha)
        {
            using var dots = SKShader.CreateImage(tile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, local);
            using var mask = SKShader.CreateRadialGradient(corner, reach * outer,
                new[] { SKColors.White.WithAlpha((byte)(255 * alpha)), SKColors.White.WithAlpha((byte)(255 * alpha)), SKColors.Transparent },
                new[] { 0f, inner / outer, 1f }, SKShaderTileMode.Clamp);
            using var both = SKShader.CreateCompose(dots, mask, SKBlendMode.DstIn);
            using var p = new SKPaint { Shader = both, IsAntialias = true };
            c.DrawRect(SKRect.Create(-w / 2, 0, w, h), p);
        }
        Layer(_bigDots, 0.15f, 0.42f, strength);
        Layer(_smallDots, 0.40f, 0.78f, strength * 0.8f);
        // A small patch of fine tone in the opposite corner, for balance.
        corner = new SKPoint(-w / 2, 0);
        Layer(_smallDots, 0.05f, 0.3f, strength * 0.55f);
    }

    /// <summary>Tapered strokes from beyond the edges towards an empty eye in the middle.</summary>
    private static void FocusLines(SKCanvas c, float w, float h, double time, float open)
    {
        int frame = (int)(time / Interval);
        float cx = 0, cy = h / 2;
        float outRx = w * 0.75f + 20, outRy = h * 0.9f + 20;
        int n = w > 200 ? 84 : 60;
        using var path = new SKPath();
        for (int i = 0; i < n; i++)
        {
            int seed = frame * 131 + i * 7;
            float ang = (i + Hash(seed) * 0.8f) / n * MathF.PI * 2;
            // Where the stroke stops: a ragged ellipse around the middle, kept well clear of the content.
            float stop = 0.66f + 0.26f * Hash(seed + 1);
            float inRx = w / 2 * stop, inRy = h / 2 * (0.62f + 0.3f * Hash(seed + 2));
            float half = (0.004f + 0.012f * Hash(seed + 3)) * (h > 60 ? 1 : 1.6f);
            float cos = MathF.Cos(ang), sin = MathF.Sin(ang);
            path.MoveTo(cx + MathF.Cos(ang - half) * outRx, cy + MathF.Sin(ang - half) * outRy);
            path.LineTo(cx + cos * inRx, cy + sin * inRy);
            path.LineTo(cx + MathF.Cos(ang + half) * outRx, cy + MathF.Sin(ang + half) * outRy);
            path.Close();
        }
        using var p = new SKPaint { IsAntialias = true, Color = new SKColor(240, 238, 245, G.A(0.22f - 0.07f * open)) };
        c.DrawPath(path, p);
    }

    /// <summary>Kira-kira: four-point stars twinkling in the corners.</summary>
    private static void Sparkles(SKCanvas c, float w, float h, double time, float open)
    {
        (float x, float y, float r)[] spots = h > 60
            ? new[] { (w / 2 - 26, 22f, 5.5f), (w / 2 - 44, 38f, 3.2f), (-w / 2 + 30, h - 24, 4.2f) }
            : new[] { (w / 2 - h * 0.7f, h * 0.32f, 2.8f), (-w / 2 + h * 0.75f, h * 0.7f, 2.2f) };
        using var p = new SKPaint { IsAntialias = true };
        for (int i = 0; i < spots.Length; i++)
        {
            var (x, y, r) = spots[i];
            float tw = MathF.Pow(0.5f + 0.5f * MathF.Sin((float)time * (1.1f + i * 0.37f) + i * 2.1f), 2);
            if (tw < 0.05f) continue;
            float s = r * (0.6f + 0.4f * tw);
            p.Color = new SKColor(255, 252, 246, G.A((0.35f + 0.5f * tw) * (1 - 0.3f * open)));
            using var star = new SKPath();
            star.MoveTo(x, y - s);
            star.QuadTo(x, y, x + s, y);
            star.QuadTo(x, y, x, y + s);
            star.QuadTo(x, y, x - s, y);
            star.QuadTo(x, y, x, y - s);
            star.Close();
            c.DrawPath(star, p);
        }
    }
}
