using SkiaSharp;

namespace Cacad;

/// <summary>Each soft theme's own touch drifting through the open card.</summary>
public static class Ambient
{
    private static SKPath? _heart;

    public static void Heart(SKCanvas c, float cx, float cy, float size, SKColor color, bool filled)
    {
        _heart ??= SKPath.ParseSvgPathData("M19 14c1.49-1.46 3-3.21 3-5.5A5.5 5.5 0 0 0 16.5 3c-1.76 0-3 .5-4.5 2-1.5-1.5-2.74-2-4.5-2A5.5 5.5 0 0 0 2 8.5c0 2.3 1.5 4.05 3 5.5l7 7Z");
        c.Save();
        c.Translate(cx, cy);
        c.Scale(size / 24f);
        c.Translate(-12, -12);
        using var p = new SKPaint
        {
            IsAntialias = true,
            Color = color,
            Style = filled ? SKPaintStyle.Fill : SKPaintStyle.Stroke,
            StrokeWidth = 2.2f,
            StrokeJoin = SKStrokeJoin.Round,
        };
        c.DrawPath(_heart, p);
        c.Restore();
    }

    private static void Star(SKCanvas c, float cx, float cy, float r, SKColor color)
    {
        if (r < 0.3f) return;
        float w = r * 0.28f;
        using var path = new SKPath();
        path.MoveTo(cx, cy - r);
        path.QuadTo(cx + w, cy - w, cx + r, cy);
        path.QuadTo(cx + w, cy + w, cx, cy + r);
        path.QuadTo(cx - w, cy + w, cx - r, cy);
        path.QuadTo(cx - w, cy - w, cx, cy - r);
        path.Close();
        using var p = new SKPaint { IsAntialias = true, Color = color };
        c.DrawPath(path, p);
    }

    private static float Hash01(int n)
    {
        uint h = unchecked((uint)n * 2654435761u);
        h ^= h >> 15; h = unchecked(h * 2246822519u); h ^= h >> 13;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    /// <summary>Draw the theme's ambience in card coordinates. Returns true when it animates.</summary>
    public static bool Draw(SKCanvas c, Theme theme, float a, float width, float bottom, double time)
    {
        if (a < 0.05f || theme.Ambience == Ambience.None) return false;
        float t = (float)time, span = 292;
        float Travel(float seed, float speed) => (seed * span + t * speed) % span;
        float Across(float seed, float drift) => (((seed * width + drift) % (width + 20)) + width + 20) % (width + 20) - 10;
        bool Hidden(float y) => y > bottom + 10 || y < -14;

        switch (theme.Ambience)
        {
            case Ambience.Petals:
                for (int i = 0; i < 11; i++)
                {
                    float fall = Travel(Hash01(i * 3 + 7), 9 + 7 * Hash01(i * 17 + 2)), y = -12 + fall;
                    if (Hidden(y)) continue;
                    float x = Across(Hash01(i * 23 + 1), MathF.Sin(t * (0.6f + 0.5f * Hash01(i * 9 + 4)) + i) * 14 + fall * 0.25f);
                    float sz = 4.6f + 2.6f * Hash01(i * 29 + 8);
                    c.Save();
                    c.Translate(x, y);
                    c.RotateDegrees(t * (40 + 50 * Hash01(i * 11 + 6)) + i * 40);
                    using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(255, 196, 210, G.A(0.44f * a)) })
                        c.DrawOval(new SKRect(0, 0, sz, sz * 0.62f), p);
                    c.Restore();
                }
                break;
            case Ambience.Hearts:
                for (int i = 0; i < 9; i++)
                {
                    float rise = Travel(Hash01(i * 5 + 3), 7 + 6 * Hash01(i * 13 + 1)), y = span - 12 - rise;
                    if (Hidden(y)) continue;
                    float x = Across(Hash01(i * 19 + 2), MathF.Sin(t * 0.9f + i * 1.7f) * 10);
                    Heart(c, x, y, 9 + 6 * Hash01(i * 7 + 9), new SKColor(255, 143, 177, G.A(0.55f * a * Math.Min(1, rise / 40))), true);
                }
                break;
            case Ambience.Stars:
                for (int i = 0; i < 16; i++)
                {
                    float y = Travel(Hash01(i * 7 + 2), 2.5f);
                    if (Hidden(y)) continue;
                    float x = Across(Hash01(i * 31 + 5), t * 1.5f);
                    float glow = MathF.Pow(0.5f + 0.5f * MathF.Sin(t * (0.9f + 1.4f * Hash01(i * 3 + 1)) + i * 2.1f), 2), al = (0.125f + 0.5f * glow) * a;
                    Star(c, x, y, 3.2f + 2.6f * glow * Hash01(i * 11 + 4), i % 4 == 0 ? G.Paper(al) : new SKColor(195, 166, 255, G.A(al)));
                }
                break;
            case Ambience.Glow:
                for (int i = 0; i < 7; i++)
                {
                    float y = Hash01(i * 13 + 6) * span + MathF.Sin(t * 0.4f + i) * 8;
                    if (Hidden(y)) continue;
                    float x = Across(Hash01(i * 17 + 3), t * (4 + 4 * Hash01(i * 5 + 2)));
                    float rr = 18 + 16 * Hash01(i * 23 + 7), aa = (0.267f + 0.094f * MathF.Sin(t * 0.7f + i * 1.3f)) * a;
                    var warm = Hash01(i * 2 + 9) > 0.5f ? new SKColor(255, 179, 138) : new SKColor(255, 214, 168);
                    using var sh = SKShader.CreateRadialGradient(new SKPoint(x, y), rr,
                        new[] { warm.WithAlpha(G.A(aa)), warm.WithAlpha(G.A(aa / 3)), warm.WithAlpha(0) }, new[] { 0f, 0.65f, 1f }, SKShaderTileMode.Clamp);
                    using var p = new SKPaint { IsAntialias = true, Shader = sh };
                    c.DrawCircle(x, y, rr, p);
                }
                break;
        }
        return true;
    }
}
