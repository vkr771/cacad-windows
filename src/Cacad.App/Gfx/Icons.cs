using SkiaSharp;

namespace Cacad;

public static partial class Icons
{
    private static readonly Dictionary<string, SKPath> Cache = new();

    public static SKPath? Path(string name)
    {
        if (Cache.TryGetValue(name, out var p)) return p;
        if (!Data.TryGetValue(name, out var d)) return null;
        p = SKPath.ParseSvgPathData(d);
        Cache[name] = p;
        return p;
    }

    /// <summary>Stroke a Lucide glyph centred on (cx, cy), <paramref name="size"/> DIPs across.</summary>
    public static void Draw(SKCanvas c, string name, float cx, float cy, float size, SKColor color, float stroke = 1.75f)
    {
        var path = Path(name);
        if (path == null || color.Alpha == 0) return;
        c.Save();
        c.Translate(cx - size / 2f, cy - size / 2f);
        c.Scale(size / 24f);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = color,
        };
        c.DrawPath(path, paint);
        c.Restore();
    }

    /// <summary>Fill (and lightly stroke) an SVG path given in a 24x24 grid, for solid glyphs.</summary>
    public static void Solid(SKCanvas c, string svg, float cx, float cy, float size, SKColor color)
    {
        if (!SolidCache.TryGetValue(svg, out var path))
            SolidCache[svg] = path = SKPath.ParseSvgPathData(svg);
        c.Save();
        c.Translate(cx - size / 2f, cy - size / 2f);
        c.Scale(size / 24f);
        using var paint = new SKPaint { IsAntialias = true, Color = color, Style = SKPaintStyle.StrokeAndFill, StrokeWidth = 1.2f, StrokeJoin = SKStrokeJoin.Round };
        c.DrawPath(path, paint);
        c.Restore();
    }

    private static readonly Dictionary<string, SKPath> SolidCache = new();
}
